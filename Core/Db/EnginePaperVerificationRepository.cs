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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

internal sealed record PaperVerifiedBoundary(EngineDatabaseIdentity Database, Guid BaselineRun,
    string Root, DelphiLiveWorkingCheckpoint Checkpoint);
internal sealed record PaperVerificationPolicy(EngineDatabaseIdentity Database, Guid BaselineRun,
    ImmutableArray<Guid> OriginalOwners);

/// <summary>Small audit resume records in the existing current-state store. No portfolio/history copy.</summary>
public sealed class EnginePaperVerificationRepository
{
    private static readonly Guid PolicyOwner = new("BA811619-70E5-4CF6-B8B5-3A4AEFAE0999");
    private const string BoundaryKind = "PaperVerifiedBoundary", PolicyKind = "PaperVerificationPolicy";

    internal static async Task<PaperVerificationPolicy?> PolicyAsync(SqlConnection c, SqlTransaction? t, CancellationToken ct)
    {
        var row = await ReadRowAsync(c, t, PolicyKind, PolicyOwner, ct);
        if (row == null) return null;
        Check(row);
        if (row.Revision != 0 || row.SourceHash != row.StateHash) throw new InvalidDataException("Paper baseline policy receipt disagrees with its identity.");
        return DelphiLiveLedgerJson.Deserialize<PaperVerificationPolicy>(row.StateJson);
    }

    internal static async Task<PaperVerifiedBoundary?> BoundaryAsync(SqlConnection c, SqlTransaction? t, Guid owner,
        PaperVerificationPolicy policy, CancellationToken ct)
    {
        var row = await ReadRowAsync(c, t, BoundaryKind, owner, ct);
        if (row == null) return null;
        Check(row);
        var value = DelphiLiveLedgerJson.Deserialize<PaperVerifiedBoundary>(row.StateJson);
        var header = JsonNode.Parse(value.Root)!;
        if (value.Database != policy.Database || value.BaselineRun != policy.BaselineRun ||
            header["portfolioId"]!.GetValue<Guid>() != owner || header["revision"]!.GetValue<long>() != row.Revision ||
            DelphiLiveSnapshotCodec.Hash(value.Root) != row.SourceHash)
            throw new InvalidDataException("Paper verification boundary identity disagrees with its baseline.");
        return value;
    }

    /// <summary>Explicit offline transition. Requires the engine ownership lease held by the command.</summary>
    public async Task PrepareAsync(Guid recovery, CancellationToken ct = default)
    {
        await using var c = await EngineIntegrityRepository.OpenAsync(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await RequireGuardsAsync(c, t, ct);
        if (await PolicyAsync(c, t, ct) != null) throw new InvalidOperationException("Paper verification is already prepared; it cannot be reset by preparation.");
        var row = await c.QuerySingleAsync<EngineIntegrityRepository.RunRow>(new CommandDefinition(
            "SELECT * FROM dbo.EngineAuditRun WHERE RunId=@Recovery;", new { Recovery = recovery }, t, cancellationToken: ct));
        var run = EngineIntegrityRepository.Decode(row);
        var db = await EngineContinuityRepository.IdentityAsync(c, t, ct);
        if (run.Plan.Kind != EngineAuditKind.Recovery || run.Plan.Scope == EngineRecoveryScope.Real ||
            run.Status != "Complete" || !run.Progress.Complete || run.Plan.Database != db)
            throw new InvalidDataException("Select a completed Paper recovery in this database.");
        if ((await EngineIntegrityRepository.ReadOpenAsync(c, t, ct)).Any(i => i.Scope is EngineIntegrityScope.Global or EngineIntegrityScope.Paper) ||
            !await c.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.EngineIntegrityResolution WHERE RecoveryRunId=@Recovery) THEN 1 ELSE 0 END AS bit);",
                new { Recovery = recovery }, t, cancellationToken: ct)))
            throw new InvalidOperationException("The selected recovery must have passed guarded clearance and Paper must have no unresolved operational findings.");
        var heads = (await c.QueryAsync<Head>(new CommandDefinition(
            "SELECT PortfolioId,Revision,SnapshotJson FROM dbo.DelphiLivePortfolioLedger WITH(HOLDLOCK);", transaction: t, cancellationToken: ct))).ToArray();
        var targets = run.Plan.Targets.Where(x => x.Family == EngineAuditFamily.PaperRevisions).ToArray();
        if (targets.Any(x => !heads.Any(h => h.PortfolioId == x.OwnerId)))
            throw new InvalidDataException("A verified Paper portfolio is missing.");
        var policy = new PaperVerificationPolicy(db, recovery, heads.Select(h => h.PortfolioId).ToImmutableArray());
        foreach (var head in heads)
        {
            var target = targets.SingleOrDefault(x => x.OwnerId == head.PortfolioId);
            long through = target?.ThroughRevision ?? -1;
            if (head.Revision < through) throw new InvalidDataException("Paper history moved behind the completed recovery.");
            var current = await EnginePaperWorkingStore.ReadAsync(c, t, head.SnapshotJson, ct);
            var originalRoot = await c.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT SnapshotJson FROM dbo.DelphiLivePortfolioRevision WHERE PortfolioId=@Id AND Revision=@Revision;",
                new { Id = head.PortfolioId, head.Revision }, t, cancellationToken: ct));
            if (originalRoot != head.SnapshotJson) throw new InvalidDataException("Paper ledger and original revision disagree during baseline adoption.");
            // Exact completed heads need no archive hydration. Legacy intervening appends have
            // no historical working receipt: qualify only that tail once during this explicit
            // transition, hydrating one preceding snapshot, never replaying earlier revisions.
            if (head.Revision != through)
            {
                DelphiLivePortfolioSnapshot? previous = null;
                if (through >= 0)
                {
                    var root = await c.QuerySingleAsync<string>(new CommandDefinition(
                        "SELECT SnapshotJson FROM dbo.DelphiLivePortfolioRevision WHERE PortfolioId=@Id AND Revision=@Revision;",
                        new { Id = head.PortfolioId, Revision = through }, t, cancellationToken: ct));
                    previous = await DelphiLiveSnapshotStorage.ReadAsync(c, t, root, ct, fresh: true);
                }
                for (long revision = through + 1; revision <= head.Revision; revision++)
                {
                    var next = await c.QuerySingleAsync<RevisionRow>(new CommandDefinition(
                        "SELECT SnapshotJson,SettingsAssignmentId FROM dbo.DelphiLivePortfolioRevision WHERE PortfolioId=@Id AND Revision=@Revision;",
                        new { Id = head.PortfolioId, Revision = revision }, t, cancellationToken: ct));
                    var state = await DelphiLiveSnapshotStorage.ReadAsync(c, t, next.SnapshotJson, ct, fresh: true);
                    await EngineHistoryVerifier.VerifyPaperTransitionAsync(c, t, head.PortfolioId, revision, next.SettingsAssignmentId, previous, state, ct);
                    previous = state;
                }
                var fullPackage = DelphiLiveSnapshotCodec.Pack(previous!);
                var reconstructed = DelphiLiveWorkingCheckpoint.Qualify(previous!, fullPackage);
                current.Checkpoint.RequireSameReconstruction(reconstructed.Checkpoint);
            }
            await RequireObservationAsync(c, t, current, head.SnapshotJson, ct);
            await SaveBoundaryAsync(c, t, head.PortfolioId, new(db, recovery, head.SnapshotJson, current.Checkpoint), ct);
        }
        await SaveAsync(c, t, PolicyKind, PolicyOwner, 0, DelphiLiveSnapshotCodec.Hash(DelphiLiveLedgerJson.Serialize(policy)), policy, ct);
        t.Commit();
    }

    public async Task QualifyRestartAsync(bool continuityMatched, CancellationToken ct = default)
    {
        PaperRestartQualification.Set(null);
        if (!continuityMatched) throw new InvalidDataException("Paper restart continuity is uncertain; investigate the saved boundary without starting a full scan.");
        await using var c = await EngineIntegrityRepository.OpenAsync(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await RequireGuardsAsync(c, t, ct);
        var policy = await PolicyAsync(c, t, ct) ?? throw new InvalidOperationException("Adopt the completed Paper recovery before enabling checkpoint restart.");
        var db = await EngineContinuityRepository.IdentityAsync(c, t, ct);
        if (db != policy.Database) throw new InvalidDataException("Paper database identity changed; investigate the affected boundary.");
        var heads = (await c.QueryAsync<Head>(new CommandDefinition(
            "SELECT PortfolioId,Revision,SnapshotJson FROM dbo.DelphiLivePortfolioLedger WITH(HOLDLOCK);", transaction: t, cancellationToken: ct))).ToArray();
        var failedOwners = (await KnownOwnersAsync(c, t, policy, ct)).Where(id => !heads.Any(h => h.PortfolioId == id)).ToHashSet();
        var pendingBuys = ImmutableDictionary.CreateBuilder<Guid, ImmutableArray<Guid>>();
        foreach (var head in heads)
        {
            try
            {
                var boundary = await BoundaryAsync(c, t, head.PortfolioId, policy, ct);
                var current = await EnginePaperWorkingStore.ReadAsync(c, t, head.SnapshotJson, ct);
                RequireCurrentBoundary(policy, db, head.PortfolioId, head.SnapshotJson, current.Checkpoint, boundary);
                // Current-state validation is deliberately independent of historical performance coverage.
                DelphiLiveLedgerIntegrity.ValidateTransition(current.Snapshot, current.Snapshot with { Revision = checked(current.Snapshot.Revision + 1) });
                await EnginePaperWorkingStore.VerifyIdentitiesAsync(c, t, head.PortfolioId, head.Revision,
                    current.Checkpoint.Items.Select(i => new DelphiLiveSnapshotItem(i.Collection, i.ArchiveOrdinal, i.ContentHash)), current.Contents, false, ct);
                var original = await c.QuerySingleAsync<string>(new CommandDefinition(
                    "SELECT SnapshotJson FROM dbo.DelphiLivePortfolioRevision WHERE PortfolioId=@Id AND Revision=@Revision;",
                    new { Id = head.PortfolioId, head.Revision }, t, cancellationToken: ct));
                if (original != head.SnapshotJson) throw new InvalidDataException("Paper ledger and original revision disagree.");
                await RequireObservationAsync(c, t, current, head.SnapshotJson, ct);
                pendingBuys[head.PortfolioId] = current.Snapshot.PendingActions.Where(a => a.Intent.Side == DelphiLiveActionSide.Buy)
                    .Select(a => a.Intent.ActionId).ToImmutableArray();
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException)
            { failedOwners.Add(head.PortfolioId); }
        }
        var incidents = await EngineIntegrityRepository.ReadOpenAsync(c, t, ct);
        if (incidents.Any(i => (i.Scope == EngineIntegrityScope.Global || i.Scope == EngineIntegrityScope.Paper && i.OwnerId == null) && !PaperRestartQualification.IsStartupFinding(i)))
            throw new InvalidOperationException("A current Paper or shared finding requires targeted investigation.");
        t.Commit();
        var repository = new EngineIntegrityRepository();
        foreach (var owner in failedOwners)
            await repository.RaiseAsync(EngineIntegrityScope.Paper, owner, "PaperCurrentState", "PaperRestartQualificationFailed", true, ct);
        // Owner findings remain local; a damaged portfolio cannot qualify through its own startup finding.
        foreach (var owner in failedOwners) pendingBuys.Remove(owner);
        incidents = await EngineIntegrityRepository.ReadOpenAsync(c, null, ct);
        PaperRestartQualification.Set(new(db, DateTime.UtcNow, pendingBuys.Keys.ToImmutableArray(), incidents)
            { PendingBuys = pendingBuys.ToImmutable() });
    }

    internal static void RequireCurrentBoundary(PaperVerificationPolicy policy, EngineDatabaseIdentity database, Guid owner,
        string root, DelphiLiveWorkingCheckpoint checkpoint, PaperVerifiedBoundary? boundary)
    {
        if (policy.Database != database || boundary != null && (boundary.Database != database || boundary.BaselineRun != policy.BaselineRun))
            throw new InvalidDataException("Paper database or verification identity changed.");
        if (boundary == null && policy.OriginalOwners.Contains(owner))
            throw new InvalidDataException("The Paper verification boundary is missing.");
        if (JsonNode.Parse(root)!["portfolioId"]!.GetValue<Guid>() != owner)
            throw new InvalidDataException("Paper current-state owner disagrees with its original portfolio.");
        if (boundary == null) return;
        if (Revision(root) < Revision(boundary.Root)) throw new InvalidDataException("The current Paper checkpoint moved behind verified history.");
        if (Revision(root) == Revision(boundary.Root))
        {
            if (root != boundary.Root) throw new InvalidDataException("The verified Paper revision changed.");
            checkpoint.RequireSameReconstruction(boundary.Checkpoint);
        }
    }

    internal static async Task<ImmutableArray<EngineAuditTarget>> TargetsAsync(SqlConnection c, SqlTransaction t, CancellationToken ct)
    {
        var policy = await PolicyAsync(c, t, ct) ?? throw new InvalidOperationException("Paper incremental verification needs its adopted recovery boundary.");
        if (await EngineContinuityRepository.IdentityAsync(c, t, ct) != policy.Database)
            throw new InvalidDataException("Paper verification database identity changed.");
        var heads = (await c.QueryAsync<Head>(new CommandDefinition(
            "SELECT PortfolioId,Revision,SnapshotJson FROM dbo.DelphiLivePortfolioLedger;", transaction: t, cancellationToken: ct))).ToArray();
        if ((await KnownOwnersAsync(c, t, policy, ct)).Any(id => !heads.Any(h => h.PortfolioId == id))) throw new InvalidDataException("A Paper verification owner disappeared.");
        var result = ImmutableArray.CreateBuilder<EngineAuditTarget>();
        foreach (var head in heads.OrderBy(h => h.PortfolioId))
        {
            var boundary = await BoundaryAsync(c, t, head.PortfolioId, policy, ct);
            if (boundary == null && policy.OriginalOwners.Contains(head.PortfolioId)) throw new InvalidDataException("Paper verification boundary is missing.");
            long first = boundary == null ? 0 : checked(Revision(boundary.Root) + 1);
            if (head.Revision < first - 1) throw new InvalidDataException("Paper history moved backwards.");
            result.Add(new(EngineAuditFamily.PaperRevisions, head.PortfolioId, first, head.Revision));
        }
        return result.ToImmutable();
    }

    internal async Task<EngineAuditRun> VerifyNextAsync(EngineAuditRun run, CancellationToken ct = default)
    {
        await using var c = await EngineIntegrityRepository.OpenAsync(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await RequireGuardsAsync(c, t, ct);
        var policy = await PolicyAsync(c, t, ct) ?? throw new InvalidDataException("Missing Paper verification policy.");
        if (await EngineContinuityRepository.IdentityAsync(c, t, ct) != policy.Database || run.Plan.Database != policy.Database)
            throw new InvalidDataException("Paper audit identity changed.");
        var p = run.Progress; p.Validate(run.Plan);
        if (p.Complete) return run;
        var target = run.Plan.Targets[p.TargetIndex];
        if (p.NextRevision > target.ThroughRevision) p = p.NextTarget(run.Plan);
        else
        {
            Guid owner = target.OwnerId!.Value;
            var prior = await BoundaryAsync(c, t, owner, policy, ct);
            if ((prior == null ? 0 : Revision(prior.Root) + 1) != p.NextRevision)
                throw new InvalidDataException("Paper audit progress and its saved working boundary disagree.");
            var row = await c.QuerySingleAsync<RevisionRow>(new CommandDefinition(
                "SELECT SnapshotJson,SettingsAssignmentId FROM dbo.DelphiLivePortfolioRevision WHERE PortfolioId=@Owner AND Revision=@Revision;",
                new { Owner = owner, Revision = p.NextRevision }, t, cancellationToken: ct));
            var hashes = prior?.Checkpoint.Items.Select(i => i.ContentHash) ?? [];
            var changes = (await c.QueryAsync<Change>(new CommandDefinition(
                "SELECT CollectionName,Ordinal,ContentHash FROM dbo.DelphiLiveSnapshotItem WHERE PortfolioId=@Owner AND Revision=@Revision;",
                new { Owner = owner, Revision = p.NextRevision }, t, cancellationToken: ct))).Select(x => new DelphiLiveSnapshotItem(x.CollectionName, x.Ordinal, x.ContentHash)).ToArray();
            var contents = await ContentsAsync(c, t, hashes.Concat(changes.Select(i => i.ContentHash)),
                new[] { DelphiLiveSnapshotCodec.CandidateStateReference(row.SnapshotJson), prior?.Checkpoint.CandidateStateHash }, ct);
            DelphiLiveWorkingCheckpoint checkpoint;
            if (prior == null)
            {
                var initial = DelphiLiveSnapshotCodec.Unpack(row.SnapshotJson, changes, contents);
                if (initial.Revision != 0) throw new InvalidDataException("A new portfolio must start with revision zero.");
                await EngineHistoryVerifier.VerifyPaperTransitionAsync(c, t, owner, 0, row.SettingsAssignmentId, null, initial, ct);
                checkpoint = DelphiLiveWorkingCheckpoint.Qualify(initial, new(row.SnapshotJson,
                    DelphiLiveSnapshotCodec.Hash(row.SnapshotJson), contents, changes)).Checkpoint;
                await EnginePaperWorkingStore.VerifyIdentitiesAsync(c, t, owner, 0, changes, contents, true, ct);
                if (!policy.OriginalOwners.Contains(owner))
                {
                    policy = policy with { OriginalOwners = policy.OriginalOwners.Add(owner) };
                    await SaveAsync(c, t, PolicyKind, PolicyOwner, 0, DelphiLiveSnapshotCodec.Hash(DelphiLiveLedgerJson.Serialize(policy)), policy, ct);
                }
            }
            else
            {
                async Task<string?> Read(string hash, CancellationToken token)
                {
                    var fetched = await EnginePaperWorkingStore.ReadContentsAsync(c, t, [hash], token);
                    return fetched[hash];
                }
                var verified = await DelphiLiveIncrementalVerification.VerifyAsync(prior.Checkpoint, prior.Root, row.SnapshotJson,
                    changes, contents, Read, ct, (before, after) => EngineHistoryVerifier.VerifyPaperTransitionAsync(c, t, owner,
                        p.NextRevision, row.SettingsAssignmentId, before, after, ct));
                checkpoint = verified.Checkpoint;
                await EnginePaperWorkingStore.VerifyIdentitiesAsync(c, t, owner, p.NextRevision,
                    verified.NewIdentities, contents, true, ct);
            }
            await SaveBoundaryAsync(c, t, owner, new(policy.Database, policy.BaselineRun, row.SnapshotJson, checkpoint), ct);
            p = p.Verified(run.Plan, p.NextRevision + 1, null);
        }
        var now = await c.QuerySingleAsync<DateTime>(new CommandDefinition("SELECT SYSUTCDATETIME();", transaction: t, cancellationToken: ct));
        var next = run with { Progress = p, Revision = run.Revision + 1, Status = p.Complete ? "Complete" : "Running", UpdatedUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc) };
        if (await c.ExecuteAsync(new CommandDefinition("""
UPDATE dbo.EngineAuditRun SET ProgressJson=@ProgressJson,ProgressHash=@ProgressHash,Revision=@Revision,Status=@Status,UpdatedUtc=@UpdatedUtc
WHERE RunId=@RunId AND Revision=@Revision-1 AND PlanHash=@PlanHash AND Status=N'Running';
""", EngineIntegrityRepository.Encode(next), t, cancellationToken: ct)) != 1)
            throw new InvalidOperationException("Paper verification progress advanced elsewhere.");
        t.Commit(); return next;
    }

    private static async Task RequireObservationAsync(SqlConnection c, SqlTransaction t, EnginePaperWorkingStore.Loaded current, string root, CancellationToken ct)
    {
        var row = await ReadRowAsync(c, t, "PaperObservation", current.Snapshot.PortfolioId, ct)
            ?? throw new InvalidDataException("Paper observation state is missing.");
        Check(row);
        var expected = DelphiLiveLedgerJson.Serialize(DelphiLiveObservationPortfolio.From(current.Snapshot));
        if (row.Revision != current.Snapshot.Revision || row.SourceHash != DelphiLiveSnapshotCodec.Hash(root) ||
            DelphiLiveSnapshotCodec.Fingerprint(expected) != DelphiLiveSnapshotCodec.Fingerprint(row.StateJson))
            throw new InvalidDataException("Paper observation membership disagrees with current state.");
    }
    internal static async Task<Dictionary<string, string>> ContentsAsync(SqlConnection c, SqlTransaction? t, IEnumerable<string> hashes,
        IEnumerable<string?> candidates, CancellationToken ct)
    {
        var contents = await EnginePaperWorkingStore.ReadContentsAsync(c, t, hashes.Concat(candidates.OfType<string>()), ct);
        var reports = contents.Values.Select(value => JsonNode.Parse(value)).Where(n => n is JsonObject).SelectMany(n =>
            DelphiLiveSnapshotCodec.DossierFields.Select(f => n![f] is JsonObject link ? link["contentHash"]?.GetValue<string>() : null)).OfType<string>();
        foreach (var entry in await EnginePaperWorkingStore.ReadContentsAsync(c, t, reports, ct)) contents[entry.Key] = entry.Value;
        return contents;
    }
    private static Task SaveBoundaryAsync(SqlConnection c, SqlTransaction t, Guid owner, PaperVerifiedBoundary value, CancellationToken ct)
        => SaveAsync(c, t, BoundaryKind, owner, Revision(value.Root), DelphiLiveSnapshotCodec.Hash(value.Root), value, ct);
    private static Task SaveAsync<T>(SqlConnection c, SqlTransaction t, string kind, Guid owner, long revision, string source, T value, CancellationToken ct)
    {
        string json = DelphiLiveLedgerJson.Serialize(value);
        return c.ExecuteAsync(new CommandDefinition("""
UPDATE dbo.EngineCurrentState WITH(UPDLOCK,HOLDLOCK) SET Revision=@Revision,SourceHash=@Source,StateHash=@Hash,StateJson=@Json,UpdatedUtc=SYSUTCDATETIME()
WHERE Kind=@Kind AND OwnerId=@Owner;
IF @@ROWCOUNT=0 INSERT dbo.EngineCurrentState(Kind,OwnerId,Revision,SourceHash,StateHash,StateJson)
VALUES(@Kind,@Owner,@Revision,@Source,@Hash,@Json);
""", new { Kind = kind, Owner = owner, Revision = revision, Source = source, Hash = DelphiLiveSnapshotCodec.Hash(json), Json = json }, t, cancellationToken: ct));
    }
    private static Task<StateRow?> ReadRowAsync(SqlConnection c, SqlTransaction? t, string kind, Guid owner, CancellationToken ct)
        => c.QuerySingleOrDefaultAsync<StateRow>(new CommandDefinition(
            "SELECT Revision,SourceHash,StateHash,StateJson FROM dbo.EngineCurrentState WHERE Kind=@Kind AND OwnerId=@Owner;",
            new { Kind = kind, Owner = owner }, t, cancellationToken: ct));
    private static void Check(StateRow row)
    {
        if (row.StateHash != DelphiLiveSnapshotCodec.Hash(row.StateJson)) throw new InvalidDataException("Paper verification receipt checksum failed.");
    }
    private static long Revision(string root) => JsonNode.Parse(root)!["revision"]!.GetValue<long>();
    private static async Task<IEnumerable<Guid>> KnownOwnersAsync(SqlConnection c, SqlTransaction t, PaperVerificationPolicy policy, CancellationToken ct)
        => policy.OriginalOwners.Concat(await c.QueryAsync<Guid>(new CommandDefinition(
            "SELECT OwnerId FROM dbo.EngineCurrentState WHERE Kind=N'PaperVerifiedBoundary';", transaction: t, cancellationToken: ct))).Distinct();
    private static async Task RequireGuardsAsync(SqlConnection c, SqlTransaction t, CancellationToken ct)
    {
        if (!await EngineIntegrityRepository.RequiredGuardsAsync(c, t, ct)) throw new InvalidDataException("Immutable guards are unavailable for Paper verification.");
    }
    private sealed record Head(Guid PortfolioId, long Revision, string SnapshotJson);
    private sealed record RevisionRow(string SnapshotJson, Guid? SettingsAssignmentId);
    private sealed record Change(string CollectionName, int Ordinal, string ContentHash);
    private sealed record StateRow(long Revision, string SourceHash, string StateHash, string StateJson);
}
