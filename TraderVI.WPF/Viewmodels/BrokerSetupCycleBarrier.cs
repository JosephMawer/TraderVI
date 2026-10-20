#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Core.Runtime;

namespace TraderVI.WPF.Viewmodels;

/// <summary>Finish active desktop paper work before broker setup; SQL fences still protect other processes.</summary>
public sealed class BrokerSetupCycleBarrier
{
    private readonly object sync = new();
    private int cycles, setups;
    private TaskCompletionSource idle = Completed();

    public IDisposable? TryBeginCycle()
    {
        lock (sync)
        {
            if (setups != 0) return null;
            if (cycles++ == 0) idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new Release(() => { lock (sync) { if (--cycles == 0) idle.TrySetResult(); } });
        }
    }

    public Setup BeginSetup()
    {
        lock (sync)
        {
            setups++;
            return new Setup(idle.Task, new Release(() => { lock (sync) setups--; }));
        }
    }

    // Transfer an already-idle setup into a bounded human review without admitting a paper cycle
    // between Prepare and Approve. The caller owns the existing review expiry, never a renewal.
    public IDisposable HoldReview(CancellationToken expires)
    {
        var setup = BeginSetup();
        var registration = expires.Register(setup.Dispose);
        return new Release(() => { registration.Dispose(); setup.Dispose(); });
    }

    private static TaskCompletionSource Completed()
    { var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); result.SetResult(); return result; }

    public sealed class Setup(Task idle, IDisposable release) : IDisposable
    {
        public async Task WaitForIdleAsync(CancellationToken ct, TimeSpan? maximumWait = null)
        {
            using var timing = PaperCycleTimings.Start(PaperCycleStage.SetupWait);
            await idle.WaitAsync(maximumWait ?? TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);
        }

        // Interactive setup keeps its admission while an existing financial cycle drains.
        // Closing setup cancels only this wait; it never cancels the admitted paper work.
        public async Task WaitForIdleWithProgressAsync(CancellationToken ct, Action<TimeSpan> progress,
            TimeSpan? updateInterval = null)
        {
            ArgumentNullException.ThrowIfNull(progress);
            var interval = updateInterval ?? TimeSpan.FromSeconds(1);
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(updateInterval));
            using var timing = PaperCycleTimings.Start(PaperCycleStage.SetupWait);
            long started = Stopwatch.GetTimestamp();
            while (!idle.IsCompleted)
            {
                ct.ThrowIfCancellationRequested();
                progress(Stopwatch.GetElapsedTime(started));
                await Task.WhenAny(idle, Task.Delay(interval, ct));
            }
            await idle.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
        }
        public void Dispose() => release.Dispose();
    }

    private sealed class Release(Action release) : IDisposable
    {
        private Action? action = release;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
