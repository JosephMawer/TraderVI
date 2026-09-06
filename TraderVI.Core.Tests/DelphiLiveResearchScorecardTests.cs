#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace TraderVI.Core.Tests;

public sealed class DelphiLiveResearchScorecardTests
{
    private static readonly DelphiLivePolicyDefinition Policy = DelphiLivePolicyDefinition.Version1;
    private static readonly DateOnly Date = new(2026, 9, 8);
    private static readonly DateTime Open = new(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);
    private static ImmutableArray<DelphiLiveDiagnosticSlot> Slots(params string[] symbols) => Enumerable.Range(0, 5)
        .Select(i => new DelphiLiveDiagnosticSlot(i + 1, i < symbols.Length ? symbols[i] : null, i < symbols.Length ? Guid.NewGuid() : null)).ToImmutableArray();

    [Fact]
    public void UnusedSlotsStayCashAndStructurallyUnavailableHorizonStaysNotApplicable()
    {
        var metrics = new Dictionary<string, DelphiLiveOutcomeMetric> { ["AAA"] = DelphiLiveOutcomeMetric.Valid(.10m) };
        var result = DelphiLiveResearchScorecards.CalculateBasket(Slots("AAA"), metrics, Policy);
        result.EqualWeightReturn.ShouldBe(.02m);
        result.Coverage.ValidCount.ShouldBe(5);
        var notApplicable = DelphiLiveResearchScorecards.CalculateBasket(Slots(), metrics, Policy, horizonApplicable: false);
        notApplicable.EqualWeightReturn.ShouldBeNull();
        notApplicable.Coverage.NotApplicableCount.ShouldBe(5);
        notApplicable.State.ShouldBe(DelphiLiveOutcomeMetricState.NotApplicable);
    }

    [Fact]
    public void MissingCheckpointAndEntireMissingSessionRemainInExpectedDenominator()
    {
        var cash = DelphiLiveResearchScorecards.CalculateBasket(Slots(), new Dictionary<string, DelphiLiveOutcomeMetric>(), Policy);
        var comparison = new DelphiLiveCheckpointComparison(Date, Open.AddMinutes(20), DelphiLiveSourceLens.Continuation, cash, cash);
        var next = Date.AddDays(1);
        var summary = DelphiLiveResearchScorecards.Aggregate(DelphiLiveSourceLens.Continuation, [comparison], Policy,
            [(Date, Open.AddMinutes(20)), (Date, Open.AddMinutes(25)), (next, Open.AddDays(1).AddMinutes(20))]);
        summary.ExpectedCohorts.ShouldBe(2);
        summary.Sessions[0].PairedCheckpointCoverage.InvalidCount.ShouldBe(1);
        summary.Sessions[1].PairedCheckpointCoverage.InvalidCount.ShouldBe(1);
        summary.CohortCoverage.Readiness.ShouldBe(DelphiLiveCoverageReadiness.Blocked);
        summary.IncrementalReturn.ShouldBeNull();
    }

    [Fact]
    public void CheckpointsAverageWithinSessionBeforeEqualCohortAverage()
    {
        DelphiLiveBasketMetric Basket(decimal value) => DelphiLiveResearchScorecards.CalculateBasket(Slots("AAA", "BBB", "CCC", "DDD", "EEE"),
            new[] { "AAA", "BBB", "CCC", "DDD", "EEE" }.ToDictionary(s => s, _ => DelphiLiveOutcomeMetric.Valid(value)), Policy);
        var rows = new[]
        {
            new DelphiLiveCheckpointComparison(Date, Open.AddMinutes(20), DelphiLiveSourceLens.Continuation, Basket(0m), Basket(.1m)),
            new DelphiLiveCheckpointComparison(Date, Open.AddMinutes(25), DelphiLiveSourceLens.Continuation, Basket(0m), Basket(.3m)),
            new DelphiLiveCheckpointComparison(Date.AddDays(1), Open.AddDays(1).AddMinutes(20), DelphiLiveSourceLens.Continuation, Basket(0m), Basket(.4m))
        };
        var summary = DelphiLiveResearchScorecards.Aggregate(DelphiLiveSourceLens.Continuation, rows, Policy, rows.Select(r => (r.TradingDate, r.BarEndUtc)).ToArray());
        summary.LiveEqualCohortReturn.ShouldBe(.3m);
    }

    [Fact]
    public void DailyControlUsesOwnFrozenLensAndLiveRequiresIndependentConfirmation()
    {
        var facts = new[] { Evidence("AAA", 1, 2, false), Evidence("BBB", 2, 1, true) };
        var continuation = DelphiLiveResearchScorecards.Snapshot(Guid.NewGuid(), Guid.NewGuid(), Date, Open.AddMinutes(20), Policy.PolicyVersionId, DelphiLiveSourceLens.Continuation, facts);
        var breakout = DelphiLiveResearchScorecards.Snapshot(Guid.NewGuid(), Guid.NewGuid(), Date, Open.AddMinutes(20), Policy.PolicyVersionId, DelphiLiveSourceLens.Breakout, facts);
        continuation.DailyTop5[0].Symbol.ShouldBe("AAA");
        breakout.DailyTop5[0].Symbol.ShouldBe("BBB");
        continuation.ConfirmedLiveTop5[0].Symbol.ShouldBe("BBB");
        continuation.ConfirmedLiveTop5.Count(s => s.Symbol is null).ShouldBe(4);
    }

    [Fact]
    public void PortfolioDailyReturnIncludesOvernightMovementAndNeverUsesStalePriorClose()
    {
        var portfolio = Portfolio() with { Marks = [Mark(Date, 1000m), Mark(Date.AddDays(1), 900m)],
            CurrentSession = Date.AddDays(1), OpeningNav = 950m, PreviousClosingNav = 900m };
        DelphiLiveResearchCoordinator.CalculateDailyReturn(portfolio, Date.AddDays(1), Date).ShouldBe(-.10m);
        DelphiLiveResearchCoordinator.CalculateDailyReturn(portfolio, Date.AddDays(1), Date.AddDays(-1)).ShouldBeNull();
        DelphiLiveResearchCoordinator.CalculateDailyReturn(portfolio, Date, Date.AddDays(-1)).ShouldBe(0m);
        DelphiLiveResearchScorecards.MaximumCheckpointDrawdown(1000m, [1200m, 1100m, 900m]).ShouldBe(.25m);
    }

    [Fact]
    public void StoredBasketAndProtocolContractsRoundTripWithoutComputedAliases()
    {
        var snapshot = DelphiLiveResearchScorecards.Snapshot(Guid.NewGuid(), Guid.NewGuid(), Date, Open.AddMinutes(20), Policy.PolicyVersionId,
            DelphiLiveSourceLens.Continuation, [Evidence("AAA", 1, 1, false)]);
        var restored = DelphiLiveLedgerJson.Deserialize<DelphiLiveRankingCheckpoint>(DelphiLiveLedgerJson.Serialize(snapshot));
        restored.DailyTop5[0].ShouldBe(snapshot.DailyTop5[0]);
        restored.ConfirmedLiveTop5.All(s => s.Symbol is null).ShouldBeTrue();
    }

    [Fact]
    public void HistoricalFillDiagnosticsExcludeFutureExitAndRequireExactClosingNav()
    {
        Guid positionId = Guid.NewGuid(), buyId = Guid.NewGuid(), sellId = Guid.NewGuid();
        var position = new DelphiLiveLedgerPosition(positionId, "AAA", 1, 100m, Open.AddHours(1), buyId, "{}",
            DelphiLiveProfitProtectionState.Open(positionId, 100m), Open.AddDays(1).AddHours(1), sellId);
        var buy = new DelphiLiveLedgerFill(Guid.NewGuid(), buyId, positionId, Guid.NewGuid(), "AAA", DelphiLiveActionSide.Buy, 1, 100m,
            DelphiLiveQuoteField.Ask, DelphiLiveFillConfidence.SideSpecific, Open.AddHours(1), Date);
        var sell = new DelphiLiveLedgerFill(Guid.NewGuid(), sellId, positionId, Guid.NewGuid(), "AAA", DelphiLiveActionSide.Sell, 1, 110m,
            DelphiLiveQuoteField.Price, DelphiLiveFillConfidence.EstimatedFill, Open.AddDays(1).AddHours(1), Date.AddDays(1));
        var portfolio = Portfolio() with { Positions = [position], Fills = [buy, sell], Marks = [Mark(Date, 1000m), Mark(Date.AddDays(1), 1010m)] };
        var first = DelphiLiveResearchCoordinator.FillDiagnostic(portfolio, Open.AddHours(6.5));
        first.AllFillCount.ShouldBe(1); first.EstimatedFillCount.ShouldBe(0); first.ClosedTradeCount.ShouldBe(0);
        first.OfficialNavReturn.ShouldBe(0m);
        DelphiLiveResearchCoordinator.FillDiagnostic(portfolio, Open.AddDays(2).AddHours(6.5)).OfficialNavReturn.ShouldBeNull();
    }

    private static DelphiLivePortfolioSnapshot Portfolio() => DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(),
        Policy.PolicyVersionId, "OperationalChampion", null, 1000m, "CAD", Date, Open, Open.AddDays(-1), "tests", "test capital"));
    private static DelphiLiveLedgerMark Mark(DateOnly date, decimal nav) => new(Guid.NewGuid(), date, DelphiLivePortfolioMarkKind.Closing,
        Open.AddDays(date.DayNumber - Date.DayNumber).AddHours(6.5), true, nav, [], "CompleteExactNav");
    private static DelphiLiveRankingEvidence Evidence(string symbol, int continuation, int breakout, bool confirmed)
    {
        var quality = new DelphiLiveDailySetupQuality(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), .5m,
            [new(DelphiLiveSourceLens.Continuation, true, true, continuation, .5m, "Published", "Passed"),
             new(DelphiLiveSourceLens.Breakout, true, true, breakout, .5m, "Published", "Passed")]);
        return new(Guid.NewGuid(), new(symbol, new(DelphiLiveMomentumState.Strong, DelphiLiveStrongTier.FourOfFour,
            DelphiLiveNeutralDetail.None, 4, 0, 0, 0), 4, quality, false), confirmed);
    }
}
