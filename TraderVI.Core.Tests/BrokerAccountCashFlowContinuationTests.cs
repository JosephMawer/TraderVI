#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerAccountCashFlowContinuationTests
{
    private static readonly DateOnly Date = new(2026, 9, 21);
    private static readonly DateTimeOffset Open = ReviewedTsxSessionCalendar.At(Date, new(9, 30));
    private static readonly DateTimeOffset Close = ReviewedTsxSessionCalendar.At(Date, new(16, 0));
    private static readonly ITsxSessionCalendar Calendar = new ReviewedTsxSessionCalendar(new(
        "fixture", "synthetic", Date, Date.AddDays(2), [Date, Date.AddDays(1), Date.AddDays(2)]));
    private static readonly string Hash = new('A', 64);
    private static readonly string EventKey = new('B', 64);
    private static readonly DateTimeOffset Now = Close.AddDays(2);

    private static BrokerAccountRiskState State() => new(Guid.NewGuid(), Guid.NewGuid(), "TFSA", null, Date,
        new(10000m, BrokerRiskReferenceSource.VerifiedSessionOpening, Guid.NewGuid(), Open),
        new(11000m, BrokerRiskReferenceSource.ReportedClosingHigh, Guid.NewGuid(), Open.AddDays(-3)),
        true, true, null, Open);

    private static BrokerRiskCashFlowEvent Flow(BrokerAccountRiskState state, decimal amount = 1000m,
        decimal before = 9800m, string? key = null, DateTimeOffset? at = null)
    {
        var when = at ?? Open.AddHours(1);
        var book = new BrokerRiskBoundaryBook(Guid.NewGuid(), state.BindingId, when, when.AddSeconds(1), before, []);
        return new(key ?? EventKey, amount, book, book with { EvidenceId = Guid.NewGuid(), Cash = before + amount });
    }

    private static BrokerRiskContinuationRequest Request(BrokerAccountRiskState state, params BrokerRiskCashFlowEvent[] flows) =>
        new(Guid.NewGuid(), state.BaselineId, state.RevisionId, BrokerRiskContinuationKind.CashFlows, null, null, null)
        { CashFlows = new(Guid.NewGuid(), state.BindingId, state.Through, flows.Last().After.At,
            flows.Max(f => f.After.VerifiedAt), Hash, flows.ToImmutableArray()) };
    private static BrokerAccountRiskState Apply(BrokerAccountRiskState state, BrokerRiskContinuationRequest request) =>
        BrokerAccountRiskContinuation.Apply(state, request, Calendar, Now);

    [Theory]
    [InlineData(1000, 9800)]
    [InlineData(-1000, 9800)]
    [InlineData(1000, 10400)]
    [InlineData(-1000, 10400)]
    [InlineData(0.01, 9800)]
    [InlineData(-9799.99, 9800)]
    public void CashMovementsPreserveEachMeasuredPercentageAndExistingHolds(decimal amount, decimal value)
    {
        var state = State();
        var flow = Flow(state, amount, value);
        var result = Apply(state, Request(state, flow));
        Assert.InRange(Math.Abs(value / state.SessionReference.Value - (value + amount) / result.SessionReference.Value), 0m, 0.000000000000000000000001m);
        Assert.InRange(Math.Abs(value / state.DrawdownReference.Value - (value + amount) / result.DrawdownReference.Value), 0m, 0.000000000000000000000001m);
        Assert.Equal(state.DailyLossHold, result.DailyLossHold);
        Assert.Equal(state.CapitalReviewRequired, result.CapitalReviewRequired);
        Assert.Equal(state.TradingDate, result.TradingDate);
        Assert.Null(result.LastCompletedSession);
        Assert.Equal(BrokerRiskReferenceSource.ConfirmedCashFlowAdjustment, result.SessionReference.Source);
        Assert.Equal(flow.After.EvidenceId, result.SessionReference.EvidenceId);
        Assert.Equal(flow.After.At, result.SessionReference.EvidenceAt);
        Assert.Equal(10000m, state.SessionReference.Value);
        Assert.Equal(11000m, state.DrawdownReference.Value);
    }

    [Theory]
    [InlineData(9700, 1000)]
    [InlineData(9700, -1000)]
    [InlineData(9700.01, 1000)]
    [InlineData(9699.99, -1000)]
    [InlineData(9000, 1000)]
    [InlineData(9000, -1000)]
    [InlineData(9000.01, 1000)]
    public void AdjustmentsDoNotChangeTheExistingGuardDecision(decimal value, decimal amount)
    {
        var state = State() with { DrawdownReference = State().SessionReference, DailyLossHold = false, CapitalReviewRequired = false };
        var before = SystemShadowPolicy.EvaluateGuards(value, state.SessionReference.Value, state.DrawdownReference.Value);
        var result = Apply(state, Request(state, Flow(state, amount, value)));
        var after = SystemShadowPolicy.EvaluateGuards(value + amount, result.SessionReference.Value, result.DrawdownReference.Value);
        Assert.Equal(before.DailyBuyingPaused, after.DailyBuyingPaused);
        Assert.Equal(before.CapitalReviewRequired, after.CapitalReviewRequired);
        var liveBefore = DelphiLivePortfolioPolicy.EvaluateGuards(value, state.SessionReference.Value,
            state.DrawdownReference.Value, false, false, DelphiLivePolicyDefinition.Version1);
        var liveAfter = DelphiLivePortfolioPolicy.EvaluateGuards(value + amount, result.SessionReference.Value,
            result.DrawdownReference.Value, false, false, DelphiLivePolicyDefinition.Version1);
        Assert.Equal(liveBefore.DailyBuyingPaused, liveAfter.DailyBuyingPaused);
        Assert.Equal(liveBefore.CapitalReviewRequired, liveAfter.CapitalReviewRequired);
        Assert.False(result.DailyLossHold);
        Assert.False(result.CapitalReviewRequired);
    }

    [Fact]
    public void MatchedHoldingsAreValuedAndRetainedWithoutChangingTheirProtectionOrHistory()
    {
        var state = State();
        var flow = Flow(state, before: 800m);
        ImmutableArray<BrokerRiskHolding> holdings = [new(Guid.NewGuid(), "FIXTURE.TO", 100, 90m, Guid.NewGuid(), Open)];
        flow = flow with { Before = flow.Before with { Holdings = holdings }, After = flow.After with { Holdings = holdings } };
        var result = Apply(state, Request(state, flow));
        Assert.InRange(Math.Abs(0.98m - 10800m / result.SessionReference.Value), 0m, 0.000000000000000000000001m);
        Assert.Equal(holdings, flow.After.Holdings);
    }

    [Fact]
    public void OpposingFlowsUseTheirIndividualValuesEvenWhenTheNetDepositChangeIsZero()
    {
        var state = State();
        var first = Flow(state, 980m, 9800m);
        // Market performance changed between events; equal net deposits must not erase that fact.
        var second = Flow(state, -980m, 10000m, new('C', 64), Open.AddHours(2));
        var result = Apply(state, Request(state, first, second));
        Assert.Equal(9922m, result.SessionReference.Value);
        Assert.Equal(10914.2m, result.DrawdownReference.Value);
        Assert.True(result.CapitalReviewRequired);
    }

    [Fact]
    public void SimultaneousFlowsNeedACompleteOrderedBookChain()
    {
        var state = State();
        var first = Flow(state);
        var second = Flow(state, -1000m, 10800m, new('C', 64));
        second = second with { Before = first.After };
        var result = Apply(state, Request(state, first, second));
        Assert.InRange(Math.Abs(10000m - result.SessionReference.Value), 0m, 0.00000000000000000000001m);
        Assert.Throws<InvalidOperationException>(() => Apply(state, Request(state, second, first)));
    }

    [Fact]
    public void ASerializedSimultaneousBookChainRetainsItsIdentityAndMarks()
    {
        var state = State();
        var first = Flow(state, before: 800m);
        ImmutableArray<BrokerRiskHolding> holdings = [new(Guid.NewGuid(), "FIXTURE.TO", 100, 90m, Guid.NewGuid(), Open)];
        first = first with { Before = first.Before with { Holdings = holdings }, After = first.After with { Holdings = holdings } };
        var second = new BrokerRiskCashFlowEvent(new('C', 64), -500m, first.After,
            first.After with { EvidenceId = Guid.NewGuid(), Cash = first.After.Cash - 500m });
        var request = Request(state, first, second);
        var restored = JsonSerializer.Deserialize<BrokerRiskContinuationRequest>(JsonSerializer.Serialize(request))!;
        Assert.Equal(Apply(state, request), Apply(state, restored));
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("interval binding")]
    [InlineData("future observation")]
    [InlineData("empty")]
    [InlineData("source")]
    [InlineData("duplicate")]
    [InlineData("event key")]
    [InlineData("zero amount")]
    [InlineData("cash mismatch")]
    [InlineData("negative cash")]
    [InlineData("empty account")]
    [InlineData("zero starting value")]
    [InlineData("book binding")]
    [InlineData("book time")]
    [InlineData("unverified book")]
    [InlineData("same evidence")]
    [InlineData("interval start")]
    [InlineData("outside interval")]
    [InlineData("holdings changed")]
    [InlineData("missing mark")]
    [InlineData("future mark")]
    [InlineData("price changed")]
    public void UnexplainedOrConflictingMovementsCannotRewriteRiskReferences(string change)
    {
        var state = State();
        var flow = Flow(state);
        var request = Request(state, flow);
        var interval = request.CashFlows!;
        ImmutableArray<BrokerRiskHolding> holdings = [new(Guid.NewGuid(), "FIXTURE.TO", 10, 100m, Guid.NewGuid(), Open)];
        flow = change switch
        {
            "event key" => flow with { EventKey = "unverified" },
            "zero amount" => flow with { Amount = 0m },
            "cash mismatch" => flow with { Amount = 1001m },
            "negative cash" => flow with { Before = flow.Before with { Cash = -1m } },
            "empty account" => Flow(state, -9800m),
            "zero starting value" => Flow(state, before: 0m),
            "book binding" => flow with { Before = flow.Before with { BindingId = Guid.NewGuid() } },
            "book time" => flow with { After = flow.After with { At = flow.Before.At.AddTicks(1) } },
            "unverified book" => flow with { After = flow.After with { VerifiedAt = Now.AddSeconds(1) } },
            "same evidence" => flow with { After = flow.After with { EvidenceId = flow.Before.EvidenceId } },
            "interval start" => Flow(state, at: state.Through),
            "outside interval" => Flow(state, at: interval.Through.AddSeconds(1)),
            "holdings changed" => flow with { Before = flow.Before with { Holdings = holdings } },
            "missing mark" => flow with { Before = flow.Before with { Holdings = [holdings[0] with { SavedPrice = null }] },
                After = flow.After with { Holdings = [holdings[0] with { SavedPrice = null }] } },
            "future mark" => flow with { Before = flow.Before with { Holdings = [holdings[0] with { SavedPriceAt = Close }] },
                After = flow.After with { Holdings = [holdings[0] with { SavedPriceAt = Close }] } },
            "price changed" => flow with { Before = flow.Before with { Holdings = holdings },
                After = flow.After with { Holdings = [holdings[0] with { SavedPrice = 101m }] } },
            _ => flow
        };
        interval = interval with { Events = [flow] };
        interval = change switch
        {
            "gap" => interval with { From = state.Through.AddSeconds(1) },
            "interval binding" => interval with { BindingId = Guid.NewGuid() },
            "future observation" => interval with { ObservedAt = Now.AddSeconds(1) },
            "empty" => interval with { Events = [] },
            "source" => interval with { SourceFingerprint = "unverified" },
            "duplicate" => interval with { Events = [flow, flow] },
            _ => interval
        };
        Assert.Throws<InvalidOperationException>(() => Apply(state, request with { CashFlows = interval }));
        Assert.Equal(10000m, state.SessionReference.Value);
        Assert.True(state.CapitalReviewRequired);
    }

    private static BrokerRiskContinuationRequest Boundary(BrokerAccountRiskState state, bool opening, decimal value)
    {
        var date = opening ? Calendar.GetNextSession(state.TradingDate) : state.TradingDate;
        var at = opening ? Calendar.GetSessionBounds(date).OpenUtc : Calendar.GetSessionBounds(date).CloseUtc;
        return new(Guid.NewGuid(), state.BaselineId, state.RevisionId,
            opening ? BrokerRiskContinuationKind.Opening : BrokerRiskContinuationKind.Closing,
            new(Guid.NewGuid(), state.BindingId, date, opening ? BrokerRiskBoundaryKind.Opening : BrokerRiskBoundaryKind.Closing,
                opening ? BrokerAccountSessionValuation.LiveOpening : BrokerAccountSessionValuation.SessionClosing,
                at, at.AddMinutes(5), value, Hash), new(Guid.NewGuid(), state.BindingId, state.Through, at, at.AddMinutes(5), Hash), null);
    }

    [Fact]
    public void FlowExactlyAtCloseCannotSkipTheClosingValuation()
    {
        var state = State();
        state = Apply(state, Request(state, Flow(state, at: Close)));
        Assert.Throws<InvalidOperationException>(() => Apply(state, Boundary(state, true, 10800m)));
        state = Apply(state, Boundary(state, false, 10800m));
        var high = state.DrawdownReference;
        state = Apply(state, Boundary(state, true, 10800m));
        Assert.Same(high, state.DrawdownReference);
        Assert.False(state.DailyLossHold);
        Assert.True(state.CapitalReviewRequired);
    }

    [Fact]
    public void OvernightFlowAdjustsHighBeforeNextOpeningAndDoesNotClearHoldsEarly()
    {
        var state = State();
        Assert.Throws<InvalidOperationException>(() => Apply(state, Request(state, Flow(state, at: Close.AddHours(1)))));
        state = Apply(state, Boundary(state, false, 9800m));
        state = Apply(state, Request(state, Flow(state, at: Close.AddHours(1))));
        Assert.True(state.DailyLossHold);
        var high = state.DrawdownReference;
        state = Apply(state, Boundary(state, true, 10800m));
        Assert.Equal(10800m, state.SessionReference.Value);
        Assert.Same(high, state.DrawdownReference);
        Assert.False(state.DailyLossHold);
        Assert.True(state.CapitalReviewRequired);
    }

    [Fact]
    public async Task DurableRecoveryChecksBrokerEventIdentityAsWellAsTheLocalRequest()
    {
        var store = new Store();
        var workflow = new BrokerAccountRiskContinuationWorkflow(store, Calendar, new Clock());
        var request = Request(store.State, Flow(store.State));
        await Assert.ThrowsAsync<TimeoutException>(() => workflow.ApplyAsync(request));
        store.FailRead = true;
        var recovered = await workflow.ApplyAsync(request);
        Assert.Equal(1, store.Writes);
        Assert.Equal(request.RequestHash, recovered.Request.RequestHash);
        Assert.True(recovered.State.CapitalReviewRequired);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(request with
        { CashFlows = request.CashFlows! with { SourceFingerprint = new('C', 64) } }));
        store.FailRead = false;
        // A replay with a new request ID AND altered effective time must still fail under the store fence.
        var duplicate = Request(store.State, Flow(store.State, at: Open.AddHours(2)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(duplicate));
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public void RequestsWithoutFlowsRetainTheirOriginalJsonHashAndStoredCashReceiptsRoundTrip()
    {
        var state = State();
        var boundary = Boundary(state, false, 9800m);
        Assert.DoesNotContain("CashFlows", JsonSerializer.Serialize(boundary));
        var originalJson = JsonSerializer.Serialize(new { boundary.RequestId, boundary.BaselineId, boundary.PreviousRevisionId,
            boundary.Kind, boundary.Boundary, boundary.NoFlows, boundary.Holds });
        Assert.Equal(EngineStrategySettings.Hash(originalJson), boundary.RequestHash);
        var request = Request(state, Flow(state));
        var receipt = new BrokerRiskContinuationReceipt(request, Apply(state, request), Now);
        var json = JsonSerializer.Serialize(receipt);
        var restored = BrokerAccountRiskContinuationRepository.Decode(new(request.RequestId, request.BaselineId,
            request.PreviousRevisionId, Date.ToDateTime(TimeOnly.MinValue), Now.UtcDateTime,
            request.RequestHash, EngineStrategySettings.Hash(json), json))!;
        Assert.Equal(request.RequestHash, restored.Request.RequestHash);
        Assert.Equal(receipt.State, restored.State);
        Assert.Equal(EventKey, restored.Request.CashFlows!.Events[0].EventKey);
        Assert.DoesNotContain(EventKey, request.ToString());
        Assert.DoesNotContain("9800", request.CashFlows!.Events[0].ToString());
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Store : IBrokerAccountRiskContinuationStore
    {
        public BrokerAccountRiskState State = BrokerAccountCashFlowContinuationTests.State();
        private readonly Dictionary<Guid, BrokerRiskContinuationReceipt> saved = [];
        public int Writes;
        public bool FailRead;
        public Task<BrokerRiskContinuationReceipt?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(saved.GetValueOrDefault(id));
        public Task<BrokerAccountRiskState> ReadAsync(Guid id, CancellationToken ct) => FailRead
            ? throw new InvalidOperationException("fixture read failure") : Task.FromResult(State);
        public Task<BrokerRiskContinuationReceipt> AppendAsync(BrokerRiskContinuationRequest request, CancellationToken ct)
        {
            var state = Apply(State, request);
            BrokerAccountCashFlowContinuation.RequireUnseenEvents(request,
                saved.Values.SelectMany(r => r.Request.CashFlows!.Events).Select(e => e.EventKey));
            // Encode/decode to model process loss rather than retaining in-memory evidence objects.
            var receipt = JsonSerializer.Deserialize<BrokerRiskContinuationReceipt>(JsonSerializer.Serialize(new BrokerRiskContinuationReceipt(request, state, Now)))!;
            saved.Add(request.RequestId, receipt); State = receipt.State; Writes++;
            throw new TimeoutException("fixture response loss");
        }
    }
}
