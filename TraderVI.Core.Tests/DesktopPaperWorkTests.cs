using Core.Runtime;
using System;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DesktopPaperWorkTests
{
    [Fact]
    public async Task SynchronousPrefixAndContinuationRunWithoutTheUiContext()
    {
        var previous = SynchronizationContext.Current;
        var ui = new SynchronizationContext();
        int caller = Environment.CurrentManagedThreadId;
        Task<int> work;
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            work = PaperCycleWorker.RunAsync(PaperCycleStage.ShadowMonitor, async () =>
            {
                Assert.NotEqual(caller, Environment.CurrentManagedThreadId);
                Assert.Null(SynchronizationContext.Current);
                await Task.Yield();
                Assert.Null(SynchronizationContext.Current);
                return 42;
            }, default);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.Equal(42, await work);
    }

    [Fact]
    public async Task PreCancelledWorkerDoesNotStartAndWorkerFailureIsObserved()
    {
        using var ct = new CancellationTokenSource(); ct.Cancel();
        bool called = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PaperCycleWorker.RunAsync(
            PaperCycleStage.TrackedMonitor, () => { called = true; return Task.FromResult(1); }, ct.Token));
        Assert.False(called);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PaperCycleWorker.RunAsync<int>(
            PaperCycleStage.TrackedMonitor, () => throw new InvalidOperationException("Fixture"), default));
    }

    [Fact]
    public async Task DisplayRequestsJoinOneReadAndFailedReadsCanRetry()
    {
        var flight = new DisplayRefreshFlight();
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        Task Refresh() => flight.RunAsync(() => { reads++; return read.Task; }, default);
        var first = Refresh(); var second = Refresh(); var third = Refresh();
        Assert.Equal(1, reads); Assert.False(second.IsCompleted);
        read.SetResult(); await Task.WhenAll(first, second, third);
        await Assert.ThrowsAsync<InvalidOperationException>(() => flight.RunAsync(
            () => Task.FromException(new InvalidOperationException("Fixture")), default));
        await flight.RunAsync(() => { reads++; return Task.CompletedTask; }, default);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task CancelledDisplayWaiterDoesNotCancelTheOriginalRead()
    {
        var flight = new DisplayRefreshFlight();
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = flight.RunAsync(() => read.Task, default);
        using var cancellation = new CancellationTokenSource();
        var waiter = flight.RunAsync(() => throw new Exception("Duplicate read"), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.False(original.IsCompleted);
        read.SetResult(); await original;
    }

    [Fact]
    public async Task SetupObtainsBoundaryAfterFinancialCompletionWhileDisplayIsStillPending()
    {
        var barrier = new BrokerSetupCycleBarrier();
        var active = barrier.TryBeginCycle();
        using var setup = barrier.BeginSetup();
        var idle = setup.WaitForIdleAsync(default);
        Assert.False(idle.IsCompleted);
        var display = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        active.Dispose(); // Engine has awaited persistence and released its settings lease.
        await idle;
        Assert.False(display.Task.IsCompleted);
        Assert.Null(barrier.TryBeginCycle()); // Later engine stages remain deferred.
        display.SetResult(); await display.Task;
    }
}
