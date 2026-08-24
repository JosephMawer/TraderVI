using Core.Calibration;
using Core.ML;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public class SwingMarkToMarketOutcomeCalculatorTests
{
    private static readonly DateTime ObservationDate = new(2026, 1, 1);

    [Fact]
    public void PreOpenRunEntersTheFirstEligibleSession()
    {
        var xiu = Bars(new DateTime(2026, 1, 2), 3, 200);
        var symbol = Bars(new DateTime(2026, 1, 2), 3, 100);

        var readiness = SwingMarkToMarketOutcomeCalculator.AssessReadiness(
            ObservationDate, Utc(2026, 1, 2, 14, 0), symbol, xiu);

        readiness.State.ShouldBe(SwingOutcomeReadinessState.Matured);
        readiness.InitialEligibleSession.ShouldBe(new DateTime(2026, 1, 2));
        readiness.EntrySession.ShouldBe(new DateTime(2026, 1, 2));
        readiness.EntryDelaySessions.ShouldBe(0);
    }

    [Fact]
    public void RunAtTheMarketOpenWaitsForTheNextSession()
    {
        var xiu = Bars(new DateTime(2026, 1, 2), 4, 200);
        var symbol = Bars(new DateTime(2026, 1, 2), 4, 100);

        var readiness = SwingMarkToMarketOutcomeCalculator.AssessReadiness(
            ObservationDate, Utc(2026, 1, 2, 14, 30), symbol, xiu);

        readiness.State.ShouldBe(SwingOutcomeReadinessState.Matured);
        readiness.InitialEligibleSession.ShouldBe(new DateTime(2026, 1, 3));
        readiness.EntrySession.ShouldBe(new DateTime(2026, 1, 3));
        readiness.EntryDelaySessions.ShouldBe(0);
    }

    [Fact]
    public void MissingFirstEntryBarCreatesADelayedEntry()
    {
        var xiu = Bars(new DateTime(2026, 1, 2), 4, 200);
        var symbol = Bars(new DateTime(2026, 1, 2), 4, 100).Skip(1).ToList();

        var readiness = SwingMarkToMarketOutcomeCalculator.AssessReadiness(
            ObservationDate, Utc(2026, 1, 2, 14, 0), symbol, xiu);

        readiness.State.ShouldBe(SwingOutcomeReadinessState.Matured);
        readiness.EntrySession.ShouldBe(new DateTime(2026, 1, 3));
        readiness.EntryDelaySessions.ShouldBe(1);
    }

    [Fact]
    public void MissingEntryRemainsPendingBeforeThreeEligibleSessions()
    {
        var xiu = Bars(new DateTime(2026, 1, 2), 2, 200);

        var readiness = SwingMarkToMarketOutcomeCalculator.AssessReadiness(
            ObservationDate, Utc(2026, 1, 2, 14, 0), Array.Empty<DailyBar>(), xiu);

        readiness.State.ShouldBe(SwingOutcomeReadinessState.Pending);
        readiness.BenchmarkSessionsAvailable.ShouldBe(2);
    }

    [Fact]
    public void MissingEntryBecomesNoEntryAfterThreeEligibleSessions()
    {
        var xiu = Bars(new DateTime(2026, 1, 2), 3, 200);

        var readiness = SwingMarkToMarketOutcomeCalculator.AssessReadiness(
            ObservationDate, Utc(2026, 1, 2, 14, 0), Array.Empty<DailyBar>(), xiu);

        readiness.State.ShouldBe(SwingOutcomeReadinessState.NoEntry);
        readiness.ReasonCode.ShouldBe("NoSymbolBarWithinEntryAllowance");
    }

    [Fact]
    public void OutcomePersistsGrossNetCostAndBenchmarkMeasuresAtEveryHorizon()
    {
        var symbol = CustomBars(new DateTime(2026, 1, 2),
            (100, 102), (102, 104), (104, 106));
        var xiu = CustomBars(new DateTime(2026, 1, 2),
            (200, 202), (202, 204), (204, 206));

        var result = SwingMarkToMarketOutcomeCalculator.Calculate(
            ObservationDate, Utc(2026, 1, 2, 14, 0), symbol, xiu);

        result.RawEntryOpen.ShouldBe(100);
        result.AdjustedEntryPrice.ShouldBe(100.25, .0000001);
        result.XiuRawEntryOpen.ShouldBe(200);
        result.Horizons.Select(x => x.Sessions).ShouldBe(new[] { 1, 2, 3 });
        result.Horizons[0].GrossReturn.ShouldBe(.02, .0000001);

        double expectedNet = (102 * .9975) / (100 * 1.0025) - 1;
        result.Horizons[0].NetReturn.ShouldBe(expectedNet, .0000001);
        result.Horizons[0].XiuGrossReturn.ShouldBe(.01, .0000001);
        result.Horizons[0].XiuRawExitClose.ShouldBe(202);
        result.Horizons[0].NetExcessReturn.ShouldBe(expectedNet - .01, .0000001);
        result.EntrySlippageRate.ShouldBe(.001);
        result.EntryHalfSpreadRate.ShouldBe(.0015);
    }

    [Fact]
    public void MissingAlignedPathSessionIsInvalidOnceBenchmarkPathMatures()
    {
        var xiu = Bars(new DateTime(2026, 1, 2), 3, 200);
        var symbol = Bars(new DateTime(2026, 1, 2), 3, 100)
            .Where((_, index) => index != 1)
            .ToList();

        var readiness = SwingMarkToMarketOutcomeCalculator.AssessReadiness(
            ObservationDate, Utc(2026, 1, 2, 14, 0), symbol, xiu);

        readiness.State.ShouldBe(SwingOutcomeReadinessState.Invalid);
        readiness.FirstInvalidSession.ShouldBe(new DateTime(2026, 1, 3));
        readiness.ReasonCode.ShouldBe("MissingSymbolSession");
    }

    [Fact]
    public void ExistingEntryRemainsPendingUntilItsThreeSessionPathExists()
    {
        var xiu = Bars(new DateTime(2026, 1, 2), 2, 200);
        var symbol = Bars(new DateTime(2026, 1, 2), 2, 100);

        var readiness = SwingMarkToMarketOutcomeCalculator.AssessReadiness(
            ObservationDate, Utc(2026, 1, 2, 14, 0), symbol, xiu);

        readiness.State.ShouldBe(SwingOutcomeReadinessState.Pending);
        readiness.EntrySession.ShouldBe(new DateTime(2026, 1, 2));
    }

    [Fact]
    public void ExcursionsAreCumulativeAndPersistFirstSessionOrdering()
    {
        var symbol = ExcursionBars(new DateTime(2026, 1, 2),
            (100, 105, 98, 104),
            (104, 108, 101, 107),
            (107, 107.5f, 95, 96));
        var xiu = Bars(new DateTime(2026, 1, 2), 3, 200);

        var result = SwingMarkToMarketOutcomeCalculator.CalculateExcursions(
            ObservationDate, Utc(2026, 1, 2, 14, 0), symbol, xiu);

        result.Horizons[0].MfeReturn.ShouldBe(.05, .0000001);
        result.Horizons[0].MaeReturn.ShouldBe(-.02, .0000001);
        result.Horizons[0].ExcursionOrderState.ShouldBe(SwingMarkToMarketOutcomeCalculator.SameSessionUnknown);

        result.Horizons[1].MfeReturn.ShouldBe(.08, .0000001);
        result.Horizons[1].MfeSessionOrdinal.ShouldBe(2);
        result.Horizons[1].MaeSessionOrdinal.ShouldBe(1);
        result.Horizons[1].ExcursionOrderState.ShouldBe(SwingMarkToMarketOutcomeCalculator.AdverseFirst);

        result.Horizons[2].MfeSession.ShouldBe(new DateTime(2026, 1, 3));
        result.Horizons[2].MaeReturn.ShouldBe(-.05, .0000001);
        result.Horizons[2].MaeSessionOrdinal.ShouldBe(3);
        result.Horizons[2].ExcursionOrderState.ShouldBe(SwingMarkToMarketOutcomeCalculator.FavorableFirst);
    }

    [Fact]
    public void ExcursionTiesKeepTheEarliestSession()
    {
        var symbol = ExcursionBars(new DateTime(2026, 1, 2),
            (100, 105, 98, 101),
            (101, 105, 98, 102),
            (102, 104, 99, 103));
        var xiu = Bars(new DateTime(2026, 1, 2), 3, 200);

        var result = SwingMarkToMarketOutcomeCalculator.CalculateExcursions(
            ObservationDate, Utc(2026, 1, 2, 14, 0), symbol, xiu);

        result.Horizons[2].MfeSessionOrdinal.ShouldBe(1);
        result.Horizons[2].MaeSessionOrdinal.ShouldBe(1);
        result.Horizons[2].ExcursionOrderState.ShouldBe(SwingMarkToMarketOutcomeCalculator.SameSessionUnknown);
    }

    [Fact]
    public void InconsistentOhlcIsInvalidForExcursionsOnly()
    {
        var symbol = ExcursionBars(new DateTime(2026, 1, 2),
            (100, 99, 98, 101),
            (101, 103, 100, 102),
            (102, 104, 101, 103));
        var xiu = Bars(new DateTime(2026, 1, 2), 3, 200);

        var markReadiness = SwingMarkToMarketOutcomeCalculator.AssessReadiness(
            ObservationDate, Utc(2026, 1, 2, 14, 0), symbol, xiu);
        var excursionReadiness = SwingMarkToMarketOutcomeCalculator.AssessExcursionReadiness(
            ObservationDate, Utc(2026, 1, 2, 14, 0), symbol, xiu);

        markReadiness.State.ShouldBe(SwingOutcomeReadinessState.Matured);
        excursionReadiness.State.ShouldBe(SwingOutcomeReadinessState.Invalid);
        excursionReadiness.ReasonCode.ShouldBe("InconsistentSymbolOhlc");
        excursionReadiness.FirstInvalidSession.ShouldBe(new DateTime(2026, 1, 2));
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static List<DailyBar> Bars(DateTime start, int count, float firstPrice) =>
        Enumerable.Range(0, count)
            .Select(i => Bar(start.AddDays(i), firstPrice + i, firstPrice + i + 1))
            .ToList();

    private static List<DailyBar> CustomBars(DateTime start, params (float Open, float Close)[] prices) =>
        prices.Select((x, i) => Bar(start.AddDays(i), x.Open, x.Close)).ToList();

    private static List<DailyBar> ExcursionBars(
        DateTime start,
        params (float Open, float High, float Low, float Close)[] prices) =>
        prices.Select((x, i) => new DailyBar
        {
            Date = start.AddDays(i),
            Open = x.Open,
            High = x.High,
            Low = x.Low,
            Close = x.Close,
            Volume = 100000
        }).ToList();

    private static DailyBar Bar(DateTime date, float open, float close) => new()
    {
        Date = date,
        Open = open,
        High = Math.Max(open, close),
        Low = Math.Min(open, close),
        Close = close,
        Volume = 100000
    };
}
