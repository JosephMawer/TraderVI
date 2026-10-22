#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.WSTrade.Orders;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Durable findings are independent of audit progress. A clean later batch never clears a hold.</summary>
public sealed class EngineIntegrityRepository
{
    internal static async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(new SQLBase().ConnectionString);
        try { await c.OpenAsync(ct).ConfigureAwait(false); return c; }
        catch { await c.DisposeAsync(); throw; }
    }
    internal static Task<bool> InstalledAsync(SqlConnection c, SqlTransaction? t, CancellationToken ct) =>
        c.QuerySingleAsync<bool>(new CommandDefinition("""
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.EngineIntegrityIncident',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.EngineIntegrityResolution',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.EngineAuditRun',N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit);
""", transaction: t, cancellationToken: ct));

    internal static async Task<ImmutableArray<EngineIntegrityIncident>> ReadOpenAsync(SqlConnection c, SqlTransaction? t, CancellationToken ct)
    {
        var rows = await c.QueryAsync<IncidentRow>(new CommandDefinition("""
SELECT i.IncidentId,i.Scope,i.OwnerId,i.EvidenceKey,i.FailureCode,i.DiscoveredUtc,i.ProtectionUntrusted,
 origin.DiscoveredUtc OriginDiscoveredUtc,origin.EvidenceKey OriginEvidenceKey
FROM dbo.EngineIntegrityIncident i
LEFT JOIN dbo.EngineIntegrityIncident origin ON i.Scope=N'Paper' AND i.FailureCode=N'PaperRecoveryRequired'
 AND i.EvidenceKey=N'ScopedFrom/'+CONVERT(nvarchar(36),origin.IncidentId)
 AND EXISTS(SELECT 1 FROM dbo.EngineIntegrityResolution resolved JOIN dbo.EngineAuditRun proof ON proof.RunId=resolved.RecoveryRunId
  WHERE resolved.IncidentId=origin.IncidentId AND proof.Kind=N'Recovery' AND proof.Status=N'Complete' AND JSON_VALUE(proof.PlanJson,'$.Scope')=N'1')
WHERE NOT EXISTS
 (SELECT 1 FROM dbo.EngineIntegrityResolution r WHERE r.IncidentId=i.IncidentId)
ORDER BY i.DiscoveredUtc,i.IncidentId;
""", transaction: t, cancellationToken: ct));
        return rows.Select(r => new EngineIntegrityIncident(r.IncidentId, Enum.Parse<EngineIntegrityScope>(r.Scope), r.OwnerId,
            r.EvidenceKey, r.FailureCode, DateTime.SpecifyKind(r.DiscoveredUtc, DateTimeKind.Utc), r.ProtectionUntrusted)
            { OriginDiscoveredUtc = r.OriginDiscoveredUtc is DateTime origin ? DateTime.SpecifyKind(origin, DateTimeKind.Utc) : null,
                OriginEvidenceKey = r.OriginEvidenceKey }).ToImmutableArray();
    }
    public async Task<EngineIntegrityAccess> ReadAccessAsync(EngineIntegrityScope scope, Guid? owner, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        if (!await InstalledAsync(c, null, ct)) throw new InvalidOperationException("Independent integrity storage is not installed.");
        if (!await RequiredGuardsAsync(c, null, ct)) throw new InvalidOperationException("Immutable history guards are unavailable.");
        return EngineIntegrityAccess.Evaluate(await ReadOpenAsync(c, null, ct), scope, owner);
    }
    internal static async Task<string?> ManualBuyBlockAsync(SqlConnection c, SqlTransaction t, bool required, CancellationToken ct)
    {
        if (!await InstalledAsync(c, t, ct)) return required ? "Independent integrity checks are unavailable. New exposure is blocked." : null;
        if (!await RequiredGuardsAsync(c, t, ct)) return "Immutable history guards are unavailable. New exposure is blocked.";
        // This lease covers the manual order path for the one linked TFSA; every unresolved broker/global hold applies.
        var open = await ReadOpenAsync(c, t, ct);
        return open.Any(i => i.Scope is EngineIntegrityScope.Global or EngineIntegrityScope.Broker)
            ? "An unresolved integrity incident blocks new exposure. Verify recovery and explicitly clear its hold." : null;
    }
    public async Task<IManualBuySafetyLease> AcquireProtectionLeaseAsync(Guid binding, CancellationToken ct)
    {
        var c = await OpenAsync(ct);
        SqlTransaction? t = null;
        try
        {
            t = c.BeginTransaction(IsolationLevel.Serializable);
            if (!await InstalledAsync(c, t, ct) || !await RequiredGuardsAsync(c, t, ct))
                return new ProtectionLease(c, t, "Automatic protection integrity checks are unavailable.");
            var access = EngineIntegrityAccess.Evaluate(await ReadOpenAsync(c, t, ct), EngineIntegrityScope.Broker, binding);
            return new ProtectionLease(c, t, access.ProtectionAllowed ? null : "An unresolved integrity incident makes automatic protection unavailable.");
        }
        catch { t?.Dispose(); await c.DisposeAsync(); throw; }
    }
    private sealed class ProtectionLease(SqlConnection c, SqlTransaction t, string? reason) : IManualBuySafetyLease
    {
        public string? BlockReason => reason;
        public async ValueTask DisposeAsync()
        {
            try { await t.RollbackAsync(); }
            finally { await t.DisposeAsync(); await c.DisposeAsync(); }
        }
    }
    public async Task RaiseAsync(EngineIntegrityScope scope, Guid? owner, string evidenceKey, string code,
        bool protectionUntrusted, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(scope) || scope == EngineIntegrityScope.Global && owner is not null ||
            string.IsNullOrWhiteSpace(evidenceKey) || evidenceKey.Length > 200 || string.IsNullOrWhiteSpace(code) || code.Length > 64)
            throw new ArgumentException("Invalid integrity incident identity.");
        await using var c = await OpenAsync(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await c.ExecuteAsync(new CommandDefinition("""
IF NOT EXISTS(SELECT 1 FROM dbo.EngineIntegrityIncident i WITH(UPDLOCK,HOLDLOCK)
 WHERE i.Scope=@Scope AND (i.OwnerId=@Owner OR i.OwnerId IS NULL AND @Owner IS NULL) AND i.EvidenceKey=@EvidenceKey
 AND NOT EXISTS(SELECT 1 FROM dbo.EngineIntegrityResolution r WHERE r.IncidentId=i.IncidentId))
 INSERT dbo.EngineIntegrityIncident(IncidentId,Scope,OwnerId,EvidenceKey,FailureCode,ProtectionUntrusted)
 VALUES(@Id,@Scope,@Owner,@EvidenceKey,@Code,@Protection);
""", new { Id = Guid.NewGuid(), Scope = scope.ToString(), Owner = owner, EvidenceKey = evidenceKey, Code = code, Protection = protectionUntrusted }, t, cancellationToken: ct));
        t.Commit();
    }
    public async Task<EngineIntegrityStatus> ReadStatusAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        var incidents = await ReadOpenAsync(c, null, ct);
        var rows = await c.QueryAsync<RunRow>(new CommandDefinition("""
SELECT a.* FROM dbo.EngineAuditRun a JOIN
 (SELECT RunId,ROW_NUMBER() OVER(PARTITION BY Kind,Status,ISNULL(JSON_VALUE(PlanJson,'$.Scope'),'0') ORDER BY BoundaryUtc DESC,UpdatedUtc DESC) n
 FROM dbo.EngineAuditRun) r ON r.RunId=a.RunId WHERE r.n=1;
""", cancellationToken: ct));
        return new(incidents, rows.Select(Decode).ToImmutableArray());
    }
    public async Task<bool> GuardsAvailableAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        return await InstalledAsync(c, null, ct) && await RequiredGuardsAsync(c, null, ct);
    }

    public async Task<EngineAuditRun> StartAsync(EngineAuditKind kind, CancellationToken ct = default,
        EngineRecoveryScope scope = EngineRecoveryScope.All)
    {
        await using var c = await OpenAsync(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        if (!Enum.IsDefined(scope) || scope != EngineRecoveryScope.All && kind != EngineAuditKind.Recovery)
            throw new ArgumentException("Only recovery may select a domain.");
        if (!await RequiredGuardsAsync(c, t, ct)) throw new InvalidOperationException("Immutable guards are unavailable.");
        // One owner per audit kind/domain, including across restart. Original plans remain immutable.
        var existing = await c.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(
            "SELECT * FROM dbo.EngineAuditRun WITH(UPDLOCK,HOLDLOCK) WHERE Kind=@Kind AND Status=N'Running' AND ISNULL(TRY_CONVERT(int,JSON_VALUE(PlanJson,'$.Scope')),0)=@Scope;",
            new { Kind = kind.ToString(), Scope = (int)scope }, t, cancellationToken: ct));
        if (existing is not null) { t.Commit(); return Decode(existing); }
        var prior = kind == EngineAuditKind.NewHistory ? await c.QueryFirstOrDefaultAsync<RunRow>(new CommandDefinition(
            "SELECT TOP(1) * FROM dbo.EngineAuditRun WHERE Kind=N'NewHistory' AND Status=N'Complete' ORDER BY BoundaryUtc DESC;", transaction: t, cancellationToken: ct)) : null;
        var targets = await CaptureTargetsAsync(c, t, kind, prior is null ? null : Decode(prior).Plan, ct, scope);
        DateTime now = await c.QuerySingleAsync<DateTime>(new CommandDefinition("SELECT SYSUTCDATETIME();", transaction: t, cancellationToken: ct));
        now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        var plan = new EngineAuditPlan(kind, now, targets) { Scope = scope,
            Database = await EngineContinuityRepository.IdentityAsync(c, t, ct) };
        plan.Validate();
        var run = new EngineAuditRun(Guid.NewGuid(), plan, EngineAuditProgress.Start(plan), 0, targets.IsEmpty ? "Complete" : "Running", now);
        var p = Encode(run);
        await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.EngineAuditRun(RunId,Kind,BoundaryUtc,PlanJson,PlanHash,ProgressJson,ProgressHash,Revision,Status,UpdatedUtc)
VALUES(@RunId,@Kind,@BoundaryUtc,@PlanJson,@PlanHash,@ProgressJson,@ProgressHash,@Revision,@Status,@UpdatedUtc);
""", p, t, cancellationToken: ct));
        if (kind == EngineAuditKind.Recovery)
            await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.EngineIntegrityIncident(IncidentId,Scope,EvidenceKey,FailureCode,ProtectionUntrusted)
VALUES(@Id,@Scope,@Key,N'RecoveryVerificationPending',1);
""", new { Id = Guid.NewGuid(), Scope = scope switch { EngineRecoveryScope.Real => "Broker", EngineRecoveryScope.Paper => "Paper", _ => "Global" },
                Key = "Recovery/" + run.RunId.ToString("D") }, t, cancellationToken: ct));
        t.Commit(); return run;
    }

    internal static async Task<ImmutableArray<EngineAuditTarget>> CaptureTargetsAsync(SqlConnection c, SqlTransaction t,
        EngineAuditKind kind, EngineAuditPlan? previous, CancellationToken ct, EngineRecoveryScope scope = EngineRecoveryScope.All)
    {
        var heads = (await c.QueryAsync<Head>(new CommandDefinition("""
SELECT N'PaperRevisions' Family,PortfolioId OwnerId,MAX(Revision) ThroughRevision FROM dbo.DelphiLivePortfolioRevision WHERE @Scope<>1 GROUP BY PortfolioId
UNION ALL SELECT N'BrokerRevisions',BindingId,MAX(Revision) FROM dbo.BrokerManagedSessionRevision WHERE @Scope<>2 GROUP BY BindingId
UNION ALL SELECT N'SharedInputs',NULL,ISNULL(MAX(PublicationSequence),0) FROM dbo.EngineInputPublication
UNION ALL SELECT N'SharedContent',NULL,ISNULL(MAX(EvidenceSequence),0) FROM dbo.EngineEvidenceContent;
""", new { Scope = (int)scope }, t, cancellationToken: ct))).ToArray();
        if (previous is not null && previous.Targets.Any(p => !heads.Any(h => h.Family == p.Family.ToString() && h.OwnerId == p.OwnerId)))
            throw new InvalidDataException("A previously audited history owner is missing.");
        var targets = heads.Select(h =>
        {
            var family = Enum.Parse<EngineAuditFamily>(h.Family);
            long first = family is EngineAuditFamily.SharedInputs or EngineAuditFamily.SharedContent ? 1 : 0;
            var old = previous?.Targets.SingleOrDefault(p => p.Family == family && p.OwnerId == h.OwnerId);
            if (old is not null) first = old.ThroughRevision + 1;
            if (old is not null && h.ThroughRevision < old.ThroughRevision) throw new InvalidDataException("Historical audit heads moved backwards.");
            return new EngineAuditTarget(family, h.OwnerId, first, h.ThroughRevision);
        }).ToImmutableArray();
        if (kind != EngineAuditKind.NewHistory && scope != EngineRecoveryScope.Real)
        {
            // Legacy evaluation/content tables are frozen by the reviewed cutover; count is a coverage bound,
            // not a new history cursor. All rows are checked in each older-archive/recovery sweep.
            long legacy = await c.QuerySingleAsync<long>(new CommandDefinition("SELECT COUNT_BIG(*) FROM dbo.DelphiLiveEvaluation;", transaction: t, cancellationToken: ct));
            targets = targets.Add(new(EngineAuditFamily.LegacyInputs, null, 0, legacy));
        }
        if (targets.Length > 4096) throw new InvalidOperationException("The audit owner count exceeds the reviewed bound.");
        return targets.Where(t => EngineAuditPlan.Includes(scope, t.Family)).OrderBy(x => x.Family).ThenBy(x => x.OwnerId).ToImmutableArray();
    }

    public async Task SaveProgressAsync(EngineAuditRun original, EngineAuditProgress progress, bool failed = false, CancellationToken ct = default)
    {
        progress.Validate(original.Plan);
        var next = original with { Progress = progress, Revision = original.Revision + 1, Status = failed ? "Failed" : progress.Complete ? "Complete" : "Running", UpdatedUtc = DateTime.UtcNow };
        await using var c = await OpenAsync(ct);
        if (await c.ExecuteAsync(new CommandDefinition("""
UPDATE dbo.EngineAuditRun SET ProgressJson=@ProgressJson,ProgressHash=@ProgressHash,Revision=@Revision,Status=@Status,UpdatedUtc=SYSUTCDATETIME()
WHERE RunId=@RunId AND Revision=@Revision-1 AND PlanHash=@PlanHash AND Status=N'Running';
""", Encode(next), cancellationToken: ct)) != 1) throw new InvalidOperationException("Audit progress advanced elsewhere. Reload its saved receipt.");
    }

    public async Task ClearVerifiedAsync(Guid recoveryRunId, ImmutableArray<Guid> incidents, string reason, CancellationToken ct = default,
        EngineRecoveryScope scope = EngineRecoveryScope.All, ITsxSessionCalendar? calendar = null)
    {
        if (incidents.IsDefaultOrEmpty || incidents.Length != incidents.Distinct().Count() ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException("Select the original incidents and supply a recovery reason.");
        await using var c = await OpenAsync(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        var row = await c.QuerySingleAsync<RunRow>(new CommandDefinition("SELECT * FROM dbo.EngineAuditRun WHERE RunId=@Id;",
            new { Id = recoveryRunId }, t, cancellationToken: ct));
        var run = Decode(row);
        var open = await ReadOpenAsync(c, t, ct);
        var heads = await CaptureTargetsAsync(c, t, EngineAuditKind.Recovery, null, ct, scope);
        EngineRecoveryPolicy.RequireClear(run, open, incidents, heads, scope);
        if (!await RequiredGuardsAsync(c, t, ct)) throw new InvalidOperationException("Immutable history guards are missing or disabled.");
        if (run.Plan.Database is not null && await EngineContinuityRepository.IdentityAsync(c, t, ct) != run.Plan.Database)
            throw new InvalidDataException("Database recovery identity changed after verification.");
        if (scope == EngineRecoveryScope.Real)
        {
            if (calendar is null) throw new InvalidOperationException("Real recovery requires the reviewed session calendar.");
            // A historical receipt-format finding is releasable only after every saved receipt verifies.
            foreach (var saved in await c.QueryAsync<RunRow>(new CommandDefinition("SELECT * FROM dbo.EngineAuditRun;", transaction: t, cancellationToken: ct)))
                _ = Decode(saved);
            foreach (var target in heads.Where(h => h.Family == EngineAuditFamily.BrokerRevisions))
            {
                var broker = await BrokerManagedSessionRepository.VerifyRevisionAsync(c, target.OwnerId!.Value, target.ThroughRevision,
                    calendar, ct, t);
                await EngineRealObservationStore.RequireCurrentAsync(c, t, broker, ct);
            }
        }
        string proof = DelphiLiveSnapshotCodec.Hash(JsonSerializer.Serialize(heads));
        foreach (var id in incidents)
        {
            var original = open.Single(i => i.IncidentId == id);
            if (scope == EngineRecoveryScope.Real && EngineRecoveryPolicy.RetainedPaperHold(original))
                await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.EngineIntegrityIncident(IncidentId,Scope,EvidenceKey,FailureCode,ProtectionUntrusted)
VALUES(@Id,N'Paper',@Key,N'PaperRecoveryRequired',@Protection);
""", new { Id = Guid.NewGuid(), Key = "ScopedFrom/" + original.IncidentId.ToString("D"), Protection = original.ProtectionUntrusted }, t, cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.EngineIntegrityResolution(IncidentId,RecoveryRunId,VerifiedHeadsHash,OperatorName,Reason)
VALUES(@Id,@Run,@Hash,@Operator,@Reason);
""", new { Id = id, Run = recoveryRunId, Hash = proof, Operator = Environment.UserName, Reason = reason.Trim() }, t, cancellationToken: ct));
        }
        t.Commit();
    }

    internal static Task<bool> RequiredGuardsAsync(SqlConnection c, SqlTransaction? t, CancellationToken ct) =>
        c.QuerySingleAsync<bool>(new CommandDefinition("""
SELECT CAST(CASE WHEN NOT EXISTS(SELECT 1 FROM (VALUES
 (N'EngineIntegrityIncident_Immutable'),(N'EngineIntegrityResolution_Immutable'),(N'EngineAuditRun_Progress'),
 (N'TR_EngineEvidenceContent_Immutable'),(N'TR_EngineInputPublication_Immutable'),
 (N'TR_EngineMarketCycle_Immutable'),(N'TR_EngineConsumerReceipt_Immutable'),
 (N'TR_DelphiLiveSnapshotContent_Immutable'),(N'TR_DelphiLiveSnapshotItem_Immutable'),
 (N'TR_EnginePaperRecordIdentity_Immutable'),
 (N'TR_DelphiLivePortfolioRevision_CompactUpdate'),(N'TR_DelphiLivePortfolioRevision_CompactStorage'),(N'BrokerManagedSessionRevision_Immutable'),
 (N'DelphiLiveEvaluation_RetiredWriter'),(N'DelphiLiveEvaluation_ArchiveImmutable')) required(Name)
 LEFT JOIN sys.triggers tr ON tr.object_id=OBJECT_ID(N'dbo.'+required.Name)
 WHERE tr.object_id IS NULL OR tr.is_disabled=1) THEN 1 ELSE 0 END AS bit);
""", transaction: t, cancellationToken: ct));

    internal static EngineAuditRun Decode(RunRow row)
    {
        if (DelphiLiveSnapshotCodec.Hash(row.PlanJson) != row.PlanHash || DelphiLiveSnapshotCodec.Hash(row.ProgressJson) != row.ProgressHash)
            throw new InvalidDataException("Historical audit receipt checksum failed.");
        var plan = JsonSerializer.Deserialize<EngineAuditPlan>(row.PlanJson) ?? throw new InvalidDataException("Missing audit plan.");
        var progress = JsonSerializer.Deserialize<EngineAuditProgress>(row.ProgressJson) ?? throw new InvalidDataException("Missing audit progress.");
        if (plan.Kind.ToString() != row.Kind || !MatchesStoredBoundary(plan.BoundaryUtc, row.BoundaryUtc) || row.Status == "Complete" != progress.Complete)
            throw new InvalidDataException("Historical audit receipt identity failed.");
        progress.Validate(plan);
        return new(row.RunId, plan, progress, row.Revision, row.Status, DateTime.SpecifyKind(row.UpdatedUtc, DateTimeKind.Utc));
    }
    // The initial native writer bound DateTime as SQL datetime, rounding its datetime2 projection.
    // Retain the exact hash-verified JSON boundary. Accept only that writer's exact 1/300-second
    // conversion, never a tolerance window or an altered plan. No stored receipt is rewritten.
    internal static bool MatchesStoredBoundary(DateTime exact, DateTime stored)
    {
        if (exact == stored) return true;
        if (exact < System.Data.SqlTypes.SqlDateTime.MinValue.Value || exact > System.Data.SqlTypes.SqlDateTime.MaxValue.Value) return false;
        var legacy = new System.Data.SqlTypes.SqlDateTime(exact);
        long ticks = (long)decimal.Round((decimal)legacy.TimeTicks * TimeSpan.TicksPerSecond / 300m,
            0, MidpointRounding.AwayFromZero);
        return new DateTime(1900, 1, 1).AddDays(legacy.DayTicks).AddTicks(ticks) == stored;
    }
    internal static DynamicParameters Encode(EngineAuditRun run)
    {
        string plan = JsonSerializer.Serialize(run.Plan), progress = JsonSerializer.Serialize(run.Progress);
        var parameters = new DynamicParameters();
        parameters.Add("RunId", run.RunId); parameters.Add("Kind", run.Plan.Kind.ToString());
        parameters.Add("PlanJson", plan); parameters.Add("PlanHash", DelphiLiveSnapshotCodec.Hash(plan));
        parameters.Add("ProgressJson", progress); parameters.Add("ProgressHash", DelphiLiveSnapshotCodec.Hash(progress));
        parameters.Add("Revision", run.Revision); parameters.Add("Status", run.Status);
        parameters.Add("BoundaryUtc", run.Plan.BoundaryUtc, DbType.DateTime2);
        parameters.Add("UpdatedUtc", run.UpdatedUtc, DbType.DateTime2);
        return parameters;
    }
    private sealed record Head(string Family, Guid? OwnerId, long ThroughRevision);
    private sealed record IncidentRow(Guid IncidentId, string Scope, Guid? OwnerId, string EvidenceKey, string FailureCode, DateTime DiscoveredUtc, bool ProtectionUntrusted, DateTime? OriginDiscoveredUtc, string? OriginEvidenceKey);
    internal sealed record RunRow(Guid RunId, string Kind, DateTime BoundaryUtc, string PlanJson, string PlanHash,
        string ProgressJson, string ProgressHash, long Revision, string Status, DateTime UpdatedUtc);
}
