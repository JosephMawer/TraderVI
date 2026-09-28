using Core.TMX.Models.Domain;
using Sandbox.Probes;
using System;
using System.Collections.Generic;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TmxOneMinuteFreshnessProbeTests
{
    private static readonly DateTime Start = new(2026, 9, 28, 14, 0, 0, DateTimeKind.Utc);
    private static OhlcvBar Bar(int minute, decimal price = 10m) => new(Start.AddMinutes(minute), price, price, price, price, 100);
    private static TmxIntradayBatch Batch(params OhlcvBar[] bars) => new("TEST", 1, Start, Start.AddMinutes(4),
        Start.AddMinutes(4).AddSeconds(10), Start.AddMinutes(4).AddSeconds(11), 1, 1, bars);

    [Fact]
    public void ExcludesFormingBarAndMeasuresFromCompletedBarEnd()
    {
        var summary = TmxOneMinuteFreshnessProbe.Analyze(Batch(Bar(0), Bar(1), Bar(2), Bar(3), Bar(4)),
            new Dictionary<DateTime, OhlcvBar>(), null);
        Assert.Equal(4, summary.CompletedMinutes);
        Assert.Equal(0, summary.MissingMinutes);
        Assert.Equal(1, summary.FormingBars);
        Assert.Equal(11d, summary.AgeSeconds);
    }

    [Fact]
    public void CountsMissingBeginningMiddleAndTailRatherThanOnlyInternalGaps()
    {
        var summary = TmxOneMinuteFreshnessProbe.Analyze(Batch(Bar(1)), new Dictionary<DateTime, OhlcvBar>(), null);
        Assert.Equal(3, summary.MissingMinutes);
        Assert.Equal(131d, summary.AgeSeconds);
    }

    [Fact]
    public void DetectsCompletedRevisionsDisappearanceAndTimestampRegression()
    {
        var previous = new Dictionary<DateTime, OhlcvBar> { [Start] = Bar(0), [Start.AddMinutes(3)] = Bar(3) };
        var summary = TmxOneMinuteFreshnessProbe.Analyze(Batch(Bar(0, 11m)), previous, Start.AddMinutes(3));
        Assert.Equal(1, summary.ComparableCompletedBars);
        Assert.Equal(1, summary.RevisedCompletedBars);
        Assert.Equal(1, summary.DisappearedCompletedBars);
        Assert.Equal("Regressed", summary.Advance);
    }

    [Fact]
    public void EmptyResponseIsMissingNotFresh()
    {
        var summary = TmxOneMinuteFreshnessProbe.Analyze(Batch(), new Dictionary<DateTime, OhlcvBar>(), null);
        Assert.Equal(4, summary.MissingMinutes);
        Assert.Null(summary.AgeSeconds);
        Assert.Equal("Missing", summary.Advance);
    }
}
