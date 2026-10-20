#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

// Fixed stage names only: never accept provider text, SQL, symbols or account identifiers.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaperCycleStage
{
    Dashboard, LiveEngine, LiveSnapshot, LiveProtocol, SnapshotRead, SnapshotUnpack,
    SnapshotPack, SnapshotVerify, TrackedMonitor, ShadowMonitor, ShadowProtection,
    LivePrepareDisplay, LiveApplyDisplay, PortfolioDisplay, TrackedDisplay, SetupWait,
    DispatcherDelay, Recommendations, ShadowPrepare, ShadowCollect, ShadowApply,
    LiveCollect, LiveEvaluate, LiveActions
}

/// <summary>Best-effort bounded diagnostics. All file I/O runs on one worker, never the dispatcher.</summary>
public static class PaperCycleTimings
{
    private sealed record Entry(DateTime Utc, int ProcessId, long Id, PaperCycleStage Stage,
        string State, double ElapsedMs, bool HasSynchronizationContext);
    private static readonly object Sync = new();
    private static readonly Queue<Entry> Pending = new();
    private static long sequence;
    private static bool writing;
    private const int MaximumPending = 256;
    private const long MaximumFileBytes = 512 * 1024;

    public static IDisposable Start(PaperCycleStage stage) => new Timing(stage);
    public static void DispatcherDelay(TimeSpan elapsed) => Enqueue(new(DateTime.UtcNow,
        Environment.ProcessId, 0, PaperCycleStage.DispatcherDelay, "Sample", elapsed.TotalMilliseconds, true));

    private sealed class Timing : IDisposable
    {
        private readonly long id = Interlocked.Increment(ref sequence);
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly PaperCycleStage stage;
        private readonly bool context = SynchronizationContext.Current is not null;
        private int disposed;
        public Timing(PaperCycleStage stage)
        {
            this.stage = stage;
            Enqueue(new(DateTime.UtcNow, Environment.ProcessId, id, stage, "Started", 0, context));
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Enqueue(new(DateTime.UtcNow, Environment.ProcessId, id, stage, "Finished",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, context));
        }
    }

    private static void Enqueue(Entry entry)
    {
        lock (Sync)
        {
            if (Pending.Count == MaximumPending) Pending.Dequeue();
            Pending.Enqueue(entry);
            if (writing) return;
            writing = true;
            _ = Task.Run(DrainAsync);
        }
    }

    private static async Task DrainAsync()
    {
        while (true)
        {
            // Coalesce bursts of nested stages; at most one append per second.
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            Entry[] batch;
            lock (Sync)
            {
                if (Pending.Count == 0) { writing = false; return; }
                batch = Pending.ToArray(); Pending.Clear();
            }
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Diagnostics");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "paper-cycle-stages.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length >= MaximumFileBytes)
                    File.Move(path, path + ".previous", overwrite: true);
                using var output = File.AppendText(path);
                foreach (var entry in batch) output.WriteLine(JsonSerializer.Serialize(entry));
            }
            catch { /* Diagnostics must never change a financial operation's result. */ }
        }
    }
}
