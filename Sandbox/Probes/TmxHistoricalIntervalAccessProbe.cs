#nullable enable
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Thesis: the existing TMX client can retrieve genuine five- and fifteen-minute
/// historical sessions beyond the recent Friday replay. Uses reviewed full sessions
/// September 4, August 4 and March 4, 2026, for XIU and RY only. Twelve sequential
/// logical reads, at most three transport attempts each, twenty seconds per read,
/// four minutes overall, and one second between reads. Stop on first request error.
/// Empty or partial valid responses are findings, not reasons to broaden the probe.
/// Writes unique normalized source files and a checksummed metadata report only under
/// ignored artifacts/tmx-historical-interval-access. No SQL, quotes, accounts, models,
/// operational receipts or strategy execution. Timestamp start semantics remain the
/// current client's convention; interval agreement is evidence, not provider certification.
/// Exit: print per-session coverage and interval agreement, without market prices.
/// This samples access, not the complete retention boundary or live reliability.
/// </summary>
public sealed class TmxHistoricalIntervalAccessProbe : IProbe
{
    private static readonly DateOnly[] Dates = [new(2026, 9, 4), new(2026, 8, 4), new(2026, 3, 4)];
    private static readonly string[] Symbols = ["XIU", "RY"];
    private static readonly int[] Intervals = [5, 15];
    public string Slug => "tmx-historical-interval-access";
    public string Description => "Twelve bounded historical XIU/RY 5/15-minute reads; private local coverage artifacts, no SQL.";

    public async Task RunAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TraderVI.sln"))) root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException("Repository root is required.");
        string calendarPath = Path.Combine(root.FullName, "Operations/Calendars/tsx-2026-through-20261223-v1.json");
        var calendar = ReviewedTsxSessionCalendar.Load(calendarPath);
        DateTime started = DateTime.UtcNow;
        foreach (var date in Dates)
            if (started <= calendar.GetSessionBounds(date).CloseUtc)
                throw new InvalidOperationException("All pinned historical full sessions must be complete.");
        string directory = Path.Combine(root.FullName, "artifacts/tmx-historical-interval-access",
            $"{started:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var rows = new List<Coverage>();
        var batches = new Dictionary<(DateOnly Date, string Symbol, int Interval), TmxIntradayBatch>();
        var comparisons = new List<Comparison>();
        string? failure = null;
        int requests = 0;
        using var overall = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var client = new TmxClient();
        Console.WriteLine("Historical access: 12 logical reads maximum; 20 seconds each; 4 minutes overall. No SQL or quotes.");
        Console.WriteLine($"Artifacts: {directory}");
        try
        {
            foreach (var date in Dates)
            foreach (string symbol in Symbols)
            foreach (int interval in Intervals)
            {
                if (requests > 0) await Task.Delay(TimeSpan.FromSeconds(1), overall.Token);
                overall.Token.ThrowIfCancellationRequested();
                if (++requests > 12) throw new InvalidOperationException("Request budget exhausted.");
                var bounds = calendar.GetSessionBounds(date);
                Console.WriteLine($"Request {requests}/12: {date:yyyy-MM-dd} {symbol} {interval} minutes.");
                using var requestBudget = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                requestBudget.CancelAfter(TimeSpan.FromSeconds(20));
                var batch = await client.GetIntradayTimeSeriesBatchAsync(symbol, interval, bounds.OpenUtc, bounds.CloseUtc, requestBudget.Token);
                batches.Add((date, symbol, interval), batch);
                string fileName = $"{date:yyyyMMdd}-{symbol}-{interval}m.json";
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(batch, options);
                await WriteNew(Path.Combine(directory, fileName), bytes);
                var bars = InSession(batch, bounds);
                int expected = checked((int)((bounds.CloseUtc - bounds.OpenUtc).TotalMinutes / interval));
                var starts = bars.Select(b => b.TimestampUtc).ToHashSet();
                var missing = Enumerable.Range(0, expected).Select(n => bounds.OpenUtc.AddMinutes(n * interval))
                    .Where(t => !starts.Contains(t)).ToArray();
                var row = new Coverage(date, symbol, interval, expected, batch.Bars.Count, bars.Length,
                    batch.Bars.Count - bars.Length, missing,
                    bars.FirstOrDefault()?.TimestampUtc, bars.LastOrDefault()?.TimestampUtc,
                    bars.Zip(bars.Skip(1), (a, b) => (b.TimestampUtc - a.TimestampUtc).TotalMinutes).Distinct().Order().ToArray(),
                    batch.FetchStartedUtc, batch.ReceivedUtc, batch.AttemptCount, batch.RequestCount,
                    fileName, Convert.ToHexString(SHA256.HashData(bytes)));
                rows.Add(row);
                Console.WriteLine($"  Coverage {row.InSessionBars}/{row.ExpectedBars}; missing {missing.Length}; boundary rows {row.ExcludedBoundaryBars}; attempts {row.AttemptCount}.");
            }
        }
        catch (Exception error)
        {
            // Exception messages can contain source payloads. Report the type only.
            failure = error.GetType().Name;
            Console.WriteLine($"Stopped at request {requests}: {failure}. Successful local source files are preserved.");
        }
        foreach (var date in Dates)
        foreach (string symbol in Symbols)
        {
            if (!batches.TryGetValue((date, symbol, 5), out var five) || !batches.TryGetValue((date, symbol, 15), out var fifteen)) continue;
            var bounds = calendar.GetSessionBounds(date);
            var lookup = InSession(five, bounds).ToDictionary(b => b.TimestampUtc);
            int comparable = 0, priceMatches = 0, volumeMatches = 0, exactMatches = 0;
            var mismatches = new List<DateTime>();
            foreach (var direct in InSession(fifteen, bounds))
            {
                if (!lookup.TryGetValue(direct.TimestampUtc, out var first) ||
                    !lookup.TryGetValue(direct.TimestampUtc.AddMinutes(5), out var second) ||
                    !lookup.TryGetValue(direct.TimestampUtc.AddMinutes(10), out var third)) continue;
                comparable++;
                bool priceMatch = first.Open == direct.Open && third.Close == direct.Close &&
                    System.Math.Max(first.High, System.Math.Max(second.High, third.High)) == direct.High &&
                    System.Math.Min(first.Low, System.Math.Min(second.Low, third.Low)) == direct.Low;
                bool volumeMatch = checked(first.Volume + second.Volume + third.Volume) == direct.Volume;
                if (priceMatch) priceMatches++;
                if (volumeMatch) volumeMatches++;
                if (priceMatch && volumeMatch) exactMatches++; else mismatches.Add(direct.TimestampUtc);
            }
            comparisons.Add(new(date, symbol, comparable, priceMatches, volumeMatches, exactMatches, mismatches.ToArray()));
            Console.WriteLine($"5-to-15-minute agreement {date:yyyy-MM-dd} {symbol}: OHLC {priceMatches}/{comparable}, volume {volumeMatches}/{comparable}, exact {exactMatches}/{comparable}.");
        }
        var report = new
        {
            Kind = "TmxHistoricalIntervalAccessV1", StartedUtc = started, CompletedUtc = DateTime.UtcNow,
            Result = failure is not null ? "Aborted" : rows.All(r => r.InSessionBars == r.ExpectedBars && r.MissingStartsUtc.Length == 0) ? "SampledSessionsComplete" : "SampledSessionsHaveGaps",
            FailureType = failure, LogicalRequestsStarted = requests, SuccessfulRequests = rows.Count,
            MaximumLogicalRequests = 12, MaximumTransportAttempts = 36, RequestTimeoutSeconds = 20, OverallTimeoutSeconds = 240,
            CalendarVersion = calendar.Version, CalendarSha256 = HashFile(calendarPath),
            CoreAssemblySha256 = HashFile(typeof(TmxClient).Assembly.Location),
            ProbeAssemblySha256 = HashFile(typeof(TmxHistoricalIntervalAccessProbe).Assembly.Location),
            DatabaseConnections = 0, OperationalReceipts = 0, CleanCohorts = 0,
            Limitations = new[] { "Two symbols and three sampled sessions do not establish universal coverage or a retention boundary.",
                "Normalized client output only; rows with null OHLCV are filtered inside the client before this probe.",
                "Bar-start labels and Eastern-to-UTC conversion follow the existing mapper; provider semantics are not independently certified.",
                "No historical receipt times, bid/ask history, adjustment identity, corporate actions or delisted-symbol coverage verified.",
                "No daily input/model eligibility, strategy performance, live publication latency, SQL durability or WPF behavior tested." },
            Coverage = rows, FiveToFifteenComparisons = comparisons
        };
        string reportPath = Path.Combine(directory, "report.json");
        byte[] reportBytes = JsonSerializer.SerializeToUtf8Bytes(report, options);
        await WriteNew(reportPath, reportBytes);
        string reportHash = Convert.ToHexString(SHA256.HashData(reportBytes));
        await WriteNew(Path.Combine(directory, "report.sha256"), System.Text.Encoding.UTF8.GetBytes(reportHash + "  report.json\n"));
        Console.WriteLine($"Result: {report.Result}; {rows.Count}/12 successful requests. Report SHA256: {reportHash}");
        if (failure is not null) throw new InvalidOperationException("Historical source access probe stopped; see the metadata report.");
    }

    private static OhlcvBar[] InSession(TmxIntradayBatch batch, DelphiLiveSessionBounds bounds) => batch.Bars
        .Where(b => b.TimestampUtc >= bounds.OpenUtc && b.TimestampUtc.AddMinutes(batch.IntervalMinutes) <= bounds.CloseUtc).ToArray();
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static async Task WriteNew(string path, byte[] bytes)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await stream.WriteAsync(bytes);
    }
    private sealed record Coverage(DateOnly Date, string Symbol, int IntervalMinutes, int ExpectedBars, int ReturnedBars,
        int InSessionBars, int ExcludedBoundaryBars, DateTime[] MissingStartsUtc, DateTime? FirstStartUtc, DateTime? LastStartUtc,
        double[] ObservedSpacingMinutes, DateTime FetchStartedUtc, DateTime ReceivedUtc, int AttemptCount, int RequestCount,
        string SourceFile, string SourceSha256);
    private sealed record Comparison(DateOnly Date, string Symbol, int Comparable, int OhlcMatches, int VolumeMatches,
        int ExactMatches, DateTime[] MismatchStartsUtc);
}
