using System;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerSetupCycleBarrierTests
{
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
}
