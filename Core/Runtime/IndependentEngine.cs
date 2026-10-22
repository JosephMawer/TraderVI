#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

public sealed record EngineWork(string Name, TimeSpan Interval, Func<Task> Execute);
public sealed record EngineWorkStatus(string Name, string State, DateTimeOffset? LastStarted,
    DateTimeOffset? LastCompleted, int ConsecutiveFailures);
public sealed record IndependentEngineStatus(Guid InstanceId, string State, DateTimeOffset CheckedAt,
    ImmutableArray<EngineWorkStatus> Work)
{
    public ImmutableArray<EngineNotice> Notices { get; init; } = [];
}
public sealed record EngineNotice(DateTimeOffset ObservedAt, string Severity, string Code, string Message);

/// <summary>
/// Process-owned schedules. A dashboard connection is never a lifetime token. Each lane has at most
/// one operation; slow research cannot queue or delay another lane. Stop prevents new admissions and
/// drains admitted operations before closing services, so an uncertain broker submission is not abandoned.
/// </summary>
public sealed class IndependentEngine
{
    private readonly object sync = new();
    private readonly EngineWork[] work;
    private readonly Dictionary<string, EngineWorkStatus> status;
    private readonly Func<Task> initialize, close;
    private readonly TimeProvider time;
    private readonly CancellationTokenSource stopScheduling = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task[] workers = [];
    private Task? start, stop;
    private string state = "Created";
    public Guid InstanceId { get; } = Guid.NewGuid();

    public IndependentEngine(IEnumerable<EngineWork> work, Func<Task> initialize, Func<Task> close,
        TimeProvider? time = null)
    {
        this.work = work.ToArray(); this.initialize = initialize; this.close = close;
        this.time = time ?? TimeProvider.System;
        if (this.work.Length == 0 || this.work.Any(w => string.IsNullOrWhiteSpace(w.Name) ||
            w.Name.Length > 64 || w.Interval <= TimeSpan.Zero || w.Execute is null) ||
            this.work.Select(w => w.Name).Distinct(StringComparer.Ordinal).Count() != this.work.Length)
            throw new ArgumentException("Engine schedules require unique names, positive intervals and operations.");
        status = this.work.ToDictionary(w => w.Name, w => new EngineWorkStatus(w.Name, "Waiting", null, null, 0));
    }

    public IndependentEngineStatus ReadStatus()
    {
        lock (sync) return new(InstanceId, state, time.GetUtcNow(), work.Select(w => status[w.Name]).ToImmutableArray());
    }
    public Task Completion => stopped.Task;
    public Task StartAsync()
    {
        lock (sync)
        {
            if (stop is not null) throw new InvalidOperationException("A stopped engine cannot be restarted in place.");
            return start ??= Task.Run(StartCoreAsync);
        }
    }
    private async Task StartCoreAsync()
    {
        try
        {
            lock (sync) if (state == "Created") state = "Starting";
            await initialize().ConfigureAwait(false);
            lock (sync)
            {
                if (stopScheduling.IsCancellationRequested) return;
                state = "Running";
                // Task.Run also isolates synchronous work preceding an operation's first await.
                workers = work.Select(w => Task.Run(() => RunAsync(w))).ToArray();
            }
        }
        catch
        {
            lock (sync) state = "StartFailed";
            throw;
        }
    }
    private async Task RunAsync(EngineWork lane)
    {
        while (true)
        {
            lock (sync)
            {
                if (stopScheduling.IsCancellationRequested) return;
                status[lane.Name] = status[lane.Name] with { State = "Running", LastStarted = time.GetUtcNow() };
            }
            try
            {
                await lane.Execute().ConfigureAwait(false);
                lock (sync) status[lane.Name] = status[lane.Name] with
                    { State = "Waiting", LastCompleted = time.GetUtcNow(), ConsecutiveFailures = 0 };
            }
            catch
            {
                // No exception text, broker/account data or credentials are exposed to the control channel.
                lock (sync) status[lane.Name] = status[lane.Name] with
                    { State = "Blocked", ConsecutiveFailures = System.Math.Min(1000, status[lane.Name].ConsecutiveFailures + 1) };
            }
            try { await Task.Delay(lane.Interval, time, stopScheduling.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stopScheduling.IsCancellationRequested) { return; }
        }
    }

    /// <summary>Cancellation stops waiting for the result, never the already-requested shutdown.</summary>
    public Task StopAsync(CancellationToken waitCancellation = default)
    {
        Task shutdown;
        lock (sync)
        {
            state = stop is null ? "Stopping" : state;
            stopScheduling.Cancel();
            shutdown = stop ??= Task.Run(StopCoreAsync);
        }
        return shutdown.WaitAsync(waitCancellation);
    }
    private async Task StopCoreAsync()
    {
        try
        {
            Task? initialization;
            lock (sync) initialization = start;
            if (initialization is not null)
            {
                try { await initialization.ConfigureAwait(false); }
                catch { /* Partial initialization still requires service cleanup. */ }
            }
            Task[] admitted;
            lock (sync) admitted = workers;
            await Task.WhenAll(admitted).ConfigureAwait(false);
            await close().ConfigureAwait(false);
            lock (sync) state = "Stopped";
            stopped.TrySetResult();
        }
        catch (Exception ex)
        {
            lock (sync) state = "StopFailed";
            stopped.TrySetException(ex);
            throw;
        }
    }
}
