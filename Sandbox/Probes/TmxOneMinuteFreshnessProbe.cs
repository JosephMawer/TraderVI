#nullable enable
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
/// Thesis: one-minute XIU bars may provide fresher observations than scheduled five-minute collection.
/// Five external reads, one per minute at approximately ten seconds past the minute, in a reviewed
/// regular TSX session. Each request has a 25-second timeout; the existing client permits at most
/// three transport attempts (15 maximum overall). Window starts ten minutes before the first poll.
/// Reads calendar configuration and calls TMX only; no SQL, account, journal or order writes.
/// Reports ages, missing minutes and changes to previously completed bars, never trading permission.
/// A five-poll benchmark sample cannot qualify held-stock coverage or an operational freshness limit.
/// </summary>
public sealed class TmxOneMinuteFreshnessProbe : IProbe
{
    public string Slug => "tmx-one-minute-freshness";
    public string Description => "Five bounded read-only XIU polls to measure one-minute freshness and revisions.";

    public async Task RunAsync()
    {
        var calendar = ReviewedTsxSessionCalendar.Load(Environment.GetEnvironmentVariable("TRADERVI_TSX_CALENDAR_PATH")
            ?? throw new InvalidOperationException("Reviewed calendar is required."));
        DateTime now = DateTime.UtcNow;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, ReviewedTsxSessionCalendar.Toronto));
        var bounds = calendar.GetSessionBounds(date);
        DateTime firstDue = FloorMinute(now).AddSeconds(10);
        if (firstDue < now) firstDue = firstDue.AddMinutes(1);
        if (now < bounds.OpenUtc.AddMinutes(10) || firstDue.AddMinutes(5) >= bounds.CloseUtc)
            throw new InvalidOperationException("Not run: requires ten minutes of regular-session history and five minutes before close.");
        DateTime start = FloorMinute(firstDue).AddMinutes(-10);
        var previous = new Dictionary<DateTime, OhlcvBar>();
        DateTime? previousLatest = null;
        using var client = new TmxClient();
        Console.WriteLine("XIU only; five one-minute reads, at most 15 transport attempts; no SQL or trading. Ages measured from bar end.");
        for (int poll = 0; poll < 5; poll++)
        {
            var wait = firstDue.AddMinutes(poll) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            if (DateTime.UtcNow >= bounds.CloseUtc) throw new InvalidOperationException("Stopped at market close.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            TmxIntradayBatch batch;
            try { batch = await client.GetIntradayTimeSeriesBatchAsync("XIU", 1, start, DateTime.UtcNow, timeout.Token); }
            catch (Exception ex)
            {
                // Do not print provider bodies or a misleading success after an incomplete sequence.
                throw new InvalidOperationException($"One-minute probe stopped at poll {poll + 1}: {ex.GetType().Name}.");
            }
            var result = Analyze(batch, previous, previousLatest);
            Console.WriteLine(JsonSerializer.Serialize(new { Poll = poll + 1, batch.FetchStartedUtc, batch.ReceivedUtc,
                batch.AttemptCount, batch.RequestCount, Result = result }));
            foreach (var bar in Completed(batch)) previous.TryAdd(bar.TimestampUtc, bar);
            previousLatest = result.LatestCompletedStartUtc;
        }
        Console.WriteLine("Measurement complete. Review gaps, revisions and age; this is not production or held-stock qualification.");
    }

    internal static Summary Analyze(TmxIntradayBatch batch, IReadOnlyDictionary<DateTime, OhlcvBar> previous,
        DateTime? previousLatest)
    {
        if (batch.IntervalMinutes != 1 || batch.RequestedEndUtc < batch.RequestedStartUtc)
            throw new ArgumentException("A one-minute request window is required.");
        var completed = Completed(batch).ToDictionary(b => b.TimestampUtc);
        DateTime cutoff = batch.RequestedEndUtc < FloorMinute(batch.ReceivedUtc)
            ? batch.RequestedEndUtc : FloorMinute(batch.ReceivedUtc);
        int expected = Math.Max(0, (int)(cutoff - batch.RequestedStartUtc).TotalMinutes);
        DateTime? latest = completed.Count == 0 ? null : completed.Keys.Max();
        int comparable = previous.Keys.Count(completed.ContainsKey);
        int revised = previous.Count(p => completed.TryGetValue(p.Key, out var current) && current != p.Value);
        int disappeared = previous.Keys.Count(t => t >= batch.RequestedStartUtc && t < cutoff && !completed.ContainsKey(t));
        string advance = latest is null ? "Missing" : previousLatest is null ? "Initial" :
            latest > previousLatest ? "Advanced" : latest == previousLatest ? "Repeated" : "Regressed";
        return new(expected, completed.Count, expected - completed.Count, comparable, revised, disappeared,
            batch.Bars.Count(b => b.TimestampUtc.AddMinutes(1) > batch.ReceivedUtc), latest,
            latest is { } time ? (batch.ReceivedUtc - time.AddMinutes(1)).TotalSeconds : null, advance);
    }

    private static IEnumerable<OhlcvBar> Completed(TmxIntradayBatch b) => b.Bars.Where(p =>
        p.TimestampUtc >= b.RequestedStartUtc && p.TimestampUtc < b.RequestedEndUtc &&
        p.TimestampUtc.AddMinutes(1) <= b.ReceivedUtc);
    private static DateTime FloorMinute(DateTime utc) => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    internal sealed record Summary(int ExpectedMinutes, int CompletedMinutes, int MissingMinutes,
        int ComparableCompletedBars, int RevisedCompletedBars, int DisappearedCompletedBars,
        int FormingBars, DateTime? LatestCompletedStartUtc, double? AgeSeconds, string Advance);
}
