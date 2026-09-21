#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerAccountRiskContinuationTests
{
    private static readonly DateOnly Date = new(2026, 9, 21);
    private static readonly DateTimeOffset Open = ReviewedTsxSessionCalendar.At(Date, new(9, 30));
    private static readonly DateTimeOffset Close = ReviewedTsxSessionCalendar.At(Date, new(16, 0));
    private static readonly DateTimeOffset Now = Close.AddMinutes(5);
    private static readonly ITsxSessionCalendar Calendar = new ReviewedTsxSessionCalendar(new(
        "fixture", "synthetic", Date.AddDays(-3), Date.AddDays(2), [Date.AddDays(-3), Date, Date.AddDays(1), Date.AddDays(2)]));
    private static readonly string Hash = new('A', 64);

    private static BrokerAccountRiskBaseline Baseline() => new(Guid.NewGuid(), Guid.NewGuid(), "TFSA", Guid.NewGuid(),
        Open.AddMinutes(20), Date, new(10000m, BrokerRiskReferenceSource.SetupObservation, Guid.NewGuid(), Open.AddMinutes(19)),
        new(11000m, BrokerRiskReferenceSource.ReportedClosingHigh, Guid.NewGuid(), Open.AddDays(-3)), null, null,
        TradingRecommendationMemory.Empty with { TradingDate = Date, DailyLossHold = true, CapitalReviewRequired = true })
        { ObservationAt = Open.AddMinutes(19) };
    private static BrokerAccountRiskState State() => BrokerAccountRiskContinuation.Start(Baseline());
    private static BrokerRiskContinuationRequest Boundary(BrokerAccountRiskState state, bool opening = false, decimal value = 10500m)
    {
        var date = opening ? Calendar.GetNextSession(state.TradingDate) : state.TradingDate;
        var bounds = Calendar.GetSessionBounds(date);
        DateTimeOffset at = opening ? bounds.OpenUtc : bounds.CloseUtc;
        return new(Guid.NewGuid(), state.BaselineId, state.RevisionId,
            opening ? BrokerRiskContinuationKind.Opening : BrokerRiskContinuationKind.Closing,
            new(Guid.NewGuid(), state.BindingId, date, opening ? BrokerRiskBoundaryKind.Opening : BrokerRiskBoundaryKind.Closing,
                opening ? BrokerAccountSessionValuation.LiveOpening : BrokerAccountSessionValuation.SessionClosing,
                at, at.AddMinutes(1), value, Hash),
            new(Guid.NewGuid(), state.BindingId, state.Through, at, at.AddMinutes(1), Hash), null);
    }
    private static BrokerAccountRiskState Apply(BrokerAccountRiskState state, BrokerRiskContinuationRequest request) =>
        BrokerAccountRiskContinuation.Apply(state, request, Calendar, Now.AddDays(2));

    [Fact]
    public void StartingFromSavedBaselinePreservesReferenceProvenanceAndHolds()
    {
        var baseline = Baseline();
        var state = BrokerAccountRiskContinuation.Start(baseline);
        Assert.Same(baseline.SessionReference, state.SessionReference);
        Assert.Same(baseline.DrawdownReference, state.DrawdownReference);
        Assert.True(state.DailyLossHold);
        Assert.True(state.CapitalReviewRequired);
        Assert.Null(state.RevisionId);
        Assert.Equal(baseline.ObservationAt, state.Through);
        Assert.False(BrokerAccountRiskContinuation.Start(baseline with
        { CarriedMemory = baseline.CarriedMemory with { TradingDate = Date.AddDays(-1) } }).DailyLossHold);
    }

    [Theory]
    [InlineData(12000, 12000)]
    [InlineData(11000, 11000)]
    [InlineData(10500, 11000)]
    public void OnlyACompletedCloseCanRaiseTheSavedHigh(decimal close, decimal expectedHigh)
    {
        var state = State();
        var request = Boundary(state, value: close);
        var result = Apply(state, request);
        Assert.Equal(expectedHigh, result.DrawdownReference.Value);
        Assert.Equal(Date, result.LastCompletedSession);
        Assert.True(result.DailyLossHold);
        Assert.True(result.CapitalReviewRequired);
        Assert.Same(state.SessionReference, result.SessionReference);
        Assert.Equal(request.RequestId, result.RevisionId);
        Assert.Equal(Close, result.Through);
        if (close > 11000m) Assert.Equal(BrokerRiskReferenceSource.VerifiedSessionClosing, result.DrawdownReference.Source);
    }

    [Fact]
    public void NextSessionResetsOnlyDailyHoldAndDoesNotReplaceClosingHigh()
    {
        var state = State();
        state = Apply(state, Boundary(state));
        var result = Apply(state, Boundary(state, opening: true, value: 12500m));
        Assert.Equal(Date.AddDays(1), result.TradingDate);
        Assert.False(result.DailyLossHold);
        Assert.True(result.CapitalReviewRequired);
        Assert.Equal(12500m, result.SessionReference.Value);
        Assert.Equal(11000m, result.DrawdownReference.Value);
        Assert.Equal(BrokerRiskReferenceSource.VerifiedSessionOpening, result.SessionReference.Source);
    }

    [Fact]
    public void SameDayReferenceCannotBeReplacedIncludingAnInitialMiddayObservation()
    {
        var state = State();
        var request = Boundary(state);
        request = request with { Kind = BrokerRiskContinuationKind.Opening, Boundary = request.Boundary! with
        { Kind = BrokerRiskBoundaryKind.Opening, At = Open, ValuationContract = BrokerAccountSessionValuation.LiveOpening } };
        Assert.Throws<InvalidOperationException>(() => Apply(state, request));
        Assert.Equal(BrokerRiskReferenceSource.SetupObservation, state.SessionReference.Source);
    }

    [Fact]
    public void RestartRecoversMissingClosingHistoryInsteadOfSkippingIt()
    {
        var state = State();
        Assert.Throws<InvalidOperationException>(() => Apply(state, Boundary(state, opening: true)));
        state = Apply(state, Boundary(state));
        var skipped = Boundary(state, opening: true);
        skipped = skipped with { Boundary = skipped.Boundary! with { TradingDate = Date.AddDays(2),
            At = Calendar.GetSessionBounds(Date.AddDays(2)).OpenUtc } };
        Assert.Throws<InvalidOperationException>(() => Apply(state, skipped));
    }

    [Theory]
    [InlineData("weekend")]
    [InlineData("after close")]
    public void FirstSetupOutsideMarketHoursDoesNotRequireAnInventedEarlierClose(string when)
    {
        var baseline = Baseline();
        baseline = when == "weekend"
            ? baseline with { TradingDate = Date.AddDays(-1), EstablishedAt = Open.AddDays(-1), ObservationAt = Open.AddDays(-1).AddSeconds(-1) }
            : baseline with { EstablishedAt = Close.AddMinutes(10), ObservationAt = Close.AddMinutes(9) };
        var state = BrokerAccountRiskContinuation.Start(baseline);
        var result = Apply(state, Boundary(state, opening: true));
        Assert.Equal(Calendar.GetNextSession(baseline.TradingDate), result.TradingDate);
        Assert.Equal(11000m, result.DrawdownReference.Value);
    }

    [Fact]
    public void OrdinaryHoldObservationsCanAddButNeverReleaseAHold()
    {
        var state = State();
        var request = new BrokerRiskContinuationRequest(Guid.NewGuid(), state.BaselineId, null,
            BrokerRiskContinuationKind.HoldObservation, null, null,
            new(Guid.NewGuid(), state.BindingId, Date, Open.AddHours(1), false, false));
        var result = Apply(state, request);
        Assert.True(result.DailyLossHold);
        Assert.True(result.CapitalReviewRequired);
        Assert.Equal(state.Through, result.Through);
        state = state with { DailyLossHold = false, CapitalReviewRequired = false };
        result = Apply(state, request with { Holds = request.Holds! with { DailyLossHold = true, CapitalReviewRequired = true } });
        Assert.True(result.DailyLossHold);
        Assert.True(result.CapitalReviewRequired);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("baseline")]
    [InlineData("binding")]
    [InlineData("future")]
    [InlineData("price")]
    [InlineData("contract")]
    [InlineData("source")]
    [InlineData("cash interval")]
    [InlineData("cash binding")]
    [InlineData("cash source")]
    [InlineData("no flow proof")]
    public void IncompleteOrConflictingEvidenceCannotAdvanceReferences(string change)
    {
        var state = State();
        var request = Boundary(state);
        request = change switch
        {
            "parent" => request with { PreviousRevisionId = Guid.NewGuid() },
            "baseline" => request with { BaselineId = Guid.NewGuid() },
            "binding" => request with { Boundary = request.Boundary! with { BindingId = Guid.NewGuid() } },
            "future" => request with { Boundary = request.Boundary! with { ObservedAt = Now.AddDays(3) } },
            "price" => request with { Boundary = request.Boundary! with { Value = 0m } },
            "contract" => request with { Boundary = request.Boundary! with { ValuationContract = "BrokerGraphTimestampAlone" } },
            "source" => request with { Boundary = request.Boundary! with { SourceFingerprint = "missing" } },
            "cash interval" => request with { NoFlows = request.NoFlows! with { From = state.Through.AddTicks(1) } },
            "cash binding" => request with { NoFlows = request.NoFlows! with { BindingId = Guid.NewGuid() } },
            "cash source" => request with { NoFlows = request.NoFlows! with { SourceFingerprint = "missing" } },
            _ => request with { NoFlows = null }
        };
        Assert.ThrowsAny<Exception>(() => Apply(state, request));
        Assert.Equal(11000m, state.DrawdownReference.Value);
    }

    [Fact]
    public async Task ACommittedRequestRecoversAfterResponseLossWithoutRereadingOrDuplicating()
    {
        var store = new Store { LoseResponse = true };
        var workflow = new BrokerAccountRiskContinuationWorkflow(store, Calendar, store.Clock);
        var request = Boundary(store.State);
        await Assert.ThrowsAsync<TimeoutException>(() => workflow.ApplyAsync(request));
        Assert.Equal(1, store.Writes);
        store.FailRead = true;
        var result = await workflow.ApplyAsync(request);
        Assert.Same(store.Saved, result);
        Assert.Equal(1, store.Writes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(request with
        { Boundary = request.Boundary! with { Value = 20000m } }));
    }

    [Fact]
    public async Task StoreRechecksTheRevisionAfterWorkflowPreflight()
    {
        var store = new Store { ChangeBeforeAppend = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BrokerAccountRiskContinuationWorkflow(store, Calendar, store.Clock).ApplyAsync(Boundary(store.State)));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task FailedReadsAndCancellationNeverInitializeReplacementState()
    {
        var store = new Store { FailRead = true };
        var workflow = new BrokerAccountRiskContinuationWorkflow(store, Calendar, store.Clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(Boundary(store.State)));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.ApplyAsync(Boundary(store.State), cts.Token));
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("id")]
    [InlineData("baseline")]
    [InlineData("parent")]
    [InlineData("date")]
    [InlineData("timestamp")]
    [InlineData("request hash")]
    [InlineData("content hash")]
    [InlineData("json")]
    public void StoredReceiptChecksExactTimestampsIdentityAndContent(string change)
    {
        var state = State();
        var request = Boundary(state);
        var receipt = new BrokerRiskContinuationReceipt(request, Apply(state, request), Now.AddTicks(12345));
        var json = JsonSerializer.Serialize(receipt);
        var row = new BrokerAccountRiskContinuationRepository.Row(request.RequestId, request.BaselineId,
            request.PreviousRevisionId, Date.ToDateTime(TimeOnly.MinValue), receipt.SavedAt.UtcDateTime,
            request.RequestHash, EngineStrategySettings.Hash(json), json);
        row = change switch
        {
            "id" => row with { RequestId = Guid.NewGuid() },
            "baseline" => row with { BaselineId = Guid.NewGuid() },
            "parent" => row with { PreviousRevisionId = Guid.NewGuid() },
            "date" => row with { TradingDate = row.TradingDate.AddDays(1) },
            "timestamp" => row with { SavedUtc = row.SavedUtc.AddTicks(-1) },
            "request hash" => row with { RequestHash = new string('B', 64) },
            "content hash" => row with { ContentHash = new string('B', 64) },
            "json" => row with { ReceiptJson = JsonSerializer.Serialize(receipt with { State = receipt.State with { CapitalReviewRequired = false } }) },
            _ => row
        };
        if (change == "none")
        {
            var restored = BrokerAccountRiskContinuationRepository.Decode(row)!;
            Assert.Equal(receipt.SavedAt, restored.SavedAt);
            Assert.Equal(request.RequestHash, restored.Request.RequestHash);
        }
        else Assert.Throws<InvalidOperationException>(() => BrokerAccountRiskContinuationRepository.Decode(row));
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Store : IBrokerAccountRiskContinuationStore
    {
        public BrokerAccountRiskState State = BrokerAccountRiskContinuationTests.State();
        public BrokerRiskContinuationReceipt? Saved;
        public readonly Clock Clock = new();
        public bool LoseResponse, FailRead, ChangeBeforeAppend;
        public int Writes;
        public Task<BrokerRiskContinuationReceipt?> FindAsync(Guid requestId, CancellationToken ct) => Task.FromResult(Saved);
        public Task<BrokerAccountRiskState> ReadAsync(Guid baselineId, CancellationToken ct) =>
            FailRead ? throw new InvalidOperationException("fixture read failure") : Task.FromResult(State);
        public Task<BrokerRiskContinuationReceipt> AppendAsync(BrokerRiskContinuationRequest request, CancellationToken ct)
        {
            if (ChangeBeforeAppend) State = State with { RevisionId = Guid.NewGuid() };
            State = BrokerAccountRiskContinuation.Apply(State, request, Calendar, Clock.GetUtcNow());
            Saved = new(request, State, Clock.GetUtcNow()); Writes++;
            return LoseResponse ? throw new TimeoutException("fixture response loss") : Task.FromResult(Saved);
        }
    }
}
