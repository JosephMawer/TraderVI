using Core.TMX;
using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Read-only reconnaissance for ADR-0028 against XIU and the existing
/// <see cref="TmxClient.GetIntradayTimeSeriesAsync"/> method.
///
/// Thesis: the TMX Money time-series operation can return consistently aligned
/// 15-minute XIU OHLCV bars with enough timestamp fidelity, session coverage,
/// and retention to support a delayed advisory swing monitor.
///
/// Assumptions: TMX timestamps are exchange-local values mapped to UTC by Core;
/// a regular TSX session contains 26 fifteen-minute bars; the returned timestamp
/// may identify either the start or end of a bar and must be inferred rather than
/// assumed.
///
/// Window: three bounded reads ending at the current UTC time, looking back 2,
/// 14, and 90 calendar days. Only symbol XIU and interval=15 are requested.
///
/// Side effects: external TMX GraphQL reads and console output only. This probe
/// does not open a SQL connection, write a file, mutate a position, or place an
/// order.
///
/// Exit signal: proceed to the storage-contract ADR work only if timestamps are
/// aligned, duplicate/OHLC faults are absent, regular-session grouping is
/// intelligible, and the returned retention is sufficient for the intended
/// short-horizon policy. Otherwise keep the collector blocked and document the
/// observed limitation.
/// </summary>
public sealed class TmxXiuIntradayProbe : IProbe
{
    private const string Symbol = "XIU";
    private const int IntervalMinutes = 15;
    private const int ExpectedRegularSessionBars = 26;
    private static readonly TimeZoneInfo TorontoTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public string Slug => "tmx-xiu-intraday";
    public string Description =>
        "Read-only XIU 15-minute TMX probe — timestamps, delay, alignment, session coverage, and retention.";

    public async Task RunAsync()
    {
        using var tmx = new TmxClient();
        DateTime probeStartedUtc = DateTime.UtcNow;
        int[] lookbackDays = [2, 14, 90];
        var results = new List<WindowResult>();

        Console.WriteLine("=== TMX XIU delayed-intraday contract probe ===");
        Console.WriteLine($"Probe started UTC: {probeStartedUtc:O}");
        Console.WriteLine($"Request: symbol={Symbol}, freq=<unset>, interval={IntervalMinutes}");
        Console.WriteLine("Side effects: external read only; no SQL, files, positions, or orders.");
        Console.WriteLine();

        foreach (int days in lookbackDays)
        {
            DateTime requestedEndUtc = DateTime.UtcNow;
            DateTime requestedStartUtc = requestedEndUtc.AddDays(-days);
            var stopwatch = Stopwatch.StartNew();
            List<OhlcvBar> bars = await tmx.GetIntradayTimeSeriesAsync(
                Symbol,
                IntervalMinutes,
                requestedStartUtc,
                requestedEndUtc);
            stopwatch.Stop();

            DateTime receivedUtc = DateTime.UtcNow;
            WindowResult result = Analyze(
                days,
                requestedStartUtc,
                requestedEndUtc,
                receivedUtc,
                stopwatch.Elapsed,
                bars);
            results.Add(result);
            PrintWindow(result, showSessions: days == 14, showLatestBars: days == 2);
        }

        Console.WriteLine("=== Cross-window summary ===");
        Console.WriteLine($"{"Lookback",10} {"Bars",7} {"Sessions",9} {"Earliest local",22} {"Latest local",22} {"Latest age",14}");
        Console.WriteLine(new string('─', 92));
        foreach (WindowResult result in results)
        {
            string earliest = result.EarliestUtc.HasValue
                ? ToToronto(result.EarliestUtc.Value).ToString("yyyy-MM-dd HH:mm")
                : "—";
            string latest = result.LatestUtc.HasValue
                ? ToToronto(result.LatestUtc.Value).ToString("yyyy-MM-dd HH:mm")
                : "—";
            Console.WriteLine(
                $"{result.LookbackDays + "d",10} {result.Bars.Count,7:N0} {result.Sessions.Count,9:N0} " +
                $"{earliest,22} {latest,22} {FormatAge(result.LatestAge),14}");
        }

        WindowResult widest = results[^1];
        bool structurallyClean = results.All(r =>
            r.DuplicateTimestampCount == 0 &&
            r.MisalignedTimestampCount == 0 &&
            r.InvalidOhlcCount == 0 &&
            r.NegativeVolumeCount == 0);
        bool hasCompleteSession = widest.Sessions.Any(s => s.BarCount == ExpectedRegularSessionBars);
        bool wideWindowWasTruncated = widest.LatestUtc.HasValue &&
            widest.LatestUtc.Value < widest.RequestedEndUtc.AddDays(-7);

        Console.WriteLine();
        Console.WriteLine("=== Probe verdict ===");
        Console.WriteLine($"Structural checks: {(structurallyClean ? "PASS" : "FAIL")}");
        Console.WriteLine($"At least one 26-bar regular session: {(hasCompleteSession ? "YES" : "NO")}");
        Console.WriteLine($"Wide-window response was capped: {(wideWindowWasTruncated ? "YES" : "NO")}");
        Console.WriteLine($"Observed timestamp label: {widest.TimestampLabelInference}");
        Console.WriteLine($"Observed 90-day request range: {widest.RetentionDescription}");
        Console.WriteLine("Delay note: latest age is measured from the returned bar timestamp; interpret it with the inferred start/end label.");
        Console.WriteLine(structurallyClean && hasCompleteSession
            ? "Result: short rolling windows are suitable for monitoring/storage design; long history must be requested in bounded chunks."
            : "Result: keep the collector blocked until the failed response-contract checks are resolved.");
    }

    private static WindowResult Analyze(
        int lookbackDays,
        DateTime requestedStartUtc,
        DateTime requestedEndUtc,
        DateTime receivedUtc,
        TimeSpan requestDuration,
        IReadOnlyList<OhlcvBar> sourceBars)
    {
        List<OhlcvBar> bars = sourceBars.OrderBy(b => b.TimestampUtc).ToList();
        int duplicates = bars
            .GroupBy(b => b.TimestampUtc)
            .Sum(group => System.Math.Max(0, group.Count() - 1));
        int misaligned = bars.Count(b =>
            b.TimestampUtc.Second != 0 ||
            b.TimestampUtc.Millisecond != 0 ||
            b.TimestampUtc.Minute % IntervalMinutes != 0);
        int invalidOhlc = bars.Count(b =>
            b.Open <= 0 || b.High <= 0 || b.Low <= 0 || b.Close <= 0 ||
            b.Low > System.Math.Min(b.Open, b.Close) ||
            b.High < System.Math.Max(b.Open, b.Close) ||
            b.Low > b.High);
        int negativeVolume = bars.Count(b => b.Volume < 0);

        List<SessionResult> sessions = bars
            .GroupBy(b => ToToronto(b.TimestampUtc).Date)
            .OrderBy(group => group.Key)
            .Select(group => AnalyzeSession(group.Key, group.OrderBy(b => b.TimestampUtc).ToList()))
            .ToList();

        string label = InferTimestampLabel(sessions);
        string retention = bars.Count == 0
            ? "No bars returned"
            : $"requested {requestedStartUtc:yyyy-MM-dd} → {requestedEndUtc:yyyy-MM-dd}; " +
              $"returned {bars[0].TimestampUtc:yyyy-MM-dd} → {bars[^1].TimestampUtc:yyyy-MM-dd} UTC";

        return new WindowResult(
            lookbackDays,
            requestedStartUtc,
            requestedEndUtc,
            receivedUtc,
            requestDuration,
            bars,
            sessions,
            duplicates,
            misaligned,
            invalidOhlc,
            negativeVolume,
            label,
            retention);
    }

    private static SessionResult AnalyzeSession(DateTime localDate, IReadOnlyList<OhlcvBar> bars)
    {
        List<DateTime> localTimes = bars
            .Select(b => ToToronto(b.TimestampUtc))
            .OrderBy(value => value)
            .ToList();
        int gaps = 0;
        for (int i = 1; i < localTimes.Count; i++)
        {
            if (localTimes[i] - localTimes[i - 1] != TimeSpan.FromMinutes(IntervalMinutes))
                gaps++;
        }

        return new SessionResult(
            localDate,
            bars.Count,
            localTimes[0],
            localTimes[^1],
            gaps);
    }

    private static string InferTimestampLabel(IReadOnlyList<SessionResult> sessions)
    {
        SessionResult? complete = sessions.LastOrDefault(session =>
            session.BarCount == ExpectedRegularSessionBars && session.GapCount == 0);
        if (complete is null)
            return "Unknown (no gap-free 26-bar session)";

        TimeSpan first = complete.FirstLocal.TimeOfDay;
        TimeSpan last = complete.LastLocal.TimeOfDay;
        if (first == new TimeSpan(9, 30, 0) && last == new TimeSpan(15, 45, 0))
            return "Bar-start timestamp";
        if (first == new TimeSpan(9, 45, 0) && last == new TimeSpan(16, 0, 0))
            return "Bar-end timestamp";
        return $"Unknown ({first.ToString(@"hh\:mm")} → {last.ToString(@"hh\:mm")} local for 26 bars)";
    }

    private static void PrintWindow(WindowResult result, bool showSessions, bool showLatestBars)
    {
        Console.WriteLine($"--- {result.LookbackDays}-calendar-day request ---");
        Console.WriteLine($"Requested UTC: {result.RequestedStartUtc:O} → {result.RequestedEndUtc:O}");
        Console.WriteLine($"Received UTC:  {result.ReceivedUtc:O} (HTTP {result.RequestDuration.TotalMilliseconds:N0} ms)");
        Console.WriteLine($"Bars/sessions: {result.Bars.Count:N0} / {result.Sessions.Count:N0}");
        Console.WriteLine(
            $"Faults: duplicate={result.DuplicateTimestampCount}, alignment={result.MisalignedTimestampCount}, " +
            $"OHLC={result.InvalidOhlcCount}, negative-volume={result.NegativeVolumeCount}");
        Console.WriteLine($"Timestamp inference: {result.TimestampLabelInference}");
        Console.WriteLine($"Retention: {result.RetentionDescription}");
        Console.WriteLine($"Latest returned age at receipt: {FormatAge(result.LatestAge)}");

        if (showSessions && result.Sessions.Count > 0)
        {
            Console.WriteLine($"{"Toronto date",14} {"Bars",6} {"First local",12} {"Last local",12} {"Gaps",6}");
            foreach (SessionResult session in result.Sessions)
            {
                Console.WriteLine(
                    $"{session.LocalDate,14:yyyy-MM-dd} {session.BarCount,6} " +
                    $"{session.FirstLocal,12:HH:mm} {session.LastLocal,12:HH:mm} {session.GapCount,6}");
            }
        }

        if (showLatestBars && result.Bars.Count > 0)
        {
            Console.WriteLine("Latest returned bars:");
            Console.WriteLine($"{"Event UTC",22} {"Toronto",18} {"Open",9} {"High",9} {"Low",9} {"Close",9} {"Volume",12}");
            foreach (OhlcvBar bar in result.Bars.TakeLast(10))
            {
                Console.WriteLine(
                    $"{bar.TimestampUtc,22:yyyy-MM-dd HH:mm} {ToToronto(bar.TimestampUtc),18:MM-dd HH:mm} " +
                    $"{bar.Open,9:F2} {bar.High,9:F2} {bar.Low,9:F2} {bar.Close,9:F2} {bar.Volume,12:N0}");
            }
        }

        Console.WriteLine();
    }

    private static DateTime ToToronto(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TorontoTimeZone);

    private static string FormatAge(TimeSpan? age)
    {
        if (!age.HasValue)
            return "—";
        if (age.Value < TimeSpan.Zero)
            return $"future {age.Value.Duration():g}";
        return age.Value.ToString("g");
    }

    private sealed record SessionResult(
        DateTime LocalDate,
        int BarCount,
        DateTime FirstLocal,
        DateTime LastLocal,
        int GapCount);

    private sealed record WindowResult(
        int LookbackDays,
        DateTime RequestedStartUtc,
        DateTime RequestedEndUtc,
        DateTime ReceivedUtc,
        TimeSpan RequestDuration,
        IReadOnlyList<OhlcvBar> Bars,
        IReadOnlyList<SessionResult> Sessions,
        int DuplicateTimestampCount,
        int MisalignedTimestampCount,
        int InvalidOhlcCount,
        int NegativeVolumeCount,
        string TimestampLabelInference,
        string RetentionDescription)
    {
        public DateTime? EarliestUtc => Bars.Count == 0 ? null : Bars[0].TimestampUtc;
        public DateTime? LatestUtc => Bars.Count == 0 ? null : Bars[^1].TimestampUtc;
        public TimeSpan? LatestAge => LatestUtc.HasValue ? ReceivedUtc - LatestUtc.Value : null;
    }
}
