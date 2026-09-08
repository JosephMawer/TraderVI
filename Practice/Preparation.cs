using Core.Db;
using Core.Indicators.Granville;
using Core.Practice;
using Core.Runtime;
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System.Text.Json;

namespace TraderVI.Practice;

internal sealed record MembershipInput(string SourceReference, string Limitations, IReadOnlyList<DatedDelphiMembership> Snapshots);

/// <summary>Explicit acquisition commands only. Neither Pack nor Run calls this class.</summary>
internal static class Preparation
{
    public static async Task ExportDailyAsync(string membershipPath, string calendarPath, string outputPath, CancellationToken ct)
    {
        RequireNew(outputPath);
        var membership = JsonSerializer.Deserialize<MembershipInput>(await File.ReadAllTextAsync(membershipPath, ct), PracticeDataset.Json)
            ?? throw new InvalidDataException("Dated universe membership and its source reference are required.");
        if (string.IsNullOrWhiteSpace(membership.SourceReference) || membership.Snapshots.Any(x => x.AsOf == default))
            throw new InvalidDataException("Membership needs explicit dates and source provenance; current membership is not automatically backdated.");
        var calendar = JsonSerializer.Deserialize<ReviewedTsxCalendarDocument>(await File.ReadAllTextAsync(calendarPath, ct), PracticeDataset.Json)
            ?? throw new InvalidDataException("A reviewed calendar is required.");
        _ = new ReviewedTsxSessionCalendar(calendar);
        var (strategy, models) = await DelphiHistoricalPreparation.ReadSelectionAsync(ct);
        var daily = new DelphiHistoricalInputs
        {
            Strategy = strategy, Models = models, Membership = membership.Snapshots.ToList(),
            Breadth = await new AdvanceDeclineRepository().GetAllAsync(),
            Leadership = await new LeadershipRepository().GetRecentAsync(20_000),
            Sectors = (await new SectorIndexRepository().GetRecentAsync(TsxSectorSymbols.AllSymbols, 20_000)).ToList(),
            Climax = await new MarketClimaxRepository().GetRecentAsync(20_000)
        };
        var symbols = membership.Snapshots.SelectMany(x => x.Equities.Select(s => s.Symbol).Concat(x.XiuConstituents))
            .Concat(["XIU", "SPY"]).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var quotes = new QuoteRepository();
        var obv = new SymbolObvRepository();
        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();
            daily.Daily.Add(symbol, await quotes.GetDailyBarsAsync(symbol));
            daily.Obv.Add(symbol, await obv.GetSeriesAsync(symbol));
        }
        var us = new UsIndexBarsRepository();
        foreach (string symbol in UsIndexSymbols.AllSymbols)
            daily.UsIndices.Add(symbol, (await us.GetBarsAsync(symbol)).ToList());
        var policies = new List<PracticeShadowPolicy>();
        var settings = new EngineSettingsRepository();
        foreach (var portfolio in await new SystemShadowRepository().GetRunnablePortfoliosAsync(ct))
        {
            var assignment = await settings.ReadAsync(portfolio.PortfolioId, EngineStrategySettings.Shadow, Core.Trader.SystemShadowPolicyConfig.Version1);
            policies.Add(new(portfolio.PortfolioCode, assignment.VersionId ?? new Guid("57E94653-57CB-41A8-8A46-98CD9B93B860"), assignment.Settings));
        }
        var document = new PracticeDatasetDocument
        {
            Calendar = calendar, Daily = daily, CoreAssemblySha256 = PracticeDataset.CurrentCoreHash(),
            ShadowPolicyId = new Guid("57E94653-57CB-41A8-8A46-98CD9B93B860"),
            PortfolioPolicies = policies,
            Provenance = new("TraderDB stored daily observations; supplied dated membership", DateTime.UtcNow,
                "Stored revisions/adjustments unverified", membership.SourceReference,
                membership.Limitations + "; present-day model set; stored-data revision history and training-label timing unverified; built-in Shadow V1 where no runnable saved assignment exists")
        };
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(document, PracticeDataset.Json), ct);
    }

    public static async Task AcquireIntradayAsync(string inputPath, string outputPath, int requestBudget, CancellationToken ct)
    {
        RequireNew(outputPath);
        RequireNew(outputPath + ".acquisition.json");
        if (requestBudget is < 1 or > 20_000) throw new ArgumentOutOfRangeException(nameof(requestBudget), "Choose a logical-request budget from 1 through 20000.");
        var input = JsonSerializer.Deserialize<PracticeDatasetDocument>(await File.ReadAllTextAsync(inputPath, ct), PracticeDataset.Json)
            ?? throw new InvalidDataException("Empty practice input.");
        var calendar = new ReviewedTsxSessionCalendar(input.Calendar);
        var batches = input.Intraday.ToList();
        var observations = new List<object>();
        int requested = 0;
        using var tmx = new TmxClient();
        try
        {
            foreach (var day in input.Calendar.RegularSessionDates.Where(d => d >= input.FirstSession && d <= input.LastSession).Order())
            {
                var bounds = calendar.GetSessionBounds(day);
                // Include earlier candidates: a previously selected stock can still be held today.
                var symbols = input.Daily.Membership.Where(x => x.AsOf.Date < day.ToDateTime(TimeOnly.MinValue))
                    .SelectMany(x => x.Equities.Select(e => e.Symbol)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
                foreach (string symbol in symbols)
                    foreach (int interval in new[] { 5, 15 })
                    {
                        if (requested >= requestBudget) return;
                        if (batches.Any(x => x.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) && x.IntervalMinutes == interval && x.RequestedStartUtc == bounds.OpenUtc)) continue;
                        ct.ThrowIfCancellationRequested();
                        requested++;
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(20));
                        try
                        {
                            var batch = await tmx.GetIntradayTimeSeriesBatchAsync(symbol, interval, bounds.OpenUtc, bounds.CloseUtc, timeout.Token);
                            batches.Add(batch);
                            observations.Add(new { day, symbol, interval, Status = "Observed", Count = batch.Bars.Count, batch.AttemptCount, batch.ReceivedUtc });
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException)
                        {
                            observations.Add(new { day, symbol, interval, Status = "Unavailable", Error = ex.GetType().Name, ReceivedUtc = DateTime.UtcNow });
                        }
                        await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    }
            }
        }
        finally
        {
            // Preserve successes and missing observations, including a bounded or cancelled acquisition.
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(input with { Intraday = batches }, PracticeDataset.Json), CancellationToken.None);
            await File.WriteAllTextAsync(outputPath + ".acquisition.json", JsonSerializer.Serialize(new
            { RequestBudget = requestBudget, LogicalRequests = requested, MaximumTransportAttempts = requested * 3,
                BudgetReached = requested >= requestBudget, Cancelled = ct.IsCancellationRequested, Observations = observations }, PracticeDataset.Json), CancellationToken.None);
        }
    }
    private static void RequireNew(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Refusing to replace an existing preparation file.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }
}
