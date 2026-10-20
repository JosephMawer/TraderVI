using System;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerSetupCycleBarrierTests
{
    [Fact]
    public async Task PreparedReviewHandsAdmissionToApprovalWithoutStartingAnotherPaperCycle()
    {
        var barrier = new BrokerSetupCycleBarrier();
        using var expiry = new CancellationTokenSource();
        IDisposable review;
        using (var prepare = barrier.BeginSetup())
        {
            await prepare.WaitForIdleAsync(default);
            review = barrier.HoldReview(expiry.Token);
        }
        Assert.Null(barrier.TryBeginCycle());
        using (var approval = barrier.BeginSetup())
        {
            review.Dispose();
            Assert.Null(barrier.TryBeginCycle());
            await approval.WaitForIdleAsync(default, TimeSpan.Zero);
        }
        using var resumed = barrier.TryBeginCycle(); Assert.NotNull(resumed);
    }

    [Fact]
    public void ExpiringOrClosingReviewResumesPaperSchedulingWithoutExtendingItsLifetime()
    {
        var barrier = new BrokerSetupCycleBarrier();
        using var expiry = new CancellationTokenSource();
        using var review = barrier.HoldReview(expiry.Token);
        Assert.Null(barrier.TryBeginCycle());
        expiry.Cancel();
        using var resumed = barrier.TryBeginCycle(); Assert.NotNull(resumed);
        review.Dispose(); review.Dispose();
    }

    [Fact]
    public void AlreadyExpiredReviewDoesNotDeferPaperWork()
    {
        var barrier = new BrokerSetupCycleBarrier();
        using var expiry = new CancellationTokenSource(); expiry.Cancel();
        using var review = barrier.HoldReview(expiry.Token);
        using var cycle = barrier.TryBeginCycle(); Assert.NotNull(cycle);
    }

    [Fact]
    public async Task SetupWaitsForEveryAlreadyRunningCycleAndBlocksNewOnes()
    {
        var barrier = new BrokerSetupCycleBarrier();
        var first = barrier.TryBeginCycle(); var second = barrier.TryBeginCycle();
        Assert.NotNull(first); Assert.NotNull(second);
        using (var setup = barrier.BeginSetup())
        {
            var idle = setup.WaitForIdleAsync(default);
            Assert.False(idle.IsCompleted); Assert.Null(barrier.TryBeginCycle());
            first.Dispose(); first.Dispose(); Assert.False(idle.IsCompleted);
            second.Dispose(); await idle;
            Assert.Null(barrier.TryBeginCycle());
        }
        using var resumed = barrier.TryBeginCycle(); Assert.NotNull(resumed);
    }

    [Fact]
    public async Task CancelledWaitReleasesDeferralWithoutCancellingTheActiveCycle()
    {
        var barrier = new BrokerSetupCycleBarrier();
        using var cycle = barrier.TryBeginCycle();
        using var cancellation = new CancellationTokenSource();
        using (var setup = barrier.BeginSetup())
        {
            var wait = setup.WaitForIdleAsync(cancellation.Token);
            cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        }
        using var resumed = barrier.TryBeginCycle(); Assert.NotNull(resumed);
    }

    [Fact]
    public async Task MultipleSetupScopesKeepDeferralUntilTheLastOneExits()
    {
        var barrier = new BrokerSetupCycleBarrier();
        var first = barrier.BeginSetup(); var second = barrier.BeginSetup();
        await first.WaitForIdleAsync(default); await second.WaitForIdleAsync(default);
        first.Dispose(); first.Dispose(); Assert.Null(barrier.TryBeginCycle());
        second.Dispose(); using var resumed = barrier.TryBeginCycle(); Assert.NotNull(resumed);
    }

    [Fact]
    public async Task AFailedSetupActionStillResumesScheduling()
    {
        var barrier = new BrokerSetupCycleBarrier();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var setup = barrier.BeginSetup(); await setup.WaitForIdleAsync(default);
            throw new InvalidOperationException("synthetic check failed");
        });
        using var resumed = barrier.TryBeginCycle(); Assert.NotNull(resumed);
    }

    [Fact]
    public async Task BoundedWaitDoesNotInterruptPaperWorkOrLeaveSchedulingDeferred()
    {
        var barrier = new BrokerSetupCycleBarrier();
        using var cycle = barrier.TryBeginCycle();
        using (var setup = barrier.BeginSetup())
            await Assert.ThrowsAsync<TimeoutException>(() => setup.WaitForIdleAsync(default, TimeSpan.Zero));
        using var resumed = barrier.TryBeginCycle(); Assert.NotNull(resumed);
    }

    [Fact]
    public async Task InteractiveWaitKeepsAdmissionAcrossProgressUpdatesUntilEveryCycleFinishes()
    {
        var barrier = new BrokerSetupCycleBarrier();
        var first = barrier.TryBeginCycle(); var second = barrier.TryBeginCycle();
        using (var setup = barrier.BeginSetup())
        {
            int updates = 0;
            await setup.WaitForIdleWithProgressAsync(default, elapsed =>
            {
                Assert.True(elapsed >= TimeSpan.Zero);
                Assert.Null(barrier.TryBeginCycle());
                if (++updates == 1) first!.Dispose();
                if (updates == 3) second!.Dispose();
            }, TimeSpan.FromMilliseconds(1));
            Assert.Equal(3, updates);
            Assert.Null(barrier.TryBeginCycle());
        }
        using var resumed = barrier.TryBeginCycle(); Assert.NotNull(resumed);
    }

    [Fact]
    public async Task ClosingInteractiveWaitCancelsChecksWithoutInterruptingActivePaperWork()
    {
        var barrier = new BrokerSetupCycleBarrier();
        var cycle = barrier.TryBeginCycle();
        using var cancellation = new CancellationTokenSource();
        bool accountChecked = false;
        using (var setup = barrier.BeginSetup())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await setup.WaitForIdleWithProgressAsync(cancellation.Token, _ => cancellation.Cancel());
                accountChecked = true;
            });
            Assert.False(accountChecked);
            Assert.Null(barrier.TryBeginCycle());
        }
        // Cancelling setup does not complete the independent, already admitted cycle.
        using (var retry = barrier.BeginSetup())
        {
            await Assert.ThrowsAsync<TimeoutException>(() => retry.WaitForIdleAsync(default, TimeSpan.Zero));
            cycle!.Dispose();
            await retry.WaitForIdleAsync(default);
        }
        using var resumed = barrier.TryBeginCycle(); Assert.NotNull(resumed);
    }

    [Fact]
    public async Task CancellationAtIdleBoundaryStillPreventsQueuedChecks()
    {
        var barrier = new BrokerSetupCycleBarrier();
        var cycle = barrier.TryBeginCycle();
        using var setup = barrier.BeginSetup();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.WaitForIdleWithProgressAsync(
            cancellation.Token, _ => { cycle!.Dispose(); cancellation.Cancel(); }));
    }

    [Fact]
    public async Task IdleInteractiveSetupHonorsCancellationWithoutInvokingProgress()
    {
        var barrier = new BrokerSetupCycleBarrier();
        using var setup = barrier.BeginSetup();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.WaitForIdleWithProgressAsync(
            cancellation.Token, _ => throw new InvalidOperationException("No active work")));
    }
}
