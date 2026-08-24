using Core.Calibration;
using Core.ML;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public class PredictionOutcomeCalculatorTests
{
    [Fact]
    public void ReusesEnabledLabelersAndComputesAlignedReturns()
    {
        var observation = Bars(new DateTime(2026, 1, 1), 60, i => 100 + i * .1f);
        DateTime date = observation[^1].Date;
        var future = Bars(date.AddDays(1), 20, i => i == 9 ? 110 : 106 + i * .1f);
        var xiu = Bars(date.AddDays(1), 20, i => 101 + i * .1f);

        var result = PredictionOutcomeCalculator.Calculate(observation, future, 100, xiu);

        result.MaturedSessions.ShouldBe(20);
        result.Return1.ShouldNotBeNull();
        result.Return5.ShouldNotBeNull();
        result.Return10.ShouldNotBeNull();
        result.Return20.ShouldNotBeNull();
        result.Events.Select(x => x.TaskType).ShouldBe(new[]
        {
            "BinaryUp10", "BinaryDown10", "VolExpansionRelative10", "BreakoutEnhanced"
        });
    }

    [Fact]
    public void MissingSymbolSessionStopsMaturityInsteadOfUsingALaterBar()
    {
        var observation = Bars(new DateTime(2026, 1, 1), 60, _ => 100);
        DateTime date = observation[^1].Date;
        var xiu = Bars(date.AddDays(1), 10, i => 100 + i);
        var future = xiu.Where((_, i) => i != 4).Select(x => new DailyBar
        {
            Date = x.Date, Open = x.Open, High = x.High, Low = x.Low, Close = x.Close, Volume = x.Volume
        }).ToList();

        var result = PredictionOutcomeCalculator.Calculate(observation, future, 100, xiu);

        result.MaturedSessions.ShouldBe(4);
        result.Return5.ShouldBeNull();
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void MissingSessionRemainsPendingUntilBenchmarkHorizonMatures()
    {
        DateTime observationDate = new(2026, 1, 1);
        var xiu = Bars(observationDate.AddDays(1), 9, i => 100 + i);
        var future = xiu.Where((_, i) => i != 4).ToList();

        var readiness = PredictionOutcomeCalculator.AssessReadiness(
            observationDate, future, xiu, PredictionOutcomeCalculator.LabelHorizon);

        readiness.State.ShouldBe(PredictionOutcomeReadinessState.Pending);
        readiness.BenchmarkSessionsAvailable.ShouldBe(9);
    }

    [Fact]
    public void MissingSessionBecomesInvalidWhenBenchmarkHorizonMatures()
    {
        DateTime observationDate = new(2026, 1, 1);
        var xiu = Bars(observationDate.AddDays(1), 10, i => 100 + i);
        var future = xiu.Where((_, i) => i != 4).ToList();

        var readiness = PredictionOutcomeCalculator.AssessReadiness(
            observationDate, future, xiu, PredictionOutcomeCalculator.LabelHorizon);

        readiness.State.ShouldBe(PredictionOutcomeReadinessState.Invalid);
        readiness.AlignedSymbolSessions.ShouldBe(4);
        readiness.FirstInvalidSession.ShouldBe(xiu[4].Date.Date);
        readiness.ReasonCode.ShouldBe("MissingSymbolSession");
    }

    [Fact]
    public void ExactAlignedHorizonIsMatured()
    {
        DateTime observationDate = new(2026, 1, 1);
        var xiu = Bars(observationDate.AddDays(1), 10, i => 100 + i);

        var readiness = PredictionOutcomeCalculator.AssessReadiness(
            observationDate, xiu, xiu, PredictionOutcomeCalculator.LabelHorizon);

        readiness.State.ShouldBe(PredictionOutcomeReadinessState.Matured);
        readiness.AlignedSymbolSessions.ShouldBe(10);
        readiness.FirstInvalidSession.ShouldBeNull();
    }

    private static List<DailyBar> Bars(DateTime start, int count, Func<int, float> close) =>
        Enumerable.Range(0, count).Select(i =>
        {
            float value = close(i);
            return new DailyBar
            {
                Date = start.AddDays(i), Open = value, High = value * 1.02f,
                Low = value * .98f, Close = value, Volume = 100000
            };
        }).ToList();
}
