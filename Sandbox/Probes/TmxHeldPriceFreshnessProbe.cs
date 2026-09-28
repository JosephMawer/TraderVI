#nullable enable
using Core.Db;
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Read-only held-paper-stock freshness and missing-minute investigation. Selects distinct currently
/// held symbols through existing repositories, adds XIU, and refuses more than eight total targets.
/// Five one-minute polls per target, then one five-minute comparison and (if gappy) one broad reread
/// plus one narrow gap request: at most eight requests per target / 64 requests / 192 transport attempts.
/// Each request has a 15-second timeout; a poll has a 45-second deadline, and the whole run ten minutes.
/// Uses a reviewed regular session; no SQL, account, journal or order writes. Output anonymizes targets.
/// Final target reread detects symbol-set changes, not every intervening account change. Measurements
/// cannot establish no-trade semantics, market-data entitlement, continuous coverage or trading authority.
/// </summary>
public sealed class TmxHeldPriceFreshnessProbe : IProbe
{
    public string Slug => "tmx-held-price-freshness";
    public string Description => "Bounded read-only one-minute held-stock checks and five-minute/gap comparisons.";

    public async Task RunAsync()
    {
        try { await RunCheckedAsync(); }
        catch (Exception ex) { throw new InvalidOperationException($"Held-price probe stopped: {ex.GetType().Name}. Private source details omitted."); }
    }

    private static async Task RunCheckedAsync()
    {
        using var run = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var calendar = ReviewedTsxSessionCalendar.Load(Environment.GetEnvironmentVariable("TRADERVI_TSX_CALENDAR_PATH")
            ?? throw new InvalidOperationException("Reviewed calendar required."));
        var held = await ReadHeldSymbolsAsync(run.Token);
        var targets = held.Append("XIU").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (held.Count == 0 || targets.Length > 8) throw new InvalidOperationException("Held target count is outside probe bounds.");
        DateTime now = DateTime.UtcNow;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, ReviewedTsxSessionCalendar.Toronto));
        var bounds = calendar.GetSessionBounds(date);
        DateTime due = Floor(now, 1).AddSeconds(10);
        if (due < now) due = due.AddMinutes(1);
        DateTime start = Floor(due, 5).AddMinutes(-10);
        if (start < bounds.OpenUtc || due.AddMinutes(9) >= bounds.CloseUtc)
            throw new InvalidOperationException("Insufficient regular-session window.");
        Console.WriteLine(JsonSerializer.Serialize(new { HeldSymbols = held.Count, Targets = targets.Length,
            MaximumRequests = targets.Length * 8, MaximumTransportAttempts = targets.Length * 24, StartedUtc = now }));
        using var client = new TmxClient();
        var history = targets.ToDictionary(s => s, _ => new Dictionary<DateTime, OhlcvBar>());
        var last = new Dictionary<string, TmxIntradayBatch>();
        var latest = targets.ToDictionary(s => s, _ => (DateTime?)null);
        int requests = 0, attempts = 0;
        async Task<TmxIntradayBatch> Fetch(string symbol, int interval, DateTime from, DateTime to, CancellationToken token)
        {
            if (DateTime.UtcNow >= bounds.CloseUtc || requests >= targets.Length * 8)
                throw new InvalidOperationException("Request budget or regular session ended.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            requests++;
            var batch = await client.GetIntradayTimeSeriesBatchAsync(symbol, interval, from, to, timeout.Token);
            attempts += batch.AttemptCount;
            return batch;
        }
        for (int poll = 0; poll < 5; poll++)
        {
            DateTime scheduled = due.AddMinutes(poll);
            TimeSpan wait = scheduled - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, run.Token);
            if (DateTime.UtcNow > scheduled.AddSeconds(15)) throw new InvalidOperationException("Missed poll; no catch-up burst.");
            using var cycle = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            cycle.CancelAfter(TimeSpan.FromSeconds(45));
            DateTime end = Floor(scheduled, 1);
            int heldAtLatestMinute = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                string symbol = targets[i];
                var batch = await Fetch(symbol, 1, start, end, cycle.Token);
                var summary = TmxOneMinuteFreshnessProbe.Analyze(batch, history[symbol], latest[symbol]);
                Console.WriteLine(JsonSerializer.Serialize(new { Poll = poll + 1, Target = $"T{i + 1:00}",
                    Benchmark = symbol == "XIU", Held = held.Contains(symbol), batch.ReceivedUtc, Result = summary }));
                if (held.Contains(symbol) && summary.LatestCompletedStartUtc == end.AddMinutes(-1)) heldAtLatestMinute++;
                foreach (var bar in batch.Bars.Where(b => b.TimestampUtc >= start && b.TimestampUtc < end && b.TimestampUtc.AddMinutes(1) <= batch.ReceivedUtc))
                    history[symbol].TryAdd(bar.TimestampUtc, bar);
                last[symbol] = batch;
                latest[symbol] = summary.LatestCompletedStartUtc;
            }
            Console.WriteLine(JsonSerializer.Serialize(new { Poll = poll + 1, HeldAtLatestRequestedMinute = heldAtLatestMinute,
                HeldSymbols = held.Count, CheckedUtc = DateTime.UtcNow }));
        }
        for (int i = 0; i < targets.Length; i++)
        {
            string symbol = targets[i];
            var one = last[symbol];
            var five = await Fetch(symbol, 5, start, Floor(one.RequestedEndUtc, 5), run.Token);
            var missing = Enumerable.Range(0, (int)(one.RequestedEndUtc - start).TotalMinutes)
                .Select(m => start.AddMinutes(m)).Where(t => !one.Bars.Any(b => b.TimestampUtc == t)).ToArray();
            bool? recoveredBroad = null, recoveredNarrow = null;
            if (missing.Length > 0)
            {
                DateTime gap = missing[0];
                var repeat = await Fetch(symbol, 1, start, one.RequestedEndUtc, run.Token);
                recoveredBroad = repeat.Bars.Any(b => b.TimestampUtc == gap);
                var narrow = await Fetch(symbol, 1, gap, gap.AddMinutes(1), run.Token);
                recoveredNarrow = narrow.Bars.Any(b => b.TimestampUtc == gap);
            }
            Console.WriteLine(JsonSerializer.Serialize(new { Target = $"T{i + 1:00}", Benchmark = symbol == "XIU",
                Comparison = CompareIntervals(one, five), FirstMissingMinute = missing.Length > 0 ? (DateTime?)missing[0] : null,
                GapRecoveredByBroadReread = recoveredBroad, GapRecoveredByNarrowRequest = recoveredNarrow }));
        }
        var after = await ReadHeldSymbolsAsync(run.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { Requests = requests, TransportAttempts = attempts,
            TargetSetUnchanged = held.SetEquals(after), CompletedUtc = DateTime.UtcNow }));
        Console.WriteLine("Measurement complete. Missing minutes remain unknown; volume agreement alone does not prove no trading occurred.");
    }

    internal static async Task<HashSet<string>> ReadHeldSymbolsAsync(CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var catalogue = await new AiPortfolioCatalogRepository(TimeSpan.FromMinutes(1)).ReadAsync(ct);
        foreach (var entry in catalogue.Entries)
        {
            if (entry.EngineFamily == "DailyShadow")
                foreach (var position in await new SystemShadowRepository().GetPositionsAsync(entry.PaperAccountId, ct))
                { if (position.Status == "Open") result.Add(position.Symbol); }
            else if (entry.EngineFamily == "DelphiLive")
            {
                var snapshot = await new DelphiLiveLedgerRepository().LoadPortfolioAsync(entry.PaperAccountId, ct)
                    ?? throw new InvalidOperationException("Missing ledger.");
                foreach (var position in snapshot.OpenPositions) result.Add(position.Symbol);
            }
            else throw new InvalidOperationException("Unsupported family.");
        }
        return result;
    }

    internal static IntervalComparison CompareIntervals(TmxIntradayBatch one, TmxIntradayBatch five)
    {
        if (one.IntervalMinutes != 1 || five.IntervalMinutes != 5 || one.Symbol != five.Symbol)
            throw new ArgumentException("Same-symbol one/five-minute batches required.");
        int complete = 0, exact = 0, sparse = 0, sparseVolumeEqual = 0;
        foreach (var bar in five.Bars.Where(b => b.TimestampUtc >= one.RequestedStartUtc && b.TimestampUtc >= five.RequestedStartUtc &&
            b.TimestampUtc.AddMinutes(5) <= one.RequestedEndUtc && b.TimestampUtc.AddMinutes(5) <= five.RequestedEndUtc &&
            b.TimestampUtc.AddMinutes(5) <= one.ReceivedUtc && b.TimestampUtc.AddMinutes(5) <= five.ReceivedUtc))
        {
            var minutes = one.Bars.Where(b => b.TimestampUtc >= bar.TimestampUtc && b.TimestampUtc < bar.TimestampUtc.AddMinutes(5))
                .OrderBy(b => b.TimestampUtc).ToArray();
            if (minutes.Length == 5 && minutes.Select(b => b.TimestampUtc).Distinct().Count() == 5)
            {
                complete++;
                if (new OhlcvBar(bar.TimestampUtc, minutes[0].Open, minutes.Max(b => b.High), minutes.Min(b => b.Low),
                    minutes[^1].Close, minutes.Sum(b => b.Volume)) == bar) exact++;
            }
            else { sparse++; if (minutes.Sum(b => b.Volume) == bar.Volume) sparseVolumeEqual++; }
        }
        return new(complete, exact, sparse, sparseVolumeEqual, sparse - sparseVolumeEqual);
    }
    private static DateTime Floor(DateTime utc, int minutes) => new(utc.Ticks - utc.Ticks % TimeSpan.FromMinutes(minutes).Ticks, DateTimeKind.Utc);
    internal sealed record IntervalComparison(int CompleteGroups, int ExactOhlcvMatches, int SparseGroups,
        int SparseGroupsWithEqualVolume, int SparseGroupsWithDifferentVolume);
}
