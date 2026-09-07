#nullable enable
using Core.Db;
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Explicit September 4 research replay: SELECT-only SQL, up to 58 sequential historical
/// TMX reads (29 symbols including XIU, five- and one-minute intervals; existing transport
/// allows up to three attempts each). Cached complete symbols are reused. Thirty seconds
/// per logical request, ten minutes overall. No quotes, activation, ledger writes or broker.
/// Outputs are private ignored local artifacts, not operational receipts or training data.
/// </summary>
public sealed class DelphiLiveFridayReplayProbe : IProbe
{
    public string Slug => "delphi-live-friday-replay";
    public string Description => "Replay September 4 picks and estimated trades using saved Shadow capital; local artifacts only.";
    public async Task RunAsync()
    {
        var date = new DateOnly(2026, 9, 4);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TraderVI.sln"))) root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException("Repository root is required.");
        var calendar = ReviewedTsxSessionCalendar.Load(Path.Combine(root.FullName, "Operations/Calendars/tsx-2026-through-20261223-v1.json"));
        var bounds = calendar.GetSessionBounds(date);
        if (DateTime.UtcNow <= bounds.CloseUtc) throw new InvalidOperationException("The selected historical session must be complete.");
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var repository = new DelphiLivePreviewRepository();
        var preview = await repository.ReadAsync(date, budget.Token);
        if (preview.Run is null || preview.Picks.Count > 28 || preview.Picks.Count == 0)
            throw new InvalidOperationException("The pinned Friday published source is missing or exceeds the reviewed request budget.");
        var baselines = await repository.ReadBaselinesAsync(preview, calendar, budget.Token);
        decimal capital = await repository.ReadShadowStartingCapitalAsync(budget.Token);
        string directory = Path.Combine(root.FullName, "artifacts/delphi-live-replays");
        Directory.CreateDirectory(directory);
        string cachePath = Path.Combine(directory, $"20260904-{preview.Run.RunId:N}-inputs.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        var sources = new List<DelphiLiveReplaySource>();
        if (File.Exists(cachePath))
        {
            var cached = JsonSerializer.Deserialize<SourceCache>(await File.ReadAllTextAsync(cachePath, budget.Token), options)
                ?? throw new InvalidOperationException("Unrecognized historical source cache.");
            if (cached.Kind != "HistoricalReplaySourceCacheV1" || cached.DailyRunId != preview.Run.RunId || cached.SessionDate != date)
                throw new InvalidOperationException("Historical source cache identity mismatch.");
            sources.AddRange(cached.Sources);
        }
        Console.WriteLine($"Friday replay: {preview.Picks.Count} published symbols, {preview.Picks.Count(p => p.Eligible)} eligible, plus XIU. Saved Shadow capital selected.");
        using var client = new TmxClient();
        int requests = 0;
        foreach (string symbol in preview.Picks.Select(p => p.Symbol).Append("XIU").Distinct().Order(StringComparer.Ordinal))
        {
            if (sources.Any(s => s.Symbol == symbol)) continue;
            var five = await Fetch(5);
            await Task.Delay(500, budget.Token);
            var minute = await Fetch(1);
            sources.Add(new(symbol, DateTime.UtcNow, five, minute));
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(new SourceCache("HistoricalReplaySourceCacheV1",
                date, preview.Run.RunId, sources), options), budget.Token);
            Console.WriteLine($"Historical sources cached: {sources.Count}/{preview.Picks.Count + 1}; latest coverage {five.Count}/78 five-minute and {minute.Count}/390 minute bars.");
            await Task.Delay(500, budget.Token);

            async Task<IReadOnlyList<DelphiLiveReplayBar>> Fetch(int interval)
            {
                if (++requests > 58) throw new InvalidOperationException("Historical request budget exhausted.");
                using var requestBudget = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                requestBudget.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    var batch = await client.GetIntradayTimeSeriesBatchAsync(symbol, interval, bounds.OpenUtc, bounds.CloseUtc, requestBudget.Token);
                    return batch.Bars.Where(b => b.TimestampUtc >= bounds.OpenUtc && b.TimestampUtc.AddMinutes(interval) <= bounds.CloseUtc)
                        .Select(b => new DelphiLiveReplayBar(b.TimestampUtc, Convert.ToDecimal(b.Open), Convert.ToDecimal(b.High),
                            Convert.ToDecimal(b.Low), Convert.ToDecimal(b.Close), b.Volume)).ToArray();
                }
                catch (Exception error)
                {
                    // Provider messages may contain private payloads; preserve only the exception type.
                    throw new InvalidOperationException($"Historical request failed ({error.GetType().Name}); cached completed symbols remain available.");
                }
            }
        }
        var report = DelphiLiveHistoricalReplay.Run(preview, baselines, sources, calendar, capital, DateTime.UtcNow);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        string reportPath = Path.Combine(directory, $"{stamp}-report.json");
        await File.WriteAllTextAsync(Path.Combine(directory, $"{stamp}-baseline.json"), JsonSerializer.Serialize(new
        {
            Kind = "HistoricalReplayDailyInputsV1", preview, baselines, CapitalSource = "Latest saved System Shadow account total", capital,
            report.ExecutionConvention, report.Assumptions
        }, options), budget.Token);
        await using var output = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(output, report, options, budget.Token);
        Console.WriteLine($"Replay complete: {report.Frames.Count} checkpoints; {report.Trades.Count(t => t.FilledUtc.HasValue)} estimated fills. No operational writes.");
        Console.WriteLine($"Coverage: {report.AvailableFiveMinuteBars}/{report.ExpectedFiveMinuteBars} five-minute bars; {report.AvailableMinuteBars}/{report.ExpectedMinuteBars} minute bars.");
        Console.WriteLine($"Report: {reportPath}");
    }
    private sealed record SourceCache(string Kind, DateOnly SessionDate, Guid DailyRunId, IReadOnlyList<DelphiLiveReplaySource> Sources);
}
