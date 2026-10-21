#nullable enable
using Core.Trader.DelphiLive;
using Core.Runtime;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Immutable item versions, selected by original portfolio revision; no future-state joins.</summary>
public static class DelphiLiveSnapshotStorage
{
    public const string Migration = "20260917_037_AddCompactDelphiLiveSnapshots.sql";
    private static readonly DelphiLiveSnapshotReadCache verifiedReads = new();

    public static Task<string> SaveAsync(SqlConnection connection, SqlTransaction transaction,
        DelphiLivePortfolioSnapshot snapshot, CancellationToken token = default) =>
        Task.Run(() => SaveJsonCoreAsync(connection, transaction, DelphiLiveLedgerJson.Serialize(snapshot), token), token);

    public static Task<string> SaveJsonAsync(SqlConnection connection, SqlTransaction transaction,
        string fullJson, CancellationToken token = default) =>
        Task.Run(() => SaveJsonCoreAsync(connection, transaction, fullJson, token), token);

    private static async Task<string> SaveJsonCoreAsync(SqlConnection connection, SqlTransaction transaction,
        string fullJson, CancellationToken token = default)
    {
        DelphiLiveSnapshotPackage package;
        using (PaperCycleTimings.Start(PaperCycleStage.SnapshotPack))
            package = DelphiLiveSnapshotCodec.PackJson(fullJson);
        var identity = JsonNode.Parse(package.SnapshotJson)!;
        await using var setup = new SqlCommand("""
DECLARE @lock int;
IF ISNULL(APPLOCK_MODE(N'public',N'TraderVI.LiveSnapshotStorage',N'Session'),N'NoLock')<>N'Exclusive'
BEGIN
 EXEC @lock=sys.sp_getapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @lock<0 THROW 53700,'Live snapshot maintenance is in progress.',1;
 IF EXISTS(SELECT 1 FROM dbo.DelphiLivePortfolioLedger WHERE PortfolioId=@Portfolio
  AND JSON_VALUE(SnapshotJson,'$."$storage".version') IS NULL)
  THROW 53709,'Complete reviewed historical conversion before writing this portfolio.',1;
END;
IF OBJECT_ID(N'dbo.DelphiLiveSnapshotContent',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLiveSnapshotItem',N'U') IS NULL
 THROW 53701,'Install reviewed migration 037 before writing Live snapshots.',1;
""", connection, transaction);
        setup.Parameters.AddWithValue("@Portfolio", identity["portfolioId"]!.GetValue<Guid>());
        await setup.ExecuteNonQueryAsync(token);
        // A parameterized command runs in sp_executesql scope: tables created there disappear
        // when it returns. This parameter-free batch keeps them alive for the following bulk copies.
        await using (var staging = new SqlCommand("""
CREATE TABLE #LiveContents(ContentHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,ContentJson nvarchar(max) NOT NULL);
CREATE TABLE #LiveItems(CollectionName nvarchar(32) COLLATE DATABASE_DEFAULT NOT NULL,Ordinal int NOT NULL,ContentHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,PRIMARY KEY(CollectionName,Ordinal));
""", connection, transaction))
            await staging.ExecuteNonQueryAsync(token);
        bool saved = false;
        try
        {
            using var contents = new DataTable(); contents.Columns.Add("ContentHash", typeof(string)); contents.Columns.Add("ContentJson", typeof(string));
            foreach (var content in package.Contents) contents.Rows.Add(content.Key, content.Value);
            using var items = new DataTable(); items.Columns.Add("CollectionName", typeof(string)); items.Columns.Add("Ordinal", typeof(int)); items.Columns.Add("ContentHash", typeof(string));
            foreach (var item in package.Items) items.Rows.Add(item.Collection, item.Ordinal, item.ContentHash);
            await Bulk(connection, transaction, "#LiveContents", contents, token);
            await Bulk(connection, transaction, "#LiveItems", items, token);
            await using var write = new SqlCommand("""
IF EXISTS(SELECT 1 FROM #LiveContents n JOIN dbo.DelphiLiveSnapshotContent c WITH(UPDLOCK,HOLDLOCK) ON c.ContentHash=n.ContentHash
 WHERE CONVERT(varbinary(max),c.ContentJson)<>CONVERT(varbinary(max),n.ContentJson))
 THROW 53702,'A snapshot content fingerprint conflicts with existing content.',1;
INSERT dbo.DelphiLiveSnapshotContent(ContentHash,ContentJson)
 SELECT n.ContentHash,n.ContentJson FROM #LiveContents n
 WHERE NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveSnapshotContent c WITH(UPDLOCK,HOLDLOCK) WHERE c.ContentHash=n.ContentHash);
IF EXISTS(SELECT 1 FROM #LiveItems n JOIN dbo.DelphiLiveSnapshotItem i WITH(UPDLOCK,HOLDLOCK)
 ON i.PortfolioId=@Portfolio AND i.CollectionName=n.CollectionName AND i.Ordinal=n.Ordinal AND i.Revision=@Revision
 WHERE i.ContentHash<>n.ContentHash)
 THROW 53703,'An immutable snapshot item version conflicts with this revision.',1;
INSERT dbo.DelphiLiveSnapshotItem(PortfolioId,CollectionName,Ordinal,Revision,ContentHash)
 SELECT @Portfolio,n.CollectionName,n.Ordinal,@Revision,n.ContentHash FROM #LiveItems n
 OUTER APPLY(SELECT TOP(1) i.ContentHash FROM dbo.DelphiLiveSnapshotItem i WITH(UPDLOCK,HOLDLOCK)
  WHERE i.PortfolioId=@Portfolio AND i.CollectionName=n.CollectionName AND i.Ordinal=n.Ordinal AND i.Revision<=@Revision
  ORDER BY i.Revision DESC) previous
 WHERE previous.ContentHash IS NULL OR previous.ContentHash<>n.ContentHash;
""", connection, transaction);
            write.Parameters.AddWithValue("@Portfolio", identity["portfolioId"]!.GetValue<Guid>());
            write.Parameters.AddWithValue("@Revision", identity["revision"]!.GetValue<long>());
            await write.ExecuteNonQueryAsync(token);
            // This also catches a missing part or an incorrect historical revision selection before commit.
            var verified = await ReadPartsAsync(connection, transaction, package.SnapshotJson, token);
            using (PaperCycleTimings.Start(PaperCycleStage.SnapshotVerify))
                DelphiLiveSnapshotCodec.Validate(package.SnapshotJson, verified.Items, verified.Contents);
            saved = true;
            return package.SnapshotJson;
        }
        finally
        {
            await using var cleanup = new SqlCommand("DROP TABLE IF EXISTS #LiveItems; DROP TABLE IF EXISTS #LiveContents;", connection, transaction);
            try { await cleanup.ExecuteNonQueryAsync(CancellationToken.None); }
            catch when (!saved) { /* Preserve the original failure; caller rolls back/disposes this connection. */ }
        }
    }

    // Callers await each operation before reusing its connection/transaction. A worker boundary
    // also protects display readers when SQL completes synchronously on the UI dispatcher.
    public static Task<DelphiLivePortfolioSnapshot> ReadAsync(SqlConnection connection, SqlTransaction? transaction,
        string json, CancellationToken token = default)
        => Task.Run(async () =>
        {
            if (!DelphiLiveSnapshotCodec.IsCompact(json))
                return DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(json);
            var saved = await ReadPartsAsync(connection, transaction, json, token);
            using var timing = PaperCycleTimings.Start(PaperCycleStage.SnapshotUnpack);
            return verifiedReads.Read(json, saved.Items, saved.Contents);
        }, token);

    public static Task<string> ReadJsonAsync(SqlConnection connection, SqlTransaction? transaction,
        string json, CancellationToken token = default) =>
        Task.Run(() => ReadJsonCoreAsync(connection, transaction, json, token), token);

    private static async Task<string> ReadJsonCoreAsync(SqlConnection connection, SqlTransaction? transaction,
        string json, CancellationToken token = default)
    {
        if (!DelphiLiveSnapshotCodec.IsCompact(json)) return json;
        var saved = await ReadPartsAsync(connection, transaction, json, token);
        return DelphiLiveSnapshotCodec.RestoreJson(json, saved.Items, saved.Contents);
    }

    private static async Task<(List<DelphiLiveSnapshotItem> Items, Dictionary<string, string> Contents)> ReadPartsAsync(
        SqlConnection connection, SqlTransaction? transaction, string json, CancellationToken token)
    {
        using var timing = PaperCycleTimings.Start(PaperCycleStage.SnapshotRead);
        var root = JsonNode.Parse(json)!.AsObject();
        var counts = DelphiLiveSnapshotCodec.ReadCounts(json);
        await using var read = new SqlCommand("""
WITH selected AS
(
 SELECT i.CollectionName,i.Ordinal,i.ContentHash,
 ROW_NUMBER() OVER(PARTITION BY i.CollectionName,i.Ordinal ORDER BY i.Revision DESC) AS rn
 FROM dbo.DelphiLiveSnapshotItem i
 WHERE i.PortfolioId=@Portfolio AND i.Revision<=@Revision
 AND i.Ordinal<CASE i.CollectionName WHEN N'sessionOpeningPositions' THEN @Opening WHEN N'positions' THEN @Positions
 WHEN N'actions' THEN @Actions WHEN N'fills' THEN @Fills WHEN N'quotes' THEN @Quotes WHEN N'marks' THEN @Marks ELSE 0 END
)
SELECT i.CollectionName,i.Ordinal,i.ContentHash,c.ContentJson
FROM selected i JOIN dbo.DelphiLiveSnapshotContent c ON c.ContentHash=i.ContentHash
WHERE i.rn=1 ORDER BY i.CollectionName,i.Ordinal;
""", connection, transaction);
        read.Parameters.AddWithValue("@Portfolio", root["portfolioId"]!.GetValue<Guid>());
        read.Parameters.AddWithValue("@Revision", root["revision"]!.GetValue<long>());
        string[] parameters = ["@Opening", "@Positions", "@Actions", "@Fills", "@Quotes", "@Marks"];
        for (int i = 0; i < parameters.Length; i++) read.Parameters.AddWithValue(parameters[i], counts[DelphiLiveSnapshotCodec.Collections[i]]);
        var items = new List<DelphiLiveSnapshotItem>();
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        var reports = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await read.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token))
        {
            while (await reader.ReadAsync(token))
            {
                string collection = reader.GetString(0); int ordinal = reader.GetInt32(1);
                string hash = reader.GetString(2), content = reader.GetString(3);
                items.Add(new(collection, ordinal, hash));
                contents[hash] = content;
                using var document = JsonDocument.Parse(content);
                foreach (string field in DelphiLiveSnapshotCodec.DossierFields)
                    if (document.RootElement.TryGetProperty(field, out var report) && report.ValueKind == JsonValueKind.Object
                        && report.TryGetProperty("contentHash", out var link) && link.ValueKind == JsonValueKind.String)
                        reports.Add(link.GetString()!);
            }
        }
        // Fetch each report once, instead of repeating its large text for every quote in the result.
        if (reports.Count > 0)
        {
            await using var reportRead = new SqlCommand("""
SELECT c.ContentHash,c.ContentJson FROM dbo.DelphiLiveSnapshotContent c
JOIN OPENJSON(@Hashes) h ON c.ContentHash=CONVERT(char(64),h.value) COLLATE Latin1_General_100_BIN2;
""", connection, transaction);
            reportRead.Parameters.Add("@Hashes", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(reports);
            await using var reader = await reportRead.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token);
            while (await reader.ReadAsync(token)) contents[reader.GetString(0)] = reader.GetString(1);
        }
        return (items, contents);
    }

    private static async Task Bulk(SqlConnection connection, SqlTransaction transaction, string table, DataTable data, CancellationToken token)
    {
        if (data.Rows.Count == 0) return;
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction) { DestinationTableName = table };
        foreach (DataColumn column in data.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        await bulk.WriteToServerAsync(data, token);
    }
}
