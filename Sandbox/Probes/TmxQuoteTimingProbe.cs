#nullable enable
using Core.TMX;
using Core.Trader.DelphiLive;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Does the quote page offer dated prices fresher than its last trades? Read current paper-held symbols
/// using existing repositories, add XIU, cap at 16 targets. Two polls one minute apart: at most 32 queries
/// / 96 transport attempts; each request 15 seconds, each poll 60 seconds, whole run five minutes.
/// Requires a reviewed regular session. No SQL/account/risk/order writes; print anonymous timing and
/// availability only. Exit signal is measured source coverage, never acceptance or trading authority.
/// </summary>
public sealed class TmxQuoteTimingProbe : IProbe
{
    public string Slug => "tmx-quote-timing";
    public string Description => "Read-only quote-page timestamp and recent-trade comparison for held paper stocks.";

    public async Task RunAsync()
    {
        try { await RunCheckedAsync(); }
        catch (Exception ex) { throw new InvalidOperationException($"Quote timing probe stopped: {ex.GetType().Name}. Private details omitted."); }
    }

    private static async Task RunCheckedAsync()
    {
        using var run = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var calendar = ReviewedTsxSessionCalendar.Load(Environment.GetEnvironmentVariable("TRADERVI_TSX_CALENDAR_PATH")
            ?? throw new InvalidOperationException("Reviewed calendar required."));
        var held = await TmxHeldPriceFreshnessProbe.ReadHeldSymbolsAsync(run.Token);
        var targets = held.Append("XIU").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (held.Count == 0 || targets.Length > 16) throw new InvalidOperationException("Target count outside bounds.");
        var began = DateTime.UtcNow;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(began, ReviewedTsxSessionCalendar.Toronto));
        var bounds = calendar.GetSessionBounds(date);
        if (began < bounds.OpenUtc || began.AddMinutes(5) >= bounds.CloseUtc) throw new InvalidOperationException("Outside bounded session window.");
        Console.WriteLine(JsonSerializer.Serialize(new { StartedUtc = began, Held = held.Count, Targets = targets.Length,
            MaximumRequests = targets.Length * 2, MaximumTransportAttempts = targets.Length * 6 }));
        using var client = new TmxClient();
        int requests = 0, attempts = 0;
        for (int poll = 1; poll <= 2; poll++)
        {
            if (poll == 2) await Task.Delay(TimeSpan.FromSeconds(60), run.Token);
            using var cycle = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            cycle.CancelAfter(TimeSpan.FromSeconds(60));
            int heldRecent = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (DateTime.UtcNow >= bounds.CloseUtc || requests >= targets.Length * 2) throw new InvalidOperationException("Request/session bound reached.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cycle.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                requests++;
                var sample = await client.GetQuoteTimingDiagnosticAsync(targets[i], timeout.Token);
                attempts += sample.AttemptCount;
                var quote = sample.Quotes.SingleOrDefault();
                var delayed = sample.DelayedQuotes.SingleOrDefault();
                var trade = sample.RecentTrades.SingleOrDefault();
                DateTime? tradeUtc = ReadExplicitUtc(trade?.datetime), delayedUtc = ReadExplicitUtc(delayed?.lasttradedatetime);
                double? age = tradeUtc is { } t ? (sample.ReceivedUtc - t).TotalSeconds : null;
                if (held.Contains(targets[i]) && age is >= 0 and <= 120 && tradeUtc >= bounds.OpenUtc && trade?.price > 0) heldRecent++;
                Console.WriteLine(JsonSerializer.Serialize(new { Poll = poll, Target = $"T{i + 1:00}", Held = held.Contains(targets[i]),
                    sample.ReceivedUtc, QuotePresent = quote is not null, QuoteHasSourceTime = false,
                    QuoteHasBidAsk = quote?.bid > 0 && quote.ask > 0, DelayedPresent = delayed is not null,
                    DelayedHasBidAsk = delayed?.bid > 0 && delayed.ask > 0, DelayedLastTradeUtc = delayedUtc,
                    DelayedAgeSeconds = delayedUtc is { } d ? (double?)(sample.ReceivedUtc - d).TotalSeconds : null,
                    RecentTradeUtc = tradeUtc, RecentTradeAgeSeconds = age,
                    QuoteEqualsLatestTrade = quote?.price is > 0 && trade?.price is > 0 ? (bool?)(quote.price == trade.price) : null }));
            }
            Console.WriteLine(JsonSerializer.Serialize(new { Poll = poll, HeldTradesWithinDiagnostic120Seconds = heldRecent, Held = held.Count,
                QualifiedBidAskTimestamps = 0 }));
        }
        var after = await TmxHeldPriceFreshnessProbe.ReadHeldSymbolsAsync(run.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { Requests = requests, TransportAttempts = attempts,
            TargetSetUnchanged = held.SetEquals(after), CompletedUtc = DateTime.UtcNow }));
        Console.WriteLine("Diagnostic only: last-trade time does not date bid/ask. Untimed/delayed quotes do not qualify account valuation; 120 seconds is not an operating policy.");
    }

    internal static DateTime? ReadExplicitUtc(string? text) => text is not null &&
        Regex.IsMatch(text, @"(?:Z|[+-]\d{2}:\d{2})$") &&
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
        ? time.UtcDateTime : null;
}
