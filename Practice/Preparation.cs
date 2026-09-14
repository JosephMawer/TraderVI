using Core.Db;
using Core.Indicators.Granville;
using Core.Practice;
using Core.Runtime;
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System.Text.Json;

namespace TraderVI.Practice;

internal sealed record MembershipInput(string SourceReference, string Limitations, IReadOnlyList<DatedDelphiMembership> Snapshots,
    DatedDelphiMembership? ExploratoryCurrentUniverse = null);

/// <summary>Explicit acquisition commands only. Neither Pack nor Run calls this class.</summary>
internal static class Preparation
{
    public static async Task CaptureCurrentUniverseAsync(string outputPath, CancellationToken ct)
    {
        RequireNew(outputPath);
        var equities = await new SymbolsRepository().GetEquitiesAsync();
        var sectors = await new StockSectorRepository().GetAllAsync();
        var membership = new MembershipInput("Current TraderDB eligible equities and sector mapping; static XIU list reviewed " +
            Core.Config.Xiu60Constituents.LastReviewedUtc.ToString("yyyy-MM-dd"),
            "Explicit exploratory current-universe assumption; historical membership/classification changes and survivorship are unverified.",
            [], new(DateTime.UtcNow, equities, sectors, Core.Config.Xiu60Constituents.Symbols));
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(membership, PracticeDataset.Json), ct);
        Console.WriteLine($"Captured {equities.Count} current eligible symbols; no historical dates were substituted.");
    }

    public static async Task ExportDailyAsync(string membershipPath, string calendarPath, string outputPath, CancellationToken ct)
    {
        RequireNew(outputPath);
        var membership = JsonSerializer.Deserialize<MembershipInput>(await File.ReadAllTextAsync(membershipPath, ct), PracticeDataset.Json)
            ?? throw new InvalidDataException("Dated universe membership and its source reference are required.");
        if (string.IsNullOrWhiteSpace(membership.SourceReference) || membership.Snapshots.Any(x => x.AsOf == default) ||
            membership.ExploratoryCurrentUniverse is { AsOf: var captured } && captured == default)
            throw new InvalidDataException("Membership needs explicit dates and source provenance; current membership is not automatically backdated.");
        var calendar = JsonSerializer.Deserialize<ReviewedTsxCalendarDocument>(await File.ReadAllTextAsync(calendarPath, ct), PracticeDataset.Json)
            ?? throw new InvalidDataException("A reviewed calendar is required.");
        _ = new ReviewedTsxSessionCalendar(calendar);
        var (strategy, models) = await DelphiHistoricalPreparation.ReadSelectionAsync(ct);
        var daily = new DelphiHistoricalInputs
        {
            Strategy = strategy, Models = models, Membership = membership.Snapshots.ToList(),
            ExploratoryCurrentUniverse = membership.ExploratoryCurrentUniverse,
            Breadth = await new AdvanceDeclineRepository().GetAllAsync(),
            Leadership = await new LeadershipRepository().GetRecentAsync(20_000),
            Sectors = (await new SectorIndexRepository().GetRecentAsync(TsxSectorSymbols.AllSymbols, 20_000)).ToList(),
            Climax = await new MarketClimaxRepository().GetRecentAsync(20_000)
        };
        var snapshots = membership.ExploratoryCurrentUniverse is { } current ? membership.Snapshots.Append(current) : membership.Snapshots;
        var symbols = snapshots.SelectMany(x => x.Equities.Select(s => s.Symbol).Concat(x.XiuConstituents))
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
            Provenance = new("TraderDB stored daily observations; " + daily.UniverseTimingNote, DateTime.UtcNow,
                "Stored revisions/adjustments unverified", membership.SourceReference,
                membership.Limitations + "; present-day model set; stored-data revision history and training-label timing unverified; built-in Shadow V1 where no runnable saved assignment exists")
        };
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(document, PracticeDataset.Json), ct);
    }

    public static async Task AcquireIntradayAsync(string inputPath, string outputPath, int requestBudget, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<PracticeDatasetDocument>(await File.ReadAllTextAsync(inputPath, ct), PracticeDataset.Json)
            ?? throw new InvalidDataException("Empty practice input.");
        var requests = input.Calendar.RegularSessionDates.Where(d => d >= input.FirstSession && d <= input.LastSession).Order()
            .SelectMany(day => (input.Daily.ExploratoryCurrentUniverse is { } current ? new[] { current } :
                input.Daily.Membership.Where(x => x.AsOf.Date < day.ToDateTime(TimeOnly.MinValue)))
                .SelectMany(x => x.Equities.Select(e => e.Symbol)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
                .SelectMany(symbol => new[] { 5, 15 }.Select(interval => new PracticeIntradayRequest(day, symbol, interval)))).ToArray();
        await AcquireAsync(input, requests, outputPath, requestBudget, "All saved historical membership", ct);
    }

    public static async Task AcquireSelectedAsync(string package, string runDirectory, string outputPath, int requestBudget, CancellationToken ct)
    {
        var dataset = PracticeDataset.Load(package);
        using var checkpoint = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(runDirectory, "checkpoint.json"), ct));
        var root = checkpoint.RootElement;
        if (!root.GetProperty("Completed").GetBoolean() ||
            root.GetProperty("Frame").GetProperty("DatasetIdentity").GetString() != dataset.Identity)
            throw new InvalidDataException("Use the completed daily planning run belonging to this exact package.");
        var daily = root.GetProperty("DailyResults").Deserialize<List<PracticeDailyResult>>(PracticeDataset.Json)!;
        var requests = PracticeAcquisitionPlan.Create(dataset, daily);
        Console.WriteLine($"Acquisition plan: {requests.Count} logical requests across {dataset.Sessions.Count} sessions.");
        await AcquireAsync(dataset.Document, requests, outputPath, requestBudget, dataset.Identity, ct);
    }

    private static async Task AcquireAsync(PracticeDatasetDocument input, IReadOnlyList<PracticeIntradayRequest> requests,
        string outputPath, int requestBudget, string planSource, CancellationToken ct)
    {
        RequireNew(outputPath);
        RequireNew(outputPath + ".acquisition.json");
        if (requestBudget is < 1 or > 20_000) throw new ArgumentOutOfRangeException(nameof(requestBudget), "Choose a logical-request budget from 1 through 20000.");
        var calendar = new ReviewedTsxSessionCalendar(input.Calendar);
        var batches = input.Intraday.ToList();
        var observations = new List<object>();
        int requested = 0;
        using var tmx = new TmxClient();
        try
        {
            foreach (var request in requests)
            {
                var (day, symbol, interval) = request;
                var bounds = calendar.GetSessionBounds(day);
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
                        if (requested % 20 == 0) Console.WriteLine($"Acquisition progress: {requested}/{requests.Count} logical requests completed.");
                        await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        finally
        {
            // Preserve successes and missing observations, including a bounded or cancelled acquisition.
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(input with { Intraday = batches }, PracticeDataset.Json), CancellationToken.None);
            await File.WriteAllTextAsync(outputPath + ".acquisition.json", JsonSerializer.Serialize(new
            { PlanSource = planSource, PlannedRequests = requests, RequestBudget = requestBudget, LogicalRequests = requested, MaximumTransportAttempts = requested * 3,
                BudgetReached = requested >= requestBudget, Cancelled = ct.IsCancellationRequested, Observations = observations }, PracticeDataset.Json), CancellationToken.None);
        }
    }
    private static void RequireNew(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Refusing to replace an existing preparation file.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }
}
