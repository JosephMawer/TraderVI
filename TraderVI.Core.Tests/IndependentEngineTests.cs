using Core.Runtime;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class IndependentEngineTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StalledResearchDoesNotDelayRealWorkAndStopDrainsBeforeClosing()
    {
        var researchStarted = Signal(); var releaseResearch = Signal(); var realRan = Signal();
        int closed = 0, researchCalls = 0;
        var engine = new IndependentEngine([
            new("Research", TimeSpan.FromMilliseconds(5), async () =>
            { Interlocked.Increment(ref researchCalls); researchStarted.TrySetResult(); await releaseResearch.Task; }),
            new("Real", TimeSpan.FromMilliseconds(5), () => { realRan.TrySetResult(); return Task.CompletedTask; })
        ], () => Task.CompletedTask, () => { Interlocked.Increment(ref closed); return Task.CompletedTask; });
        try
        {
            await engine.StartAsync();
            await Task.WhenAll(researchStarted.Task, realRan.Task).WaitAsync(Deadline);
            var stop = engine.StopAsync();
            Assert.False(stop.IsCompleted); Assert.Equal(0, closed); Assert.Equal(1, researchCalls);
            Assert.Equal("Stopping", engine.ReadStatus().State);
            releaseResearch.SetResult(); await stop.WaitAsync(Deadline);
            Assert.Equal(1, closed); Assert.Equal("Stopped", engine.ReadStatus().State);
            Assert.Equal(1, researchCalls);
        }
        finally { releaseResearch.TrySetResult(); await engine.StopAsync().WaitAsync(Deadline); }
    }

    [Fact]
    public async Task ConcurrentStartsCreateOneOwnerAndClientCancellationDoesNotCancelStop()
    {
        int starts = 0, closes = 0; var entered = Signal(); var finish = Signal();
        var engine = new IndependentEngine([new("Real", TimeSpan.FromMinutes(1), async () =>
            { entered.TrySetResult(); await finish.Task; })],
            () => { Interlocked.Increment(ref starts); return Task.CompletedTask; },
            () => { Interlocked.Increment(ref closes); return Task.CompletedTask; });
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => engine.StartAsync()));
            await entered.Task.WaitAsync(Deadline);
            using var client = new CancellationTokenSource(); client.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.StopAsync(client.Token));
            finish.SetResult(); await engine.Completion.WaitAsync(Deadline);
            Assert.Equal(1, starts); Assert.Equal(1, closes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync());
        }
        finally { finish.TrySetResult(); await engine.StopAsync().WaitAsync(Deadline); }
    }

    [Fact]
    public async Task FailedLaneIsVisibleWithoutStoppingAnotherLaneOrExposingExceptionText()
    {
        var realRan = Signal(); var failed = Signal();
        var engine = new IndependentEngine([
            new("Research", TimeSpan.FromHours(1), () => { failed.SetResult(); throw new InvalidOperationException("private evidence"); }),
            new("Real", TimeSpan.FromHours(1), () => { realRan.SetResult(); return Task.CompletedTask; })
        ], () => Task.CompletedTask, () => Task.CompletedTask);
        try
        {
            await engine.StartAsync(); await Task.WhenAll(realRan.Task, failed.Task).WaitAsync(Deadline);
            await engine.StopAsync().WaitAsync(Deadline);
            var status = engine.ReadStatus();
            Assert.Equal("Blocked", status.Work.Single(w => w.Name == "Research").State);
            Assert.Equal(1, status.Work.Single(w => w.Name == "Research").ConsecutiveFailures);
            Assert.DoesNotContain("private evidence", status.ToString());
        }
        finally { await engine.StopAsync().WaitAsync(Deadline); }
    }

    [Fact]
    public async Task StopDuringStartupPreventsEveryLaneAndCleansUpOnce()
    {
        var started = Signal(); var release = Signal(); int calls = 0, closed = 0;
        var engine = new IndependentEngine([new("Real", TimeSpan.FromSeconds(1), () =>
            { Interlocked.Increment(ref calls); return Task.CompletedTask; })],
            async () => { started.TrySetResult(); await release.Task; },
            () => { Interlocked.Increment(ref closed); return Task.CompletedTask; });
        try
        {
            var starting = engine.StartAsync(); await started.Task.WaitAsync(Deadline);
            var stopping = engine.StopAsync(); release.SetResult();
            await Task.WhenAll(starting, stopping).WaitAsync(Deadline);
            Assert.Equal(0, calls); Assert.Equal(1, closed);
        }
        finally { release.TrySetResult(); await engine.StopAsync().WaitAsync(Deadline); }
    }

    [Fact]
    public void ExclusiveOwnershipRejectsSecondProcessAndCanBeReacquiredAfterRelease()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tradervi-owner-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "engine.lock");
        try
        {
            using (var first = new EngineOwnershipLease(path))
                Assert.Throws<IOException>(() => new EngineOwnershipLease(path));
            using var recovered = new EngineOwnershipLease(path);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
