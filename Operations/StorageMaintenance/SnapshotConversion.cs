using Core.Db;
using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.Text.Json.Nodes;

internal static class SnapshotConversion
{
    // Run only after explicit approval of migration 037 and this historical conversion.
    // Each transaction changes one exact primary-key row. No table reset or history deletion.
    public static async Task RunAsync(string verifiedBackup)
    {
        foreach (string name in new[] { "TraderVI.WPF", "TraderVI", "Sandbox" })
            if (Process.GetProcessesByName(name).Length != 0)
                throw new InvalidOperationException("Close all TraderVI writers before conversion.");
        await using var connection = new SqlConnection(SQLBase.Database);
        await connection.OpenAsync();
        await using (var guard = new SqlCommand("""
IF OBJECT_ID(N'dbo.DelphiLiveSnapshotContent',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLiveSnapshotItem',N'U') IS NULL
 OR OBJECT_ID(N'dbo.TR_DelphiLivePortfolioRevision_CompactStorage',N'TR') IS NULL
 THROW 53720,'Install reviewed migration 037 first.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 53721,'An active Live host prevents maintenance.',1;
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@Backup,@Backup+N'.partial'))
 THROW 53722,'A fresh checksum full backup of TraderDB is required.',1;
RESTORE VERIFYONLY FROM DISK=@Backup WITH CHECKSUM;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
IF @lock<0 THROW 53723,'Another storage writer prevents maintenance.',1;
""", connection) { CommandTimeout = 600 })
        {
            guard.Parameters.AddWithValue("@Backup", Path.GetFullPath(verifiedBackup));
            await guard.ExecuteNonQueryAsync();
        }
        // This session lock is released on connection disposal, including any failed/resumable run.
        var keys = new List<(Guid Id, long Revision)>();
        await using (var read = new SqlCommand("SELECT PortfolioId,Revision FROM dbo.DelphiLivePortfolioRevision ORDER BY PortfolioId,Revision;", connection))
        {
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync()) keys.Add((reader.GetGuid(0), reader.GetInt64(1)));
        }
        int converted = 0, processed = 0;
        foreach (var key in keys)
        {
            await RequireNoHost(connection);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            string raw = await ReadRow(connection, transaction, "DelphiLivePortfolioRevision", key.Id, key.Revision);
            if (DelphiLiveSnapshotCodec.IsCompact(raw))
                _ = await DelphiLiveSnapshotStorage.ReadJsonAsync(connection, transaction, raw);
            else
            {
                RequireIdentity(raw, key.Id, key.Revision);
                string compact = await DelphiLiveSnapshotStorage.SaveJsonAsync(connection, transaction, raw);
                await ReplaceRow(connection, transaction, "DelphiLivePortfolioRevision", key.Id, key.Revision, compact);
                converted++;
            }
            await transaction.CommitAsync();
            processed++;
            if (processed % 25 == 0) Console.Error.WriteLine($"History rows verified: {processed}/{keys.Count}; newly converted: {converted}");
        }
        var current = new List<(Guid Id, long Revision)>();
        await using (var read = new SqlCommand("SELECT PortfolioId,Revision FROM dbo.DelphiLivePortfolioLedger ORDER BY PortfolioId;", connection))
        {
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync()) current.Add((reader.GetGuid(0), reader.GetInt64(1)));
        }
        foreach (var key in current)
        {
            await RequireNoHost(connection);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            string raw = await ReadRow(connection, transaction, "DelphiLivePortfolioLedger", key.Id, key.Revision);
            string history = await ReadRow(connection, transaction, "DelphiLivePortfolioRevision", key.Id, key.Revision);
            string historicalFull = await DelphiLiveSnapshotStorage.ReadJsonAsync(connection, transaction, history);
            string currentFull = await DelphiLiveSnapshotStorage.ReadJsonAsync(connection, transaction, raw);
            if (DelphiLiveSnapshotCodec.Fingerprint(historicalFull) != DelphiLiveSnapshotCodec.Fingerprint(currentFull))
                throw new InvalidOperationException("Current portfolio differs from its immutable revision. Stop and investigate.");
            if (!DelphiLiveSnapshotCodec.IsCompact(raw))
                await ReplaceRow(connection, transaction, "DelphiLivePortfolioLedger", key.Id, key.Revision, history);
            await transaction.CommitAsync();
        }
        Console.WriteLine($"Converted {converted} historical rows; verified {keys.Count} revisions and {current.Count} current portfolios. No rows deleted.");
    }

    private static async Task RequireNoHost(SqlConnection connection)
    {
        await using var command = new SqlCommand("""
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 53721,'A Live host started during maintenance. Stop it before resuming.',1;
""", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadRow(SqlConnection c, SqlTransaction t, string table, Guid id, long revision)
    {
        await using var cmd = new SqlCommand($"SELECT SnapshotJson FROM dbo.{table} WITH(UPDLOCK,HOLDLOCK) WHERE PortfolioId=@Id AND Revision=@Revision;", c, t) { CommandTimeout = 180 };
        cmd.Parameters.AddWithValue("@Id", id); cmd.Parameters.AddWithValue("@Revision", revision);
        // Synchronous sequential streaming avoids SqlClient's costly async large-LOB scalar buffering.
        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
        if (!reader.Read()) throw new InvalidOperationException("An expected snapshot row is missing.");
        using var text = reader.GetTextReader(0);
        return await Task.FromResult(text.ReadToEnd());
    }
    private static async Task ReplaceRow(SqlConnection c, SqlTransaction t, string table, Guid id, long revision, string compact)
    {
        await using var cmd = new SqlCommand($"""
UPDATE dbo.{table} SET SnapshotJson=@Json WHERE PortfolioId=@Id AND Revision=@Revision
;
IF @@ROWCOUNT<>1 THROW 53724,'Snapshot changed during maintenance.',1;
""", c, t) { CommandTimeout = 180 };
        cmd.Parameters.AddWithValue("@Id", id); cmd.Parameters.AddWithValue("@Revision", revision);
        cmd.Parameters.Add("@Json", SqlDbType.NVarChar, -1).Value = compact;
        await cmd.ExecuteNonQueryAsync();
        string persisted = await ReadRow(c, t, table, id, revision);
        if (persisted != compact) throw new InvalidOperationException("Saved compact root differs from the verified root.");
        // ReadRow already held an update lock before the caller checked the legacy format.
        // Parts were fully reconstructed/verified in this transaction and are immutable; compare
        // the saved root exactly here, then independently verify all revisions after conversion.
    }
    private static void RequireIdentity(string json, Guid portfolio, long revision)
    {
        var root = JsonNode.Parse(json)!;
        if (root["portfolioId"]!.GetValue<Guid>() != portfolio || root["revision"]!.GetValue<long>() != revision)
            throw new InvalidOperationException("Snapshot identity differs from its SQL primary key.");
    }
}
