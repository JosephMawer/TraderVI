#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Explicit read-only diagnosis of the Live five-minute provider path. One operator-selected failed
/// symbol plus optional XIU, one recent completed window, at most six transport attempts under the existing
/// client's three-attempt bound. Each logical request has a 25-second deadline. Uses the reviewed
/// regular session; no SQL, journal, price persistence, monitor tick, quote fallback or trading.
/// Reports disposition or exception source locations, never provider bodies, prices or credentials.
/// A successful later read cannot establish the cause of an earlier failure or repair historical gaps.
/// </summary>
public sealed class TmxLiveCollectionFailureProbe : IProbe
{
    public string Slug => "tmx-live-collection-failure";
    public string Description => "Explicit two-target, read-only diagnosis of Live five-minute collection failures.";

    public async Task RunAsync()
    {
        string symbol = Environment.GetEnvironmentVariable("TRADERVI_TMX_FAILURE_SYMBOL") ?? "";
        if (symbol.Length is < 1 or > 20 || symbol.Any(c => !(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-')))
            throw new ArgumentException("An explicit single canonical TSX symbol is required.");
        string calendarPath = Environment.GetEnvironmentVariable("TRADERVI_TSX_CALENDAR_PATH")
            ?? throw new InvalidOperationException("An explicit reviewed calendar is required.");
        string benchmarkSetting = Environment.GetEnvironmentVariable("TRADERVI_TMX_FAILURE_INCLUDE_BENCHMARK") ?? "true";
        if (!bool.TryParse(benchmarkSetting, out bool includeBenchmark))
            throw new ArgumentException("Benchmark selection must be true or false.");
        var calendar = ReviewedTsxSessionCalendar.Load(calendarPath);
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, ReviewedTsxSessionCalendar.Toronto));
        var bounds = calendar.GetSessionBounds(today);
        // Match the normal collection delay and request one exact completed five-minute interval.
        var delayed = now.AddMinutes(-2);
        var end = new DateTime(delayed.Ticks - delayed.Ticks % TimeSpan.FromMinutes(5).Ticks, DateTimeKind.Utc);
        var start = end.AddMinutes(-5);
        if (start < bounds.OpenUtc || now >= bounds.CloseUtc)
            throw new InvalidOperationException("A completed window in the open reviewed session is required.");

        using var source = new TmxDelphiLiveMarketDataSource();
        Console.WriteLine($"Read-only provider check; UTC window {start:O} to {end:O}; no SQL or saved-price writes.");
        foreach (var target in (includeBenchmark ? new[] { symbol, "XIU" } : new[] { symbol }).Distinct(StringComparer.Ordinal))
        {
            string label = target == "XIU" ? "benchmark" : "reported-failure target";
            var requested = DateTime.UtcNow;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try
            {
                var receipt = await source.GetExactFiveMinuteBarAsync(new(Guid.NewGuid(), target, start,
                    end, requested.AddSeconds(25), requested, 1), timeout.Token);
                Console.WriteLine($"{label}: {receipt.Disposition}; exact bar present: {receipt.ExactCompletedBar is not null}; " +
                    $"received UTC: {receipt.ReceivedUtc:O}; transport attempts: {receipt.ProviderAttemptCount}; requests: {receipt.ProviderRequestCount}.");
            }
            catch (Exception error)
            {
                Console.WriteLine($"{label}: {error.GetType().Name}; timed out: {timeout.IsCancellationRequested}; " +
                    "transport count unavailable on failure (maximum three attempts).");
                // Only this exact local validator's messages are safe: fixed text and timestamps,
                // never GraphQL/provider bodies, symbols, prices, credentials or arbitrary exceptions.
                if (error.TargetSite?.DeclaringType == typeof(Core.TMX.TmxClient) &&
                    error.TargetSite.Name == "ValidateIntradayResponse")
                    Console.WriteLine($"Local validation: {error.Message}");
                foreach (var frame in new StackTrace(error, true).GetFrames()
                    .Where(f => f.GetMethod()?.DeclaringType?.Assembly == typeof(TmxDelphiLiveMarketDataSource).Assembly).Take(6))
                    Console.WriteLine($"Source: {frame.GetMethod()?.DeclaringType?.FullName}.{frame.GetMethod()?.Name}; line {frame.GetFileLineNumber()}.");
            }
        }
        Console.WriteLine("Diagnostic completed. Results do not alter eligibility, freshness or historical collection records.");
    }
}
