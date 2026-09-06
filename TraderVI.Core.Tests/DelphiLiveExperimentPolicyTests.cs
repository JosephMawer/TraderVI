#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
namespace TraderVI.Core.Tests;

public sealed class DelphiLiveExperimentPolicyTests
{
    private static readonly Guid Champion = DelphiLivePolicyDefinition.Version1.PolicyVersionId;
    private static readonly Guid Challenger = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DelphiLiveExperimentDefinition Definition = new(Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Champion, [Challenger], DelphiLiveHypothesisFamily.RawMoveThreshold, 1000m, "CAD", "FrozenTestAssemblyHash");
    private static readonly DateTime Now = new(2026, 9, 5, 21, 0, 0, DateTimeKind.Utc);
    private static readonly DelphiLiveLease Lease = new(Guid.NewGuid(), "tests", 1, Now, Now.AddHours(1));

    [Fact]
    public void DeterministicUntouchedBootstrapUsesThirtyCohortsAndOwnRiskGates()
    {
        var discovery = Cohorts(0, 30, -.9m, -.8m);
        var untouched = Cohorts(30, 30, .001m, .002m);
        var first = DelphiLiveExperimentPolicy.Score(Definition, Challenger, discovery, untouched, DelphiLivePolicyDefinition.Version1);
        var second = DelphiLiveExperimentPolicy.Score(Definition, Challenger, discovery, untouched, DelphiLivePolicyDefinition.Version1);
        first.EligibleForHumanReview.ShouldBeTrue();
        first.Lower95.ShouldBe(.001m); first.Upper95.ShouldBe(.001m);
        first.BlockLength.ShouldBe(5); first.Resamples.ShouldBe(10000);
        DelphiLiveLedgerJson.Serialize(second).ShouldBe(DelphiLiveLedgerJson.Serialize(first));
        first.MeanDailyImprovement.ShouldBe(.001m); // discovery never enters primary interval
    }

    [Fact]
    public void IneligibleCohortsAndCalendarGapsDoNotCreateBootstrapBlocks()
    {
        var untouched = Cohorts(30, 30, 0m, .001m).Select((c, i) => c with { CanonicalSessionOrdinal = 100 + i * 2 }).ToArray();
        var score = DelphiLiveExperimentPolicy.Score(Definition, Challenger, Cohorts(0, 30), untouched, DelphiLivePolicyDefinition.Version1);
        score.EligibleForHumanReview.ShouldBeFalse();
        score.FailureReasons.ShouldContain("InsufficientConsecutiveBootstrapBlocks");
        score.Lower95.ShouldBeNull();
    }

    [Fact]
    public void ImprovementCannotCompensateForWorseDrawdownOrOwnWorstTail()
    {
        var untouched = Cohorts(30, 30, .001m, .003m);
        untouched[0] = untouched[0] with { MaximumCheckpointDrawdowns = ImmutableDictionary<Guid, decimal?>.Empty.Add(Champion, .02m).Add(Challenger, .03m),
            DailyPortfolioReturns = ImmutableDictionary<Guid, decimal?>.Empty.Add(Champion, .001m).Add(Challenger, -.02m) };
        var score = DelphiLiveExperimentPolicy.Score(Definition, Challenger, Cohorts(0, 30), untouched, DelphiLivePolicyDefinition.Version1);
        score.FailureReasons.ShouldContain("WorseMaximumCheckpointDrawdown");
        score.FailureReasons.ShouldContain("WorseOwnWorstDecileReturn");
    }

    [Fact]
    public void UnknownRegimeIsRecordedButCannotBePairedPromotionEvidence()
    {
        var row = Cohorts(0, 1)[0] with { Regime = "Unavailable" };
        Should.NotThrow(() => DelphiLiveExperimentPolicy.ValidateCohorts([row]));
        row.IsClean.ShouldBeTrue();
        DelphiLiveExperimentPolicy.IsPaired(row, Champion, Challenger).ShouldBeFalse();
        DelphiLiveResearchCoordinator.ReadFrozenRegime("{\"regime\":{\"isBothBearish\":false,\"isAnyBenchmarkUptrend\":true}}").ShouldBe("Bullish");
        DelphiLiveResearchCoordinator.ReadFrozenRegime("{\"regime\":{}}").ShouldBe("Unavailable");
        DelphiLiveResearchCoordinator.ReadFrozenRegime("{\"regime\":{\"isBothBearish\":true,\"isAnyBenchmarkUptrend\":true}}").ShouldBe("Unavailable");
    }

    [Fact]
    public void ExactlyOneSelectedFamilyMayVaryIncludingAfterJsonRoundtrip()
    {
        var champion = DelphiLivePolicyDefinition.Version1;
        var challenger = champion with { PolicyVersionId = Challenger, SelectedRawMoveThreshold = .15m };
        var restored = DelphiLiveLedgerJson.Deserialize<DelphiLivePolicyDefinition>(DelphiLiveLedgerJson.Serialize(challenger));
        DelphiLiveExperimentPolicy.ValidateDefinition(Definition, new Dictionary<Guid, DelphiLivePolicyDefinition> { [Champion] = champion, [Challenger] = restored });
        Should.Throw<ArgumentException>(() => DelphiLiveExperimentPolicy.ValidateOneFamily(champion,
            challenger with { SelectedExcessMoveThreshold = .10m }, DelphiLiveHypothesisFamily.RawMoveThreshold));
        Should.Throw<ArgumentException>(() => DelphiLiveExperimentPolicy.ValidateOneFamily(champion,
            challenger with { SelectedRawMoveThreshold = .25m }, DelphiLiveHypothesisFamily.RawMoveThreshold));
    }

    [Fact]
    public async Task MaturityAfterPhaseTransitionUpdatesOriginalCohortAndCorporateAuditOnlyExcludes()
    {
        var original = Cohorts(0, 1)[0] with { FiveSessionResearchMature = false };
        var store = new Store(State() with { Phase = DelphiLiveExperimentPhase.UntouchedConfirmation, DiscoveryCohorts = [original] });
        var workflow = new DelphiLiveExperimentWorkflow(store);
        var matured = await workflow.RecordCohortAsync(original with { FiveSessionResearchMature = true }, Now, Lease);
        matured.DiscoveryCohorts.Single().FiveSessionResearchMature.ShouldBeTrue();
        matured.UntouchedCohorts.ShouldBeEmpty();
        var flagged = await workflow.RecordCohortAsync(original with { FiveSessionResearchMature = true, CorporateActionUnsupported = true }, Now, Lease);
        flagged.DiscoveryCohorts.Single().CorporateActionUnsupported.ShouldBeTrue();
        await Should.ThrowAsync<InvalidOperationException>(() => workflow.RecordCohortAsync(original with { FiveSessionResearchMature = true }, Now, Lease));
    }

    [Fact]
    public async Task PassingEvidenceNeverPromotesWithoutHumanBoundaryCommand()
    {
        var state = State() with { Phase = DelphiLiveExperimentPhase.UntouchedConfirmation, SelectedChallenger = Challenger,
            DiscoveryCohorts = Cohorts(0, 30).ToImmutableArray(), UntouchedCohorts = Cohorts(30, 30).ToImmutableArray() };
        var store = new Store(state);
        var workflow = new DelphiLiveExperimentWorkflow(store);
        var noCommand = await workflow.ApplySessionBoundaryAsync(new(2026, 9, 8), Now, Now, Lease);
        noCommand.ChampionPolicyVersionId.ShouldBe(Champion);
        var command = new DelphiLiveExperimentBoundaryPlan(Guid.NewGuid(), "Promote", Definition, new(2026, 9, 8), Now.AddDays(3), Now, "Operator", "Reviewed evidence", Challenger, null);
        var scheduled = await workflow.ApprovePromotionAsync(command, DelphiLivePolicyDefinition.Version1, Lease);
        scheduled.ChampionPolicyVersionId.ShouldBe(Champion);
        scheduled.PendingBoundary!.PromotionEvidence!.EligibleForHumanReview.ShouldBeTrue();
        var promoted = await workflow.ApplySessionBoundaryAsync(new(2026, 9, 8), Now.AddDays(3), Now.AddDays(3), Lease);
        promoted.ChampionPolicyVersionId.ShouldBe(Challenger);
        promoted.Phase.ShouldBe(DelphiLiveExperimentPhase.ShadowBaseline);
        promoted.Definition!.ChallengerPolicyVersionIds.Single().ShouldBe(Champion);
    }

    [Fact]
    public async Task MeasurementDefectEndsComparisonAtNextBoundaryAndRestartsCleanSequence()
    {
        var store = new Store(State() with { Phase = DelphiLiveExperimentPhase.Discovery, EngineeringCohorts = Cohorts(0, 10).ToImmutableArray() });
        var workflow = new DelphiLiveExperimentWorkflow(store);
        var reset = await workflow.RecordMeasurementDefectAsync("Window timestamp defect", Now, Lease);
        reset.EngineeringCohorts.ShouldBeEmpty();
        reset.Definition.ShouldNotBeNull(); // historical comparison stops at next boundary
        var stopped = await workflow.ApplySessionBoundaryAsync(new(2026, 9, 8), Now.AddDays(3), Now.AddDays(3), Lease);
        stopped.Definition.ShouldBeNull();
        stopped.Phase.ShouldBe(DelphiLiveExperimentPhase.EngineeringShakedown);
        store.Applied!.Kind.ShouldBe("StopInvalidExperiment");
    }

    [Fact]
    public async Task LateAuditCancelsPreviouslyApprovedPromotionAtBoundary()
    {
        var store = new Store(State() with { Phase = DelphiLiveExperimentPhase.UntouchedConfirmation, SelectedChallenger = Challenger,
            DiscoveryCohorts = Cohorts(0, 30).ToImmutableArray(), UntouchedCohorts = Cohorts(30, 30).ToImmutableArray() });
        var workflow = new DelphiLiveExperimentWorkflow(store);
        var command = new DelphiLiveExperimentBoundaryPlan(Guid.NewGuid(), "Promote", Definition, new(2026, 9, 8), Now.AddDays(3), Now, "Operator", "Review", Challenger, null);
        await workflow.ApprovePromotionAsync(command, DelphiLivePolicyDefinition.Version1, Lease);
        await workflow.RecordCohortAsync(store.State.UntouchedCohorts[0] with { EvidenceConflict = true }, Now.AddMinutes(1), Lease);
        var cancelled = await workflow.ApplySessionBoundaryAsync(new(2026, 9, 8), Now.AddDays(3), Now.AddDays(3), Lease);
        cancelled.ChampionPolicyVersionId.ShouldBe(Champion);
        cancelled.Phase.ShouldBe(DelphiLiveExperimentPhase.UntouchedConfirmation);
        cancelled.PendingBoundary.ShouldBeNull();
        cancelled.LastReason.ShouldBe("PromotionCancelledEvidenceInvalidated");
        store.Applied.ShouldBeNull();
    }

    private static DelphiLiveExperimentState State() => new(DelphiLiveExperimentWorkflow.ProtocolId, Guid.NewGuid(), Champion, 0,
        DelphiLiveExperimentPhase.EngineeringShakedown, Definition, null, [], [], [], [], null, Now.AddDays(-1), "Test");
    private static DelphiLiveCohortEvidence[] Cohorts(int start, int count, decimal champion = .001m, decimal challenger = .002m) =>
        Enumerable.Range(start, count).Select(i => new DelphiLiveCohortEvidence(new DateOnly(2026, 1, 1).AddDays(i), i,
            i % 2 == 0 ? "Bullish" : "Mixed", 156, 156, false, false, true, true, true, false, false,
            ImmutableDictionary<Guid, decimal?>.Empty.Add(Champion, champion).Add(Challenger, challenger),
            ImmutableDictionary<Guid, decimal?>.Empty.Add(Champion, .02m).Add(Challenger, .01m))).ToArray();
    private sealed class Store(DelphiLiveExperimentState initial) : IDelphiLiveExperimentStore
    {
        public DelphiLiveExperimentState State = initial;
        public DelphiLiveExperimentBoundaryPlan? Applied;
        public Task<DelphiLiveExperimentState?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<DelphiLiveExperimentState?>(State);
        public Task<DelphiLiveExperimentState> CommitAsync(long expectedRevision, DelphiLiveExperimentState next, Guid commandId, string eventKind, DelphiLiveLease lease, CancellationToken cancellationToken = default)
        { expectedRevision.ShouldBe(State.Revision); State = next; return Task.FromResult(next); }
        public Task<DelphiLiveExperimentState> ApplyBoundaryAsync(long expectedRevision, DelphiLiveExperimentState next, DelphiLiveExperimentBoundaryPlan plan, DateOnly tradingDate, DateTime sessionOpenUtc, DelphiLiveLease lease, CancellationToken cancellationToken = default)
        { Applied = plan; State = next; return Task.FromResult(next); }
    }
}
