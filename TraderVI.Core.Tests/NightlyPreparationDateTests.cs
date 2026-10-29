using Core.Runtime;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.IO;
using Xunit;

namespace TraderVI.Core.Tests;

public class NightlyPreparationDateTests
{
    private static ReviewedTsxSessionCalendar Calendar() => new(new("fixture", "synthetic",
        new(2026, 10, 1), new(2026, 11, 30),
        [new(2026, 10, 27), new(2026, 10, 28), new(2026, 10, 29), new(2026, 10, 30), new(2026, 11, 2)]));

    [Fact]
    public void EarlyAndScheduledPreparationUseTheSameCompletedSessionAndCollectionWindow()
    {
        var early = NightlyPreparationDate.Resolve("2026-10-29", DateTimeOffset.Parse("2026-10-28T21:00:00-04:00"), Calendar());
        var overnight = NightlyPreparationDate.Resolve(null, DateTimeOffset.Parse("2026-10-29T00:30:00-04:00"), Calendar());
        early.ShouldBe(overnight);
        Calendar().GetImmediatelyPrecedingSession(DateOnly.FromDateTime(early)).ShouldBe(new(2026, 10, 28));
        DailyBenchmarkPolicy.UsIndexRefreshRange(early, new(2026, 10, 27), 10)
            .ShouldBe(DailyBenchmarkPolicy.UsIndexRefreshRange(overnight, new(2026, 10, 27), 10));
        DailyBenchmarkPolicy.UsIndexRefreshRange(early, null, 10).Through.ShouldBe(new(2026, 10, 28));
    }

    [Theory]
    [InlineData("2026-10-28T19:59:59Z")]
    [InlineData("2026-10-28T13:00:00Z")]
    public void TomorrowCannotConsumeAnUnclosedSession(string now) =>
        Should.Throw<InvalidDataException>(() => NightlyPreparationDate.Resolve("2026-10-29", DateTimeOffset.Parse(now), Calendar()));

    [Theory]
    [InlineData("2026-10-27")]
    [InlineData("2026-10-30")]
    [InlineData("10/29/2026")]
    [InlineData("2026-02-30")]
    [InlineData("")]
    public void InvalidOrOutOfRangeDatesFailClosed(string date) =>
        Should.Throw<InvalidDataException>(() => NightlyPreparationDate.Resolve(date, DateTimeOffset.Parse("2026-10-28T21:00:00-04:00"), Calendar()));

    [Fact]
    public void DefaultIsTorontoDateEvenWhenUtcHasCrossedMidnight() =>
        NightlyPreparationDate.Resolve(null, DateTimeOffset.Parse("2026-10-29T01:00:00Z"), Calendar()).ShouldBe(new(2026, 10, 28));

    [Fact]
    public void WeekendUsesReviewedFridayAndHonorsDaylightSavingChange()
    {
        var early = NightlyPreparationDate.Resolve("2026-11-02", DateTimeOffset.Parse("2026-11-01T21:00:00-05:00"), Calendar());
        Calendar().GetImmediatelyPrecedingSession(DateOnly.FromDateTime(early)).ShouldBe(new(2026, 10, 30));
        Should.Throw<InvalidDataException>(() => NightlyPreparationDate.Resolve("2026-11-02", DateTimeOffset.Parse("2026-11-01T15:59:59-05:00"), Calendar()));
    }

    [Fact]
    public void MissingCalendarCoverageCannotBeGuessed() =>
        Should.Throw<InvalidOperationException>(() => NightlyPreparationDate.Resolve("2026-12-01", DateTimeOffset.Parse("2026-11-30T21:00:00-05:00"), Calendar()));
}
