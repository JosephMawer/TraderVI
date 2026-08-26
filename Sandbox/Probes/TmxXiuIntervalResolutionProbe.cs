using Core.TMX;
using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Read-only comparison of TMX's 1-, 5-, and 15-minute XIU responses.
///
/// Thesis: the corrected interval request can expose usable bars below the
/// initial 15-minute policy cadence.
///
/// Assumptions: the probe runs during a regular TSX session and requests only
/// the current Toronto trading day, keeping every response below the observed
/// single-response cap.
///
/// Window: one request per supported interval from today's 9:30 a.m. Toronto
/// open through the current time.
///
/// Side effects: three bounded external TMX GraphQL reads and console output
/// only. No SQL connection, file write, position mutation, alert, or order.
///
/// Exit signal: report the number and spacing of bars plus the newest completed
/// interval's age for each resolution. This establishes data availability; it
/// does not by itself choose a production polling cadence.
/// </summary>
public sealed class TmxXiuIntervalResolutionProbe : IProbe
{
    private const string Symbol = "XIU";
    private static readonly int[] Intervals = [1, 5, 15];
    private static readonly TimeZoneInfo TorontoTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public string Slug => "tmx-xiu-interval-resolution";
    public string Description =>
        "Read-only XIU comparison of current-session 1-, 5-, and 15-minute bars.";

    public async Task RunAsync()
    {
        DateTime requestedEndUtc = DateTime.UtcNow;
        DateTime localNow = ToToronto(requestedEndUtc);
        DateTime marketOpenLocal = localNow.Date.AddHours(9).AddMinutes(30);
        DateTime marketCloseLocal = localNow.Date.AddHours(16);

        Console.WriteLine("=== TMX XIU interval-resolution probe ===");
        Console.WriteLine($"Started: {requestedEndUtc:O} UTC / {localNow:yyyy-MM-dd HH:mm:ss} Toronto");
        Console.WriteLine("Requests: XIU at 1, 5, and 15 minutes for the current regular session.");
        Console.WriteLine("Side effects: three external reads and console output only; no SQL, files, positions, alerts, or orders.");
        Console.WriteLine();

        if (localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
            localNow < marketOpenLocal ||
            localNow >= marketCloseLocal)
        {
            Console.WriteLine("Result: not run because Toronto is outside the regular 9:30-16:00 TSX window.");
            return;
        }

        DateTime requestedStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(marketOpenLocal, DateTimeKind.Unspecified),
            TorontoTimeZone);

        using var tmx = new TmxClient();
        var batches = new Dictionary<int, TmxIntradayBatch>();
        Console.WriteLine(
            $"{"Interval",10} {"Bars",7} {"First",12} {"Latest returned",18} " +
            $"{"Latest complete",18} {"Complete age",14} {"Missing",9} {"Forming",9} {"Gaps",12}");
        Console.WriteLine(new string('─', 122));

        foreach (int interval in Intervals)
        {
            TmxIntradayBatch batch = await tmx.GetIntradayTimeSeriesBatchAsync(
                Symbol,
                interval,
                requestedStartUtc,
                requestedEndUtc);
            batches.Add(interval, batch);

            OhlcvBar latestCompleted = batch.LatestCompletedBarAtReceipt;
            string first = FormatLocal(batch.Bars.FirstOrDefault()?.TimestampUtc);
            string latest = FormatLocal(batch.LatestEventUtc);
            string completed = FormatLocal(latestCompleted?.TimestampUtc);
            string gaps = FormatObservedGaps(batch.Bars);
            string age = FormatAge(batch.LatestCompletedEvidenceAgeAtReceipt);
            int missing = CountMissingSlots(batch.Bars, interval);
            string forming = batch.HasFormingBarAtReceipt ? "yes" : "no";

            Console.WriteLine(
                $"{interval + " min",10} {batch.Bars.Count,7:N0} {first,12} {latest,18} " +
                $"{completed,18} {age,14} {missing,9:N0} {forming,9} {gaps,12}");
        }

        Console.WriteLine();
        PrintFiveMinuteAggregationComparison(batches[5], batches[15]);
        Console.WriteLine("Result: compare returned spacing and freshness; availability does not imply that one-minute polling is strategically useful.");
    }

    private static void PrintFiveMinuteAggregationComparison(
        TmxIntradayBatch fiveMinuteBatch,
        TmxIntradayBatch fifteenMinuteBatch)
    {
        IReadOnlyDictionary<DateTime, OhlcvBar> fiveMinuteBars =
            fiveMinuteBatch.Bars.ToDictionary(bar => bar.TimestampUtc);
        List<OhlcvBar> directCompleted = fifteenMinuteBatch.Bars
            .Where(bar =>
                bar.TimestampUtc.AddMinutes(15) <= fifteenMinuteBatch.ReceivedUtc)
            .ToList();
        int comparable = 0;
        int matches = 0;

        foreach (OhlcvBar direct in directCompleted)
        {
            if (!fiveMinuteBars.TryGetValue(direct.TimestampUtc, out OhlcvBar first) ||
                !fiveMinuteBars.TryGetValue(
                    direct.TimestampUtc.AddMinutes(5),
                    out OhlcvBar second) ||
                !fiveMinuteBars.TryGetValue(
                    direct.TimestampUtc.AddMinutes(10),
                    out OhlcvBar third))
            {
                continue;
            }

            comparable++;
            var aggregated = new OhlcvBar(
                direct.TimestampUtc,
                first.Open,
                System.Math.Max(first.High, System.Math.Max(second.High, third.High)),
                System.Math.Min(first.Low, System.Math.Min(second.Low, third.Low)),
                third.Close,
                checked(first.Volume + second.Volume + third.Volume));
            if (aggregated == direct)
                matches++;
        }

        Console.WriteLine(
            $"Completed 5-minute → 15-minute aggregation: " +
            $"{matches:N0}/{comparable:N0} comparable bars exactly matched " +
            $"({directCompleted.Count - comparable:N0} lacked a complete 5-minute triplet).");
    }

    private static string FormatObservedGaps(IReadOnlyList<OhlcvBar> bars)
    {
        if (bars.Count < 2)
            return "—";

        string[] gaps = bars
            .Zip(bars.Skip(1), (left, right) =>
                (right.TimestampUtc - left.TimestampUtc).TotalMinutes)
            .Distinct()
            .OrderBy(minutes => minutes)
            .Select(minutes => $"{minutes:N0}m")
            .ToArray();
        return string.Join(",", gaps);
    }

    private static int CountMissingSlots(
        IReadOnlyList<OhlcvBar> bars,
        int intervalMinutes)
    {
        if (bars.Count < 2)
            return 0;

        int expected = checked((int)(
            (bars[^1].TimestampUtc - bars[0].TimestampUtc).TotalMinutes /
            intervalMinutes) + 1);
        return System.Math.Max(0, expected - bars.Count);
    }

    private static string FormatLocal(DateTime? utc) =>
        utc.HasValue ? ToToronto(utc.Value).ToString("HH:mm:ss") : "—";

    private static DateTime ToToronto(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TorontoTimeZone);

    private static string FormatAge(TimeSpan? age)
    {
        if (!age.HasValue)
            return "—";
        return age.Value.ToString("g");
    }
}
