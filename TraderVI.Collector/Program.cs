using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.Collector;

internal sealed record CollectorConfiguration(string CalendarPath, string SubscriptionPath, string ControlDirectory,
    string SourceContract, CollectorLimits Limits)
{
    public bool UseVerifiedRealSubscriptions { get; init; }
    // Zero leaves the legacy relay disabled. Cutover selects and qualifies a bounded concurrency.
    public int LegacyIntradayConcurrency { get; init; }
}

internal static class Program
{
    // Explicit run only; builds and --check-config perform no SQL/provider/broker operations.
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("--run" or "--check-config") || !Path.IsPathFullyQualified(args[1])) return 64;
        try
        {
            var options = new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
            var config = JsonSerializer.Deserialize<CollectorConfiguration>(File.ReadAllText(args[1]), options) ?? throw new InvalidDataException();
            foreach (var path in new[] { config.CalendarPath, config.SubscriptionPath, config.ControlDirectory })
                if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Absolute collector paths required.");
            if (config.SourceContract != TmxCollectorSource.Contract) throw new InvalidDataException("Unsupported collector source.");
            config.Limits.Validate();
            if (config.LegacyIntradayConcurrency is < 0 or > 8) throw new InvalidDataException("Invalid legacy collector concurrency.");
            var calendar = ReviewedTsxSessionCalendar.Load(config.CalendarPath);
            if (args[0] == "--check-config") return 0;
            // A different configuration/control directory cannot create a second transport owner.
            using var processOwnership = new EngineOwnershipLease(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Collector", "process.lock"));
            using var ownership = new EngineOwnershipLease(Path.Combine(config.ControlDirectory, "collector.lock"));
            using var source = new TmxCollectorSource();
            var worker = new IntradayCollector(new MarketCollectorRepository(), source, config.Limits);
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            string statusPath = Path.Combine(config.ControlDirectory, "status.json");
            string stopPath = Path.Combine(config.ControlDirectory, "stop.request");
            using var legacyProvider = config.LegacyIntradayConcurrency > 0 ? new CollectorLegacyTmxProvider() : null;
            bool LegacyAllowed(DateTime now)
            {
                var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, ReviewedTsxSessionCalendar.Toronto));
                if (stop.IsCancellationRequested || File.Exists(stopPath) || !calendar.IsRegularSession(date)) return false;
                var bounds = calendar.GetSessionBounds(date);
                return now >= bounds.OpenUtc && now < bounds.CloseUtc.AddMinutes(5);
            }
            var legacy = legacyProvider is not null && !File.Exists(stopPath)
                ? new CollectorIntradayServer(legacyProvider, LegacyAllowed).RunAsync(config.LegacyIntradayConcurrency, stop.Token)
                : Task.CompletedTask;
            DateTime nextSubscriptionRead = DateTime.MinValue;
            try
            {
                while (!stop.IsCancellationRequested && !File.Exists(stopPath))
                {
                    if (legacy.IsFaulted) await legacy; // A failed listener requires a supervised restart.
                    CollectorTickResult status;
                    string stage = "Session";
                    try
                    {
                        var now = DateTime.UtcNow;
                        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, ReviewedTsxSessionCalendar.Toronto));
                        if (!calendar.IsRegularSession(date)) status = new("ClosedSession", 0, 0, 0);
                        else
                        {
                            var bounds = calendar.GetSessionBounds(date);
                            if (config.UseVerifiedRealSubscriptions && now >= nextSubscriptionRead)
                            {
                                stage = "SubscriptionRefresh";
                                nextSubscriptionRead = now.AddSeconds(30);
                                var current = await new CollectorSubscriptionRepository().ReadAsync(date, bounds, stop.Token);
                                string temporary = config.SubscriptionPath + ".writing";
                                File.WriteAllText(temporary, JsonSerializer.Serialize(current));
                                File.Move(temporary, config.SubscriptionPath, true);
                            }
                            stage = "SubscriptionRead";
                            var subscriptions = JsonSerializer.Deserialize<CollectorSubscriptionSnapshot>(File.ReadAllText(config.SubscriptionPath), options)
                                ?? throw new InvalidDataException();
                            if (subscriptions.SessionOpenUtc != bounds.OpenUtc || subscriptions.SessionCloseUtc != bounds.CloseUtc)
                                throw new InvalidDataException("Subscriptions do not match the reviewed current session.");
                            stage = "Collection";
                            status = await worker.TickAsync(subscriptions, stop.Token);
                        }
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                    catch (Exception error)
                    {
                        status = new("CollectorAttention", 0, 0, 0)
                        { Failure = CollectorFailureDiagnostic.From(stage, error) };
                    }
                    SaveStatus(statusPath, status);
                    await Task.Delay(status.PollDelay(DateTime.UtcNow), stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally
            {
                stop.Cancel();
                try { await legacy; } catch (OperationCanceledException) { }
                finally
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    await worker.StopAsync(timeout.Token);
                    SaveStatus(statusPath, new("Stopped", 0, 0, 0));
                }
            }
            return 0;
        }
        catch { Console.Error.WriteLine("Collector unavailable; inspect configuration, ownership and database readiness. No order capability exists in this host."); return 1; }
    }

    private static void SaveStatus(string path, CollectorTickResult result)
    {
        string temporary = path + ".writing";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { ObservedUtc = DateTime.UtcNow, Result = result }));
        File.Move(temporary, path, true);
    }
}
