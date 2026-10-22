#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Current paper records only. Archive hydration is reserved for explicit qualification and history readers.</summary>
public sealed class EnginePaperWorkingStore : SQLBase
{
    internal sealed record Loaded(DelphiLiveWorkingCheckpoint Checkpoint, DelphiLivePortfolioSnapshot Snapshot,
        IReadOnlyDictionary<string, string> Contents, string StateJson);
    internal static Task<bool> InstalledAsync(SqlConnection c, SqlTransaction? t, CancellationToken ct) =>
        c.QuerySingleAsync<bool>(new CommandDefinition("SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.EnginePaperRecordIdentity',N'U') IS NULL THEN 0 ELSE 1 END AS bit);",
            transaction: t, cancellationToken: ct));

    internal static async Task<Loaded> ReadAsync(SqlConnection c, SqlTransaction? t, string root, CancellationToken ct)
    {
        var header = JsonNode.Parse(root)!;
        Guid owner = header["portfolioId"]!.GetValue<Guid>(); long revision = header["revision"]!.GetValue<long>();
        var row = await c.QuerySingleOrDefaultAsync<StateRow>(new CommandDefinition("""
SELECT Revision,SourceHash,StateHash,StateJson FROM dbo.EngineCurrentState
WHERE Kind=N'PaperWorking' AND OwnerId=@Owner;
""", new { Owner = owner }, t, cancellationToken: ct));
        if (row is null || row.Revision != revision || row.SourceHash != DelphiLiveSnapshotCodec.Hash(root) ||
            row.StateHash != DelphiLiveSnapshotCodec.Hash(row.StateJson))
            throw new InvalidDataException("Current paper checkpoint is missing, stale or damaged; explicit qualification is required.");
        var checkpoint = DelphiLiveLedgerJson.Deserialize<DelphiLiveWorkingCheckpoint>(row.StateJson);
        var contents = await ReadContentsAsync(c, t, checkpoint.Items.Select(i => i.ContentHash), ct);
        var reports = new HashSet<string>(StringComparer.Ordinal);
        if (checkpoint.CandidateStateHash is string checkpointCandidateHash) reports.Add(checkpointCandidateHash);
        if (DelphiLiveSnapshotCodec.CandidateStateReference(root) is string candidateHash) reports.Add(candidateHash);
        foreach (var item in contents.Values)
        {
            var node = JsonNode.Parse(item)!;
            foreach (string field in DelphiLiveSnapshotCodec.DossierFields)
                if (node[field] is JsonObject link && link["contentHash"] is JsonValue hash) reports.Add(hash.GetValue<string>());
        }
        foreach (var pair in await ReadContentsAsync(c, t, reports, ct)) contents[pair.Key] = pair.Value;
        var snapshot = checkpoint.Restore(root, contents);
        if (snapshot.PortfolioId != owner || snapshot.Revision != revision) throw new InvalidDataException("Paper checkpoint identity mismatch.");
        return new(checkpoint, snapshot, contents, row.StateJson);
    }

    internal static async Task<DelphiLiveWorkingCheckpoint.Prepared> PrepareAsync(SqlConnection c, Loaded prior,
        DelphiLivePortfolioSnapshot next, CancellationToken ct)
    {
        var nodes = new Dictionary<string, string?>(StringComparer.Ordinal);
        return await prior.Checkpoint.AdvanceAsync(prior.Snapshot, next, prior.Contents, async (hash, token) =>
        {
            if (nodes.TryGetValue(hash, out var saved)) return saved;
            string? value = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT ContentJson FROM dbo.DelphiLiveSnapshotContent WHERE ContentHash=@Hash;", new { Hash = hash }, cancellationToken: token));
            nodes[hash] = value; return value;
        }, ct);
    }

    internal static async Task SavePreparedAsync(SqlConnection c, SqlTransaction t, DelphiLiveWorkingCheckpoint.Prepared next,
        CancellationToken ct)
    {
        await SaveIdentitiesAsync(c, t, next, qualification: false, ct);
        var prepared = new DelphiLiveSnapshotStorage.PreparedSnapshot(next.Archive,
            new(next.ChangedItems, next.NewContents), DelphiLiveObservationPortfolio.From(next.Snapshot), CurrentStateManaged: true);
        await DelphiLiveSnapshotStorage.SavePreparedAsync(c, t, prepared, ct);
        await SaveStateAsync(c, t, next.Checkpoint, next.Archive.SnapshotJson, next.Snapshot, ct);
    }

    internal static async Task SaveFullIfInstalledAsync(SqlConnection c, SqlTransaction t, DelphiLiveSnapshotPackage package, CancellationToken ct)
    {
        if (!await InstalledAsync(c, t, ct)) return;
        // Rare administrative/new-generation writers already hold the complete verified snapshot.
        var full = DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, package.Items, package.Contents);
        var qualified = DelphiLiveWorkingCheckpoint.Qualify(full, package);
        await SaveQualificationAsync(c, t, qualified, package.SnapshotJson, ct);
    }

    private static async Task SaveQualificationAsync(SqlConnection c, SqlTransaction t,
        DelphiLiveWorkingCheckpoint.Prepared qualified, string originalRoot, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition("""
DECLARE @Nodes TABLE(ContentHash char(64) COLLATE Latin1_General_100_BIN2 PRIMARY KEY,ContentJson nvarchar(max));
INSERT @Nodes SELECT ContentHash,ContentJson FROM OPENJSON(@NodesJson)
 WITH(ContentHash char(64) '$.ContentHash',ContentJson nvarchar(max) '$.ContentJson');
IF EXISTS(SELECT 1 FROM @Nodes n JOIN dbo.DelphiLiveSnapshotContent c WITH(UPDLOCK,HOLDLOCK) ON c.ContentHash=n.ContentHash
 WHERE CONVERT(varbinary(max),c.ContentJson)<>CONVERT(varbinary(max),n.ContentJson))
 THROW 54461,'An archive tree node conflicts with its original content.',1;
INSERT dbo.DelphiLiveSnapshotContent(ContentHash,ContentJson)
 SELECT n.ContentHash,n.ContentJson FROM @Nodes n WHERE NOT EXISTS
 (SELECT 1 FROM dbo.DelphiLiveSnapshotContent c WITH(UPDLOCK,HOLDLOCK) WHERE c.ContentHash=n.ContentHash);
IF EXISTS(SELECT 1 FROM @Nodes n LEFT JOIN dbo.DelphiLiveSnapshotContent c ON c.ContentHash=n.ContentHash
 WHERE c.ContentHash IS NULL OR CONVERT(varbinary(max),c.ContentJson)<>CONVERT(varbinary(max),n.ContentJson))
 THROW 54462,'Archive tree qualification read-back failed.',1;
""", new { NodesJson = EngineEvidenceStorage.Serialize(qualified.NewContents) }, t, cancellationToken: ct));
        await SaveIdentitiesAsync(c, t, qualified, qualification: true, ct);
        await EnginePaperObservationStore.SaveIfInstalledAsync(c, t, qualified.Archive, ct,
            DelphiLiveObservationPortfolio.From(qualified.Snapshot));
        await SaveStateAsync(c, t, qualified.Checkpoint, originalRoot, qualified.Snapshot, ct);
    }

    private static async Task SaveStateAsync(SqlConnection c, SqlTransaction t, DelphiLiveWorkingCheckpoint checkpoint,
        string archiveRoot, DelphiLivePortfolioSnapshot snapshot, CancellationToken ct)
    {
        string json = DelphiLiveSnapshotCodec.CanonicalJson(DelphiLiveLedgerJson.Serialize(checkpoint with
            { SnapshotRoot = DelphiLiveSnapshotCodec.CanonicalJson(checkpoint.SnapshotRoot) }));
        await c.ExecuteAsync(new CommandDefinition("""
IF EXISTS(SELECT 1 FROM dbo.EngineCurrentState WITH(UPDLOCK,HOLDLOCK) WHERE Kind=N'PaperWorking' AND OwnerId=@Owner)
BEGIN
 IF EXISTS(SELECT 1 FROM dbo.EngineCurrentState WHERE Kind=N'PaperWorking' AND OwnerId=@Owner
  AND (Revision>@Revision OR Revision=@Revision AND (StateHash<>@Hash OR SourceHash<>@Source)))
  THROW 54463,'Current paper checkpoint conflicts with an existing revision.',1;
 UPDATE dbo.EngineCurrentState SET Revision=@Revision,SourceHash=@Source,StateHash=@Hash,StateJson=@Json,UpdatedUtc=SYSUTCDATETIME()
 WHERE Kind=N'PaperWorking' AND OwnerId=@Owner;
END
ELSE INSERT dbo.EngineCurrentState(Kind,OwnerId,Revision,SourceHash,StateHash,StateJson)
 VALUES(N'PaperWorking',@Owner,@Revision,@Source,@Hash,@Json);
IF NOT EXISTS(SELECT 1 FROM dbo.EngineCurrentState WHERE Kind=N'PaperWorking' AND OwnerId=@Owner AND Revision=@Revision
 AND SourceHash=@Source AND StateHash=@Hash AND CONVERT(varbinary(max),StateJson)=CONVERT(varbinary(max),@Json))
 THROW 54464,'Current paper checkpoint read-back failed.',1;
""", new { Owner = snapshot.PortfolioId, snapshot.Revision, Source = DelphiLiveSnapshotCodec.Hash(archiveRoot),
            Hash = DelphiLiveSnapshotCodec.Hash(json), Json = json }, t, cancellationToken: ct));
    }

    private static List<Identity> Identities(DelphiLiveWorkingCheckpoint.Prepared next)
    {
        var identities = new List<Identity>();
        foreach (var item in next.NewIdentities)
        {
            string json = next.Archive.Contents[item.ContentHash];
            identities.Add(new(item.Collection, DelphiLiveWorkingCheckpoint.RecordId(item.Collection, json), item.Ordinal));
            var node = JsonNode.Parse(json)!;
            if (item.Collection == "fills") identities.Add(new("fillAction", node["actionId"]!.GetValue<Guid>(), item.Ordinal));
            if (item.Collection == "actions" && node["intent"]!["side"]!.GetValue<int>() == (int)DelphiLiveActionSide.Buy && node["evaluationId"] is JsonValue id)
                identities.Add(new("buyEvaluation", id.GetValue<Guid>(), item.Ordinal));
        }
        return identities;
    }
    private static async Task SaveIdentitiesAsync(SqlConnection c, SqlTransaction t, DelphiLiveWorkingCheckpoint.Prepared next,
        bool qualification, CancellationToken ct)
    {
        var identities = Identities(next);
        if (identities.Count == 0) return;
        await c.ExecuteAsync(new CommandDefinition("""
DECLARE @Ids TABLE(Kind nvarchar(32),RecordId uniqueidentifier,Ordinal int,PRIMARY KEY(Kind,RecordId),UNIQUE(Kind,Ordinal));
INSERT @Ids SELECT Kind,RecordId,Ordinal FROM OPENJSON(@IdsJson)
 WITH(Kind nvarchar(32) '$.Kind',RecordId uniqueidentifier '$.RecordId',Ordinal int '$.Ordinal');
IF EXISTS(SELECT 1 FROM @Ids n JOIN dbo.EnginePaperRecordIdentity i WITH(UPDLOCK,HOLDLOCK)
 ON i.PortfolioId=@Owner AND i.Kind=n.Kind AND i.RecordId=n.RecordId
 WHERE @Qualification=0 OR i.Ordinal<>n.Ordinal)
 OR EXISTS(SELECT 1 FROM @Ids n JOIN dbo.EnginePaperRecordIdentity i WITH(UPDLOCK,HOLDLOCK)
 ON i.PortfolioId=@Owner AND i.Kind=n.Kind AND i.Ordinal=n.Ordinal
 WHERE @Qualification=0 OR i.RecordId<>n.RecordId)
 THROW 54465,'A paper record, fill action or evaluation was already archived; replay is blocked.',1;
INSERT dbo.EnginePaperRecordIdentity(PortfolioId,Kind,RecordId,Ordinal,CreatedRevision)
 SELECT @Owner,n.Kind,n.RecordId,n.Ordinal,@Revision FROM @Ids n WHERE NOT EXISTS
 (SELECT 1 FROM dbo.EnginePaperRecordIdentity i WITH(UPDLOCK,HOLDLOCK) WHERE i.PortfolioId=@Owner AND i.Kind=n.Kind AND i.RecordId=n.RecordId);
IF EXISTS(SELECT 1 FROM @Ids n LEFT JOIN dbo.EnginePaperRecordIdentity i
 ON i.PortfolioId=@Owner AND i.Kind=n.Kind AND i.RecordId=n.RecordId AND i.Ordinal=n.Ordinal WHERE i.RecordId IS NULL)
 THROW 54466,'Paper record identity read-back failed.',1;
""", new { Owner = next.Snapshot.PortfolioId, next.Snapshot.Revision, Qualification = qualification, IdsJson = JsonSerializer.Serialize(identities) }, t, cancellationToken: ct));
    }

    internal static async Task RequireWriteAccessAsync(SqlConnection c, SqlTransaction t, DelphiLivePortfolioSnapshot prior,
        DelphiLivePortfolioSnapshot next, CancellationToken ct)
    {
        if (!await EngineIntegrityRepository.InstalledAsync(c, t, ct) || !await EngineIntegrityRepository.RequiredGuardsAsync(c, t, ct))
            throw new InvalidOperationException("Independent paper integrity guards are unavailable.");
        var access = EngineIntegrityAccess.Evaluate(await EngineIntegrityRepository.ReadOpenAsync(c, t, ct), EngineIntegrityScope.Paper, next.PortfolioId);
        if (!access.ProtectionAllowed) throw new InvalidOperationException("The paper state is held for integrity recovery.");
        if (!access.NewExposureAllowed && (next.Fills.Skip(prior.Fills.Length).Any(f => f.Side == DelphiLiveActionSide.Buy) ||
            next.Actions.Skip(prior.Actions.Length).Any(a => a.Intent.Side == DelphiLiveActionSide.Buy)))
            throw new InvalidOperationException("An integrity hold blocks the paper Buy before its atomic append.");
    }

    internal static async Task FenceAsync(SqlConnection c, SqlTransaction t, Loaded source, CancellationToken ct)
    {
        string? actual = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
SELECT StateJson FROM dbo.EngineCurrentState WITH(UPDLOCK,HOLDLOCK)
WHERE Kind=N'PaperWorking' AND OwnerId=@Owner AND Revision=@Revision;
""", new { Owner = source.Snapshot.PortfolioId, source.Snapshot.Revision }, t, cancellationToken: ct));
        if (actual != source.StateJson) throw new InvalidOperationException("Current paper checkpoint changed while preparing its append.");
    }

    internal static async Task VerifyCurrentAsync(SqlConnection c, Guid owner, long revision, CancellationToken ct)
    {
        string root = await c.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT SnapshotJson FROM dbo.DelphiLivePortfolioLedger WHERE PortfolioId=@Owner AND Revision=@Revision;",
            new { Owner = owner, Revision = revision }, cancellationToken: ct));
        var verified = await DelphiLiveSnapshotStorage.ReadVerifiedAsync(c, root, ct, fresh: true);
        var package = new DelphiLiveSnapshotPackage(root, DelphiLiveSnapshotCodec.Hash(root), verified.Contents, verified.Items);
        var reconstructed = DelphiLiveWorkingCheckpoint.Qualify(verified.Snapshot, package);
        var current = await ReadAsync(c, null, root, ct);
        current.Checkpoint.RequireSameReconstruction(reconstructed.Checkpoint);
        var nodes = await ReadContentsAsync(c, null, reconstructed.NewContents.Keys, ct);
        if (reconstructed.NewContents.Any(p => nodes[p.Key] != p.Value)) throw new InvalidDataException("Current archive paths differ from reconstructed history.");
        var identities = (await c.QueryAsync<Identity>(new CommandDefinition(
            "SELECT Kind,RecordId,Ordinal FROM dbo.EnginePaperRecordIdentity WHERE PortfolioId=@Owner;",
            new { Owner = owner }, cancellationToken: ct))).OrderBy(i => i.Kind, StringComparer.Ordinal).ThenBy(i => i.Ordinal);
        if (!identities.SequenceEqual(Identities(reconstructed).OrderBy(i => i.Kind, StringComparer.Ordinal).ThenBy(i => i.Ordinal)))
            throw new InvalidDataException("Current replay identities differ from original archived records.");
        var observation = await c.QuerySingleAsync<StateRow>(new CommandDefinition(
            "SELECT Revision,SourceHash,StateHash,StateJson FROM dbo.EngineCurrentState WHERE Kind=N'PaperObservation' AND OwnerId=@Owner;",
            new { Owner = owner }, cancellationToken: ct));
        string expectedObservation = DelphiLiveLedgerJson.Serialize(DelphiLiveObservationPortfolio.From(verified.Snapshot));
        if (observation.Revision != revision || observation.SourceHash != DelphiLiveSnapshotCodec.Hash(root) ||
            observation.StateHash != DelphiLiveSnapshotCodec.Hash(observation.StateJson) ||
            DelphiLiveSnapshotCodec.Fingerprint(observation.StateJson) != DelphiLiveSnapshotCodec.Fingerprint(expectedObservation))
            throw new InvalidDataException("Paper observation membership differs from its reconstructed revision.");
    }

    public async Task<int> QualifyExistingAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(ct);
        try
        {
            await c.ExecuteAsync(new CommandDefinition("""
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'DelphiLiveCollectionV1',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
IF @lock<0 THROW 54467,'Stop collection before qualifying paper working state.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 54468,'Stop the Live host before qualifying paper working state.',1;
EXEC @lock=sys.sp_getapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
IF @lock<0 THROW 54469,'Another snapshot writer prevents working-state qualification.',1;
""", cancellationToken: ct));
            if (!await InstalledAsync(c, null, ct)) throw new InvalidOperationException("Install reviewed migration 051 before qualification.");
            var roots = await c.QueryAsync<string>(new CommandDefinition("""
SELECT SnapshotJson FROM dbo.DelphiLivePortfolioLedger ORDER BY PortfolioId;
""", cancellationToken: ct));
            int count = 0;
            foreach (string root in roots)
            {
                var verified = await DelphiLiveSnapshotStorage.ReadVerifiedAsync(c, root, ct);
                var package = new DelphiLiveSnapshotPackage(root, DelphiLiveSnapshotCodec.Hash(root), verified.Contents, verified.Items);
                var prepared = DelphiLiveWorkingCheckpoint.Qualify(verified.Snapshot, package);
                using var t = c.BeginTransaction(IsolationLevel.Serializable);
                string actual = await c.QuerySingleAsync<string>(new CommandDefinition(
                    "SELECT SnapshotJson FROM dbo.DelphiLivePortfolioLedger WITH(UPDLOCK,HOLDLOCK) WHERE PortfolioId=@Id;",
                    new { Id = verified.Snapshot.PortfolioId }, t, cancellationToken: ct));
                if (actual != root) throw new InvalidDataException("Paper history changed during offline qualification.");
                await SaveQualificationAsync(c, t, prepared, root, ct);
                t.Commit(); count++;
            }
            return count;
        }
        finally
        {
            try { await c.ExecuteAsync("EXEC sys.sp_releaseapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockOwner=N'Session'; EXEC sys.sp_releaseapplock @Resource=N'DelphiLiveCollectionV1',@LockOwner=N'Session';"); }
            catch { SqlConnection.ClearPool(c); }
        }
    }

    private static async Task<Dictionary<string, string>> ReadContentsAsync(SqlConnection c, SqlTransaction? t,
        IEnumerable<string> hashes, CancellationToken ct)
    {
        var keys = hashes.Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0) return new(StringComparer.Ordinal);
        var rows = await c.QueryAsync<ContentRow>(new CommandDefinition("""
SELECT c.ContentHash,c.ContentJson FROM dbo.DelphiLiveSnapshotContent c JOIN OPENJSON(@Hashes) h
 ON c.ContentHash=CONVERT(char(64),h.value) COLLATE Latin1_General_100_BIN2;
""", new { Hashes = JsonSerializer.Serialize(keys) }, t, cancellationToken: ct));
        var contents = rows.ToDictionary(r => r.ContentHash, r => r.ContentJson, StringComparer.Ordinal);
        if (contents.Count != keys.Length || contents.Any(p => DelphiLiveSnapshotCodec.Hash(p.Value) != p.Key))
            throw new InvalidDataException("Current paper content is missing or damaged.");
        return contents;
    }
    private sealed record StateRow(long Revision, string SourceHash, string StateHash, string StateJson);
    private sealed record ContentRow(string ContentHash, string ContentJson);
    private sealed record Identity(string Kind, Guid RecordId, int Ordinal);
}
