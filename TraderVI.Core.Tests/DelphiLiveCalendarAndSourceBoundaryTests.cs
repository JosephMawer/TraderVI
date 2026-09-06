#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveCalendarAndSourceBoundaryTests
{
    [Fact]
    public void ReviewedCalendarUsesListedSessionsAndTorontoDaylightSavingBoundaries()
    {
        var winter = new DateOnly(2026, 3, 6);
        var summer = new DateOnly(2026, 3, 9);
        var calendar = new ReviewedTsxSessionCalendar(new("synthetic-fixture", "test only",
            winter, summer.AddDays(1), [winter, summer]));

        calendar.GetSessionBounds(winter).OpenUtc.Hour.ShouldBe(14);
        calendar.GetSessionBounds(summer).OpenUtc.Hour.ShouldBe(13);
        calendar.GetNextSession(winter).ShouldBe(summer);
        calendar.GetImmediatelyPrecedingSession(summer).ShouldBe(winter);
        calendar.IsRegularSession(summer.AddDays(1)).ShouldBeFalse(); // A weekday is not inferred.
        Should.Throw<InvalidOperationException>(() => calendar.GetNextSession(summer));
        Should.Throw<InvalidOperationException>(() => calendar.IsRegularSession(summer.AddDays(2)));
    }

    [Theory]
    [InlineData("{\"version\":\"test\",\"sourceReference\":\"test\",\"regularSessionDates\":[\"2026-09-08\"]}")]
    [InlineData("{\"version\":\"test\",\"sourceReference\":\"test\",\"firstCoveredDate\":\"2026-09-08\",\"lastCoveredDate\":\"2026-09-08\",\"regularSessionDates\":null}")]
    public void MissingCalendarCoverageAndNullDatesFailClosed(string json)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Exception? error = Record.Exception(() => ReviewedTsxSessionCalendar.Load(path));
            (error is JsonException or ArgumentException).ShouldBeTrue();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IdenticalSourceTimestampsUseSqlServerGuidOrderForTheSameFrozenWinner()
    {
        var date = new DateOnly(2026, 9, 8);
        var boundary = new DateTime(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);
        var sqlFirst = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-000000000001");
        var dotnetFirst = Guid.Parse("00000001-0000-0000-0000-FFFFFFFFFFFF");
        var run = new DelphiLiveOfficialRunSource(sqlFirst, Guid.NewGuid(), "OfficialPaper", "Valid",
            date, date.AddDays(-4), boundary.AddHours(-1), boundary.AddMinutes(-1));
        var winner = DelphiLiveFrozenSourceSelector.Freeze(date, date.AddDays(-4), boundary,
            [run with { RunId = dotnetFirst }, run], [], []);
        winner.Run!.RunId.ShouldBe(sqlFirst);
    }
}
