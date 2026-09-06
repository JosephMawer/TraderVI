#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveExperimentBoundaryPlan(
    Guid CommandId, string Kind, DelphiLiveExperimentDefinition Definition,
    DateOnly EffectiveSession, DateTime EffectiveSessionOpenUtc,
    DateTime AuthorizedUtc, string AuthorizedBy, string Reason,
    Guid? SelectedChallenger, DelphiLivePromotionScore? PromotionEvidence);

public sealed record DelphiLiveExperimentState(
    Guid ProtocolId, Guid OperationalPortfolioId, Guid ChampionPolicyVersionId,
    long Revision, DelphiLiveExperimentPhase Phase,
    DelphiLiveExperimentDefinition? Definition, Guid? SelectedChallenger,
    ImmutableArray<DelphiLiveCohortEvidence> EngineeringCohorts,
    ImmutableArray<DelphiLiveCohortEvidence> DiscoveryCohorts,
    ImmutableArray<DelphiLiveCohortEvidence> UntouchedCohorts,
    ImmutableArray<DelphiLiveCohortEvidence> BaselineCohorts,
    DelphiLiveExperimentBoundaryPlan? PendingBoundary,
    DateTime UpdatedUtc, string LastReason)
{
    public DateTime PhaseStartedUtc { get; init; }
}

public interface IDelphiLiveExperimentStore
{
    Task<DelphiLiveExperimentState?> LoadAsync(CancellationToken cancellationToken = default);
    Task<DelphiLiveExperimentState> CommitAsync(long expectedRevision, DelphiLiveExperimentState next,
        Guid commandId, string eventKind, DelphiLiveLease lease, CancellationToken cancellationToken = default);
    // Boundary application includes comparison generations, role assignments,
    // operational policy carry and protocol revision in one transaction.
    Task<DelphiLiveExperimentState> ApplyBoundaryAsync(long expectedRevision, DelphiLiveExperimentState next,
        DelphiLiveExperimentBoundaryPlan plan, DateOnly tradingDate, DateTime sessionOpenUtc,
        DelphiLiveLease lease, CancellationToken cancellationToken = default);
}

public interface IDelphiLiveResearchStore
{
    Task RecordExpectedSlotsAsync(IReadOnlyCollection<DelphiLiveExpectedResearchSlot> slots,
        DelphiLiveLease lease, CancellationToken cancellationToken = default);
    Task RecordRankingCheckpointAsync(DelphiLiveRankingCheckpoint checkpoint,
        DelphiLiveLease lease, CancellationToken cancellationToken = default);
    Task AppendOutcomeAsync(DelphiLiveResearchOutcomeRevision revision,
        DelphiLiveLease lease, CancellationToken cancellationToken = default);
    Task RecordSessionReviewAsync(Guid sessionId, DateTime reviewedUtc, DelphiLiveLease lease, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DelphiLiveExpectedResearchSlot>> ReadExpectedSlotsAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DelphiLiveResearchOutcomeRevision>> ReadLatestOutcomesAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DelphiLiveRankingCheckpoint>> ReadRankingCheckpointsAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default);
}

public sealed class DelphiLiveExperimentWorkflow(IDelphiLiveExperimentStore store)
{
    public static readonly Guid ProtocolId = Guid.Parse("1FA71BAC-0995-5BAF-84FD-296FF003BEAF");

    public async Task<DelphiLiveExperimentState> InitializeAsync(Guid operationalPortfolioId,
        Guid championPolicyId, DateTime nowUtc, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        var existing = await store.LoadAsync(cancellationToken);
        if (existing is not null) return existing;
        if (operationalPortfolioId == Guid.Empty || championPolicyId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Initialization requires the existing activated portfolio and champion identity.");
        var initial = new DelphiLiveExperimentState(ProtocolId, operationalPortfolioId, championPolicyId, 0,
            DelphiLiveExperimentPhase.EngineeringShakedown, null, null, [], [], [], [], null, nowUtc, "EngineeringShakedownStarted")
            { PhaseStartedUtc = nowUtc };
        return await store.CommitAsync(-1, initial, Guid.NewGuid(), initial.LastReason, lease, cancellationToken);
    }

    public async Task<DelphiLiveExperimentState> RecordCohortAsync(DelphiLiveCohortEvidence cohort,
        DateTime nowUtc, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        var current = await Required(cancellationToken);
        DelphiLiveExperimentPolicy.ValidateCohorts([cohort]);
        var next = current with { Revision = current.Revision + 1, UpdatedUtc = nowUtc, LastReason = "CohortEvidenceRecorded" };
        // Maturity belongs to the phase in which the market session occurred,
        // even when its five-session endpoint arrives in a later phase.
        if (current.EngineeringCohorts.Any(c => c.SessionDate == cohort.SessionDate))
            next = next with { EngineeringCohorts = Upsert(current.EngineeringCohorts, cohort) };
        else if (current.DiscoveryCohorts.Any(c => c.SessionDate == cohort.SessionDate))
            next = next with { DiscoveryCohorts = Upsert(current.DiscoveryCohorts, cohort) };
        else if (current.UntouchedCohorts.Any(c => c.SessionDate == cohort.SessionDate))
            next = next with { UntouchedCohorts = Upsert(current.UntouchedCohorts, cohort) };
        else if (current.BaselineCohorts.Any(c => c.SessionDate == cohort.SessionDate))
            next = next with { BaselineCohorts = Upsert(current.BaselineCohorts, cohort) };
        else
            next = current.Phase switch
            {
                DelphiLiveExperimentPhase.EngineeringShakedown => next with { EngineeringCohorts = Upsert(current.EngineeringCohorts, cohort) },
                DelphiLiveExperimentPhase.Discovery => next with { DiscoveryCohorts = Upsert(current.DiscoveryCohorts, cohort) },
                DelphiLiveExperimentPhase.UntouchedConfirmation or DelphiLiveExperimentPhase.PromotionScheduled =>
                    next with { UntouchedCohorts = Upsert(current.UntouchedCohorts, cohort) },
                DelphiLiveExperimentPhase.ShadowBaseline => next with { BaselineCohorts = Upsert(current.BaselineCohorts, cohort) },
                _ => next
            };
        if (cohort.CapitalChanged)
            next = next with { Phase = DelphiLiveExperimentPhase.Invalidated, PendingBoundary = null, LastReason = "CapitalChangeUnsupportedV1" };
        return await store.CommitAsync(current.Revision, next, Guid.NewGuid(), next.LastReason, lease, cancellationToken);
    }

    public async Task<DelphiLiveExperimentState> RecordMeasurementDefectAsync(string reason, DateTime nowUtc,
        DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A defect reason is required.");
        var current = await Required(cancellationToken);
        var next = current with
        {
            Revision = current.Revision + 1, Phase = DelphiLiveExperimentPhase.EngineeringShakedown,
            EngineeringCohorts = [], PendingBoundary = null, UpdatedUtc = nowUtc,
            PhaseStartedUtc = nowUtc,
            LastReason = "MeasurementDefectRestartedShakedown: " + reason
        };
        return await store.CommitAsync(current.Revision, next, Guid.NewGuid(), "MeasurementDefectRestartedShakedown", lease, cancellationToken);
    }

    public async Task<DelphiLiveExperimentState> ScheduleDiscoveryAsync(DelphiLiveExperimentBoundaryPlan command,
        IReadOnlyDictionary<Guid, DelphiLivePolicyDefinition> policies, DelphiLiveLease lease,
        CancellationToken cancellationToken = default)
    {
        var current = await Required(cancellationToken);
        ValidateCommand(command, "StartDiscovery");
        DelphiLiveExperimentPolicy.ValidateDefinition(command.Definition, policies);
        if (current.PendingBoundary is not null ||
            current.Phase is not (DelphiLiveExperimentPhase.EngineeringShakedown or DelphiLiveExperimentPhase.Completed or DelphiLiveExperimentPhase.Invalidated) ||
            current.EngineeringCohorts.Count(c => c.IsClean) < 10 ||
            command.Definition.ChampionPolicyVersionId != current.ChampionPolicyVersionId)
            throw new InvalidOperationException("Discovery requires ten clean engineering cohorts, the current champion, and an available comparison boundary.");
        return await Schedule(current, command, lease, cancellationToken);
    }

    public async Task<DelphiLiveExperimentState> ScheduleUntouchedAsync(DelphiLiveExperimentBoundaryPlan command,
        DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        var current = await Required(cancellationToken);
        ValidateCommand(command, "StartUntouched");
        if (current.Phase != DelphiLiveExperimentPhase.Discovery || current.Definition is not { } definition ||
            current.PendingBoundary is not null || command.SelectedChallenger is not Guid selected ||
            !definition.ChallengerPolicyVersionIds.Contains(selected) ||
            DelphiLiveLedgerJson.Serialize(command.Definition) != DelphiLiveLedgerJson.Serialize(definition) ||
            current.DiscoveryCohorts.Count(c => DelphiLiveExperimentPolicy.IsPairedDiscovery(c, definition)) < 30)
            throw new InvalidOperationException("Untouched confirmation selects one predeclared contender after thirty eligible paired discovery cohorts for every contender.");
        return await Schedule(current, command, lease, cancellationToken);
    }

    public async Task<DelphiLiveExperimentState> ApprovePromotionAsync(DelphiLiveExperimentBoundaryPlan command,
        DelphiLivePolicyDefinition policy, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        var current = await Required(cancellationToken);
        ValidateCommand(command, "Promote");
        if (current.Phase != DelphiLiveExperimentPhase.UntouchedConfirmation || current.Definition is not { } definition ||
            current.SelectedChallenger is not Guid selected || current.PendingBoundary is not null ||
            command.SelectedChallenger != selected ||
            DelphiLiveLedgerJson.Serialize(command.Definition) != DelphiLiveLedgerJson.Serialize(definition))
            throw new InvalidOperationException("Only the frozen untouched contender may be submitted for human promotion.");
        DelphiLivePromotionScore score = DelphiLiveExperimentPolicy.Score(definition, selected,
            current.DiscoveryCohorts, current.UntouchedCohorts, policy);
        if (!score.EligibleForHumanReview)
            throw new InvalidOperationException("NotProvenRetainV1: " + string.Join(", ", score.FailureReasons));
        // Recalculate from durable evidence rather than trusting a UI pass flag.
        return await Schedule(current with { Phase = DelphiLiveExperimentPhase.PromotionScheduled },
            command with { PromotionEvidence = score }, lease, cancellationToken);
    }

    public async Task<DelphiLiveExperimentState> ApplySessionBoundaryAsync(DateOnly tradingDate,
        DateTime sessionOpenUtc, DateTime nowUtc, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        var current = await Required(cancellationToken);
        if (sessionOpenUtc.Kind != DateTimeKind.Utc || nowUtc.Kind != DateTimeKind.Utc || nowUtc < sessionOpenUtc)
            throw new ArgumentException("A policy change can apply only at or after its regular-session boundary.");
        var plan = current.PendingBoundary;
        if (plan is null && current.Definition is not null && current.Phase is
            DelphiLiveExperimentPhase.EngineeringShakedown or DelphiLiveExperimentPhase.Invalidated)
            plan = new(Guid.NewGuid(), "StopInvalidExperiment", current.Definition, tradingDate, sessionOpenUtc,
                current.UpdatedUtc, "DelphiLivePromotionV1", current.LastReason, null, null);
        if (plan is null && current.Phase == DelphiLiveExperimentPhase.ShadowBaseline && current.BaselineCohorts.Count(c => c.IsClean) >= 30)
            plan = new(Guid.NewGuid(), "EndBaseline", current.Definition!, tradingDate, sessionOpenUtc,
                current.UpdatedUtc, "DelphiLivePromotionV1", "ThirtyCleanBaselineSessionsCompleted", null, null);
        if (plan is null || tradingDate < plan.EffectiveSession) return current;
        if (plan.Kind == "Promote")
        {
            var refreshed = DelphiLiveExperimentPolicy.Score(current.Definition!, current.SelectedChallenger!.Value,
                current.DiscoveryCohorts, current.UntouchedCohorts, DelphiLivePolicyDefinition.Version1);
            if (!refreshed.EligibleForHumanReview)
            {
                var cancelled = current with { Revision = current.Revision + 1, PendingBoundary = null,
                    Phase = DelphiLiveExperimentPhase.UntouchedConfirmation, UpdatedUtc = nowUtc,
                    LastReason = "PromotionCancelledEvidenceInvalidated" };
                return await store.CommitAsync(current.Revision, cancelled, Guid.NewGuid(), cancelled.LastReason, lease, cancellationToken);
            }
        }
        var next = current with { Revision = current.Revision + 1, PendingBoundary = null, UpdatedUtc = nowUtc, LastReason = plan.Kind,
            PhaseStartedUtc = sessionOpenUtc };
        switch (plan.Kind)
        {
            case "StartDiscovery":
                next = next with { Definition = plan.Definition, Phase = DelphiLiveExperimentPhase.Discovery,
                    SelectedChallenger = null, DiscoveryCohorts = [], UntouchedCohorts = [], BaselineCohorts = [] };
                break;
            case "StartUntouched":
                next = next with { Phase = DelphiLiveExperimentPhase.UntouchedConfirmation,
                    SelectedChallenger = plan.SelectedChallenger, UntouchedCohorts = [] };
                break;
            case "Promote":
                var baseline = new DelphiLiveExperimentDefinition(Guid.NewGuid(), plan.SelectedChallenger!.Value,
                    [current.ChampionPolicyVersionId], plan.Definition.HypothesisFamily,
                    plan.Definition.StartingCapital, plan.Definition.Currency, plan.Definition.CodeIdentity);
                next = next with { ChampionPolicyVersionId = plan.SelectedChallenger.Value, Definition = baseline,
                    Phase = DelphiLiveExperimentPhase.ShadowBaseline, SelectedChallenger = null, BaselineCohorts = [] };
                break;
            case "EndBaseline": next = next with { Phase = DelphiLiveExperimentPhase.Completed }; break;
            case "StopInvalidExperiment": next = next with { Definition = null, SelectedChallenger = null }; break;
            default: throw new InvalidOperationException("Unknown experiment boundary action.");
        }
        return await store.ApplyBoundaryAsync(current.Revision, next, plan, tradingDate, sessionOpenUtc, lease, cancellationToken);
    }

    private async Task<DelphiLiveExperimentState> Schedule(DelphiLiveExperimentState current,
        DelphiLiveExperimentBoundaryPlan command, DelphiLiveLease lease, CancellationToken cancellationToken)
    {
        var next = current with { Revision = current.Revision + 1, PendingBoundary = command,
            UpdatedUtc = command.AuthorizedUtc, LastReason = command.Kind + "Authorized" };
        return await store.CommitAsync(current.Revision, next, command.CommandId, next.LastReason, lease, cancellationToken);
    }

    private async Task<DelphiLiveExperimentState> Required(CancellationToken cancellationToken) =>
        await store.LoadAsync(cancellationToken) ?? throw new InvalidOperationException("The activated champion's experiment protocol is not initialized.");

    private static void ValidateCommand(DelphiLiveExperimentBoundaryPlan command, string expectedKind)
    {
        if (command.CommandId == Guid.Empty || command.Kind != expectedKind ||
            command.AuthorizedUtc.Kind != DateTimeKind.Utc || command.EffectiveSessionOpenUtc.Kind != DateTimeKind.Utc ||
            command.AuthorizedUtc >= command.EffectiveSessionOpenUtc ||
            string.IsNullOrWhiteSpace(command.AuthorizedBy) || string.IsNullOrWhiteSpace(command.Reason))
            throw new ArgumentException("An experiment change requires explicit human identity, reason, and authorization before its next-session boundary.");
    }

    private static ImmutableArray<DelphiLiveCohortEvidence> Upsert(ImmutableArray<DelphiLiveCohortEvidence> existing,
        DelphiLiveCohortEvidence cohort)
    {
        int index = -1;
        for (int i = 0; i < existing.Length; i++) if (existing[i].SessionDate == cohort.SessionDate) index = i;
        if (index < 0) return existing.Add(cohort).OrderBy(c => c.SessionDate).ToImmutableArray();
        var prior = existing[index];
        if (DelphiLiveLedgerJson.Serialize(prior with { FiveSessionResearchMature = cohort.FiveSessionResearchMature,
                CorporateActionUnsupported = cohort.CorporateActionUnsupported, EvidenceConflict = cohort.EvidenceConflict }) !=
                DelphiLiveLedgerJson.Serialize(cohort) || prior.FiveSessionResearchMature && !cohort.FiveSessionResearchMature ||
                prior.CorporateActionUnsupported && !cohort.CorporateActionUnsupported || prior.EvidenceConflict && !cohort.EvidenceConflict)
            throw new InvalidOperationException("A cohort's operational facts and original policy returns cannot be rewritten by later research recovery.");
        return existing.SetItem(index, cohort);
    }
}
