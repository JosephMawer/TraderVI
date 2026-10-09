using System;
using Core.Trader.DelphiLive;
using Sandbox.Probes;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TmxLiveCollectionFailureProbeTests
{
    private static readonly ReviewedTsxSessionCalendar Calendar = new(new("test", "synthetic",
        new(2026, 10, 2), new(2026, 10, 10), [new(2026, 10, 2), new(2026, 10, 9)]));
    private static readonly DateTime Now = new(2026, 10, 9, 13, 42, 0, DateTimeKind.Utc);

    [Fact]
    public void FixedHistoricalWindowDoesNotMoveToLatestInterval()
    {
        var window = TmxLiveCollectionFailureProbe.SelectWindow(Calendar, Now, "2026-10-02T14:55:00Z");
        Assert.Equal(new DateTime(2026, 10, 2, 14, 50, 0, DateTimeKind.Utc), window.StartUtc);
        Assert.Equal(window, TmxLiveCollectionFailureProbe.SelectWindow(Calendar, Now.AddHours(8), "2026-10-02T14:55:00Z"));
    }

    [Fact]
    public void DefaultUsesLatestFiveMinuteBoundaryAfterTwoMinuteDelay()
    {
        var window = TmxLiveCollectionFailureProbe.SelectWindow(Calendar, Now.AddSeconds(-1), null);
        Assert.Equal(Now.AddMinutes(-7), window.EndUtc);
        Assert.Equal(Now.AddMinutes(-12), window.StartUtc);
        Assert.Equal(Now.AddMinutes(-2), TmxLiveCollectionFailureProbe.SelectWindow(Calendar, Now, null).EndUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-10-02T14:55:00")]
    [InlineData("2026-10-02T14:55:00+00:00")]
    [InlineData("2026-10-02T14:54:00Z")]
    [InlineData("2026-10-02T14:55:01Z")]
    [InlineData("2026-10-09T13:45:00Z")]
    [InlineData("2026-10-02T13:30:00Z")]
    [InlineData("2026-10-02T20:05:00Z")]
    [InlineData("2026-10-03T14:55:00Z")]
    [InlineData("2026-10-01T14:55:00Z")]
    public void InvalidOrUnqualifiedFixedWindowIsRejected(string end) =>
        Assert.ThrowsAny<Exception>(() => TmxLiveCollectionFailureProbe.SelectWindow(Calendar, Now, end));

    [Theory]
    [InlineData(13, 29)]
    [InlineData(13, 36)]
    [InlineData(20, 0)]
    public void DefaultCannotUsePriorSessionOrUnreleasedFirstBar(int hour, int minute) =>
        Assert.Throws<InvalidOperationException>(() => TmxLiveCollectionFailureProbe.SelectWindow(
            Calendar, new(2026, 10, 9, hour, minute, 0, DateTimeKind.Utc), null));
}
