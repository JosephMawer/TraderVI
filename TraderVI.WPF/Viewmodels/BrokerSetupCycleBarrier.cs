#nullable enable
using System;
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

    private static TaskCompletionSource Completed()
    { var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); result.SetResult(); return result; }

    public sealed class Setup(Task idle, IDisposable release) : IDisposable
    {
        public async Task WaitForIdleAsync(CancellationToken ct, TimeSpan? maximumWait = null)
        {
            using var timing = PaperCycleTimings.Start(PaperCycleStage.SetupWait);
            await idle.WaitAsync(maximumWait ?? TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);
        }
        public void Dispose() => release.Dispose();
    }

    private sealed class Release(Action release) : IDisposable
    {
        private Action? action = release;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
