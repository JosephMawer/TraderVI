using Core.Db;
using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using System.Text.Json;

// No model, collection or trading runtime is constructed by this tool.
// Preview is read-only. Conversion is a separately approved maintenance operation, never a test.
if (args.Length == 1 && args[0] == "verify-storage")
{
    await NativeStorageVerification.RunAsync();
    return 0;
}
if (args.Length == 1 && args[0] == "backup")
{
    var service = new TraderDbBackupService(SQLBase.Database, TraderDbBackupPaths.FromEnvironment());
    var receipt = await service.CreateAndReplicateAsync(message => Console.Error.WriteLine(message));
    Console.WriteLine(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
if (args.Length == 3 && args[0] == "retention-preview")
{
    var completed = await TraderDbBackupService.ReadCompletedBackupNamesAsync(SQLBase.Database);
    var files = TraderDbBackupRetention.Preview(args[1], args[2], new FileInfo(args[1]).Length, completed);
    Console.WriteLine(JsonSerializer.Serialize(new { DatabaseChanged = false, FilesDeleted = 0,
        KeepPerLocation = TraderDbBackupRetention.BackupsToKeep, ProposedRemovalCount = files.Count,
        ProposedRemovalBytes = files.Sum(x => new FileInfo(x).Length) }));
    return 0;
}
if (args.Length == 3 && args[0] == "convert" && args[1] == "--verified-backup")
{
    await SnapshotConversion.RunAsync(args[2]);
    return 0;
}
bool verifyAll = args.Length == 1 && args[0] == "verify-all";
if (!verifyAll && (args.Length == 0 || args[0] != "preview"))
{
    Console.Error.WriteLine("Usage: StorageMaintenance preview [--all] or verify-all (read-only). After separate approval: convert --verified-backup <path>.");
    return 2;
}
bool all = verifyAll || (args.Length == 2 && args[1] == "--all");
if (args.Length > 2 || (args.Length == 2 && !all)) return 2;
await using var connection = new SqlConnection(SQLBase.Database);
await connection.OpenAsync();
var identities = new List<(Guid Portfolio, long Revision, string Source)>();
await using (var command = new SqlCommand(all ? """
SELECT PortfolioId,Revision,N'History' FROM dbo.DelphiLivePortfolioRevision ORDER BY PortfolioId,Revision;
""" : """
WITH boundaries AS
(
 SELECT PortfolioId,MIN(Revision) FirstRevision,MAX(Revision) LastRevision
 FROM dbo.DelphiLivePortfolioRevision GROUP BY PortfolioId,CONVERT(date,PersistedUtc)
)
SELECT PortfolioId,FirstRevision,N'History' FROM boundaries
UNION SELECT PortfolioId,LastRevision,N'History' FROM boundaries
ORDER BY PortfolioId,FirstRevision;
""", connection))
{
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) identities.Add((reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2)));
}
await using (var command = new SqlCommand("SELECT PortfolioId,Revision FROM dbo.DelphiLivePortfolioLedger ORDER BY PortfolioId;", connection))
{
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) identities.Add((reader.GetGuid(0), reader.GetInt64(1), "Current"));
}
if (verifyAll)
{
    await CompactSnapshotVerification.RunAsync(connection, identities);
    return 0;
}
long fullBytes = 0, rootBytes = 0, uniqueContentBytes = 0, versionCount = 0;
var contentHashes = new HashSet<string>(StringComparer.Ordinal);
var previous = new Dictionary<(Guid, string, int), string>();
var evidence = new List<object>();
foreach (var identity in identities)
{
    string table = identity.Source == "Current" ? "DelphiLivePortfolioLedger" : "DelphiLivePortfolioRevision";
    await using var read = new SqlCommand($"SELECT SnapshotJson FROM dbo.{table} WHERE PortfolioId=@Id AND Revision=@Revision;", connection) { CommandTimeout = 180 };
    read.Parameters.AddWithValue("@Id", identity.Portfolio);
    read.Parameters.AddWithValue("@Revision", identity.Revision);
    string json;
    using (var reader = read.ExecuteReader(CommandBehavior.SequentialAccess))
    {
        if (!reader.Read()) throw new InvalidOperationException("Snapshot changed during preview; rerun with writers stopped.");
        using var text = reader.GetTextReader(0);
        json = text.ReadToEnd();
    }
    string full = await DelphiLiveSnapshotStorage.ReadJsonAsync(connection, null, json);
    var package = DelphiLiveSnapshotCodec.PackJson(full);
    string reconstructed = DelphiLiveSnapshotCodec.RestoreJson(package.SnapshotJson, package.Items, package.Contents);
    if (DelphiLiveSnapshotCodec.Fingerprint(reconstructed) != DelphiLiveSnapshotCodec.Fingerprint(full))
        throw new InvalidOperationException("Snapshot round-trip failed.");
    // Verify domain readers as well as raw historical JSON (which can omit older optional fields).
    _ = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(reconstructed);
    fullBytes += Encoding.Unicode.GetByteCount(full);
    rootBytes += Encoding.Unicode.GetByteCount(package.SnapshotJson);
    foreach (var part in package.Contents)
        if (contentHashes.Add(part.Key)) uniqueContentBytes += Encoding.Unicode.GetByteCount(part.Value);
    foreach (var item in package.Items)
    {
        var key = (identity.Portfolio, item.Collection, item.Ordinal);
        if (!previous.TryGetValue(key, out string? hash) || hash != item.ContentHash) versionCount++;
        previous[key] = item.ContentHash;
    }
    evidence.Add(new { identity.Source, identity.Portfolio, identity.Revision, package.FullHash,
        OriginalBytes = Encoding.Unicode.GetByteCount(full), CompactRootBytes = Encoding.Unicode.GetByteCount(package.SnapshotJson), Items = package.Items.Count });
    Console.Error.WriteLine($"Verified snapshot {evidence.Count}/{identities.Count}");
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    CheckedUtc = DateTime.UtcNow, DatabaseChanged = false, Scope = all ? "All revisions plus current" : "First/last revision per day plus current",
    Snapshots = evidence.Count, OriginalJsonBytes = fullBytes, CompactRootJsonBytes = rootBytes,
    UniqueContentJsonBytes = uniqueContentBytes, ItemVersions = versionCount,
    Note = "JSON payload sizes exclude SQL indexes, row overhead and pages. Sample estimates are not a full-database size forecast.",
    Evidence = evidence
}, new JsonSerializerOptions { WriteIndented = true }));
return 0;
