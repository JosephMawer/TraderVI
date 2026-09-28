using Core.TMX.Models.Domain;
using Sandbox.Probes;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TmxHeldPriceFreshnessProbeTests
{
    private static readonly DateTime Start = new(2026, 9, 28, 14, 0, 0, DateTimeKind.Utc);
    private static OhlcvBar Bar(int minute, long volume = 100) => new(Start.AddMinutes(minute), 10m, 10m, 10m, 10m, volume);
    private static TmxIntradayBatch Batch(int interval, params OhlcvBar[] bars) => new("TEST", interval, Start,
        Start.AddMinutes(5), Start.AddMinutes(5).AddSeconds(10), Start.AddMinutes(5).AddSeconds(11), 1, 1, bars);

    [Fact]
    public void CompleteMinutesCanReconstructExactFiveMinuteBar()
    {
        var result = TmxHeldPriceFreshnessProbe.CompareIntervals(Batch(1, Bar(0), Bar(1), Bar(2), Bar(3), Bar(4)), Batch(5, Bar(0, 500)));
        Assert.Equal(1, result.CompleteGroups);
        Assert.Equal(1, result.ExactOhlcvMatches);
        Assert.Equal(0, result.SparseGroups);
    }

    [Theory]
    [InlineData(200, 1, 0)]
    [InlineData(500, 0, 1)]
    public void SparseGroupsRemainSparseEvenWhenVolumeMatches(long fiveVolume, int equal, int different)
    {
        var result = TmxHeldPriceFreshnessProbe.CompareIntervals(Batch(1, Bar(0), Bar(4)), Batch(5, Bar(0, fiveVolume)));
        Assert.Equal(0, result.CompleteGroups);
        Assert.Equal(0, result.ExactOhlcvMatches);
        Assert.Equal(1, result.SparseGroups);
        Assert.Equal(equal, result.SparseGroupsWithEqualVolume);
        Assert.Equal(different, result.SparseGroupsWithDifferentVolume);
    }

    [Fact]
    public void DifferentPricesAreNotAnExactMatchAndPartialEdgeBucketsAreExcluded()
    {
        var one = Batch(1, Bar(0), Bar(1), Bar(2), Bar(3), Bar(4));
        var different = Bar(0, 500) with { Close = 11m, High = 11m };
        Assert.Equal(0, TmxHeldPriceFreshnessProbe.CompareIntervals(one, Batch(5, different)).ExactOhlcvMatches);
        Assert.Equal(0, TmxHeldPriceFreshnessProbe.CompareIntervals(one with { RequestedStartUtc = Start.AddMinutes(1) },
            Batch(5, Bar(0, 500))).CompleteGroups);
        Assert.Equal(0, TmxHeldPriceFreshnessProbe.CompareIntervals(one with { ReceivedUtc = Start.AddMinutes(4) },
            Batch(5, Bar(0, 500))).CompleteGroups);
    }

    [Fact]
    public void DifferentSymbolsCannotBeCompared()
        => Assert.Throws<ArgumentException>(() => TmxHeldPriceFreshnessProbe.CompareIntervals(Batch(1), Batch(5) with { Symbol = "OTHER" }));
}
