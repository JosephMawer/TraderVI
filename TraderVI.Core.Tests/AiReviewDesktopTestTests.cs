#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiReviewDesktopTestTests
{
    [Theory]
    [InlineData("")]
    [InlineData("2026-09-28T13:30:00Z")]
    [InlineData("2026-09-28T13:40:01Z")]
    [InlineData("2026-09-28T13:35:00")]
    [InlineData("2026-09-28T13:35:00+00:00")]
    public void InvalidExpiredOrExcessiveDeadlineCannotStartTest(string value) =>
        Assert.Throws<InvalidDataException>(() => AiReviewTestSession.Parse(value, AiReviewSchedulerTests.Open));

    [Fact]
    public void AbsentDeadlineDoesNotActivateTest() =>
        Assert.Null(AiReviewTestSession.Parse(null, AiReviewSchedulerTests.Open));

    [Fact]
    public void TestRequiresDisabledSetupAndLeavesOriginalSettingsOff()
    {
        var test = AiReviewTestSession.Parse("2026-09-28T13:35:00Z", AiReviewSchedulerTests.Open)!;
        var settings = new AiPaperReviewSettings(false, Guid.NewGuid(), null, "synthetic", "synthetic",
            AiReviewSchedulerTests.Date, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10), 15, TimeSpan.FromMinutes(5), []);
        Assert.Throws<InvalidOperationException>(() => test.EnableForThisLaunch(null));
        Assert.Throws<InvalidOperationException>(() => test.EnableForThisLaunch(settings with { Enabled = true }));
        var temporary = test.EnableForThisLaunch(settings);
        Assert.True(temporary.Enabled);
        Assert.False(settings.Enabled);
        Assert.Equal(settings, temporary with { Enabled = false });
    }

    [Theory]
    [InlineData(AiReviewOutcome.Reviewed, 2, 5)]
    [InlineData(AiReviewOutcome.DataUnavailable, 1, 3)]
    public async Task ActualSchedulerRunsOnlyWhenDueAndStopsAfterTwoDistinctAttempts(
        AiReviewOutcome outcome, int minutes, int expectedTicks)
    {
        var clock = new Clock(AiReviewSchedulerTests.Open);
        var schedule = AiReviewSchedulerTests.NewSchedule();
        schedule.RequestInterval(clock.Now, TimeSpan.FromMinutes(minutes), "Synthetic initial cadence");
        var notices = new List<AiReviewHostNotice>();
        int ticks = 0, reviews = 0, delays = 0;
        var loop = new AiReviewHostLoop(clock, (duration, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal(TimeSpan.FromSeconds(30), duration);
            delays++;
            clock.Now += duration;
            return Task.CompletedTask;
        });
        Task<AiScheduledReviewResult> Tick(CancellationToken ct)
        {
            ticks++;
            var request = schedule.TryBegin(clock.Now);
            if (request is not null)
            {
                reviews++;
                schedule.Complete(request.Id, clock.Now, outcome,
                    outcome == AiReviewOutcome.Reviewed ? clock.Now.AddMinutes(5) : null,
                    outcome == AiReviewOutcome.Reviewed ? new(minutes, "Synthetic next interval") : null);
            }
            return Task.FromResult(new AiScheduledReviewResult(schedule.Read(clock.Now)));
        }
        var test = AiReviewTestSession.Parse(clock.Now.AddMinutes(10).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), clock.Now);

        await loop.RunAsync(Tick, notices.Add, default, test);

        Assert.Equal(2, reviews);
        Assert.Equal(expectedTicks, ticks);
        Assert.Equal(expectedTicks - 1, delays);
        Assert.Equal("TestComplete", notices.Last().State);
        Assert.Equal(2, notices.Where(n => n.State != "TestComplete").Select(n => n.LastAttemptId).Distinct().Count());
    }

    [Fact]
    public async Task RecoveredOldAttemptDoesNotCountAndDeadlineStopsIdleLoop()
    {
        var clock = new Clock(AiReviewSchedulerTests.Open);
        var schedule = AiReviewSchedulerTests.NewSchedule();
        var old = schedule.TryBegin(clock.Now)!;
        schedule.Complete(old.Id, clock.Now, AiReviewOutcome.Reviewed, clock.Now.AddMinutes(15), new(15, "Old interval"));
        clock.Now = clock.Now.AddMinutes(1);
        var test = AiReviewTestSession.Parse(clock.Now.AddMinutes(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), clock.Now);
        var notices = new List<AiReviewHostNotice>();
        int ticks = 0;
        var loop = new AiReviewHostLoop(clock, (duration, _) => { clock.Now += duration; return Task.CompletedTask; });

        await loop.RunAsync(_ => { ticks++; return Task.FromResult(new AiScheduledReviewResult(schedule.Read(clock.Now))); },
            notices.Add, default, test);

        Assert.Equal(2, ticks);
        Assert.Equal("TestExpired", notices.Last().State);
        Assert.DoesNotContain(notices, n => n.State == "TestComplete");
    }

    [Fact]
    // Match the background host: xUnit's parallel synchronization context must not
    // delay the synthetic tick's cancellation/cleanup beyond the unchanged deadline.
    public Task DeadlineCancelsInFlightWorkAndWaitsForItsCleanup() => Task.Run(async () =>
    {
        Assert.Null(SynchronizationContext.Current);
        var now = DateTimeOffset.UtcNow;
        var end = now.AddSeconds(2).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var test = AiReviewTestSession.Parse(end, now);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notices = new List<AiReviewHostNotice>();
        async Task<AiScheduledReviewResult> Tick(CancellationToken ct)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { cleanupStarted.SetResult(); await finishCleanup.Task; }
            throw new Exception("Unreachable");
        }
        var running = new AiReviewHostLoop().RunAsync(Tick, notices.Add, default, test);
        try
        {
            await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(running.IsCompleted);
        }
        finally
        {
            finishCleanup.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal("TestExpired", Assert.Single(notices).State);
    });

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
