using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Services;

namespace TraderVI.Engine;

internal static class Program
{
    // Construction/building never launches this host. Starting it is an explicit operator action.
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || args[0] is not ("--run" or "--prepare-observations")) return 64;
        try
        {
            using var owner = new EngineOwnershipLease(EnginePaths.OwnershipPath);
            if (args[0] == "--prepare-observations")
            {
                await new EnginePaperObservationStore().QualifyExistingAsync().ConfigureAwait(false);
                return 0;
            }
            var calendarPath = Environment.GetEnvironmentVariable(DelphiLiveDesktopService.CalendarPathEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(calendarPath)) return 65;
            var calendar = ReviewedTsxSessionCalendar.Load(calendarPath);
            using var sessionStore = new WindowsWealthsimpleSessionStore();
            var connection = new WealthsimpleConnection(sessionStore);
            var orderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Wealthsimple", "ManualOrders");
            using var journal = new ManualBuyFileJournal(orderPath, new WindowsManualBuyProtector());
            var grants = new BrokerExecutionHostGrantFileStore(Path.Combine(orderPath, "execution-host.dat"), new WindowsManualBuyProtector());
            var broker = new EngineBrokerService(connection, journal, sessionStore, grants, calendar,
                IndependentExecutionReadiness.VerifyAsync);
            var market = new IndependentMarketService(calendar);
            var engine = new IndependentEngine([
                new("Connection", TimeSpan.FromSeconds(10), broker.PollAuthenticationAsync),
                new("Market inputs", TimeSpan.FromSeconds(30), market.TickAsync),
                new("Real trading", TimeSpan.FromSeconds(1), broker.PollExecutionAsync)
            ], broker.StartAsync, async () =>
            {
                try { await market.StopAsync().ConfigureAwait(false); }
                finally { await broker.CloseAsync().ConfigureAwait(false); }
            });
            using var stopAccepting = new CancellationTokenSource();
            var server = new EngineRpcServer(EnginePaths.PipeName, async request =>
            {
                if (request.Operation == EngineOperation.Status) return engine.ReadStatus();
                if (request.Operation == EngineOperation.Stop)
                {
                    broker.BeginStop();
                    await engine.StopAsync().ConfigureAwait(false);
                    return engine.ReadStatus();
                }
                return await broker.DispatchAsync(request).ConfigureAwait(false);
            });
            var serving = server.RunAsync(stopAccepting.Token);
            try
            {
                await engine.StartAsync().ConfigureAwait(false);
                // Losing the control listener is an engine failure, not an invisible unmanaged lifetime.
                var completed = await Task.WhenAny(engine.Completion, serving).ConfigureAwait(false);
                if (completed == serving)
                {
                    await serving.ConfigureAwait(false);
                    throw new InvalidOperationException("The engine control listener stopped unexpectedly.");
                }
                await engine.Completion.ConfigureAwait(false);
                return 0;
            }
            finally
            {
                broker.BeginStop();
                await engine.StopAsync().ConfigureAwait(false);
                stopAccepting.Cancel();
                await serving.ConfigureAwait(false);
            }
        }
        catch { return 1; } // Never write credentials, account contents or SQL connection details to console/logs.
    }
}
