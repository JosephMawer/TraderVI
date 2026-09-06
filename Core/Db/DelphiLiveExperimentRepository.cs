#nullable enable

using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed partial class DelphiLiveExperimentRepository : SQLBase, IDelphiLiveExperimentStore, IDelphiLiveResearchStore
{
    public const string MigrationFileName = "20260905_025_AddDelphiLiveResearchAndExperiments.sql";

    public async Task RegisterPolicyAsync(DelphiLivePolicyDefinition policy, string decisionRef,
        DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        policy.Validate();
        if (string.IsNullOrWhiteSpace(decisionRef) || decisionRef.Length > 64)
            throw new ArgumentException("An immutable policy requires its reviewed decision reference.");
        var identity = new DelphiLiveStoredPolicyIdentity(policy.PolicyVersionId, policy.PolicyDefinitionName,
            policy.PolicyDefinitionSchemaVersion, policy.EvaluatorVersion, policy.CollectorVersion,
            policy.CollectorSourceContractVersion, policy.DecisionDossierVersion, policy.DecisionDossierSchemaVersion,
            policy.QuoteFillVersion, policy.ShadowPortfolioVersion, policy.ResearchOutcomeVersion,
            policy.RankingDiagnosticVersion, policy.PromotionProtocolVersion);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() } };
        JsonObject settings = JsonSerializer.SerializeToNode(policy, options)!.AsObject();
        foreach (var property in JsonSerializer.SerializeToNode(identity, options)!.AsObject()) settings.Remove(property.Key);
        string json = settings.ToJsonString();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        _ = DelphiLivePolicyStorage.Read(identity, json, hash);
        await using var c = await Open(cancellationToken);
        await using var t = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await Fence(c, t, lease, cancellationToken);
        await using var command = new SqlCommand("""
IF EXISTS(SELECT 1 FROM dbo.DelphiLivePolicyVersion WITH(UPDLOCK,HOLDLOCK) WHERE DelphiLivePolicyVersionId=@Policy)
BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLivePolicyVersion WHERE DelphiLivePolicyVersionId=@Policy AND SettingsSha256=@Hash
 AND SettingsJson=@Settings AND PolicyDefinitionName=@Name AND PolicyDefinitionSchemaVersion=@Schema
 AND EvaluatorVersion=@Evaluator AND CollectorVersion=@Collector AND CollectorSourceContractVersion=@Source
 AND DecisionDossierVersion=@Dossier AND DecisionDossierSchemaVersion=@DossierSchema AND QuoteFillVersion=@Quote
 AND ShadowPortfolioVersion=@Shadow AND ResearchOutcomeVersion=@Outcome AND RankingDiagnosticVersion=@Ranking
 AND PromotionProtocolVersion=@Promotion)
  THROW 51271, 'An immutable policy identity cannot be edited in place.', 1;
END
ELSE INSERT dbo.DelphiLivePolicyVersion
(DelphiLivePolicyVersionId,PolicyDefinitionName,PolicyDefinitionSchemaVersion,EvaluatorVersion,CollectorVersion,CollectorSourceContractVersion,
DecisionDossierVersion,DecisionDossierSchemaVersion,QuoteFillVersion,ShadowPortfolioVersion,ResearchOutcomeVersion,RankingDiagnosticVersion,PromotionProtocolVersion,
SettingsJson,SettingsEncoding,SettingsSha256,InitialActivationState,DecisionRef)
VALUES(@Policy,@Name,@Schema,@Evaluator,@Collector,@Source,@Dossier,@DossierSchema,@Quote,@Shadow,@Outcome,@Ranking,@Promotion,
@Settings,N'UTF-8',@Hash,N'Inactive',@DecisionRef);
""", c, t);
        command.Parameters.AddRange([P("@Policy", policy.PolicyVersionId), P("@Name", policy.PolicyDefinitionName, 64),
            P("@Schema", policy.PolicyDefinitionSchemaVersion), P("@Evaluator", policy.EvaluatorVersion, 64),
            P("@Collector", policy.CollectorVersion, 64), P("@Source", policy.CollectorSourceContractVersion),
            P("@Dossier", policy.DecisionDossierVersion, 64), P("@DossierSchema", policy.DecisionDossierSchemaVersion),
            P("@Quote", policy.QuoteFillVersion, 64), P("@Shadow", policy.ShadowPortfolioVersion, 64),
            P("@Outcome", policy.ResearchOutcomeVersion, 64), P("@Ranking", policy.RankingDiagnosticVersion, 64),
            P("@Promotion", policy.PromotionProtocolVersion, 64), P("@Settings", json),
            new SqlParameter("@Hash", SqlDbType.Binary, 32) { Value = hash }, P("@DecisionRef", decisionRef, 64)]);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await Fence(c, t, lease, cancellationToken); await t.CommitAsync(cancellationToken);
    }

    public async Task<DelphiLiveExperimentState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await Open(cancellationToken);
        return await Load(connection, null, false, cancellationToken);
    }

    public async Task<DelphiLiveExperimentState> CommitAsync(long expectedRevision, DelphiLiveExperimentState next,
        Guid commandId, string eventKind, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        await using var connection = await Open(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await Fence(connection, transaction, lease, cancellationToken);
        var prior = await Load(connection, transaction, true, cancellationToken);
        if (prior is not null && prior.Revision == next.Revision && Json(prior) == Json(next)) return prior;
        ValidateRevision(prior, expectedRevision, next);
        if (prior is not null && (prior.ChampionPolicyVersionId != next.ChampionPolicyVersionId ||
            Json(prior.Definition) != Json(next.Definition)))
            throw new InvalidOperationException("Policy/experiment replacement requires the atomic session-boundary path.");
        if (prior is null)
        {
            await Execute(connection, transaction, """
IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLivePortfolioLedger l
 JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId
 WHERE l.PortfolioId=@Portfolio AND l.DelphiLivePolicyVersionId=@Champion AND g.PortfolioRole=N'OperationalChampion')
 THROW 51262, 'Experiment initialization requires an already activated operational portfolio.', 1;
""", cancellationToken, P("@Portfolio", next.OperationalPortfolioId), P("@Champion", next.ChampionPolicyVersionId));
        }
        await WriteState(connection, transaction, next, expectedRevision, commandId, eventKind, cancellationToken);
        await Fence(connection, transaction, lease, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    public async Task<DelphiLiveExperimentState> ApplyBoundaryAsync(long expectedRevision, DelphiLiveExperimentState next,
        DelphiLiveExperimentBoundaryPlan plan, DateOnly tradingDate, DateTime sessionOpenUtc,
        DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        await using var connection = await Open(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await Fence(connection, transaction, lease, cancellationToken);
        var prior = await Load(connection, transaction, true, cancellationToken)
            ?? throw new InvalidOperationException("Experiment protocol does not exist.");
        if (prior.Revision == next.Revision && Json(prior) == Json(next)) return prior;
        ValidateRevision(prior, expectedRevision, next);
        bool automaticBaselineEnd = plan.Kind == "EndBaseline" && prior.Phase == DelphiLiveExperimentPhase.ShadowBaseline &&
            prior.BaselineCohorts.Count(c => c.IsClean) >= 30;
        bool automaticInvalidEnd = plan.Kind == "StopInvalidExperiment" && prior.Definition is not null &&
            prior.Phase is DelphiLiveExperimentPhase.EngineeringShakedown or DelphiLiveExperimentPhase.Invalidated;
        if ((!automaticBaselineEnd && !automaticInvalidEnd && Json(prior.PendingBoundary) != Json(plan)) || tradingDate < plan.EffectiveSession ||
            sessionOpenUtc.Kind != DateTimeKind.Utc || plan.AuthorizedUtc >= plan.EffectiveSessionOpenUtc)
            throw new InvalidOperationException("The boundary must apply its exact persisted, pre-session authorized command.");
        await Execute(connection, transaction, """
IF SYSUTCDATETIME() < @Open THROW 51263, 'The effective regular session has not started.', 1;
IF EXISTS (SELECT 1 FROM dbo.DelphiLiveSession WITH (UPDLOCK,HOLDLOCK) WHERE TradingDate=@Date)
 THROW 51264, 'Policy assignments are already frozen for this session.', 1;
UPDATE dbo.DelphiLivePortfolioGeneration SET EndExclusiveTradingDate=@Date
 WHERE PortfolioRole<>N'OperationalChampion' AND EndExclusiveTradingDate IS NULL;
UPDATE dbo.DelphiLivePolicyAssignment SET EndExclusiveTradingDate=@Date
 WHERE RoleSlot IN (1,2) AND EndExclusiveTradingDate IS NULL AND CancelledUtc IS NULL;
""", cancellationToken, P("@Open", sessionOpenUtc), P("@Date", tradingDate));
        if (plan.Kind == "Promote")
        {
            if (plan.PromotionEvidence?.EligibleForHumanReview != true || plan.SelectedChallenger != next.ChampionPolicyVersionId)
                throw new InvalidOperationException("A promotion requires persisted passing evidence and human approval.");
            await PromoteOperational(connection, transaction, prior, next, plan, tradingDate, lease, cancellationToken);
        }
        if (plan.Kind is not ("EndBaseline" or "StopInvalidExperiment"))
        {
            var definition = next.Definition!;
            var challengerIds = plan.Kind == "StartUntouched"
                ? new[] { next.SelectedChallenger!.Value }
                : definition.ChallengerPolicyVersionIds.ToArray();
            await CreateComparisonPortfolio(connection, transaction, next.ChampionPolicyVersionId,
                "ChampionControl", 0, definition, plan, tradingDate, sessionOpenUtc, cancellationToken);
            for (int index = 0; index < challengerIds.Length; index++)
                await CreateComparisonPortfolio(connection, transaction, challengerIds[index],
                    next.Phase == DelphiLiveExperimentPhase.ShadowBaseline ? "ShadowBaseline" : "ActiveShadowChallenger",
                    index + 1, definition, plan, tradingDate, sessionOpenUtc, cancellationToken);
        }
        await WriteState(connection, transaction, next, expectedRevision, Guid.NewGuid(), plan.Kind + "Applied", cancellationToken);
        await Fence(connection, transaction, lease, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    public async Task RecordExpectedSlotsAsync(IReadOnlyCollection<DelphiLiveExpectedResearchSlot> slots,
        DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slots);
        foreach (var slot in slots)
        {
            if (slot.SlotId == Guid.Empty || slot.SessionId == Guid.Empty || slot.BarEndUtc.Kind != DateTimeKind.Utc ||
                string.IsNullOrWhiteSpace(slot.Symbol) || slot.Symbol.Length > 20 || slot.Symbol != slot.Symbol.Trim().ToUpperInvariant() ||
                slot.IsBenchmark != (slot.Symbol == "XIU") ||
                slot.SlotId != DelphiLiveResearchCoordinator.StableId($"slot/{slot.SessionId:D}/{slot.Symbol}/{slot.BarEndUtc:O}"))
                throw new ArgumentException("Expected research slots require canonical symbol/session/checkpoint identities.");
        }
        await using var connection = await Open(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await Fence(connection, transaction, lease, cancellationToken);
        foreach (var slot in slots)
        {
            await Execute(connection, transaction, """
IF EXISTS (SELECT 1 FROM dbo.DelphiLiveExpectedResearchSlot WHERE SessionId=@Session AND Symbol=@Symbol AND BarEndUtc=@End)
BEGIN
 IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveExpectedResearchSlot WHERE SessionId=@Session AND Symbol=@Symbol AND BarEndUtc=@End AND SlotJson=@Json)
  THROW 51265, 'Expected research slots cannot be replaced or operationally repaired by later data.', 1;
END
ELSE
BEGIN
 -- Derive the denominator from frozen membership and the canonical session
 -- schedule. Neither a caller nor a late recovery may create another grid.
 IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveSession s
 JOIN dbo.DelphiLiveSessionSymbol m ON m.SessionId=s.SessionId
 WHERE s.SessionId=@Session AND s.TradingDate=@Date AND m.Symbol=@Symbol AND m.IsXiuBenchmark=@Benchmark
 AND @End BETWEEN m.RequiredFromBarEndUtc AND m.RequiredThroughBarEndUtc
 AND @End>s.SessionOpenUtc AND @End<=s.SessionCloseUtc
 AND DATEDIFF(MINUTE,s.SessionOpenUtc,@End)%5=0
 AND @End=DATEADD(MINUTE,DATEDIFF(MINUTE,s.SessionOpenUtc,@End),s.SessionOpenUtc))
  THROW 51272, 'Expected research slot is outside its immutable session membership or five-minute grid.', 1;
 IF EXISTS (SELECT 1 FROM dbo.IntradayCollectionSlot WHERE SessionId=@Session AND Symbol=@Symbol AND ExpectedBarEndUtc=@End)
 BEGIN
  IF NOT EXISTS (SELECT 1 FROM dbo.IntradayCollectionSlot sl
  JOIN dbo.IntradayCollectionCycle c ON c.CycleId=sl.CycleId
  WHERE sl.SessionId=@Session AND sl.Symbol=@Symbol AND sl.ExpectedBarEndUtc=@End
  AND sl.IntervalMinutes=5 AND c.CollectorVersion=N'IntradayEvidenceCollectorV3' AND c.SourceContractVersion=1
  AND c.CycleStatus NOT IN(N'Planned',N'Collecting') AND sl.Disposition<>N'Pending'
  AND sl.SettledUtc IS NOT NULL AND sl.SettledUtc<=SYSUTCDATETIME()
  AND (sl.EvidenceBarId=@Anchor OR (sl.EvidenceBarId IS NULL AND @Anchor IS NULL))
  AND @Disposition=sl.Disposition AND @Operational=sl.OperationallyUsable)
   THROW 51273, 'Expected research slot must preserve its settled canonical operational result.', 1;
 END
 ELSE IF SYSUTCDATETIME()<DATEADD(MINUTE,7,@End) OR @Anchor IS NOT NULL OR @Operational<>0 OR @Disposition<>N'MissingScheduledSlot'
  THROW 51274, 'An absent scheduled slot can be frozen as a miss only after its deadline.', 1;
 INSERT dbo.DelphiLiveExpectedResearchSlot (SlotId,SessionId,TradingDate,BarEndUtc,Symbol,IsBenchmark,SlotJson)
 VALUES(@Slot,@Session,@Date,@End,@Symbol,@Benchmark,@Json);
END;
""", cancellationToken, P("@Slot", slot.SlotId), P("@Session", slot.SessionId), P("@Date", slot.TradingDate),
                P("@End", slot.BarEndUtc), P("@Symbol", slot.Symbol, 20), P("@Benchmark", slot.IsBenchmark), P("@Json", Json(slot)),
                new SqlParameter("@Anchor", SqlDbType.UniqueIdentifier) { Value = (object?)slot.AnchorObservationId ?? DBNull.Value },
                P("@Operational", slot.OperationalUsable), P("@Disposition", slot.OperationalDisposition, 32));
        }
        await Fence(connection, transaction, lease, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordRankingCheckpointAsync(DelphiLiveRankingCheckpoint checkpoint,
        DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        await FencedWrite(lease, """
IF EXISTS (SELECT 1 FROM dbo.DelphiLiveRankingCheckpoint WHERE SessionId=@Session AND BarEndUtc=@End AND Lens=@Lens)
BEGIN
 IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveRankingCheckpoint WHERE SessionId=@Session AND BarEndUtc=@End AND Lens=@Lens AND CheckpointJson=@Json)
  THROW 51266, 'A pre-portfolio ranking checkpoint cannot be rewritten.', 1;
END
ELSE INSERT dbo.DelphiLiveRankingCheckpoint (CheckpointId,SessionId,TradingDate,BarEndUtc,Lens,ChampionPolicyVersionId,CheckpointJson)
VALUES(@Id,@Session,@Date,@End,@Lens,@Policy,@Json);
""", cancellationToken, P("@Id", checkpoint.CheckpointId), P("@Session", checkpoint.SessionId), P("@Date", checkpoint.TradingDate),
            P("@End", checkpoint.BarEndUtc), P("@Lens", checkpoint.Lens.ToString(), 16),
            P("@Policy", checkpoint.ChampionPolicyVersionId), P("@Json", Json(checkpoint)));
    }

    public async Task AppendOutcomeAsync(DelphiLiveResearchOutcomeRevision revision,
        DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        await FencedWrite(lease, """
IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveExpectedResearchSlot WHERE SlotId=@Slot AND IsBenchmark=0)
 THROW 51267, 'Only an expected stock slot can receive a research outcome.', 1;
IF EXISTS (SELECT 1 FROM dbo.DelphiLiveResearchOutcomeRevision WHERE RevisionId=@Id OR (SlotId=@Slot AND CalculatedUtc=@Time))
BEGIN
 IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveResearchOutcomeRevision WHERE RevisionId=@Id AND SlotId=@Slot AND CalculatedUtc=@Time AND OutcomeJson=@Json)
  THROW 51268, 'An outcome revision is immutable.', 1;
END
ELSE INSERT dbo.DelphiLiveResearchOutcomeRevision (RevisionId,SlotId,CalculatedUtc,OutcomeJson) VALUES(@Id,@Slot,@Time,@Json);
""", cancellationToken, P("@Id", revision.RevisionId), P("@Slot", revision.SlotId), P("@Time", revision.CalculatedUtc), P("@Json", Json(revision)));
    }

    public Task<IReadOnlyList<DelphiLiveExpectedResearchSlot>> ReadExpectedSlotsAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default) => ReadJson<DelphiLiveExpectedResearchSlot>(
        "SELECT SlotJson FROM dbo.DelphiLiveExpectedResearchSlot WHERE TradingDate BETWEEN @From AND @Through ORDER BY TradingDate,BarEndUtc,Symbol;", from, through, cancellationToken);
    public Task<IReadOnlyList<DelphiLiveResearchOutcomeRevision>> ReadLatestOutcomesAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default) => ReadJson<DelphiLiveResearchOutcomeRevision>("""
SELECT latest.OutcomeJson FROM dbo.DelphiLiveExpectedResearchSlot s
CROSS APPLY (SELECT TOP(1) r.OutcomeJson FROM dbo.DelphiLiveResearchOutcomeRevision r WHERE r.SlotId=s.SlotId ORDER BY r.CalculatedUtc DESC,r.RevisionId) latest
WHERE s.TradingDate BETWEEN @From AND @Through ORDER BY s.TradingDate,s.BarEndUtc,s.Symbol;
""", from, through, cancellationToken);
    public Task<IReadOnlyList<DelphiLiveRankingCheckpoint>> ReadRankingCheckpointsAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default) => ReadJson<DelphiLiveRankingCheckpoint>(
        "SELECT CheckpointJson FROM dbo.DelphiLiveRankingCheckpoint WHERE TradingDate BETWEEN @From AND @Through ORDER BY TradingDate,BarEndUtc,Lens;", from, through, cancellationToken);

    private static async Task PromoteOperational(SqlConnection c, SqlTransaction t, DelphiLiveExperimentState prior,
        DelphiLiveExperimentState next, DelphiLiveExperimentBoundaryPlan plan, DateOnly date, DelphiLiveLease lease, CancellationToken token)
    {
        await using var read = Command(c, t, "SELECT SnapshotJson FROM dbo.DelphiLivePortfolioLedger WITH(UPDLOCK,HOLDLOCK) WHERE PortfolioId=@Id;", P("@Id", prior.OperationalPortfolioId));
        var portfolio = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>((string)(await read.ExecuteScalarAsync(token))!);
        var promoted = portfolio with { PolicyVersionId = next.ChampionPolicyVersionId, Revision = portfolio.Revision + 1, UpdatedUtc = next.UpdatedUtc };
        DelphiLiveLedgerIntegrity.ValidatePolicyPromotion(portfolio, promoted);
        await Execute(c, t, """
UPDATE dbo.DelphiLivePolicyAssignment SET EndExclusiveTradingDate=@Date
 WHERE RoleSlot=0 AND EndExclusiveTradingDate IS NULL AND CancelledUtc IS NULL;
INSERT dbo.DelphiLivePolicyAssignment (AssignmentId,DelphiLivePolicyVersionId,PolicyRole,RoleSlot,ExperimentId,EffectiveTradingDate,AuthorizedUtc,AuthorizedBy,AuthorizationReason,DecisionRef)
VALUES(@Assignment,@Policy,N'OperationalChampion',0,NULL,@Date,@Authorized,@By,@Reason,N'ADR-0053');
UPDATE dbo.DelphiLivePortfolioLedger SET DelphiLivePolicyVersionId=@Policy,Revision=@Revision,SnapshotJson=@Json,UpdatedUtc=@Now WHERE PortfolioId=@Portfolio;
INSERT dbo.DelphiLivePortfolioRevision (PortfolioId,Revision,SnapshotJson,LeaseId,LeaseFencingToken) VALUES(@Portfolio,@Revision,@Json,@Lease,@Fence);
INSERT dbo.DelphiLiveLedgerEvent (EventId,PortfolioId,Revision,EventKind,RecordedUtc,DataJson)
VALUES(@Event,@Portfolio,@Revision,N'HumanApprovedPolicyPromotion',@Now,@Plan);
""", token, P("@Date", date), P("@Assignment", Guid.NewGuid()), P("@Policy", next.ChampionPolicyVersionId),
            P("@Authorized", plan.AuthorizedUtc), P("@By", plan.AuthorizedBy, 128), P("@Reason", plan.Reason, 1024),
            P("@Portfolio", portfolio.PortfolioId), P("@Revision", promoted.Revision), P("@Json", Json(promoted)),
            P("@Now", next.UpdatedUtc), P("@Lease", lease.LeaseId), P("@Fence", lease.FencingToken), P("@Event", Guid.NewGuid()), P("@Plan", Json(plan)));
    }

    private static async Task CreateComparisonPortfolio(SqlConnection c, SqlTransaction t, Guid policyId,
        string role, int slot, DelphiLiveExperimentDefinition definition, DelphiLiveExperimentBoundaryPlan plan,
        DateOnly date, DateTime openUtc, CancellationToken token)
    {
        Guid assignment;
        if (slot == 0)
        {
            await using var query = Command(c, t, "SELECT AssignmentId FROM dbo.DelphiLivePolicyAssignment WHERE RoleSlot=0 AND DelphiLivePolicyVersionId=@Policy AND EndExclusiveTradingDate IS NULL AND CancelledUtc IS NULL;", P("@Policy", policyId));
            assignment = (Guid)(await query.ExecuteScalarAsync(token) ?? throw new InvalidOperationException("The operational champion assignment is absent."));
        }
        else
        {
            assignment = Guid.NewGuid();
            await Execute(c, t, """
INSERT dbo.DelphiLivePolicyAssignment (AssignmentId,DelphiLivePolicyVersionId,PolicyRole,RoleSlot,ExperimentId,EffectiveTradingDate,AuthorizedUtc,AuthorizedBy,AuthorizationReason,DecisionRef)
VALUES(@Assignment,@Policy,@Role,@Slot,@Experiment,@Date,@Authorized,@By,@Reason,N'ADR-0053');
""", token, P("@Assignment", assignment), P("@Policy", policyId), P("@Role", role, 32), P("@Slot", slot),
                P("@Experiment", definition.ExperimentId), P("@Date", date), P("@Authorized", plan.AuthorizedUtc),
                P("@By", plan.AuthorizedBy, 128), P("@Reason", plan.Reason, 1024));
        }
        var request = new DelphiLiveGenerationRequest(Guid.NewGuid(), Guid.NewGuid(), policyId, role,
            definition.ExperimentId, definition.StartingCapital, definition.Currency, date, openUtc,
            plan.AuthorizedUtc, plan.AuthorizedBy, plan.Reason);
        var state = DelphiLiveLedgerIntegrity.Create(request);
        await Execute(c, t, """
INSERT dbo.DelphiLivePortfolioGeneration (GenerationId,AssignmentId,DelphiLivePolicyVersionId,PortfolioRole,ExperimentId,StartingCapital,Currency,EffectiveTradingDate,EffectiveSessionOpenUtc,AuthorizedUtc,AuthorizedBy,AuthorizationReason,AuthorizationJson)
VALUES(@Generation,@Assignment,@Policy,@Role,@Experiment,@Capital,@Currency,@Date,@Open,@Authorized,@By,@Reason,@Authorization);
INSERT dbo.DelphiLivePortfolioLedger (PortfolioId,GenerationId,DelphiLivePolicyVersionId,Revision,SnapshotSchemaVersion,SnapshotJson,UpdatedUtc)
VALUES(@Portfolio,@Generation,@Policy,0,1,@Json,@Authorized);
INSERT dbo.DelphiLivePortfolioRevision (PortfolioId,Revision,SnapshotJson) VALUES(@Portfolio,0,@Json);
INSERT dbo.DelphiLiveLedgerEvent (EventId,PortfolioId,Revision,EventKind,RecordedUtc,DataJson) VALUES(@Event,@Portfolio,0,N'AlignedCashOnlyComparisonInception',@Authorized,@Authorization);
""", token, P("@Generation", request.GenerationId), P("@Assignment", assignment), P("@Policy", policyId), P("@Role", role, 32),
            P("@Experiment", definition.ExperimentId), P("@Capital", definition.StartingCapital), P("@Currency", definition.Currency, 3),
            P("@Date", date), P("@Open", openUtc), P("@Authorized", plan.AuthorizedUtc), P("@By", plan.AuthorizedBy, 128), P("@Reason", plan.Reason, 1024),
            P("@Authorization", Json(request)), P("@Portfolio", state.PortfolioId), P("@Json", Json(state)), P("@Event", Guid.NewGuid()));
    }

    private static async Task WriteState(SqlConnection c, SqlTransaction t, DelphiLiveExperimentState next,
        long expected, Guid command, string kind, CancellationToken token)
    {
        await Execute(c, t, """
IF @Expected=-1 INSERT dbo.DelphiLiveExperimentProtocol (ProtocolId,OperationalPortfolioId,Revision,SnapshotJson,UpdatedUtc) VALUES(@Id,@Portfolio,@Revision,@Json,@Now);
ELSE BEGIN
 UPDATE dbo.DelphiLiveExperimentProtocol SET Revision=@Revision,SnapshotJson=@Json,UpdatedUtc=@Now WHERE ProtocolId=@Id AND Revision=@Expected;
 IF @@ROWCOUNT<>1 THROW 51269, 'Experiment protocol revision conflict.', 1;
END;
INSERT dbo.DelphiLiveExperimentRevision (ProtocolId,Revision,SnapshotJson) VALUES(@Id,@Revision,@Json);
INSERT dbo.DelphiLiveExperimentEvent (CommandId,ProtocolId,Revision,EventKind,DataJson,RecordedUtc) VALUES(@Command,@Id,@Revision,@Kind,@Json,@Now);
""", token, P("@Expected", expected), P("@Id", next.ProtocolId), P("@Portfolio", next.OperationalPortfolioId),
            P("@Revision", next.Revision), P("@Json", Json(next)), P("@Now", next.UpdatedUtc), P("@Command", command), P("@Kind", kind, 64));
    }

    private static void ValidateRevision(DelphiLiveExperimentState? prior, long expected, DelphiLiveExperimentState next)
    {
        if (next.ProtocolId != DelphiLiveExperimentWorkflow.ProtocolId || next.Revision != expected + 1 ||
            next.UpdatedUtc.Kind != DateTimeKind.Utc || (prior is null) != (expected == -1) ||
            (prior is not null && (prior.Revision != expected || prior.OperationalPortfolioId != next.OperationalPortfolioId || next.UpdatedUtc < prior.UpdatedUtc)))
            throw new InvalidOperationException("Experiment protocol identity/revision changed; reload its durable state.");
    }

    private static async Task<DelphiLiveExperimentState?> Load(SqlConnection c, SqlTransaction? t, bool locked, CancellationToken token)
    {
        await using var command = Command(c, t, locked
            ? "SELECT SnapshotJson FROM dbo.DelphiLiveExperimentProtocol WITH(UPDLOCK,HOLDLOCK) WHERE ProtocolId=@Id;"
            : "SELECT SnapshotJson FROM dbo.DelphiLiveExperimentProtocol WHERE ProtocolId=@Id;", P("@Id", DelphiLiveExperimentWorkflow.ProtocolId));
        return await command.ExecuteScalarAsync(token) is string json ? DelphiLiveLedgerJson.Deserialize<DelphiLiveExperimentState>(json) : null;
    }
    private async Task<SqlConnection> Open(CancellationToken token)
    {
        var connection = new SqlConnection(ConnectionString);
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    private async Task FencedWrite(DelphiLiveLease lease, string sql, CancellationToken token, params SqlParameter[] parameters)
    {
        await using var c = await Open(token);
        await using var t = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await Fence(c, t, lease, token); await Execute(c, t, sql, token, parameters); await Fence(c, t, lease, token);
        await t.CommitAsync(token);
    }
    private async Task<IReadOnlyList<T>> ReadJson<T>(string sql, DateOnly from, DateOnly through, CancellationToken token)
    {
        await using var c = await Open(token);
        await using var command = Command(c, null, sql, P("@From", from), P("@Through", through));
        var result = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(DelphiLiveLedgerJson.Deserialize<T>(reader.GetString(0)));
        return result;
    }
    private static Task Fence(SqlConnection c, SqlTransaction t, DelphiLiveLease lease, CancellationToken token) => Execute(c, t, """
IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveHostLease WITH(UPDLOCK,HOLDLOCK)
 WHERE LeaseId=@Lease AND OwnerId=@Owner AND FencingToken=@Fence AND IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 51270, 'The experiment/research writer lost its durable host lease.', 1;
""", token, P("@Lease", lease.LeaseId), P("@Owner", lease.OwnerId, 128), P("@Fence", lease.FencingToken));
    private static async Task Execute(SqlConnection c, SqlTransaction t, string sql, CancellationToken token, params SqlParameter[] parameters)
    {
        await using var command = Command(c, t, sql, parameters); await command.ExecuteNonQueryAsync(token);
    }
    private static SqlCommand Command(SqlConnection c, SqlTransaction? t, string sql, params SqlParameter[] parameters)
    {
        var command = new SqlCommand(sql, c, t); command.Parameters.AddRange(parameters); return command;
    }
    private static string Json<T>(T value) => DelphiLiveLedgerJson.Serialize(value);
    private static SqlParameter P(string name, object value, int size = -1)
    {
        var parameter = value switch
        {
            Guid => new SqlParameter(name, SqlDbType.UniqueIdentifier),
            DateOnly => new SqlParameter(name, SqlDbType.Date),
            DateTime => new SqlParameter(name, SqlDbType.DateTime2),
            bool => new SqlParameter(name, SqlDbType.Bit),
            int => new SqlParameter(name, SqlDbType.Int),
            long => new SqlParameter(name, SqlDbType.BigInt),
            decimal => new SqlParameter(name, SqlDbType.Decimal) { Precision = 28, Scale = 6 },
            _ => new SqlParameter(name, SqlDbType.NVarChar, size)
        };
        parameter.Value = value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : value;
        return parameter;
    }
}
