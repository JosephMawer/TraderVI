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
