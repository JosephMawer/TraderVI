#nullable enable

using Core.Trader.DelphiLive;
using Core.TMX.Models.Domain;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveMonitorWorkflowTests
{
    private static readonly DateOnly Date = new(2026, 9, 8);
    private static readonly DateTime Open = new(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DelphiLivePolicyDefinition Policy = DelphiLivePolicyDefinition.Version1;

    [Fact]
    public async Task InactiveSystem_NeverAcquiresLeaseCollectsOrQuotes()
    {
        var h = new Harness(enabled: false);
        h.Clock.Now = Open.AddMinutes(7);
        var result = await h.Workflow.TickAsync();
        result.Status.ShouldBe("Inactive");
        h.Collection.AcquireCount.ShouldBe(0);
        h.Source.BarRequests.ShouldBeEmpty();
        h.Source.QuoteRequests.ShouldBeEmpty();
        h.Sessions.FreezeCount.ShouldBe(0);
    }

    [Fact]
    public async Task PreOpenArmingPreservesOpeningCoverageDespiteNormalTimerJitter_InitialLateStartDoesNot()
    {
        var armed = new Harness();
        armed.Clock.Now = Open.AddSeconds(-15);
        await armed.Workflow.TickAsync();
        armed.Collection.AcquireCount.ShouldBe(0);
        armed.Clock.Now = Open.AddSeconds(15);
        var opened = await armed.Workflow.TickAsync();
        opened.Warnings.ShouldNotContain(w => w.Contains("Host coverage gap", StringComparison.Ordinal));
        armed.Collection.LastArmedAtOpen.ShouldBeTrue();

        var late = new Harness();
        late.Clock.Now = Open.AddMinutes(1);
        var started = await late.Workflow.TickAsync();
        started.Warnings.ShouldContain(w => w.Contains("Host coverage gap", StringComparison.Ordinal));
        late.Collection.LastArmedAtOpen.ShouldBeFalse();
        late.Source.BarRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task StalePreOpenHeartbeat_CannotHideSleepAcrossOpenOrAMissedPendingWake()
    {
        foreach (TimeSpan beforeOpen in new[] { TimeSpan.FromHours(8.5), TimeSpan.FromSeconds(15) })
        {
            var h = new Harness();
            h.Clock.Now = Open.Subtract(beforeOpen);
            await h.Workflow.TickAsync();
            h.Clock.Now = Open.AddMinutes(1);
            var resumed = await h.Workflow.TickAsync();
            h.Collection.LastArmedAtOpen.ShouldBeFalse();
            resumed.Warnings.ShouldContain(w => w.Contains("Host coverage gap", StringComparison.Ordinal));
            h.Source.BarRequests.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task OpeningHeartbeat_UsesInjectedCadenceAndCannotAttestWhenCadenceIsUnknown()
    {
        var fastHost = new Harness(tickCadenceSeconds: 10);
        fastHost.Clock.Now = Open.AddSeconds(-5);
        await fastHost.Workflow.TickAsync();
        fastHost.Clock.Now = Open.AddSeconds(20); // More than one pending 10-second wake was missed.
        await fastHost.Workflow.TickAsync();
        fastHost.Collection.LastArmedAtOpen.ShouldBeFalse();

        var unknownHost = new Harness(tickCadenceSeconds: null);
        unknownHost.Clock.Now = Open.AddSeconds(-15);
        await unknownHost.Workflow.TickAsync();
        unknownHost.Clock.Now = Open.AddSeconds(15);
        await unknownHost.Workflow.TickAsync();
        unknownHost.Collection.LastArmedAtOpen.ShouldBeFalse();

        var exactOpeningHost = new Harness(tickCadenceSeconds: null);
        await exactOpeningHost.Workflow.TickAsync();
        exactOpeningHost.Collection.LastArmedAtOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task HealthyOpeningHeartbeat_IsCapturedBeforeInitializationIo()
    {
        var h = new Harness();
        h.Clock.Now = Open.AddSeconds(-15);
        await h.Workflow.TickAsync();
        h.Clock.Now = Open.AddSeconds(15);
        h.Sessions.BeforeFreeze = () => h.Clock.Now = h.Clock.Now.AddMinutes(2);
        var opened = await h.Workflow.TickAsync();
        h.Collection.LastArmedAtOpen.ShouldBeTrue();
        opened.Warnings.ShouldNotContain(w => w.Contains("Host coverage gap", StringComparison.Ordinal));
        h.Source.BarRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task PostCloseCheckpointAndStopPersistResearchWithoutNewSourceRequests()
    {
        var h = new Harness();
        int closedCallbacks = 0;
        h.Workflow.PersistSessionResearchAsync = (_, _, _) => { closedCallbacks++; return Task.CompletedTask; };
        await h.Workflow.TickAsync();
        h.Clock.Now = Open.AddHours(6.5).AddMinutes(7);
        await h.Workflow.TickAsync();
        closedCallbacks.ShouldBe(1);
        await h.Workflow.StopAsync();
        closedCallbacks.ShouldBe(2);
        h.Source.BarRequests.ShouldBeEmpty();
        h.Source.QuoteRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task OpeningHost_CollectsExactBarsAtTwoMinuteOffset_ConfirmsOnlyAfterTwoMatureObservations()
    {
        var h = new Harness();
        await h.Workflow.TickAsync(); // 09:30 starts the session, no forming bars.
        h.Source.BarRequests.ShouldBeEmpty();
        h.Clock.Now = Open.AddMinutes(6).AddSeconds(59);
        await h.Workflow.TickAsync();
        h.Source.BarRequests.ShouldBeEmpty();
        for (int index = 1; index <= 4; index++)
        {
            h.Clock.Now = Open.AddMinutes(5 * index + 2);
            var result = await h.Workflow.TickAsync();
            result.Evaluations.Single().Result.FamiliesMature.ShouldBe(index == 4);
            h.Ledger.State!.Fills.ShouldBeEmpty();
        }
        h.Clock.Now = Open.AddMinutes(27);
        var confirmed = await h.Workflow.TickAsync();
        confirmed.Evaluations.Single().Result.NextState.Lifecycle.ConsecutiveStrongObservations.ShouldBe(2);
        h.Ledger.State!.Fills.Single().Side.ShouldBe(DelphiLiveActionSide.Buy);
        h.Source.BarRequests.Count.ShouldBe(10); // Stock + XIU once per checkpoint.
        h.Clock.Now = Open.AddMinutes(32);
        await h.Workflow.TickAsync(); // Exercises original-entry dossier reload.
        h.Ledger.State.OpenPositions.Count().ShouldBe(1);
        h.Ledger.State.Fills.Count(f => f.Side == DelphiLiveActionSide.Buy).ShouldBe(1);
        h.Sessions.FreezeCount.ShouldBe(1);
    }

    [Fact]
    public async Task LateStart_NeverBackfillsOperationalHistory_RequiresFiveFreshBarsThenConfirmation()
    {
        var h = new Harness(startingCapital: 1m);
        h.Clock.Now = Open.AddMinutes(31);
        await h.Workflow.TickAsync();
        h.Source.BarRequests.ShouldBeEmpty();
        for (int index = 1; index <= 5; index++)
        {
            h.Clock.Now = Open.AddMinutes(30 + 5 * index - 3); // 10:02, 10:07, ...
            var snapshot = await h.Workflow.TickAsync();
            snapshot.Evaluations.Single().Result.FamiliesMature.ShouldBe(index == 5);
            if (index < 5) h.Source.QuoteRequests.ShouldBeEmpty();
        }
        h.Source.BarRequests.Min(r => r.BarStartUtc).ShouldBe(Open.AddMinutes(25));
        h.Clock.Now = Open.AddMinutes(57);
        var confirmed = await h.Workflow.TickAsync();
        confirmed.Evaluations.Single().Result.NextState.Lifecycle.ConsecutiveStrongObservations.ShouldBe(2);
        confirmed.Warnings.ShouldContain(w => w.Contains("Host coverage gap", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restart_RetainsDurableEvidenceAndResetsOrdinaryConfirmation()
    {
        var h = new Harness(startingCapital: 1m);
        await h.Workflow.TickAsync();
        for (int index = 1; index <= 4; index++)
        {
            h.Clock.Now = Open.AddMinutes(5 * index + 2);
            await h.Workflow.TickAsync();
        }
        h.Evaluations.Latest.Single().Result.NextState.Lifecycle.ConsecutiveStrongObservations.ShouldBe(1);
        await h.Workflow.StopAsync();
        int oldBars = h.Collection.Bars.Count;
        h.Clock.Now = Open.AddMinutes(26);
        await h.Workflow.TickAsync();
        h.Clock.Now = Open.AddMinutes(27);
        var resumed = await h.Workflow.TickAsync();
        resumed.Evaluations.Single().Result.FamiliesMature.ShouldBeFalse();
        resumed.Evaluations.Single().Result.NextState.Lifecycle.ConsecutiveStrongObservations.ShouldBe(0);
        h.Collection.Bars.Count.ShouldBe(oldBars + 2);
        h.Source.QuoteRequests.ShouldBeEmpty();
        h.Collection.RecoveryCount.ShouldBe(2);
    }

    [Fact]
    public async Task CalendarOutsideReviewedCoverage_FailsClosedBeforeCollectionOrActivation()
    {
        var h = new Harness();
        h.Clock.Now = Open.AddYears(1);
        await Should.ThrowAsync<InvalidOperationException>(() => h.Workflow.TickAsync());
        h.Source.BarRequests.ShouldBeEmpty();
        h.Source.QuoteRequests.ShouldBeEmpty();
        h.Collection.AcquireCount.ShouldBe(0);
    }

    [Fact]
    public async Task SamePolicyPortfolios_KeepDismissedRecoverySeparateFromControlConfirmation()
    {
        var h = new Harness();
        h.Ledger.AddControl();
        await h.Workflow.TickAsync();
        for (int index = 1; index <= 3; index++)
        { h.Clock.Now = Open.AddMinutes(5 * index + 2); await h.Workflow.TickAsync(); }
        var existing = h.Ledger.State!.CandidateStates["AAA"];
        h.Ledger.State = h.Ledger.State with { CandidateStates = h.Ledger.State.CandidateStates.SetItem("AAA", existing with
            { Lifecycle = existing.Lifecycle with { State = DelphiLiveRecommendationState.Dismissed, ReasonCode = "DismissedWeakeningConfirmed" } }) };
        h.Clock.Now = Open.AddMinutes(22);
        var result = await h.Workflow.TickAsync();
        result.Evaluations.Count.ShouldBe(1); // One policy market evaluation, two isolated lifecycles.
        h.Ledger.State.CandidateStates["AAA"].Lifecycle.State.ShouldBe(DelphiLiveRecommendationState.Dismissed);
        h.Ledger.Other.Single().CandidateStates["AAA"].Lifecycle.State.ShouldBe(DelphiLiveRecommendationState.Emerging);
        h.Clock.Now = Open.AddMinutes(27);
        await h.Workflow.TickAsync();
        h.Ledger.State.OpenPositions.Count().ShouldBe(1);
        h.Ledger.Other.Single().OpenPositions.Count().ShouldBe(1);
        h.Ledger.State.Positions.Single().PositionId.ShouldNotBe(h.Ledger.Other.Single().Positions.Single().PositionId);
        h.Source.BarRequests.Count.ShouldBe(10);
    }

    [Fact]
    public async Task PositiveNotifications_UseOperationalRecoveryInsteadOfSharedResearchOrControlState()
    {
        var h = new Harness(startingCapital: 1m);
        h.Ledger.AddControl();
        await h.Workflow.TickAsync();
        for (int index = 1; index <= 4; index++)
        { h.Clock.Now = Open.AddMinutes(5 * index + 2); await h.Workflow.TickAsync(); }
        var own = h.Ledger.State!.CandidateStates["AAA"];
        h.Ledger.State = h.Ledger.State with { CandidateStates = h.Ledger.State.CandidateStates.SetItem("AAA", own with
            { Lifecycle = own.Lifecycle with { State = DelphiLiveRecommendationState.Dismissed, ConsecutiveStrongObservations = 0 } }) };
        h.Clock.Now = Open.AddMinutes(27);
        await h.Workflow.TickAsync();
        h.Evaluations.Latest.Single().Result.Lifecycle.MayCreateBuyDecision.ShouldBeTrue();
        h.Ledger.Other.Single().CandidateStates["AAA"].Lifecycle.State.ShouldBe(DelphiLiveRecommendationState.EntryEligible);
        h.Ledger.State.CandidateStates["AAA"].Lifecycle.State.ShouldBe(DelphiLiveRecommendationState.Dismissed);
        h.Notifications.Values.ShouldBeEmpty();
        h.Clock.Now = Open.AddMinutes(32);
        await h.Workflow.TickAsync();
        h.Notifications.Values.Single().Code.ShouldBe("EntryEligible");
        h.Notifications.Values.Single().Symbol.ShouldBe("AAA");
    }

    [Fact]
    public async Task OnePortfolioExit_RequiresItsOwnFreshConfirmation_WhileSamePolicyControlRemainsHeld()
    {
        var h = new Harness();
        h.Ledger.AddControl();
        await h.Workflow.TickAsync();
        for (int index = 1; index <= 5; index++)
        { h.Clock.Now = Open.AddMinutes(5 * index + 2); await h.Workflow.TickAsync(); }
        var controlEntry = h.Ledger.Other.Single().Fills.Single();
        h.Source.Enqueue(90m, 90m, 90.1m); // Operational holding protection trigger.
        h.Source.Enqueue(89.9m, 89.9m, 90m); // Operational sell fill only.
        h.Source.Enqueue(106m, 105.9m, 106.1m); // Control remains held.
        h.Clock.Now = Open.AddMinutes(32);
        await h.Workflow.TickAsync();
        h.Ledger.State!.OpenPositions.ShouldBeEmpty();
        h.Ledger.Other.Single().OpenPositions.Count().ShouldBe(1);
        h.Ledger.Other.Single().Fills.Count().ShouldBe(1);
        h.Clock.Now = Open.AddMinutes(37);
        await h.Workflow.TickAsync();
        h.Ledger.State.Fills.Count(f => f.Side == DelphiLiveActionSide.Buy).ShouldBe(1);
        h.Clock.Now = Open.AddMinutes(42);
        await h.Workflow.TickAsync();
        h.Ledger.State.Fills.Count(f => f.Side == DelphiLiveActionSide.Buy).ShouldBe(2);
        h.Ledger.Other.Single().Fills.Single().FillId.ShouldBe(controlEntry.FillId);
    }

    [Fact]
    public async Task OperatorCommands_UseTemporaryLeaseOutsideHours_AndNeverReleaseTheActiveHostLease()
    {
        var h = new Harness();
        h.Clock.Now = Open.AddHours(-2);
        await Should.ThrowAsync<InvalidOperationException>(() => h.Workflow.ExecuteOperatorCommandAsync((lease, ct) =>
        { lease.FencingToken.ShouldBe(1); throw new InvalidOperationException("fixture command failure"); }));
        h.Collection.ReleaseCount.ShouldBe(1);
        h.Source.BarRequests.ShouldBeEmpty();
        h.Clock.Now = Open;
        await h.Workflow.TickAsync();
        await h.Workflow.ExecuteOperatorCommandAsync((lease, ct) => Task.CompletedTask);
        h.Collection.AcquireCount.ShouldBe(2);
        h.Collection.ReleaseCount.ShouldBe(1); // Existing session lease remains owned.
        await h.Workflow.StopAsync();
        h.Collection.ReleaseCount.ShouldBe(2);
    }

    [Fact]
    public void ForeignCarryObservation_CannotReviveAnOlderClosedPositionAsTodaysEntryScope()
    {
        var h = new Harness();
        Guid id = Guid.NewGuid();
        var historical = new DelphiLiveLedgerPosition(id, "XYZ", 1, 100m, Open.AddDays(-7), Guid.NewGuid(), "{}",
            DelphiLiveProfitProtectionState.Open(id, 100m), Open.AddDays(-6), Guid.NewGuid());
        var control = h.Ledger.State! with { Positions = [historical] };
        DelphiLiveMonitorWorkflow.IsWithinOwnEntryScope(control, "XYZ", Date, false).ShouldBeFalse();
        DelphiLiveMonitorWorkflow.IsWithinOwnEntryScope(control, "XYZ", Date, true).ShouldBeTrue();
        DelphiLiveMonitorWorkflow.IsWithinOwnEntryScope(control with { Positions = [historical with { ClosedUtc = Open.AddMinutes(5) }] }, "XYZ", Date, false).ShouldBeTrue();
        DelphiLiveMonitorWorkflow.IsWithinOwnEntryScope(control with { Positions = [historical with { ClosedUtc = null }] }, "XYZ", Date, false).ShouldBeTrue();
    }

    private sealed class Harness
    {
        public readonly TestClock Clock = new() { Now = Open };
        public readonly SessionStore Sessions;
        public readonly EvaluationStore Evaluations = new();
        public readonly CollectionStore Collection;
        public readonly LedgerStore Ledger;
        public readonly MarketSource Source;
        public readonly Notifier Notifications = new();
        public readonly DelphiLiveMonitorWorkflow Workflow;
        public Harness(bool enabled = true, decimal startingCapital = 1000m, double? tickCadenceSeconds = 30)
        {
            Sessions = new SessionStore(enabled);
            Collection = new CollectionStore(Clock);
            Ledger = new LedgerStore(enabled ? DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(),
                Policy.PolicyVersionId, "OperationalChampion", null, startingCapital, "CAD", Date, Open,
                Open.AddDays(-1), "operator", "Explicit simulation capital")) : null);
            Source = new MarketSource(Clock);
            var calendar = new ReviewedTsxSessionCalendar(new("test-reviewed", "official-calendar-fixture", Date.AddDays(-4), Date.AddDays(2),
                new[] { Date.AddDays(-4), Date, Date.AddDays(1), Date.AddDays(2) }));
            Workflow = new(Clock, calendar, Sessions, Evaluations, Collection, Ledger, new HoldingSource(), Source, Notifications,
                hostTickCadence: tickCadenceSeconds.HasValue ? TimeSpan.FromSeconds(tickCadenceSeconds.Value) : null);
        }
    }
    private sealed class TestClock : IDelphiLiveClock { public DateTime Now; public DateTime UtcNow => Now; }
    private sealed class SessionStore : IDelphiLiveSessionContextStore
    {
        private readonly bool enabled;
        private readonly DelphiLiveSessionContext context;
        public int FreezeCount;
        public Action? BeforeFreeze;
        public SessionStore(bool enabled)
        {
            this.enabled = enabled;
            var candidate = new DelphiLiveFrozenCandidate(Guid.NewGuid(), "AAA", 0.8m, "{}", []);
            candidate = candidate with { SourceLenses = new[] { new DelphiLiveLensSource(Guid.NewGuid(), candidate.CandidateId,
                "Continuation", true, true, 1, 0.7m, null, "{\"gates\":[]}") } };
            var ruler = new DelphiLiveTrueRangeRulerMeasurement(10, Date.AddDays(-4), DelphiLiveScalarMeasurement.Available(0.04m));
            var baseline = new DelphiLiveFrozenBaseline(100m, [], [], new(ruler, ruler, ruler, ruler));
            var session = new DelphiLiveFrozenSession(Guid.NewGuid(), Date, Guid.NewGuid(), Guid.NewGuid(), "Frozen", Open, new[] { "AAA", "XIU" });
            var assignment = new DelphiLivePolicyAssignment(Guid.NewGuid(), Policy.PolicyVersionId, DelphiLivePolicyRole.OperationalChampion, Date);
            context = new(session, new(Date, Open, Open.AddHours(6.5)), new[] { assignment },
                new Dictionary<Guid, DelphiLivePolicyDefinition> { [Policy.PolicyVersionId] = Policy },
                new Dictionary<string, DelphiLiveFrozenCandidate> { ["AAA"] = candidate },
                new Dictionary<string, DelphiLiveFrozenBaseline> { ["AAA"] = baseline, ["XIU"] = baseline });
        }
        public Task<IReadOnlyList<DelphiLivePolicyAssignment>> GetAssignmentsForSessionAsync(DateOnly tradingDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(enabled ? context.Assignments : (IReadOnlyList<DelphiLivePolicyAssignment>)Array.Empty<DelphiLivePolicyAssignment>());
        public Task<DelphiLiveFrozenSession?> GetFrozenSessionAsync(DateOnly tradingDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(FreezeCount > 0 ? context.Session : null);
        public Task<DelphiLiveFrozenSession> FreezeSessionAsync(DelphiLiveSessionFreezeRequest request, CancellationToken cancellationToken = default)
        { BeforeFreeze?.Invoke(); FreezeCount++; return Task.FromResult(context.Session); }
        public Task<DelphiLiveSessionContext?> ReadContextAsync(DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<DelphiLiveSessionContext?>(context);
        public Task<DelphiLiveSessionContext> SynchronizeObservationSetAsync(Guid sessionId, DateTime nextBarEndUtc,
            DelphiLiveLease lease, IReadOnlyList<DelphiLivePortfolioSnapshot> portfolios, CancellationToken cancellationToken = default) => Task.FromResult(context);
        public Task<DelphiLivePolicyDefinition> GetPolicyAsync(Guid policyId, CancellationToken cancellationToken = default) => Task.FromResult(Policy);
    }
    private sealed class EvaluationStore : IDelphiLiveEvaluationStore
    {
        public readonly List<DelphiLiveStoredEvaluation> All = new();
        public IReadOnlyList<DelphiLiveStoredEvaluation> Latest => All.GroupBy(e => (e.Input.Policy.PolicyVersionId, e.Input.Stock.Symbol)).Select(g => g.Last()).ToArray();
        public Task<DelphiLiveStoredEvaluation?> GetLatestAsync(Guid sessionId, Guid policyId, string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult(Latest.SingleOrDefault(e => e.Input.Policy.PolicyVersionId == policyId && e.Input.Stock.Symbol == symbol));
        public Task<IReadOnlyList<DelphiLiveStoredEvaluation>> GetLatestSnapshotAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult(Latest);
        public Task PersistAsync(DelphiLiveEvaluationInput input, DelphiLiveEvaluationResult result, int continuityEpoch, DelphiLiveLease lease, CancellationToken cancellationToken = default)
        { All.Add(new(input, result, continuityEpoch)); return Task.CompletedTask; }
    }
    private sealed class CollectionStore(TestClock clock) : IDelphiLiveCollectionRuntimeStore
    {
        public int AcquireCount;
        public int ReleaseCount;
        public int RecoveryCount;
        public bool LastArmedAtOpen;
        public readonly List<DelphiLiveFiveMinuteBar> Bars = new();
        private DelphiLiveLease? current;
        public Task<DelphiLiveLease?> TryAcquireAsync(string ownerId, DateTime acquiredUtc, DateTime expiresUtc, CancellationToken cancellationToken = default)
        { AcquireCount++; current = new(Guid.NewGuid(), ownerId, AcquireCount, acquiredUtc, expiresUtc); return Task.FromResult<DelphiLiveLease?>(current); }
        public Task<bool> TryRenewAsync(DelphiLiveLease lease, DateTime renewedUtc, DateTime expiresUtc, CancellationToken cancellationToken = default) => Task.FromResult(current?.LeaseId == lease.LeaseId);
        public Task ReleaseAsync(DelphiLiveLease lease, DateTime releasedUtc, CancellationToken cancellationToken = default) { ReleaseCount++; current = null; return Task.CompletedTask; }
        public Task<DelphiLiveCollectionRecovery> RecoverSessionAsync(Guid sessionId, DelphiLiveLease lease, CancellationToken cancellationToken = default,
            bool wasArmedAtSessionOpen = false)
        { RecoveryCount++; LastArmedAtOpen = wasArmedAtSessionOpen; return Task.FromResult(new DelphiLiveCollectionRecovery(Guid.NewGuid(), RecoveryCount, RecoveryCount > 1 || !wasArmedAtSessionOpen, 0, clock.Now)); }
        public Task BeginCycleAsync(DelphiLiveCollectionCycle cycle, IReadOnlyList<DelphiLiveObservationTarget> expectedTargets, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DelphiLiveMarketDataReceipt> RecordReceiptAsync(DelphiLiveMarketDataReceipt receipt, CancellationToken cancellationToken = default)
        {
            var bar = receipt.ExactCompletedBar!;
            Bars.Add(new(Guid.NewGuid(), receipt.Request.Symbol, Date, receipt.Request.BarStartUtc, receipt.Request.BarEndUtc,
                bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, receipt.ReceivedUtc, "Fixture", 1,
                receipt.Disposition == "OperationalOnTime" ? DelphiLiveEvidenceDisposition.OperationalOnTime : DelphiLiveEvidenceDisposition.LateResearchOnly));
            return Task.FromResult(receipt);
        }
        public Task CompleteCycleAsync(Guid cycleId, DateTime completedUtc, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FinishSessionAsync(Guid sessionId, DelphiLiveLease lease, bool hostStopping, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DelphiLiveFiveMinuteBar>> GetSessionBarsAsync(Guid sessionId, DateTime throughBarEndUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DelphiLiveFiveMinuteBar>>(Bars.Where(b => b.EndUtc <= throughBarEndUtc).ToArray());
    }
    private sealed class LedgerStore(DelphiLivePortfolioSnapshot? initial) : IDelphiLiveLedgerStore
    {
        public DelphiLivePortfolioSnapshot? State = initial;
        public readonly List<DelphiLivePortfolioSnapshot> Other = new();
        public void AddControl() => Other.Add(DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Policy.PolicyVersionId,
            "ChampionControl", Guid.NewGuid(), State!.StartingCapital, "CAD", Date, Open, Open.AddDays(-1), "operator", "Aligned control fixture")));
        public Task<DelphiLivePortfolioSnapshot?> LoadPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken = default) =>
            Task.FromResult(State?.PortfolioId == portfolioId ? State : Other.SingleOrDefault(p => p.PortfolioId == portfolioId));
        public Task<IReadOnlyList<DelphiLivePortfolioSnapshot>> GetPortfoliosForSessionAsync(DateOnly tradingDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DelphiLivePortfolioSnapshot>>(State is null ? Other.ToArray() : new[] { State }.Concat(Other).ToArray());
        public Task<DelphiLivePortfolioSnapshot> CreateGenerationAsync(DelphiLiveGenerationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DelphiLivePortfolioSnapshot> CommitAsync(long expectedRevision, DelphiLivePortfolioSnapshot next, IReadOnlyList<DelphiLiveLedgerEvent> events, DelphiLiveLease lease, CancellationToken cancellationToken = default)
        {
            var prior = State?.PortfolioId == next.PortfolioId ? State : Other.Single(p => p.PortfolioId == next.PortfolioId);
            prior!.Revision.ShouldBe(expectedRevision);
            DelphiLiveLedgerIntegrity.ValidateTransition(prior, next);
            var copy = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(DelphiLiveLedgerJson.Serialize(next));
            if (State?.PortfolioId == next.PortfolioId) State = copy;
            else Other[Other.FindIndex(p => p.PortfolioId == next.PortfolioId)] = copy;
            return Task.FromResult(copy);
        }
    }
    private sealed class MarketSource(TestClock clock) : IDelphiLiveMarketDataSource
    {
        public readonly List<DelphiLiveMarketDataRequest> BarRequests = new();
        public readonly List<DelphiLiveQuoteRequest> QuoteRequests = new();
        private readonly Queue<(decimal? Price, decimal? Bid, decimal? Ask)> quotes = new();
        public void Enqueue(decimal? price, decimal? bid, decimal? ask) => quotes.Enqueue((price, bid, ask));
        public Task<DelphiLiveMarketDataReceipt> GetExactFiveMinuteBarAsync(DelphiLiveMarketDataRequest request, CancellationToken cancellationToken = default)
        {
            BarRequests.Add(request);
            decimal step = (decimal)(request.BarEndUtc - Open).TotalMinutes / 5m;
            decimal close = request.Symbol == "XIU" ? 100m : 100m + step;
            decimal open = request.Symbol == "XIU" ? 100m : close - 1m;
            var bar = new OhlcvBar(request.BarStartUtc, open, close + 0.1m, open - 0.1m, close, 1000);
            return Task.FromResult(new DelphiLiveMarketDataReceipt(request, bar, clock.Now, "OperationalOnTime"));
        }
        public Task<DelphiLiveQuoteReceipt> GetQuoteAsync(DelphiLiveQuoteRequest request, CancellationToken cancellationToken = default)
        {
            QuoteRequests.Add(request);
            decimal price = 100m + decimal.Floor((decimal)(clock.Now - Open).TotalMinutes / 5m);
            clock.Now = clock.Now.AddSeconds(1);
            var values = quotes.Count > 0 ? quotes.Dequeue() : (price, price - 0.1m, price + 0.1m);
            return Task.FromResult(new DelphiLiveQuoteReceipt(request, values.Item1, values.Item2, values.Item3, clock.Now, DelphiLiveIdentities.QuoteFill));
        }
    }
    private sealed class HoldingSource : IDelphiLiveHoldingSource
    { public Task<IReadOnlyList<DelphiLiveObservedHolding>> GetObservedHoldingsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DelphiLiveObservedHolding>>([]); }
    private sealed class Notifier : IDelphiLiveNotifier
    {
        public readonly List<DelphiLiveNotification> Values = [];
        public Task NotifyAsync(DelphiLiveNotification notification, CancellationToken cancellationToken = default)
        { Values.Add(notification); return Task.CompletedTask; }
    }
}
