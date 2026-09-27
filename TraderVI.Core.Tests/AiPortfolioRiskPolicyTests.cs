#nullable enable
using System;
using System.Collections.Immutable;
using System.Text.Json;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiPortfolioRiskPolicyTests
{
    private static readonly DateOnly Friday = new(2026, 9, 25);
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateTimeOffset Open = ReviewedTsxSessionCalendar.At(Friday, new(9, 30));
    private static readonly Guid Portfolio = Guid.NewGuid();
    private static readonly ImmutableArray<string> NoBlocks = [];
    private static readonly ITsxSessionCalendar Calendar = new ReviewedTsxSessionCalendar(new(
        "test", "synthetic calendar", Friday, Monday.AddDays(2), [Friday, Monday, Monday.AddDays(2)]));
    private readonly AiPortfolioRiskPolicy policy = new(Calendar);

    private static PortfolioRiskValuation Value(decimal amount, int minute = 0, DateOnly? session = null)
    {
        var date = session ?? Friday;
        DateTimeOffset at = ReviewedTsxSessionCalendar.At(date, new(9, 30)).AddMinutes(minute);
        return new(Portfolio, Guid.NewGuid(), date, at, at.AddMinutes(2), amount, true, true);
    }

    private AiPortfolioRiskState Start(decimal amount = 10000m)
    {
        var value = Value(amount);
        return policy.Start(value, value.At);
    }

    private AiPortfolioRiskState Observe(AiPortfolioRiskState state, decimal amount, int minute, DateOnly? session = null)
    {
        var value = Value(amount, minute, session);
        return policy.Observe(state, value, value.At);
    }

    private static AiRoundClosure Closure(AiPortfolioRiskState state) => new(Portfolio, state.RoundId, Guid.NewGuid(),
        state.LastValuation.At.AddSeconds(1), 0, 0, state.LastValuation.Value, true);

    private AiPortfolioRiskState Close(AiPortfolioRiskState state)
    {
        var closure = Closure(state);
        return policy.ConfirmClosure(state, closure, closure.At);
    }

    private static AiRoundEntry Entry(AiPortfolioRiskState state, decimal amount, int minute, DateOnly? session = null)
    {
        var value = Value(amount, minute, session);
        return new(state.RoundId, value, value.At, value.At.AddSeconds(1), value.ValidUntil, NoBlocks);
    }

    private AiPortfolioRiskState Next(AiPortfolioRiskState state, int minute, DateOnly? session = null)
    {
        var entry = Entry(state, state.LastValuation.Value, minute, session);
        return policy.BeginNextRound(state, entry, entry.ReviewAt);
    }

    private PortfolioRiskAssessment Assess(AiPortfolioRiskState state, bool review = true) =>
        policy.Assess(state, state.LastValuation.At, review, NoBlocks);

    [Fact]
    public void PeakRaisesTriggerAndAllowanceRecoversWithoutIncreasingNormalAllocation()
    {
        var state = Start();
        Assert.Equal(9500m, state.ExitTrigger);
        state = Observe(state, 11000m, 1);
        Assert.Equal(10450m, state.ExitTrigger);
        Assert.Equal(1m, Assess(state).NewBuyAllowanceMultiplier);
        state = Observe(state, 10725m, 2);
        Assert.Equal(0.5m, Assess(state).NewBuyAllowanceMultiplier);
        state = Observe(state, 10890m, 3);
        Assert.Equal(0.8m, Assess(state).NewBuyAllowanceMultiplier);
        Assert.Equal(11000m, state.RoundPeak);
        Assert.Equal(10000m, state.RoundOpeningCapital);
    }

    [Theory]
    [InlineData(9500)]
    [InlineData(9400)]
    [InlineData(0)]
    public void LimitHitCommitsToClosureDespiteRecoveryOrUnavailableAi(decimal value)
    {
        var state = Observe(Start(), value, 1);
        Assert.Equal(AiRiskRoundPhase.Closing, state.Phase);
        Assert.Equal(1, state.FailedAttempts);
        var risk = Assess(state, review: false);
        Assert.True(risk.RequiresManagedLiquidation);
        Assert.Equal(0m, risk.NewBuyAllowanceMultiplier);
        Assert.Contains("FreshAiReviewRequired", risk.NewBuyBlocks);
        state = Observe(state, 12000m, 2);
        Assert.Equal(AiRiskRoundPhase.Closing, state.Phase);
        Assert.Equal(10000m, state.RoundPeak);
        Assert.True(Assess(state).RequiresManagedLiquidation);
    }

    [Fact]
    public void OneMinuteProtectionContinuesDuringSlowOrUnavailableAiAttention()
    {
        var state = Observe(Start(), 11000m, 1);
        Assert.Equal(0m, Assess(state, review: false).NewBuyAllowanceMultiplier);
        state = Observe(state, 10450m, 2);
        Assert.True(Assess(state, review: false).RequiresManagedLiquidation);
    }

    [Fact]
    public void OrdinarySalesCashPeriodsAndNewSessionPreserveRoundAndPeak()
    {
        var original = Observe(Start(), 11000m, 1);
        // The valuation is identical whether this value is held as stocks or reconciled cash.
        var cash = Observe(original, 11000m, 2);
        var monday = Observe(cash, 10900m, 0, Monday);
        Assert.Equal(original.RoundId, monday.RoundId);
        Assert.Equal(original.SequenceId, monday.SequenceId);
        Assert.Equal(original.RoundPeak, monday.RoundPeak);
        Assert.Equal(original.ExitTrigger, monday.ExitTrigger);
        Assert.Equal(0, monday.FailedAttempts);
        Assert.Throws<InvalidOperationException>(() => policy.ConfirmClosure(monday, Closure(monday), monday.LastValuation.At.AddSeconds(1)));
    }

    [Fact]
    public void SecondRoundCarriesAcrossWeekendThenAdvancesToThirdWithActualCapital()
    {
        var first = Close(Observe(Start(), 9490m, 1));
        var second = Next(first, 2);
        Assert.Equal(2, second.RoundNumber);
        Assert.Equal(0.03m, second.LossLimit);
        Assert.Equal(9490m, second.RoundOpeningCapital);
        var monday = Observe(second, 9490m, 0, Monday);
        Assert.Equal(second.RoundId, monday.RoundId);
        Assert.Equal(2, monday.RoundNumber);
        monday = Close(Observe(monday, monday.ExitTrigger, 1, Monday));
        var third = Next(monday, 2, Monday);
        Assert.Equal(3, third.RoundNumber);
        Assert.Equal(0.01m, third.LossLimit);
        Assert.Equal(10000m, third.OriginalCapital);
        Assert.Equal(second.SequenceId, third.SequenceId);
    }

    [Fact]
    public void ExhaustionRequiresNextReviewedMarketSessionAndDoesNotEraseOriginalCapital()
    {
        var state = Exhaust();
        Assert.Equal(AiRiskRoundPhase.SequencePaused, state.Phase);
        Assert.Equal(3, state.FailedAttempts);
        Assert.False(Assess(state).RequiresManagedLiquidation);
        Assert.Throws<InvalidOperationException>(() => Next(state, 6));
        Assert.Throws<InvalidOperationException>(() => Next(state, 0, Friday.AddDays(1)));
        var resumed = Next(state, 0, Monday);
        Assert.Equal(1, resumed.RoundNumber);
        Assert.Equal(2, resumed.SequenceNumber);
        Assert.NotEqual(state.SequenceId, resumed.SequenceId);
        Assert.Equal(10000m, resumed.OriginalCapital);
        Assert.Equal(9122.85m, resumed.RoundOpeningCapital);
    }

    [Fact]
    public void ExhaustionUsesReviewedCalendarRatherThanAddingOneDay()
    {
        var state = Exhaust();
        var monday = Next(state, 0, Monday);
        for (int round = 1; round <= 3; round++)
        {
            monday = Close(Observe(monday, monday.ExitTrigger, round * 2 - 1, Monday));
            if (round < 3) monday = Next(monday, round * 2, Monday);
        }
        // This synthetic reviewed calendar intentionally omits Tuesday.
        Assert.Throws<InvalidOperationException>(() => Next(monday, 0, Monday.AddDays(1)));
        Assert.Equal(1, Next(monday, 0, Monday.AddDays(2)).RoundNumber);
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(0, 0, false)]
    public void PartialUncertainOrUnreconciledClosureCannotGrantAnotherRound(int holdings, int orders, bool reconciled)
    {
        var state = Observe(Start(), 9500m, 1);
        var closure = Closure(state) with { RemainingManagedHoldings = holdings, UnresolvedOrders = orders, IsReconciled = reconciled };
        Assert.Throws<InvalidOperationException>(() => policy.ConfirmClosure(state, closure, closure.At));
        Assert.Throws<InvalidOperationException>(() => Next(state, 2));
    }

    [Theory]
    [InlineData("CapitalReviewRequired")]
    [InlineData("OperatorPaused")]
    [InlineData("CashFlowReconciliationRequired")]
    public void IndependentHoldsBlockBuyingAndReentryWithoutBlockingProtectiveClosure(string block)
    {
        var failed = Observe(Start(), 9500m, 1);
        var assessment = policy.Assess(failed, failed.LastValuation.At, true, [block]);
        Assert.Equal(0m, assessment.NewBuyAllowanceMultiplier);
        Assert.True(assessment.RequiresManagedLiquidation);
        var closed = Close(failed);
        var entry = Entry(closed, 9500m, 2) with { IndependentBlocks = [block] };
        Assert.Throws<InvalidOperationException>(() => policy.BeginNextRound(closed, entry, entry.ReviewAt));
    }

    [Fact]
    public void PreClosureSignalOrExpiredReviewCannotStartNextRound()
    {
        var state = Close(Observe(Start(), 9500m, 1));
        var entry = Entry(state, 9500m, 2);
        Assert.Throws<InvalidOperationException>(() => policy.BeginNextRound(state,
            entry with { SignalAt = state.ReconciledAt!.Value }, entry.ReviewAt));
        Assert.Throws<InvalidOperationException>(() => policy.BeginNextRound(state,
            entry with { ValidUntil = entry.ReviewAt.AddSeconds(-1) }, entry.ReviewAt));
        Assert.Throws<InvalidOperationException>(() => policy.BeginNextRound(state,
            entry with { ReviewAt = entry.SignalAt.AddSeconds(-1) }, entry.ReviewAt));
    }

    [Fact]
    public void ClosureAndEntryEvidenceCannotBeReusedForAnotherRound()
    {
        var failed = Observe(Start(), 9500m, 1);
        var closure = Closure(failed) with { RoundId = Guid.NewGuid() };
        Assert.Throws<InvalidOperationException>(() => policy.ConfirmClosure(failed, closure, closure.At));
        var closed = Close(failed);
        var entry = Entry(closed, 9500m, 2) with { PreviousRoundId = Guid.NewGuid() };
        Assert.Throws<InvalidOperationException>(() => policy.BeginNextRound(closed, entry, entry.ReviewAt));
    }

    [Fact]
    public void MissingStaleFutureWrongPortfolioOrUnreconciledFlowDataCannotChangePeak()
    {
        var state = Start();
        var good = Value(12000m, 1);
        PortfolioRiskValuation[] bad = [
            good with { IsComplete = false }, good with { NoCapitalFlowsConfirmed = false },
            good with { PortfolioId = Guid.NewGuid() }, good with { EvidenceId = Guid.Empty },
            good with { ValidUntil = good.At.AddSeconds(-1) }, good with { At = good.At.AddSeconds(1) },
            good with { Value = -1m }, good with { TradingDate = Monday }
        ];
        foreach (var value in bad)
            Assert.Throws<InvalidOperationException>(() => policy.Observe(state, value, good.At));
        Assert.Equal(10000m, state.RoundPeak);
        var stale = policy.Assess(state, state.LastValuation.ValidUntil.AddTicks(1), true, NoBlocks);
        Assert.Equal(0m, stale.NewBuyAllowanceMultiplier);
        Assert.Contains("ValuationNotCurrent", stale.NewBuyBlocks);
    }

    [Fact]
    public void DuplicateEvidenceIsIdempotentAndOlderOrConflictingPricesAreRejected()
    {
        var state = Observe(Start(), 11000m, 1);
        Assert.Equal(state, policy.Observe(state, state.LastValuation, state.LastValuation.At));
        Assert.Throws<InvalidOperationException>(() => policy.Observe(state,
            state.LastValuation with { Value = 12000m }, state.LastValuation.At));
        Assert.Throws<InvalidOperationException>(() => policy.Observe(state, Value(12000m, 0), state.LastValuation.At));
    }

    [Fact]
    public void SerializedStateRetainsLatchedFailureButIsNotADurableJournal()
    {
        var state = Close(Observe(Start(), 9500m, 1));
        var restored = JsonSerializer.Deserialize<AiPortfolioRiskState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(state, restored);
        Assert.Equal(0m, Assess(restored).NewBuyAllowanceMultiplier);
        Assert.Equal(2, Next(restored, 2).RoundNumber);
    }

    [Fact]
    public void InvalidStateAndMissingIndependentChecksFailClosed()
    {
        var state = Start();
        Assert.Throws<InvalidOperationException>(() => Assess(state with { RoundNumber = 4 }));
        Assert.Throws<InvalidOperationException>(() => Assess(state with { RoundPeak = 9000m }));
        Assert.Throws<InvalidOperationException>(() => Assess(state with { Phase = AiRiskRoundPhase.Active, FailedSession = Friday }));
        Assert.Throws<ArgumentException>(() => policy.Assess(state, Open, true, default));
        Assert.Throws<InvalidOperationException>(() => policy.Start(Value(0m), Open));
    }

    private AiPortfolioRiskState Exhaust()
    {
        var state = Start();
        for (int round = 1; round <= 3; round++)
        {
            state = Close(Observe(state, state.ExitTrigger, round * 2 - 1));
            if (round < 3) state = Next(state, round * 2);
        }
        return state;
    }
}
