#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Thesis: the production Delphi Live TMX adapter retrieves exact, valid five-minute
/// intervals from the most recent completed session selected for this trial.
/// Window: September4,2026, endpoints09:35,09:50,16:00 Toronto, for XIU/RY/ENB.
/// Assumptions: the reviewed calendar covers this full session; this probe runs
/// outside a regular session and never substitutes historical receipt times.
/// Effects: at most nine sequential TMX logical reads (up to27 transport attempts
/// under the existing client), bounded to20seconds each and4minutes overall.
/// Writes only a unique metadata report under artifacts/delphi-live-collection-trials.
/// No SQL, quotes, leases, assignments, positions, evaluation, activation or broker call.
/// Exit: stop on the first failed request; report exactness, timestamps, attempts and
/// latency without OHLCV values. This cannot establish live availability or clean cohorts.
/// </summary>
public sealed class DelphiLiveClosedMarketTrialProbe : IProbe
{
    public string Slug => "delphi-live-closed-market-trial";
    public string Description => "Nine historical September4 five-minute TMX reads; local metadata report only.";
    private static readonly DateOnly SessionDate = new(2026, 9, 4);
    private static readonly string[] Symbols = ["XIU", "RY", "ENB"];
    private static readonly TimeOnly[] Endpoints = [new(9, 35), new(9, 50), new(16, 0)];

    public async Task RunAsync()
    {
        string root = FindRoot();
        var calendar = ReviewedTsxSessionCalendar.Load(Path.Combine(root, "Operations", "Calendars",
            "tsx-2026-through-20261223-v1.json"));
        DateTime started = DateTime.UtcNow;
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(started, ReviewedTsxSessionCalendar.Toronto));
        if (calendar.IsRegularSession(today) && started >= calendar.GetSessionBounds(today).OpenUtc &&
            started < calendar.GetSessionBounds(today).CloseUtc)
            throw new InvalidOperationException("This bounded historical probe runs only while the regular market is closed.");
        if (!calendar.IsRegularSession(SessionDate) || started <= calendar.GetSessionBounds(SessionDate).CloseUtc)
            throw new InvalidOperationException("The pinned historical session must already be complete.");

        Guid trialId = Guid.NewGuid();
        string directory = Path.Combine(root, "artifacts", "delphi-live-collection-trials");
        Directory.CreateDirectory(directory);
        string reportPath = Path.Combine(directory, $"closed-market-{started:yyyyMMdd-HHmmss}-{trialId:N}.json");
        // Reserve a unique local report before the first provider request.
        await using var reportFile = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var rows = new List<Observation>();
        bool aborted = false;
        using var overall = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var source = new TmxDelphiLiveMarketDataSource();
        Console.WriteLine($"Closed-market historical trial: {SessionDate:yyyy-MM-dd}, XIU/RY/ENB, nine exact intervals.");
        Console.WriteLine("External TMX reads and local metadata report only. Historical results are not operational receipts or clean cohorts.");
        Console.WriteLine($"Report: {reportPath}");

        foreach (TimeOnly endpoint in Endpoints)
        {
            foreach (string symbol in Symbols)
            {
                DateTime requestStarted = DateTime.UtcNow;
                DateTime barEnd = ReviewedTsxSessionCalendar.At(SessionDate, endpoint);
                // Deadline is the local probe budget, not a historical collection deadline.
                var request = new DelphiLiveMarketDataRequest(trialId, symbol, barEnd.AddMinutes(-5),
                    barEnd, requestStarted.AddSeconds(20), requestStarted, rows.Count + 1);
                var timer = Stopwatch.StartNew();
                try
                {
                    using var requestBudget = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                    requestBudget.CancelAfter(TimeSpan.FromSeconds(20));
                    var receipt = await source.GetExactFiveMinuteBarAsync(request, requestBudget.Token);
                    var bar = receipt.ExactCompletedBar;
                    // The adapter's client has already checked UTC alignment, OHLCV and duplicates.
                    bool exact = bar is not null && bar.TimestampUtc.Kind == DateTimeKind.Utc &&
                        bar.TimestampUtc == request.BarStartUtc && barEnd < receipt.ReceivedUtc &&
                        receipt.ReceivedUtc >= requestStarted;
                    string outcome = exact ? "HistoricalExactBarReceived" : "HistoricalExactBarUnavailable";
                    rows.Add(new(symbol, endpoint, requestStarted, receipt.ReceivedUtc, request.BarStartUtc,
                        bar?.TimestampUtc, Math.Round(timer.Elapsed.TotalMilliseconds, 2),
                        receipt.ProviderRequestCount, receipt.ProviderAttemptCount, receipt.ProviderFetchStartedUtc,
                        outcome, null));
                    Console.WriteLine($"{symbol,-3} {endpoint:HH:mm} {outcome} {timer.Elapsed.TotalMilliseconds:F0} ms; attempts={receipt.ProviderAttemptCount}");
                    if (!exact) { aborted = true; break; }
                }
                catch (Exception error)
                {
                    // Exception messages can contain provider payloads; keep only types and status codes.
                    int? httpStatus = error is GraphQL.Client.Http.GraphQLHttpRequestException graphQl
                        ? (int)graphQl.StatusCode : error is System.Net.Http.HttpRequestException http && http.StatusCode.HasValue
                            ? (int)http.StatusCode.Value : null;
                    string failure = error.GetType().Name + (httpStatus.HasValue ? $"/HTTP{httpStatus}" : "");
                    rows.Add(new(symbol, endpoint, requestStarted, null, request.BarStartUtc, null,
                        Math.Round(timer.Elapsed.TotalMilliseconds, 2), null, null, null, "Failed", failure));
                    Console.WriteLine($"{symbol} {endpoint:HH:mm}: failed ({failure}); remaining requests cancelled.");
                    aborted = true;
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), overall.Token);
            }
            if (aborted) break;
        }

        int successes = rows.Count(r => r.Outcome == "HistoricalExactBarReceived");
        var report = new
        {
            TrialId = trialId, Kind = "ClosedMarketHistoricalSourceTrial", StartedUtc = started,
            CompletedUtc = DateTime.UtcNow, SessionDate, CalendarVersion = calendar.Version,
            ExpectedRequests = 9, AttemptedRequests = rows.Count, SuccessfulRequests = successes,
            Result = !aborted && successes == 9 ? "PassedHistoricalSourceCheck" : "IncompleteHistoricalSourceCheck",
            OperationalReceipts = 0, CleanEngineeringCohorts = 0, DatabaseConnections = 0,
            Limitations = new[] { "Historical bars cannot prove live publication latency.",
                "Three sampled symbols cannot establish full watchlist or combined collector capacity.",
                "Database durability, scheduler recovery and quote fills were not exercised." },
            Observations = rows
        };
        await JsonSerializer.SerializeAsync(reportFile, report, new JsonSerializerOptions { WriteIndented = true });
        await reportFile.FlushAsync();
        Console.WriteLine($"Result: {report.Result}; {successes}/9 exact historical bars; operational receipts=0; clean cohorts=0.");
        if (aborted) throw new InvalidOperationException("Historical source trial incomplete; see the saved metadata report.");
    }

    private sealed record Observation(string Symbol, TimeOnly EndpointToronto, DateTime RequestStartedUtc,
        DateTime? ReceivedUtc, DateTime ExpectedBarStartUtc, DateTime? ActualBarStartUtc, double ElapsedMs,
        int? ProviderRequestCount, int? ProviderAttemptCount, DateTime? ProviderFetchStartedUtc,
        string Outcome, string? Failure);

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TraderVI.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("The reviewed calendar repository could not be located.");
    }
}
