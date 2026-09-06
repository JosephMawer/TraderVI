#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.IO;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveInstalledCalendarTests
{
    [Fact]
    public void Reviewed2026CalendarRespectsCanadianClosuresAndLabourDayBoundary()
    {
        var calendar = Load();
        foreach (string closed in new[] { "2026-01-01", "2026-02-16", "2026-04-03", "2026-05-18",
            "2026-07-01", "2026-08-03", "2026-09-07", "2026-10-12" })
            calendar.IsRegularSession(DateOnly.Parse(closed)).ShouldBeFalse(closed);

        calendar.GetNextSession(new(2026, 9, 6)).ShouldBe(new DateOnly(2026, 9, 8));
        calendar.GetImmediatelyPrecedingSession(new(2026, 9, 8)).ShouldBe(new DateOnly(2026, 9, 4));
        calendar.GetNextSession(new(2026, 10, 9)).ShouldBe(new DateOnly(2026, 10, 13));
    }

    [Fact]
    public void SettlementAndBankHolidaysDoNotRemoveOpenEquitySessions()
    {
        var calendar = Load();
        foreach (string open in new[] { "2026-01-19", "2026-05-25", "2026-06-19", "2026-07-03",
            "2026-09-30", "2026-11-11", "2026-11-26", "2026-11-27" })
            calendar.IsRegularSession(DateOnly.Parse(open)).ShouldBeTrue(open);
    }

    [Fact]
    public void InstalledCalendarUsesTorontoDaylightSavingInBothDirections()
    {
        var calendar = Load();
        calendar.GetSessionBounds(new(2026, 3, 6)).OpenUtc.Hour.ShouldBe(14);
        calendar.GetSessionBounds(new(2026, 3, 9)).OpenUtc.Hour.ShouldBe(13);
        calendar.GetSessionBounds(new(2026, 10, 30)).CloseUtc.Hour.ShouldBe(20);
        calendar.GetSessionBounds(new(2026, 11, 2)).CloseUtc.Hour.ShouldBe(21);
    }

    [Fact]
    public void BoundedCalendarPreservesHistoryAndRefusesTheUnsupportedShortSession()
    {
        var calendar = Load();
        var history = new DateOnly(2026, 9, 8);
        for (int i = 0; i < 21; i++) history = calendar.GetImmediatelyPrecedingSession(history);
        history.ShouldBe(new DateOnly(2026, 8, 7));

        int count = 0;
        for (var date = new DateOnly(2026, 1, 1); date <= new DateOnly(2026, 12, 23); date = date.AddDays(1))
        {
            if (!calendar.IsRegularSession(date)) continue;
            date.DayOfWeek.ShouldNotBe(DayOfWeek.Saturday);
            date.DayOfWeek.ShouldNotBe(DayOfWeek.Sunday);
            var bounds = calendar.GetSessionBounds(date);
            (bounds.CloseUtc - bounds.OpenUtc).ShouldBe(TimeSpan.FromHours(6.5));
            count++;
        }
        count.ShouldBe(247);
        // December24 is a real short session outside this snapshot, not a holiday to skip.
        Should.Throw<InvalidOperationException>(() => calendar.IsRegularSession(new(2026, 12, 24)));
        Should.Throw<InvalidOperationException>(() => calendar.GetNextSession(new(2026, 12, 23)));
        Should.Throw<InvalidOperationException>(() => calendar.IsRegularSession(new(2025, 12, 31)));
    }

    private static ReviewedTsxSessionCalendar Load()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TraderVI.sln")))
                return ReviewedTsxSessionCalendar.Load(Path.Combine(directory.FullName, "Operations", "Calendars",
                    "tsx-2026-through-20261223-v1.json"));
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("The reviewed calendar repository could not be located.");
    }
}
