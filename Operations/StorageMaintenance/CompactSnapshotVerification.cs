using Core.Db;
using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using System.Text.Json;

internal static class CompactSnapshotVerification
{
    public static async Task RunAsync(SqlConnection guardConnection,
        IReadOnlyList<(Guid Portfolio, long Revision, string Source)> identities)
    {
        await using (var guard = new SqlCommand("""
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 53721,'Stop all Live hosts before final verification.',1;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
IF @lock<0 THROW 53723,'Another storage writer prevents final verification.',1;
""", guardConnection)) await guard.ExecuteNonQueryAsync();

        // Independent immutable revisions can be checked concurrently. Keep two workers to bound
        // memory when full reconstructed histories contain tens of megabytes of repeated reports.
        var evidence = new object[identities.Count];
        int next = -1, completed = 0;
        long fullBytes = 0, rootBytes = 0;
        using var stop = new CancellationTokenSource();
        async Task Worker()
        {
            try
            {
                await using var connection = new SqlConnection(SQLBase.Database);
                await connection.OpenAsync(stop.Token);
                while (Interlocked.Increment(ref next) is int index && index < identities.Count)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    var identity = identities[index];
                    string table = identity.Source == "Current" ? "DelphiLivePortfolioLedger" : "DelphiLivePortfolioRevision";
                    await using var read = new SqlCommand($"SELECT SnapshotJson FROM dbo.{table} WHERE PortfolioId=@Id AND Revision=@Revision;", connection) { CommandTimeout = 180 };
                    read.Parameters.AddWithValue("@Id", identity.Portfolio);
                    read.Parameters.AddWithValue("@Revision", identity.Revision);
                    string json;
                    using (var reader = read.ExecuteReader(CommandBehavior.SequentialAccess))
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Expected snapshot is missing or changed during verification.");
                        using var text = reader.GetTextReader(0); json = text.ReadToEnd();
                    }
                    if (!DelphiLiveSnapshotCodec.IsCompact(json)) throw new InvalidOperationException("Unconverted snapshot remains.");
                    // ReadJsonAsync validates every referenced content hash and the complete
                    // reconstructed fingerprint. Then exercise the actual domain decoder too.
                    string full = await DelphiLiveSnapshotStorage.ReadJsonAsync(connection, null, json, stop.Token);
                    _ = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(full);
                    using var root = JsonDocument.Parse(json);
                    if (root.RootElement.GetProperty("portfolioId").GetGuid() != identity.Portfolio
                        || root.RootElement.GetProperty("revision").GetInt64() != identity.Revision)
                        throw new InvalidOperationException("Saved snapshot identity differs from its SQL row.");
                    string fullHash = root.RootElement.GetProperty("$storage").GetProperty("fullHash").GetString()!;
                    long originalBytes = Encoding.Unicode.GetByteCount(full), compactBytes = Encoding.Unicode.GetByteCount(json);
                    Interlocked.Add(ref fullBytes, originalBytes); Interlocked.Add(ref rootBytes, compactBytes);
                    evidence[index] = new { identity.Source, identity.Portfolio, identity.Revision, FullHash = fullHash,
                        OriginalBytes = originalBytes, CompactRootBytes = compactBytes,
                        Items = DelphiLiveSnapshotCodec.ReadCounts(json).Values.Sum() };
                    int count = Interlocked.Increment(ref completed);
                    if (count % 25 == 0 || count == identities.Count)
                        Console.Error.WriteLine($"Verified snapshot {count}/{identities.Count}");
                }
            }
            catch { await stop.CancelAsync(); throw; }
        }
        await Task.WhenAll(Worker(), Worker());
        if (completed != identities.Count || evidence.Any(x => x is null))
            throw new InvalidOperationException("Final verification did not cover every snapshot.");
        await using var totals = new SqlCommand("""
SELECT (SELECT COALESCE(SUM(CONVERT(bigint,DATALENGTH(ContentJson))),0) FROM dbo.DelphiLiveSnapshotContent),
       (SELECT COUNT_BIG(*) FROM dbo.DelphiLiveSnapshotItem);
""", guardConnection);
        await using var totalReader = await totals.ExecuteReaderAsync(); await totalReader.ReadAsync();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            CheckedUtc = DateTime.UtcNow, DatabaseChanged = false, Scope = "All revisions plus current",
            Snapshots = completed, OriginalJsonBytes = fullBytes, CompactRootJsonBytes = rootBytes,
            UniqueContentJsonBytes = totalReader.GetInt64(0), ItemVersions = totalReader.GetInt64(1),
            Note = "Every snapshot was reconstructed, fingerprint-checked and domain-decoded. Payload sizes exclude SQL overhead.",
            Evidence = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
