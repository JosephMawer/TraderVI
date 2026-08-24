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

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static List<DailyBar> Bars(DateTime start, int count, float firstPrice) =>
        Enumerable.Range(0, count)
            .Select(i => Bar(start.AddDays(i), firstPrice + i, firstPrice + i + 1))
            .ToList();

    private static List<DailyBar> CustomBars(DateTime start, params (float Open, float Close)[] prices) =>
        prices.Select((x, i) => Bar(start.AddDays(i), x.Open, x.Close)).ToList();

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
