using Core.Runtime;
using Core.Trader.DelphiLive;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using TraderVI.WPF.Viewmodels;

// Offline only. No desktop composition, SQL, provider, broker, credentials or private input.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var frame = new DispatcherFrame();
        Exception? failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await RunAsync(); }
            catch (Exception error) { failure = error; }
            finally { frame.Continue = false; }
        });
        Dispatcher.PushFrame(frame);
        if (failure is not null) throw failure;
    }

    private static async Task RunAsync()
    {
        var open = new DateTime(2026, 10, 20, 13, 30, 0, DateTimeKind.Utc);
        var snapshot = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "OperationalChampion", null, 1000m, "CAD", new(2026, 10, 20), open, open.AddDays(-1), "Fixture", "Offline"));
        var quote = new DelphiLiveLedgerQuote(Guid.NewGuid(), null, null, "Protection", Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), "ABC", 1, open, open.AddSeconds(1), 10m, 9.9m, 10.1m, "Fixture"),
            "Observed", new string('x', 20_000));
        snapshot = snapshot with { Quotes = Enumerable.Range(0, 3000).Select(_ => quote with { QuoteId = Guid.NewGuid() }).ToImmutableArray() };
        var package = DelphiLiveSnapshotCodec.Pack(snapshot);
        // Warm serializers; comparison measures the same verified reconstruction in both cases.
        DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, package.Items, package.Contents);
        foreach (bool worker in new[] { false, true })
        {
            double gap = 0;
            int pulses = 0;
            var clock = Stopwatch.StartNew();
            double last = clock.Elapsed.TotalMilliseconds;
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { double now = clock.Elapsed.TotalMilliseconds; gap = Math.Max(gap, now - last); last = now; pulses++; };
            timer.Start();
            await Task.Delay(30);
            int uiThread = Environment.CurrentManagedThreadId, workThread = 0;
            bool workHadContext = true;
            var elapsed = Stopwatch.StartNew();
            Task<DelphiLivePortfolioSnapshot> Work()
            {
                workThread = Environment.CurrentManagedThreadId;
                workHadContext = SynchronizationContext.Current is not null;
                // Models CPU work following synchronously completed local I/O.
                return Task.FromResult(DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, package.Items, package.Contents));
            }
            var restored = worker ? await PaperCycleWorker.RunAsync(PaperCycleStage.SnapshotUnpack, Work, default) : await Work();
            elapsed.Stop();
            await Task.Delay(30);
            timer.Stop();
            if (restored.Quotes.Length != 3000 || (worker && (workThread == uiThread || workHadContext)))
                throw new InvalidOperationException("Worker isolation or restored history failed.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode = worker ? "Worker" : "Dispatcher", operationMs = elapsed.Elapsed.TotalMilliseconds,
                maximumDispatcherGapMs = gap, pulses, workHadContext, historyCount = restored.Quotes.Length }));
        }

        var barrier = new BrokerSetupCycleBarrier();
        var active = barrier.TryBeginCycle()!;
        using var setup = barrier.BeginSetup();
        var wait = setup.WaitForIdleAsync(default);
        if (wait.IsCompleted || barrier.TryBeginCycle() is not null) throw new InvalidOperationException("Unsafe admission.");
        active.Dispose();
        await wait.WaitAsync(TimeSpan.FromSeconds(2));
        Console.WriteLine("Synthetic idle boundary passed; no financial work interrupted.");
    }
}
