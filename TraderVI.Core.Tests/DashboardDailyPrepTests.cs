using Core.Trader.DelphiLive;
using System;
using System.IO;
using System.Text.Json;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public class DashboardDailyPrepTests
{
    private static readonly ReviewedTsxSessionCalendar Calendar = new(new("fixture", "fixture",
        new(2026, 10, 29), new(2026, 11, 4),
        [new(2026, 10, 29), new(2026, 10, 30), new(2026, 11, 2), new(2026, 11, 3), new(2026, 11, 4)]));

    [Theory]
    [InlineData("2026-10-30T19:59:00Z", "2026-10-30", "Succeeded", "Succeeded", "Oct 30 · Ready")]
    [InlineData("2026-10-30T20:00:00Z", "2026-10-30", "Succeeded", "Succeeded", "Nov 2 · Pending")]
    [InlineData("2026-11-01T22:00:00Z", "2026-11-02", "Succeeded", "Succeeded", "Nov 2 · Ready")]
    [InlineData("2026-11-02T14:30:00Z", "2026-11-01", "Succeeded", "Succeeded", "Nov 2 · Overdue")]
    [InlineData("2026-11-02T14:30:00Z", "2026-11-02", "Attention", "Attention", "Nov 2 · Attention")]
    [InlineData("2026-11-02T14:30:00Z", "2026-11-02", "Failed", "Failed", "Nov 2 · Failed")]
    [InlineData("2026-11-02T14:30:00Z", "2026-11-02", "Running", "Running", "Nov 2 · Preparing")]
    [InlineData("2026-11-02T14:30:00Z", "2026-11-02", "Succeeded", "Running", "Nov 2 · Unverified")]
    public void MatchesTheReceiptToTheTradingSession(string clock, string preparationDate, string state, string stage, string label)
    {
        var now = DateTimeOffset.Parse(clock);
        var path = Path.GetTempFileName();
        try
        {
            DateTimeOffset? end = now.AddMinutes(-1);
            File.WriteAllText(path, JsonSerializer.Serialize(new { preparationDate, state, startedUtc = now.AddMinutes(-5),
                completedUtc = state == "Running" ? null : end,
                stages = new[] { new { name = "Delphi", state = stage, completedUtc = stage == "Running" ? null : end } } }));
            var display = DashboardDailyPrep.Read(path, Calendar, now);
            Assert.Equal(label, display.Label);
            if (stage != "Running") Assert.Contains("Last finished step: Delphi", display.Detail);
        }
        finally { File.Delete(path); }
    }
}
