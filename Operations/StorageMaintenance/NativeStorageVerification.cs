using Core.Db;
using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

internal static class NativeStorageVerification
{
    // Explicit maintenance check only. Real historical inputs exercise the actual bulk-write/read SQL;
    // every inserted content/item row is rolled back and portfolio/revision rows are never updated.
    public static async Task RunAsync()
    {
        await using var c = new SqlConnection(SQLBase.Database);
        await c.OpenAsync();
        await using (var guard = new SqlCommand("""
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 53721,'Stop all Live hosts before native storage verification.',1;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
IF @lock<0 THROW 53723,'Another storage writer prevents verification.',1;
""", c)) await guard.ExecuteNonQueryAsync();
        var before = await Counts(c);
        var samples = new List<(Guid Id, long Revision)>();
        await using (var read = new SqlCommand("""
SELECT PortfolioId,Revision FROM dbo.DelphiLivePortfolioLedger ORDER BY PortfolioId;
""", c))
        {
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                Guid id = reader.GetGuid(0); long last = reader.GetInt64(1);
                foreach (long revision in new[] { 0L, last / 2, last }.Distinct()) samples.Add((id, revision));
            }
        }
        var saved = new List<(string Compact, string FullHash)>();
        await using (var transaction = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            foreach (var sample in samples)
            {
                string raw;
                await using (var read = new SqlCommand("SELECT SnapshotJson FROM dbo.DelphiLivePortfolioRevision WHERE PortfolioId=@Id AND Revision=@Revision", c, transaction))
                {
                    read.Parameters.AddWithValue("@Id", sample.Id); read.Parameters.AddWithValue("@Revision", sample.Revision);
                    using var reader = read.ExecuteReader(CommandBehavior.SequentialAccess);
                    if (!reader.Read()) throw new InvalidOperationException("A native verification sample is missing.");
                    using var text = reader.GetTextReader(0); raw = text.ReadToEnd();
                }
                string full = await DelphiLiveSnapshotStorage.ReadJsonAsync(c, transaction, raw);
                string compact = await DelphiLiveSnapshotStorage.SaveJsonAsync(c, transaction, full);
                saved.Add((compact, DelphiLiveSnapshotCodec.Fingerprint(full)));
            }
            // Read older snapshots again after later versions exist, proving temporal isolation.
            foreach (var sample in saved)
            {
                string restored = await DelphiLiveSnapshotStorage.ReadJsonAsync(c, transaction, sample.Compact);
                if (DelphiLiveSnapshotCodec.Fingerprint(restored) != sample.FullHash)
                    throw new InvalidOperationException("Native historical reconstruction changed after writing a later revision.");
                _ = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(restored);
            }
            await transaction.RollbackAsync();
        }
        var after = await Counts(c);
        if (before != after) throw new InvalidOperationException("Native storage verification left persistent rows.");
        Console.WriteLine(JsonSerializer.Serialize(new { Passed = true, SnapshotsVerified = saved.Count,
            LaterVersionsDidNotChangeEarlierSnapshots = true, TransactionRolledBack = true, PersistentRowsChanged = 0 }));
    }

    private static async Task<(long Contents, long Items)> Counts(SqlConnection c)
    {
        await using var query = new SqlCommand("SELECT (SELECT COUNT_BIG(*) FROM dbo.DelphiLiveSnapshotContent),(SELECT COUNT_BIG(*) FROM dbo.DelphiLiveSnapshotItem);", c);
        await using var reader = await query.ExecuteReaderAsync(); await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }
}
