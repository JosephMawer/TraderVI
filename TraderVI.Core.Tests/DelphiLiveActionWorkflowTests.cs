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

public sealed class DelphiLiveActionWorkflowTests
{
    private static readonly DateOnly Date = new(2026, 9, 8);
    private static readonly DateTime Open = new(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DelphiLivePolicyDefinition Policy = DelphiLivePolicyDefinition.Version1;

    [Fact]
    public async Task Buy_PersistsDecisionBeforeQuote_CommitsWholeShares_AndRepeatedCycleIsIdempotent()
    {
        var harness = new Harness();
        var input = harness.Input("AAA");
        harness.Source.BeforeRequest = request =>
        {
            harness.Store.State.Actions.Single(a => a.Intent.DecisionId == request.DecisionId).Status.ShouldBe("Pending");
            harness.Store.Events.Last().Kind.ShouldBe("QuoteAttemptStarted");
        };
        harness.Source.Enqueue(10m, 9.99m, 10m);
        var state = await harness.Workflow.RunCycleAsync(input, Policy, harness.Lease);
        state.Cash.ShouldBe(800m);
        state.OpenPositions.Single().Quantity.ShouldBe(20);
        state.Fills.Single().Field.ShouldBe(DelphiLiveQuoteField.Ask);
        state.Quotes.Single().Observation.ReceivedUtc.ShouldBeGreaterThan(state.Actions.Single().Intent.DecisionPersistedUtc);
        long revision = state.Revision;
        (await harness.Workflow.RunCycleAsync(input, Policy, harness.Lease)).Revision.ShouldBe(revision);
        harness.Source.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RaisedFloor_CommitsBeforeTriggerQuote_ThenDecisionBeforeFreshSellFill_ThenBuy()
    {
        var harness = new Harness();
        harness.Source.Enqueue(10m, 9.99m, 10m);
        await harness.Workflow.RunCycleAsync(harness.Input("AAA"), Policy, harness.Lease);
        var held = harness.Store.State.OpenPositions.Single();
        harness.Clock.Now = Open.AddMinutes(27);
        var input = harness.Input("BBB", checkpointMinutes: 25) with
        {
            Candidates = new[] { harness.Candidate("AAA", 10.5m, false), harness.Candidate("BBB", 20m) },
            ExactCheckpointMarks = new[] { new DelphiLivePositionMark(held.PositionId, "AAA", 20, 10.5m, Open.AddMinutes(25)) }
        };
        harness.Source.BeforeRequest = request =>
        {
            if (request.Symbol == "AAA" && harness.Store.State.PendingActions.All(a => a.Intent.Side != DelphiLiveActionSide.Sell))
            {
                harness.Store.State.OpenPositions.Single().Protection.FloorPrice.ShouldBe(10.29m);
                harness.Store.Events.Any(e => e.Kind == "ProfitProtectionFloorChanged").ShouldBeTrue();
            }
        };
        harness.Source.Enqueue(10.2m, 10.2m, 10.21m); // Trigger, never the fill.
        harness.Source.Enqueue(10.1m, 10.1m, 10.11m); // Fresh sell fill.
        harness.Source.Enqueue(20m, 19.99m, 20m);
        var state = await harness.Workflow.RunCycleAsync(input, Policy, harness.Lease);
        state.Fills.Select(f => f.Side).ShouldBe(new[] { DelphiLiveActionSide.Buy, DelphiLiveActionSide.Sell, DelphiLiveActionSide.Buy });
        state.Fills[1].Price.ShouldBe(10.1m);
        state.OpenPositions.Single().Symbol.ShouldBe("BBB");
        state.Cash.ShouldBe(802m);
        var sell = state.Actions.Single(a => a.Intent.Side == DelphiLiveActionSide.Sell);
        sell.PrimaryReason.ShouldBe("ProfitProtectionFloorBreach");
        sell.Intent.DecisionEvidenceId.ShouldNotBe(state.Fills[1].QuoteObservationId);
        var ordered = harness.Store.Events.Select(e => e.Kind).ToList();
        ordered.IndexOf("SellFillCommitted").ShouldBeLessThan(ordered.LastIndexOf("BuyDecisionPersisted"));
    }

    [Fact]
    public async Task PendingSell_RetriesThreeInitiallyThenOncePerCycle_PreservesIdentityOvernightAndRestart()
    {
        var harness = new Harness();
        harness.Source.Enqueue(10m, 9.99m, 10m);
        await harness.Workflow.RunCycleAsync(harness.Input("AAA"), Policy, harness.Lease);
        harness.Clock.Now = Open.AddMinutes(27);
        harness.Source.Enqueue(9.4m, 9.4m, 9.41m); // Hard-loss trigger.
        for (int i = 0; i < 3; i++) harness.Source.Enqueue(null, null, null);
        var protect = harness.Protection();
        var state = await harness.Workflow.ProtectHoldingsAsync(protect, Policy, harness.Lease);
        var pending = state.PendingActions.Single();
        pending.AttemptCount.ShouldBe(3);
        harness.Source.Requests.Count.ShouldBe(5);
        harness.Clock.Now = Open.AddMinutes(32);
        harness.Source.Enqueue(null, null, null);
        var later = protect with { CycleId = Guid.NewGuid() };
        state = await harness.Workflow.ProtectHoldingsAsync(later, Policy, harness.Lease);
        state.PendingActions.Single().AttemptCount.ShouldBe(4);
        await harness.Workflow.ProtectHoldingsAsync(later, Policy, harness.Lease);
        harness.Source.Requests.Count.ShouldBe(6);
        harness.Clock.Now = Open.AddHours(6.5);
        state = await harness.Workflow.ProtectHoldingsAsync(protect with { CycleId = Guid.NewGuid() }, Policy, harness.Lease);
        state.PendingActions.Single().Status.ShouldBe("ExitPendingOvernight");
        harness.Clock.Now = Open.AddDays(1).AddSeconds(1);
        harness.Source.Enqueue(9.1m, 9.1m, 9.2m);
        state = await harness.Workflow.ProtectHoldingsAsync(protect with
        {
            CycleId = Guid.NewGuid(), TradingDate = Date.AddDays(1), SessionOpenUtc = Open.AddDays(1),
            SessionCloseUtc = Open.AddDays(1).AddHours(6.5), IsRestart = true
        }, Policy, harness.Lease);
        state.OpenPositions.ShouldBeEmpty();
        state.Actions.Single(a => a.Intent.Side == DelphiLiveActionSide.Sell).Intent.ActionId.ShouldBe(pending.Intent.ActionId);
        state.Fills.Last().Price.ShouldBe(9.1m);
    }

    [Fact]
    public async Task Restart_ExpiresInterruptedBuyWithoutRevivingItsDecision()
    {
        var harness = new Harness();
        harness.Source.BeforeRequest = _ => throw new OperationCanceledException();
        await Should.ThrowAsync<OperationCanceledException>(() => harness.Workflow.RunCycleAsync(harness.Input("AAA"), Policy, harness.Lease));
        harness.Store.State.PendingActions.Single().Intent.Side.ShouldBe(DelphiLiveActionSide.Buy);
        harness.Source.BeforeRequest = null;
        var state = await harness.Workflow.ProtectHoldingsAsync(harness.Protection() with { IsRestart = true }, Policy, harness.Lease);
        state.Actions.Single().TerminalReason.ShouldBe("BuyRestartExpired");
        state.Cash.ShouldBe(1000m);
        state.Fills.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuyQuoteAtCutoff_ExpiresAndNeverCreatesPosition()
    {
        var harness = new Harness();
        harness.Clock.Now = Open.AddHours(6).AddMinutes(14).AddSeconds(59);
        harness.Source.Enqueue(10m, 9.99m, 10m);
        var state = await harness.Workflow.RunCycleAsync(harness.Input("AAA", checkpointMinutes: 370), Policy, harness.Lease);
        state.Fills.ShouldBeEmpty();
        state.Actions.Single().TerminalReason.ShouldBe("BuyCutoffExpired");
        state.Cash.ShouldBe(1000m);
    }

    [Fact]
    public async Task MissingExactHeldMark_BlocksEntryWhileHardLossProtectionContinues()
    {
        var harness = new Harness();
        harness.Source.Enqueue(10m, 9.99m, 10m);
        await harness.Workflow.RunCycleAsync(harness.Input("AAA"), Policy, harness.Lease);
        harness.Clock.Now = Open.AddMinutes(27);
        harness.Source.Enqueue(10m, 10m, 10.01m);
        var state = await harness.Workflow.RunCycleAsync(harness.Input("BBB", 25), Policy, harness.Lease);
        state.Fills.Count().ShouldBe(1);
        state.Marks.Last().Reason.ShouldBe("PortfolioNavUnavailable");
        harness.Clock.Now = Open.AddMinutes(32);
        harness.Source.Enqueue(9.4m, 9.4m, 9.41m);
        harness.Source.Enqueue(9.3m, 9.3m, 9.4m);
        state = await harness.Workflow.ProtectHoldingsAsync(harness.Protection(), Policy, harness.Lease);
        state.OpenPositions.ShouldBeEmpty();
        state.Fills.Last().Side.ShouldBe(DelphiLiveActionSide.Sell);
    }

    [Fact]
    public async Task OpeningNav_RetainsCarriedQuantityEvenWhenProtectionSellsBeforeOpeningBarArrives()
    {
        var harness = new Harness();
        harness.Source.Enqueue(10m, 9.99m, 10m);
        await harness.Workflow.RunCycleAsync(harness.Input("AAA"), Policy, harness.Lease);
        var position = harness.Store.State.OpenPositions.Single();
        harness.Clock.Now = Open.AddDays(1).AddSeconds(1);
        harness.Source.Enqueue(9m, 9m, 9.1m);
        harness.Source.Enqueue(8.9m, 8.9m, 9m);
        await harness.Workflow.ProtectHoldingsAsync(harness.Protection() with
        {
            TradingDate = Date.AddDays(1), SessionOpenUtc = Open.AddDays(1), SessionCloseUtc = Open.AddDays(1).AddHours(6.5), IsWarmingUp = true
        }, Policy, harness.Lease);
        harness.Clock.Now = Open.AddDays(1).AddMinutes(7);
        var input = harness.Input("BBB") with
        {
            TradingDate = Date.AddDays(1), SessionOpenUtc = Open.AddDays(1), SessionCloseUtc = Open.AddDays(1).AddHours(6.5),
            CheckpointBarEndUtc = Open.AddDays(1).AddMinutes(5), BuyCutoffUtc = Open.AddDays(1).AddHours(6.25), Candidates = [],
            ExactOpeningMarks = new[] { new DelphiLivePositionMark(position.PositionId, "AAA", 20, 9m, Open.AddDays(1).AddMinutes(5)) }
        };
        var state = await harness.Workflow.RunCycleAsync(input, Policy, harness.Lease);
        state.OpeningNav.ShouldBe(980m); // Original cash 800 plus 20 shares at opening 9.
        state.Cash.ShouldBe(978m); // Actual later sell was 8.90.
    }

    [Fact]
    public async Task CapitalReview_ProtectsHoldingsUntilExplicitResume_PersistsReasonAndRearmsWithoutClearingDailyPause()
    {
        var h = new Harness();
        h.Source.Enqueue(10m, 9.99m, 10m);
        await h.Workflow.RunCycleAsync(h.Input("AAA"), Policy, h.Lease);
        h.Store.State = h.Store.State with { Guards = new(true, true, -0.05m, -0.10m, 1100m) };
        h.Clock.Now = Open.AddMinutes(27);
        h.Source.Enqueue(9.4m, 9.4m, 9.41m);
        h.Source.Enqueue(9.39m, 9.39m, 9.4m);
        var exited = await h.Workflow.ProtectHoldingsAsync(h.Protection(), Policy, h.Lease);
        exited.Fills.Last().Side.ShouldBe(DelphiLiveActionSide.Sell);
        exited.Guards.CapitalReviewRequired.ShouldBeTrue();
        exited.OpenPositions.ShouldBeEmpty();

        await Should.ThrowAsync<ArgumentException>(() => h.Workflow.ResumeCapitalReviewAsync(exited.PortfolioId,
            exited.Cash, "reviewer", " ", h.Lease));
        var resumed = await h.Workflow.ResumeCapitalReviewAsync(exited.PortfolioId, exited.Cash,
            "reviewer", "Reviewed the latest complete portfolio NAV after the protective exit", h.Lease);
        resumed.Guards.CapitalReviewRequired.ShouldBeFalse();
        resumed.Guards.HighestClosingNav.ShouldBe(exited.Cash);
        resumed.Guards.DrawdownFromHighestClosingNav.ShouldBe(0m);
        resumed.Guards.DailyBuyingPaused.ShouldBeTrue();
        resumed.Guards.DailyReturn.ShouldBe(-0.05m);
        h.Store.Events.Last().Kind.ShouldBe("CapitalReviewResumed");
        h.Store.Events.Last().DataJson.ShouldContain("Reviewed the latest complete portfolio NAV");
        h.Store.Events.Last().DataJson.ShouldContain("reviewer");
        await h.Workflow.RunCycleAsync(h.Input("BBB", 25), Policy, h.Lease);
        h.Store.State.Fills.Length.ShouldBe(2);
        h.Store.State.Guards.DailyBuyingPaused.ShouldBeTrue();
    }

    [Fact]
    public void LedgerTransition_RejectsCapitalInjectionAndHistoricalFillRewriting()
    {
        var harness = new Harness();
        var state = harness.Store.State;
        Should.Throw<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(state,
            state with { Revision = 1, Cash = state.Cash + 1 }));
        Should.Throw<ArgumentException>(() => DelphiLiveLedgerIntegrity.Create(harness.Request with { StartingCapital = 0m }));
        Should.Throw<ArgumentException>(() => DelphiLiveLedgerIntegrity.Create(harness.Request with { AuthorizedUtc = Open }));
    }

    private sealed class Harness
    {
        public readonly TestClock Clock = new() { Now = Open.AddMinutes(22) };
        public readonly MemoryStore Store;
        public readonly QuoteSource Source;
        public readonly DelphiLiveActionWorkflow Workflow;
        public readonly DelphiLiveLease Lease = new(Guid.NewGuid(), "test", 1, Open.AddDays(-1), Open.AddDays(7));
        public DelphiLiveGenerationRequest Request { get; }
        public Harness()
        {
            Request = new(Guid.NewGuid(), Guid.NewGuid(), Policy.PolicyVersionId, "OperationalChampion", null,
                1000m, "CAD", Date, Open, Open.AddDays(-1), "operator", "Explicit simulation capital");
            Store = new MemoryStore(DelphiLiveLedgerIntegrity.Create(Request));
            Source = new QuoteSource(Clock);
            Workflow = new(Store, Source, Clock);
        }
        public DelphiLivePortfolioCycleInput Input(string symbol, int checkpointMinutes = 20) => new(
            Store.State.PortfolioId, Guid.NewGuid(), Date, Open, Open.AddHours(6.5), Open.AddMinutes(checkpointMinutes),
            Open.AddHours(6.25), [], [], new[] { Candidate(symbol, 10m) }) { SessionId = Guid.NewGuid() };
        public DelphiLiveProtectionCycleInput Protection() => new(Store.State.PortfolioId, Guid.NewGuid(), Date, Open, Open.AddHours(6.5), false) { SessionId = Guid.NewGuid() };
        public DelphiLiveActionCandidate Candidate(string symbol, decimal close, bool eligible = true) => new(
            symbol, Guid.NewGuid(), Guid.NewGuid(), 1, eligible, Open.AddMinutes(15), DelphiLiveDataConfidence.Normal,
            new(false, false, null, null, null, close, close, true, false, true, false,
                new(DelphiLiveSignalFamily.VolumeSupport, DelphiLiveFamilyState.Supportive, "VolumeSupportive"),
                new(DelphiLiveMomentumState.Strong, DelphiLiveStrongTier.FourOfFour, DelphiLiveNeutralDetail.None, 4, 0, 0, 0), false, null), "{\"fullDeterministicEvidence\":true}");
    }

    private sealed class TestClock : IDelphiLiveClock
    {
        public DateTime Now;
        public DateTime UtcNow => Now;
    }
    private sealed class QuoteSource(TestClock clock) : IDelphiLiveMarketDataSource
    {
        private readonly Queue<(decimal? Price, decimal? Bid, decimal? Ask)> quotes = new();
        public readonly List<DelphiLiveQuoteRequest> Requests = new();
        public Action<DelphiLiveQuoteRequest>? BeforeRequest;
        public void Enqueue(decimal? price, decimal? bid, decimal? ask) => quotes.Enqueue((price, bid, ask));
        public Task<DelphiLiveQuoteReceipt> GetQuoteAsync(DelphiLiveQuoteRequest request, CancellationToken cancellationToken = default)
        {
            BeforeRequest?.Invoke(request);
            Requests.Add(request);
            var values = quotes.Count > 0 ? quotes.Dequeue() : (null, null, null);
            clock.Now = clock.Now.AddSeconds(1);
            return Task.FromResult(new DelphiLiveQuoteReceipt(request, values.Item1, values.Item2, values.Item3, clock.Now, DelphiLiveIdentities.QuoteFill));
        }
        public Task<DelphiLiveMarketDataReceipt> GetExactFiveMinuteBarAsync(DelphiLiveMarketDataRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class MemoryStore(DelphiLivePortfolioSnapshot initial) : IDelphiLiveLedgerStore
    {
        public DelphiLivePortfolioSnapshot State = initial;
        public readonly List<DelphiLiveLedgerEvent> Events = new();
        public Task<DelphiLivePortfolioSnapshot?> LoadPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken = default) => Task.FromResult<DelphiLivePortfolioSnapshot?>(Roundtrip(State));
        public Task<IReadOnlyList<DelphiLivePortfolioSnapshot>> GetPortfoliosForSessionAsync(DateOnly tradingDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DelphiLivePortfolioSnapshot>>(new[] { Roundtrip(State) });
        public Task<DelphiLivePortfolioSnapshot> CreateGenerationAsync(DelphiLiveGenerationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DelphiLivePortfolioSnapshot> CommitAsync(long expectedRevision, DelphiLivePortfolioSnapshot next, IReadOnlyList<DelphiLiveLedgerEvent> events, DelphiLiveLease lease, CancellationToken cancellationToken = default)
        {
            State.Revision.ShouldBe(expectedRevision);
            DelphiLiveLedgerIntegrity.ValidateTransition(Roundtrip(State), next);
            State = Roundtrip(next);
            Events.AddRange(events);
            return Task.FromResult(State);
        }
        private static DelphiLivePortfolioSnapshot Roundtrip(DelphiLivePortfolioSnapshot state) =>
            DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(DelphiLiveLedgerJson.Serialize(state));
    }
}
