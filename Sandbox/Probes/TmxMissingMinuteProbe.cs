#nullable enable
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.TMX.Models.Dto;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Read-only diagnosis of absent versus filtered TMX minutes for current paper-held stocks and XIU.
/// Uses the reviewed current session, from open through a five-minute boundary at least a minute old.
/// At most eight targets and four requests per target (32 requests / 96 transport attempts), with
/// 15-second request and eight-minute run deadlines. Reads SQL holdings; writes no SQL/accounts/orders.
/// Prints anonymous counts and one gap timestamp per target, never prices or account identities.
/// Exit signal: raw row classification and interval volume agreement, not proof of no-trade semantics.
/// </summary>
public sealed class TmxMissingMinuteProbe : IProbe
{
    public string Slug => "tmx-missing-minute";
    public string Description => "Read-only raw TMX missing-minute classification for paper holdings and XIU.";

    public async Task RunAsync()
    {
        try { await RunCheckedAsync(); }
        catch (Exception ex) { throw new InvalidOperationException($"Missing-minute probe stopped: {ex.GetType().Name}. Private source details omitted."); }
    }

    private static async Task RunCheckedAsync()
    {
        using var run = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var calendar = ReviewedTsxSessionCalendar.Load(Environment.GetEnvironmentVariable("TRADERVI_TSX_CALENDAR_PATH")
            ?? throw new InvalidOperationException("Reviewed calendar required."));
        var held = await TmxHeldPriceFreshnessProbe.ReadHeldSymbolsAsync(run.Token);
        var targets = held.Append("XIU").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (held.Count == 0 || targets.Length > 8) throw new InvalidOperationException("Target count outside bounds.");
        DateTime now = DateTime.UtcNow;
        var bounds = calendar.GetSessionBounds(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, ReviewedTsxSessionCalendar.Toronto)));
        DateTime end = new(now.AddMinutes(-1).Ticks - now.AddMinutes(-1).Ticks % TimeSpan.FromMinutes(5).Ticks, DateTimeKind.Utc);
        DateTime start = bounds.OpenUtc;
        if (end <= start || now >= bounds.CloseUtc || (end - start).TotalMinutes > 390)
            throw new InvalidOperationException("Outside bounded regular-session window.");
        int requests = 0, attempts = 0;
        using var client = new TmxClient();
        Console.WriteLine(JsonSerializer.Serialize(new { StartedUtc = now, StartUtc = start, EndUtc = end,
            HeldSymbols = held.Count, Targets = targets.Length, MaximumRequests = targets.Length * 4, MaximumTransportAttempts = targets.Length * 12 }));
        async Task<(TmxIntradayBatch Batch, bool SourceDataPresent, IReadOnlyList<TmxTimeSeriesPointDto> SourcePoints)>
            Fetch(string symbol, int interval, DateTime from, DateTime to)
        {
            if (requests >= targets.Length * 4 || DateTime.UtcNow >= bounds.CloseUtc)
                throw new InvalidOperationException("Request budget/session ended.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            requests++;
            var result = await client.GetIntradayTimeSeriesDiagnosticAsync(symbol, interval, from, to, timeout.Token);
            attempts += result.Batch.AttemptCount;
            if (!result.SourceDataPresent) throw new InvalidOperationException("Missing source data.");
            return result;
        }
        for (int i = 0; i < targets.Length; i++)
        {
            string symbol = targets[i];
            if (requests >= targets.Length * 4 || DateTime.UtcNow >= bounds.CloseUtc)
                throw new InvalidOperationException("Request budget/session ended.");
            using var tradeTimeout = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            tradeTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            requests++;
            var trades = await client.GetRecentTradesDiagnosticAsync(symbol, tradeTimeout.Token);
            attempts += trades.AttemptCount;
            var one = await Fetch(symbol, 1, start, end);
            var five = await Fetch(symbol, 5, start, end);
            var slots = Classify(one.SourcePoints, start, end, 1);
            var fiveSlots = Classify(five.SourcePoints, start, end, 5);
            var gap = slots.LastOrDefault(s => s.Status != "Complete");
            Slot? narrow = null;
            if (gap != null)
            {
                var retry = await Fetch(symbol, 1, gap.StartUtc, gap.StartUtc.AddMinutes(1));
                narrow = Classify(retry.SourcePoints, gap.StartUtc, gap.StartUtc.AddMinutes(1), 1).Single();
            }
            Console.WriteLine(JsonSerializer.Serialize(new { Target = $"T{i + 1:00}", Benchmark = symbol == "XIU", one.Batch.ReceivedUtc,
                OneMinute = Summarize(slots), FiveMinute = Summarize(fiveSlots),
                Comparison = TmxHeldPriceFreshnessProbe.CompareIntervals(one.Batch, five.Batch),
                RecentTrades = CompareTrades(trades.Trades, one.Batch),
                LastGap = gap, NarrowReread = narrow }));
        }
        var after = await TmxHeldPriceFreshnessProbe.ReadHeldSymbolsAsync(run.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { Requests = requests, TransportAttempts = attempts,
            TargetSetUnchanged = held.SetEquals(after), CompletedUtc = DateTime.UtcNow }));
    }

    // Classify raw points before mapping. Never interpret null/missing volume as zero or fill a gap.
    internal static Slot[] Classify(IReadOnlyList<TmxTimeSeriesPointDto> points, DateTime start, DateTime end, int interval)
    {
        if (interval is not (1 or 5) || start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc || end <= start ||
            start.Ticks % TimeSpan.FromMinutes(interval).Ticks != 0 || end.Ticks % TimeSpan.FromMinutes(interval).Ticks != 0)
            throw new ArgumentException("Aligned UTC bounds and one/five-minute interval required.");
        var byTime = new Dictionary<DateTime, TmxTimeSeriesPointDto>();
        foreach (var point in points)
        {
            if (!DateTime.TryParse(point.dateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                throw new InvalidOperationException("Unparseable source timestamp.");
            DateTime utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), ReviewedTsxSessionCalendar.Toronto);
            if (utc < start || utc >= end) continue;
            if (utc.Ticks % TimeSpan.FromMinutes(interval).Ticks != 0 || !byTime.TryAdd(utc, point))
                throw new InvalidOperationException("Misaligned or duplicate source timestamp.");
        }
        return Enumerable.Range(0, (int)((end - start).TotalMinutes / interval)).Select(index =>
        {
            DateTime time = start.AddMinutes(index * interval);
            if (!byTime.TryGetValue(time, out var point)) return new Slot(time, "Absent", false, false, false);
            bool allNull = !point.open.HasValue && !point.high.HasValue && !point.low.HasValue && !point.close.HasValue && !point.volume.HasValue;
            return new Slot(time, point.IsComplete ? "Complete" : allNull ? "AllNull" : "PartialNull",
                point.volume.HasValue, point.volume == 0, point.volume > 0);
        }).ToArray();
    }

    private static object Summarize(Slot[] slots) => new { Expected = slots.Length,
        Complete = slots.Count(s => s.Status == "Complete"), Absent = slots.Count(s => s.Status == "Absent"),
        AllNull = slots.Count(s => s.Status == "AllNull"), PartialNull = slots.Count(s => s.Status == "PartialNull"),
        ExplicitZeroVolume = slots.Count(s => s.ZeroVolume), IncompleteWithPositiveVolume = slots.Count(s => s.Status != "Complete" && s.PositiveVolume) };

    internal sealed record Slot(DateTime StartUtc, string Status, bool VolumePresent, bool ZeroVolume, bool PositiveVolume);

    internal static TradeComparison CompareTrades(IReadOnlyList<TmxRecentTradeDto> trades, TmxIntradayBatch one)
    {
        if (one.IntervalMinutes != 1) throw new ArgumentException("One-minute batch required.");
        var timed = trades.Select(t =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(t.datetime, @"(?:Z|[+-]\d{2}:\d{2})$") ||
                !DateTimeOffset.TryParse(t.datetime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ||
                !t.volume.HasValue || t.volume <= 0 || !t.price.HasValue || t.price <= 0)
                throw new InvalidOperationException("Invalid recent-trade evidence.");
            return new { Time = time.UtcDateTime, Volume = t.volume.Value, t.exchangeCode };
        }).ToArray();
        if (timed.Length == 0) return new(0, 0, 0, 0, 0, 0, 0);
        DateTime Minute(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        var inWindow = timed.Where(t => t.Time >= one.RequestedStartUtc && Minute(t.Time).AddMinutes(1) <= one.RequestedEndUtc &&
            Minute(t.Time).AddMinutes(1) <= one.ReceivedUtc).GroupBy(t => Minute(t.Time)).ToArray();
        var bars = one.Bars.ToDictionary(b => b.TimestampUtc);
        DateTime first = Minute(timed.Min(t => t.Time));
        DateTime last = Minute(timed.Max(t => t.Time));
        // The oldest returned minute can be truncated by the 25-row cap; do not compare its volume.
        var interior = inWindow.Where(g => g.Key > first && bars.ContainsKey(g.Key)).ToArray();
        int gapsBetween = 0;
        DateTime gapStart = first.AddMinutes(1) < one.RequestedStartUtc ? one.RequestedStartUtc : first.AddMinutes(1);
        for (DateTime time = gapStart; time < last && time < one.RequestedEndUtc; time = time.AddMinutes(1))
            if (time >= one.RequestedStartUtc && time.AddMinutes(1) <= one.ReceivedUtc && !bars.ContainsKey(time)) gapsBetween++;
        return new(timed.Length, inWindow.Sum(g => g.Count()), inWindow.Length,
            inWindow.Count(g => !bars.ContainsKey(g.Key)), gapsBetween,
            interior.Count(g => g.Sum(t => t.Volume) == bars[g.Key].Volume),
            interior.Count(g => g.Sum(t => t.Volume) != bars[g.Key].Volume));
    }

    internal sealed record TradeComparison(int Rows, int TradesInCompletedWindow, int TradeMinutes,
        int TradeMinutesWithoutBar, int AbsentMinutesBetweenTradeTimes, int InteriorVolumeMatches, int InteriorVolumeMismatches);
}
