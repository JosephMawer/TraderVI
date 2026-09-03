using Core.Trader;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class PaperTradingMonitorScheduleTests
{
    [Theory]
    [InlineData(9, 31, 9, 47)]
    [InlineData(9, 46, 9, 47)]
    [InlineData(9, 47, 9, 52)]
    [InlineData(15, 46, 15, 47)]
    [InlineData(15, 47, 15, 52)]
    [InlineData(16, 1, 16, 2)]
    public void NextScheduledPollLocal_UsesFiveMinuteCadenceAfterFirstSafePoll(
        int hour,
        int minute,
        int expectedHour,
        int expectedMinute)
    {
        var local = new DateTime(2026, 8, 26, hour, minute, 0);

        DateTime result = PaperTradingMonitor.NextScheduledPollLocal(local);

        Assert.Equal(new DateTime(2026, 8, 26, expectedHour, expectedMinute, 0), result);
    }

    [Theory]
    [InlineData(9, 46, false)]
    [InlineData(9, 47, true)]
    [InlineData(16, 2, true)]
    [InlineData(16, 3, false)]
    public void IsAutomaticPollTime_BoundsRegularMonitoringWindow(
        int hour,
        int minute,
        bool expected)
    {
        var local = new DateTime(2026, 8, 26, hour, minute, 0);

        Assert.Equal(expected, PaperTradingMonitor.IsAutomaticPollTime(local));
    }

    [Fact]
    public void IsAutomaticPollTime_RejectsWeekend()
    {
        var saturday = new DateTime(2026, 8, 29, 12, 0, 0);

        Assert.False(PaperTradingMonitor.IsAutomaticPollTime(saturday));
    }
}
