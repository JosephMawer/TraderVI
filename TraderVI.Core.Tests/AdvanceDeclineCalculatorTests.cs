using Core.Indicators;
using Core.ML;
using Shouldly;
using System;
using System.Collections.Generic;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AdvanceDeclineCalculatorTests
{
    private static readonly DateTime Day0 = new(2026, 8, 15);
    private static readonly DateTime LookbackDay = new(2026, 8, 18);
    private static readonly DateTime FirstNewDay = new(2026, 8, 19);
    private static readonly DateTime SecondNewDay = new(2026, 8, 20);

    [Fact]
    public void Compute_IncrementalRun_PrimesLookbackWithoutAccumulatingItAgain()
    {
        var entries = AdvanceDeclineCalculator.Compute(
            CreateSymbolBars(),
            CreateXiuBars(),
            previousCumulative: 100,
            accumulateFromDate: FirstNewDay);

        entries.Count.ShouldBe(2);

        entries[0].Date.ShouldBe(FirstNewDay);
        entries[0].DailyPlurality.ShouldBe(0);
        entries[0].CumulativeDifferential.ShouldBe(100);

        entries[1].Date.ShouldBe(SecondNewDay);
        entries[1].DailyPlurality.ShouldBe(1);
        entries[1].CumulativeDifferential.ShouldBe(101);
    }

    [Fact]
    public void Compute_FullRun_AccumulatesEveryComparableDateFromZero()
    {
        var entries = AdvanceDeclineCalculator.Compute(CreateSymbolBars(), CreateXiuBars());

        entries.Count.ShouldBe(3);
        entries[0].Date.ShouldBe(LookbackDay);
        entries[0].DailyPlurality.ShouldBe(3);
        entries[0].CumulativeDifferential.ShouldBe(3);
        entries[1].CumulativeDifferential.ShouldBe(3);
        entries[2].CumulativeDifferential.ShouldBe(4);
    }

    private static Dictionary<string, IReadOnlyList<DailyBar>> CreateSymbolBars() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AAA"] = Bars(10, 11, 12, 13),
            ["BBB"] = Bars(20, 21, 20, 22),
            ["CCC"] = Bars(30, 31, 31, 30)
        };

    private static IReadOnlyList<DailyBar> CreateXiuBars() => Bars(50, 51, 52, 53);

    private static IReadOnlyList<DailyBar> Bars(
        float day0Close,
        float lookbackClose,
        float firstNewClose,
        float secondNewClose) =>
        new List<DailyBar>
        {
            new() { Date = Day0, Close = day0Close },
            new() { Date = LookbackDay, Close = lookbackClose },
            new() { Date = FirstNewDay, Close = firstNewClose },
            new() { Date = SecondNewDay, Close = secondNewClose }
        };
}
