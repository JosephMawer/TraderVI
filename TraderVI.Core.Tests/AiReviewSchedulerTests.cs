using System;
using System.Linq;
using System.Threading.Tasks;
using Core.Oracle;
using Core.Trader.DelphiLive;
using Xunit;

namespace TraderVI.Core.Tests;

public class AiReviewSchedulerTests
{
    internal static readonly DateOnly Date = new(2026, 9, 28);
    internal static readonly DateTimeOffset Open = new(2026, 9, 28, 13, 30, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset Close = Open.AddHours(6.5);
    internal static ReviewedTsxSessionCalendar Calendar() => new(new("synthetic", "test fixture",
        Date.AddDays(-1), Date.AddDays(1), [Date, Date.AddDays(1)]));
    internal static AiReviewScheduler NewSchedule() => new(Calendar(), Date,
        new(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(195), TimeSpan.FromMinutes(5)));
    internal static AiReviewEvent Event(string id, DateTimeOffset at, AiReviewEventKind kind = AiReviewEventKind.EntrySignal) =>
        new(id, kind, at, "synthetic/" + id);
    private static void Succeed(AiReviewScheduler scheduler, AiReviewRequest request, DateTimeOffset now) =>
        scheduler.Complete(request.Id, now, AiReviewOutcome.Reviewed, Close);

    [Fact]
    public void ReviewedCalendarAndExplicitCheckpointOrderAreRequired()
    {
        var timing = new AiReviewTiming(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(195), TimeSpan.FromMinutes(5));
        Assert.Throws<InvalidOperationException>(() => new AiReviewScheduler(Calendar(), Date.AddDays(-1), timing));
        Assert.Throws<InvalidOperationException>(() => new AiReviewScheduler(Calendar(), Date.AddDays(2), timing));
        Assert.Throws<ArgumentException>(() => new AiReviewScheduler(Calendar(), Date, timing with { InitialInterval = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => new AiReviewScheduler(Calendar(), Date, timing with { PreCloseBeforeClose = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => new AiReviewScheduler(Calendar(), Date, timing with { MidSessionAfterOpen = TimeSpan.FromHours(7) }));
    }

    [Fact]
    public void SessionBoundariesAndThreeCheckpointsDoNotCreateCatchupBursts()
    {
        var scheduler = NewSchedule();
        Assert.Null(scheduler.TryBegin(Open.AddTicks(-1)));
        Assert.Equal("OutsideSession", scheduler.Read(Open.AddTicks(-1)).ReviewBlock);
        var first = scheduler.TryBegin(Open)!;
        Assert.Equal(new[] { "SessionStart" }, first.Reasons);
        Succeed(scheduler, first, Open);
        var mid = scheduler.TryBegin(Open.AddMinutes(195))!;
        Assert.Contains("MidSession", mid.Reasons);
        Assert.DoesNotContain("SessionStart", mid.Reasons);
        Succeed(scheduler, mid, Open.AddMinutes(195));
        var end = scheduler.TryBegin(Close.AddMinutes(-5))!;
        Assert.Contains("PreClose", end.Reasons);
        Succeed(scheduler, end, Close.AddMinutes(-5));
        Assert.Null(scheduler.TryBegin(Close));
        Assert.Null(scheduler.Read(Close).NextReviewUtc);
        Assert.Equal("OutsideSession", scheduler.Read(Close).ReviewBlock);

        var late = NewSchedule();
        var combined = late.TryBegin(Close.AddMinutes(-4))!;
        Assert.Equal(new[] { "SessionStart", "MidSession", "PreClose" }, combined.Reasons);
        Succeed(late, combined, Close.AddMinutes(-4));
        Assert.Null(late.TryBegin(Close.AddMinutes(-4)));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0.5, 1)]
    [InlineData(5, 5)]
    [InlineData(30, 15)]
    public void RequestedCadenceIsBoundedAndRetainsItsExplanation(double requested, double applied)
    {
        var scheduler = NewSchedule();
        var request = scheduler.TryBegin(Open)!;
        Succeed(scheduler, request, Open);
        var change = scheduler.RequestInterval(Open, TimeSpan.FromMinutes(requested), "Synthetic attention request");
        Assert.Equal(TimeSpan.FromMinutes(requested), change.Requested);
        Assert.Equal(TimeSpan.FromMinutes(applied), change.Applied);
        Assert.Equal("Synthetic attention request", change.Reason);
        Assert.Equal(Open.AddMinutes(applied), scheduler.Read(Open).NextReviewUtc);
    }

    [Fact]
    public void EventsCoalesceAndCannotBypassMinimumOrRepeatAfterConsumption()
    {
        var scheduler = NewSchedule();
        Succeed(scheduler, scheduler.TryBegin(Open)!, Open);
        var events = Enum.GetValues<AiReviewEventKind>().Select(kind => Event(kind.ToString(), Open.AddSeconds(10), kind)).ToArray();
        foreach (var item in events) Assert.True(scheduler.RequestEvent(item, Open.AddSeconds(10)));
        Assert.False(scheduler.RequestEvent(events[0], Open.AddSeconds(10)));
        Assert.Null(scheduler.TryBegin(Open.AddSeconds(59)));
        var combined = scheduler.TryBegin(Open.AddMinutes(1))!;
        Assert.Equal(3, combined.Events.Length);
        Assert.Contains("HoldingClosed", combined.Reasons);
        Assert.Equal("PendingEvents", scheduler.Read(Open.AddMinutes(1)).ReviewBlock);
        Succeed(scheduler, combined, Open.AddMinutes(1));
        Assert.Null(scheduler.Read(Open.AddMinutes(1)).ReviewBlock);
        Assert.False(scheduler.RequestEvent(events[0], Open.AddMinutes(2)));
        Assert.Null(scheduler.TryBegin(Open.AddMinutes(2)));
    }

    [Fact]
    public async Task ConcurrentTicksAdmitOnlyOneReviewAndLaterEventsRemainPending()
    {
        var scheduler = NewSchedule();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() => scheduler.TryBegin(Open))));
        var admitted = Assert.Single(attempts.Where(a => a != null));
        scheduler.RequestEvent(Event("during-call", Open.AddMinutes(2)), Open.AddMinutes(2));
        Assert.Null(scheduler.TryBegin(Open.AddMinutes(2)));
        Succeed(scheduler, admitted!, Open.AddMinutes(3));
        Assert.Equal("PendingEvents", scheduler.Read(Open.AddMinutes(3)).ReviewBlock);
        Assert.Null(scheduler.TryBegin(Open.AddMinutes(3).AddSeconds(59)));
        var followup = scheduler.TryBegin(Open.AddMinutes(4))!;
        Assert.Equal("during-call", Assert.Single(followup.Events).Id);
    }

    [Theory]
    [InlineData(AiReviewOutcome.BudgetUnavailable)]
    [InlineData(AiReviewOutcome.DataUnavailable)]
    [InlineData(AiReviewOutcome.Failed)]
    [InlineData(AiReviewOutcome.Cancelled)]
    public void FailureKeepsRequestsAndBlockUntilAFreshSuccessfulReview(AiReviewOutcome outcome)
    {
        var scheduler = NewSchedule();
        scheduler.RequestEvent(Event("entry", Open), Open);
        var failed = scheduler.TryBegin(Open)!;
        scheduler.Complete(failed.Id, Open.AddMinutes(2), outcome);
        Assert.Equal(outcome.ToString(), scheduler.Read(Open.AddMinutes(2)).ReviewBlock);
        Assert.Null(scheduler.TryBegin(Open.AddMinutes(2).AddSeconds(59)));
        var retry = scheduler.TryBegin(Open.AddMinutes(3))!;
        Assert.NotEqual(failed.Id, retry.Id);
        Assert.Single(retry.Events);
        Assert.Contains("SessionStart", retry.Reasons);
        Assert.Equal(outcome.ToString(), scheduler.Read(Open.AddMinutes(3)).ReviewBlock);
        Succeed(scheduler, retry, Open.AddMinutes(3));
        Assert.Null(scheduler.Read(Open.AddMinutes(3)).ReviewBlock);
    }

    [Fact]
    public void StaleAndAfterCloseResultsCannotClearReviewBlock()
    {
        var scheduler = NewSchedule();
        scheduler.Complete(scheduler.TryBegin(Open)!.Id, Open.AddMinutes(1), AiReviewOutcome.Reviewed, Open.AddMinutes(1));
        Assert.Equal("DataUnavailable", scheduler.Read(Open.AddMinutes(1)).ReviewBlock);
        var late = scheduler.TryBegin(Close.AddSeconds(-1))!;
        scheduler.Complete(late.Id, Close, AiReviewOutcome.Reviewed, Close.AddMinutes(5));
        Assert.Equal(AiReviewOutcome.DataUnavailable, scheduler.Read(Close).LastAttempt!.Outcome);
    }

    [Fact]
    public void SuccessfulReviewExpiresAndCompletionCannotBeReplayed()
    {
        var scheduler = NewSchedule();
        var first = scheduler.TryBegin(Open)!;
        scheduler.Complete(first.Id, Open, AiReviewOutcome.Reviewed, Open.AddMinutes(2));
        Assert.Null(scheduler.Read(Open.AddMinutes(1)).ReviewBlock);
        Assert.Equal("FreshReviewRequired", scheduler.Read(Open.AddMinutes(2)).ReviewBlock);
        Assert.Throws<InvalidOperationException>(() => scheduler.Complete(first.Id, Open.AddMinutes(2), AiReviewOutcome.Reviewed, Close));
    }

    [Fact]
    public void ChangedEventIdentityLatchesBlockEvenIfInFlightReviewSucceeds()
    {
        var scheduler = NewSchedule();
        var item = Event("entry", Open);
        scheduler.RequestEvent(item, Open);
        var attempt = scheduler.TryBegin(Open)!;
        Assert.Throws<InvalidOperationException>(() => scheduler.RequestEvent(item with { EvidenceId = "changed" }, Open));
        Succeed(scheduler, attempt, Open);
        Assert.Equal("EventHistoryRequiresReview", scheduler.Read(Open).ReviewBlock);
        Assert.Null(scheduler.TryBegin(Open.AddMinutes(15)));
    }

    [Fact]
    public void InvalidEventsAndClockRollbackCannotAlterSchedule()
    {
        var scheduler = NewSchedule();
        Assert.Throws<ArgumentException>(() => scheduler.RequestEvent(Event("future", Open.AddMinutes(1)), Open));
        Assert.Throws<ArgumentException>(() => scheduler.RequestEvent(Event("old", Open.AddDays(-1)), Open));
        scheduler.Read(Open);
        Assert.Throws<InvalidOperationException>(() => scheduler.TryBegin(Open.AddTicks(-1)));
        Assert.NotNull(scheduler.TryBegin(Open));
    }
}
