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
    string SourceContract, CollectorLimits Limits);

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
            var calendar = ReviewedTsxSessionCalendar.Load(config.CalendarPath);
            if (args[0] == "--check-config") return 0;
            using var ownership = new EngineOwnershipLease(Path.Combine(config.ControlDirectory, "collector.lock"));
            using var source = new TmxCollectorSource();
            var worker = new IntradayCollector(new MarketCollectorRepository(), source, config.Limits);
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            string statusPath = Path.Combine(config.ControlDirectory, "status.json");
            string stopPath = Path.Combine(config.ControlDirectory, "stop.request");
            try
            {
                while (!stop.IsCancellationRequested && !File.Exists(stopPath))
                {
                    CollectorTickResult status;
                    try
                    {
                        var now = DateTime.UtcNow;
                        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, ReviewedTsxSessionCalendar.Toronto));
                        if (!calendar.IsRegularSession(date)) status = new("ClosedSession", 0, 0, 0);
                        else
                        {
                            var bounds = calendar.GetSessionBounds(date);
                            var subscriptions = JsonSerializer.Deserialize<CollectorSubscriptionSnapshot>(File.ReadAllText(config.SubscriptionPath), options)
                                ?? throw new InvalidDataException();
                            if (subscriptions.SessionOpenUtc != bounds.OpenUtc || subscriptions.SessionCloseUtc != bounds.CloseUtc)
                                throw new InvalidDataException("Subscriptions do not match the reviewed current session.");
                            status = await worker.TickAsync(subscriptions, stop.Token);
                        }
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                    catch { status = new("CollectorAttention", 0, 0, 0); }
                    SaveStatus(statusPath, status);
                    await Task.Delay(status.State == "CollectorAttention" ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1), stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await worker.StopAsync(timeout.Token);
                SaveStatus(statusPath, new("Stopped", 0, 0, 0));
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
