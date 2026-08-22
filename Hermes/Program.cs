using Core.Db;
using Core.Indicators;
using Core.Indicators.Granville;
using Core.Indicators.Models;
using Core.ML;
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("=== Hermes: Market Data Collector ===\n");

Console.WriteLine("[Backfill Mode] Downloading historical data...");
await RunBackfillAsync();

try
{
    Console.WriteLine("\n── Post-Hermes Database Backup ──\n");

    var backupPaths = TraderDbBackupPaths.FromEnvironment();
    var backupService = new TraderDbBackupService(SQLBase.Database, backupPaths);
    var backup = await backupService.CreateAndReplicateAsync(
        message => Console.WriteLine($"  {message}"));

    Console.WriteLine($"  Backup completed: {backup.SizeBytes / 1048576.0:F2} MB");
    Console.WriteLine($"  Staging: {backup.StagingFile}");
    Console.WriteLine($"  OneDrive: {backup.DestinationFile}");
    Console.WriteLine($"  SHA-256: {backup.Sha256}");
    Console.WriteLine("  Backup protection complete ✓\n");
}
catch (Exception ex)
{
    Console.Error.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
    Console.Error.WriteLine("║ DATA UPDATE COMPLETED, BUT THE DATABASE BACKUP FAILED.       ║");
    Console.Error.WriteLine("╚══════════════════════════════════════════════════════════════╝");
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("TraderDB contains the completed data update, but this run is not protected by a new off-machine backup.");
    Environment.ExitCode = 2;
}

// ── One-time A/D Line backfill (uncomment to rebuild from scratch) ──
// await BackfillAdvanceDeclineLineAsync(months: 6);

static async Task RunBackfillAsync()
{
    var tmx = new TmxClient();
    var repository = new QuoteRepository();

    // Backfill parameters
    var defaultStartDate = new DateTime(2020, 1, 1); // Adjust as needed
    var endDate = DateTime.Today.AddDays(-1);        // Up to yesterday

    // Get all TSX constituents
    var db = new SymbolsRepository();
    var constituents = await db.GetSymbols();

    Console.WriteLine($"Backfilling {constituents.Count} symbols up to {endDate:yyyy-MM-dd}");
    Console.WriteLine($"Estimated time: ~{constituents.Count * 0.5 / 60:F1} minutes\n");

    int processed = 0;
    int failed = 0;
    int totalBarsInserted = 0;

    foreach (var constituent in constituents)
    {
        try
        {
            Console.Write($"[{processed + 1}/{constituents.Count}] {constituent.Symbol,-10} ");

            var latestDate = await repository.GetLatestDailyBarDateAsync(constituent.Symbol);

            var startDate = latestDate.HasValue
                ? latestDate.Value.Date.AddDays(1)
                : defaultStartDate;

            if (startDate > endDate)
            {
                Console.WriteLine("✓ Up-to-date");
                processed++;
                continue;
            }

            var dailyBars = await tmx.GetHistoricalTimeSeriesAsync(
                symbol: constituent.Symbol,
                freq: "day",
                startDate: startDate.ToString("yyyy-MM-dd"),
                endDate: endDate.ToString("yyyy-MM-dd")
            );

            if (dailyBars == null || dailyBars.Count == 0)
            {
                Console.WriteLine("⚠️  No data");
                processed++;
                continue;
            }

            await repository.InsertDailyBarsAsync(constituent.Symbol, dailyBars);

            totalBarsInserted += dailyBars.Count;
            Console.WriteLine($"✓ {dailyBars.Count,4} bars ({startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd})");
            processed++;

            await Task.Delay(500); // 2 req/sec
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine($"\n{'=',-50}");
    Console.WriteLine("Backfill Complete:");
    Console.WriteLine($"  Symbols processed: {processed}");
    Console.WriteLine($"  Failed: {failed}");
    Console.WriteLine($"  Total bars inserted: {totalBarsInserted:N0}");
    Console.WriteLine($"{'=',-50}");

    // ═══════════════════════════════════════════════════════════════════
    // UPDATE ADVANCE-DECLINE LINE
    // ═══════════════════════════════════════════════════════════════════
    await UpdateAdvanceDeclineLineAsync(repository, constituents);

    // ──────────────────────────────────────────────────────────────────
    // UPDATE PER-SYMBOL ON-BALANCE VOLUME (OBV) — Granville field trend
    // Incremental + gap-safe; prunes to the rolling retention window.
    // Foundation for the upcoming market-wide Climax indicator.
    // ──────────────────────────────────────────────────────────────────
    await UpdateObvAsync(repository, constituents);

    // UPDATE MARKET CLIMAX (CLX) -- Granville's market-wide net OBV-breakout tally.
    // Sibling to the A/D Line: counts UP vs DOWN OBV designations across the XIU-60 leaders.
    // Diagnostic-only (v1): persists CLX + XIU close so Delphi can report confirmation/divergence.
    // Runs after OBV so every name's latest designation is already up to date.
    await UpdateMarketClimaxAsync(repository);

    // ═══════════════════════════════════════════════════════════════════
    // BACKFILL SECTOR INDEX HISTORY (TMX getTimeSeriesData)
    // Fills dbo.SectorIndices with multi-year daily history per ^TT* symbol
    // so Delphi RS composites (60d + 20d Z window) have enough bars to compute.
    // Safe to run daily — resumes from each symbol's last stored date.
    // ═══════════════════════════════════════════════════════════════════
    await BackfillSectorIndexHistoryAsync(tmx);

    // ═══════════════════════════════════════════════════════════════════
    // UPDATE SECTOR INDICES (today's snapshot)
    // ═══════════════════════════════════════════════════════════════════
    await UpdateSectorIndicesAsync(tmx);

    // ═══════════════════════════════════════════════════════════════════
    // REFRESH STOCK → SECTOR MAP (weekly staleness check)
    // ═══════════════════════════════════════════════════════════════════
    await RefreshStockSectorMapIfStaleAsync(tmx, TimeSpan.FromDays(7));

    // ═══════════════════════════════════════════════════════════════════
    // UPDATE LEADERSHIP DATA (Granville #7–#10)
    // ═══════════════════════════════════════════════════════════════════
    await UpdateLeadershipDataAsync(tmx, repository, constituents);

    // ═══════════════════════════════════════════════════════════════════
    // UPDATE US INDEX BARS (Granville #17–#20 Genuity)
    // ═══════════════════════════════════════════════════════════════════
    // Sourced from Yahoo Finance's chart endpoint (see ADR-0004). TMX does
    // not return OHLC for ^GSPC:US / ^NYA:US despite recognizing the symbols.
    await UpdateUsIndexBarsAsync(backfillYearsIfEmpty: 10);
}

static async Task UpdateUsIndexBarsAsync(int backfillYearsIfEmpty)
{
    Console.WriteLine("\n── US Index Bars Update (Genuity #17–#20) ──\n");

    var repo = new UsIndexBarsRepository();
    using var source = new YahooChartUsIndexDataSource();

    var endDate = DateTime.Today;

    foreach (var symbol in UsIndexSymbols.AllSymbols)
    {
        try
        {
            var latest = await repo.GetLatestBarDateAsync(symbol);

            DateTime startDate = latest.HasValue
                ? latest.Value.Date.AddDays(1)
                : endDate.AddYears(-backfillYearsIfEmpty);

            if (startDate > endDate)
            {
                Console.WriteLine($"  {symbol,-8} ✓ Up-to-date (latest {latest:yyyy-MM-dd})");
                continue;
            }

            var bars = await source.GetDailyBarsAsync(symbol, startDate, endDate);

            // Defensive: don't insert bars older than `startDate` (Yahoo can return a
            // wider window than asked for); also filter same-day intraday previews.
            var clean = bars
                .Where(b => b.Date >= startDate && b.Date <= endDate)
                .ToList();

            if (clean.Count == 0)
            {
                Console.WriteLine($"  {symbol,-8} ⚠️  No new bars ({startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd})");
                continue;
            }

            await repo.UpsertBarsAsync(clean);

            var last = clean[^1];
            string scope = latest.HasValue ? "incremental" : $"{backfillYearsIfEmpty}y backfill";
            Console.WriteLine(
                $"  {symbol,-8} ✓ {clean.Count,4} bars [{scope}] " +
                $"({clean[0].Date:yyyy-MM-dd}..{last.Date:yyyy-MM-dd}, close={last.Close:F2})");

            await Task.Delay(300); // be polite to Yahoo
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {symbol,-8} ✗ {ex.Message}");
        }
    }
}

static async Task UpdateAdvanceDeclineLineAsync(
    QuoteRepository repository,
    List<SymbolInfo> constituents)
{
    Console.WriteLine("\n── Advance-Decline Line Update ──\n");

    var adRepo = new AdvanceDeclineRepository();
    var (lastDate, lastCumulative) = await adRepo.GetLastCumulativeAsync();

    if (lastDate.HasValue)
    {
        Console.WriteLine($"Last stored A/D entry: {lastDate.Value:yyyy-MM-dd} (cumulative: {lastCumulative:+#,0;-#,0;0})");
    }
    else
    {
        Console.WriteLine("No existing A/D data — run BackfillAdvanceDeclineLineAsync first.");
        Console.WriteLine("Skipping A/D Line update.\n");
        return;
    }

    // Load bars starting 5 calendar days BEFORE the last stored date so that
    // every new day has a prior close available for advance/decline comparison.
    // Without this lookback, the first new day would be silently skipped.
    var dataLoadStart = lastDate.Value.AddDays(-5);
    var computeFromDate = lastDate.Value.AddDays(1);

    if (computeFromDate > DateTime.Today.AddDays(-1))
    {
        Console.WriteLine("A/D Line is already up-to-date. ✓\n");
        return;
    }

    Console.WriteLine($"Computing from {computeFromDate:yyyy-MM-dd} (loading bars from {dataLoadStart:yyyy-MM-dd} for prior close)");

    // Load XIU benchmark
    var xiuBars = await repository.GetDailyBarsAsync("XIU", dataLoadStart);
    if (xiuBars.Count == 0)
    {
        Console.WriteLine("⚠️  No XIU data for this range. Skipping A/D Line update.\n");
        return;
    }

    // Load all symbols' bars with lookback
    var allBars = new Dictionary<string, IReadOnlyList<DailyBar>>(StringComparer.OrdinalIgnoreCase);
    int loaded = 0;

    foreach (var constituent in constituents)
    {
        var bars = await repository.GetDailyBarsAsync(constituent.Symbol, dataLoadStart);
        if (bars.Count >= 2)
        {
            allBars[constituent.Symbol] = bars;
            loaded++;
        }
    }

    Console.WriteLine($"Loaded {loaded} symbols for A/D calculation");

    // The lookback days prime priorCloseBySymbol only. Accumulation and output
    // start at computeFromDate so stored pluralities are not counted twice.
    var newEntries = AdvanceDeclineCalculator.Compute(
        allBars,
        xiuBars,
        lastCumulative,
        accumulateFromDate: computeFromDate);

    if (newEntries.Count == 0)
    {
        Console.WriteLine("No new trading days to add. ✓\n");
        return;
    }

    // Preview
    Console.WriteLine($"\n{"Date",-12} {"Adv",5} {"Dec",5} {"Plurality",10} {"Cumulative",11} {"XIU",9}");
    Console.WriteLine(new string('─', 56));
    foreach (var entry in newEntries.TakeLast(10))
    {
        Console.WriteLine(
            $"{entry.Date:yyyy-MM-dd}  {entry.Advancers,5} {entry.Decliners,5} {entry.DailyPlurality,10} {entry.CumulativeDifferential,11} {(entry.XiuClose.HasValue ? $"{entry.XiuClose.Value,9:F2}" : "     N/A")}");
    }

    await adRepo.UpsertAsync(newEntries);

    var last = newEntries[^1];
    Console.WriteLine($"\nA/D Line updated: +{newEntries.Count} entries → {last.Date:yyyy-MM-dd} (cumulative: {last.CumulativeDifferential:+#,0;-#,0;0}) ✓\n");
}

static async Task UpdateObvAsync(
    QuoteRepository repository,
    List<SymbolInfo> constituents)
{
    Console.WriteLine("\n── On-Balance Volume (OBV) Update ──\n");

    var obvRepo = new SymbolObvRepository();
    var retentionCutoff = DateTime.Today.AddMonths(-Core.Constants.ObvRetentionMonths);

    int updated = 0;
    int pointsAdded = 0;
    int skipped = 0;

    foreach (var constituent in constituents)
    {
        var symbol = constituent.Symbol;
        var (lastDate, lastObv) = await obvRepo.GetLatestAsync(symbol);

        List<OBV> newPoints;

        if (lastDate.HasValue)
        {
            // Incremental: load from the last stored date inclusive so its close
            // seeds the comparison, then extend the cumulative over every newer
            // session (this fills multi-day gaps in a single pass).
            var bars = await repository.GetDailyBarsAsync(symbol, lastDate.Value);
            var newBars = bars.Where(b => b.Date.Date > lastDate.Value.Date).ToList();
            if (newBars.Count == 0)
            {
                skipped++;
                continue;
            }

            var seedBar = bars.FirstOrDefault(b => b.Date.Date == lastDate.Value.Date);
            decimal? seedPrevClose = seedBar is null ? null : (decimal)seedBar.Close;

            newPoints = newBars.CalculateOBV(seedObv: lastObv, seedPrevClose: seedPrevClose);
        }
        else
        {
            // Fresh: build a new chain over the retention window from a 0 anchor.
            var bars = await repository.GetDailyBarsAsync(symbol, retentionCutoff);
            if (bars.Count == 0)
            {
                skipped++;
                continue;
            }
            newPoints = bars.CalculateOBV();
        }

        if (newPoints.Count == 0)
        {
            skipped++;
            continue;
        }

        await obvRepo.UpsertAsync(symbol, newPoints);
        updated++;
        pointsAdded += newPoints.Count;
    }

    // Enforce the rolling retention window. Safe because the running cumulative is
    // already baked into the retained rows — pruning the tail never alters the head.
    int pruned = await obvRepo.PruneOlderThanAsync(retentionCutoff);

    Console.WriteLine(
        $"OBV updated: {updated} symbols (+{pointsAdded:N0} points), {skipped} up-to-date/empty, " +
        $"pruned {pruned:N0} rows older than {retentionCutoff:yyyy-MM-dd}. ✓\n");
}

static async Task UpdateMarketClimaxAsync(QuoteRepository repository)
{
    Console.WriteLine("\n-- Market Climax (CLX) Update --\n");

    var climaxRepo = new MarketClimaxRepository();
    var obvRepo = new SymbolObvRepository();

    // CLX reads each XIU-60 leader's stored OBV series (maintained by UpdateObvAsync above)
    // and tallies UP vs DOWN field-trend designations. We only need the OBV retention window.
    var seriesStart = DateTime.Today.AddMonths(-Core.Constants.ObvRetentionMonths);

    var seriesBySymbol = new Dictionary<string, IReadOnlyList<OBV>>(StringComparer.OrdinalIgnoreCase);
    DateTime? clxDate = null;

    foreach (var symbol in Xiu60Constituents.Symbols)
    {
        var series = await obvRepo.GetSeriesFromDateAsync(symbol, seriesStart);
        if (series.Count == 0) continue;

        seriesBySymbol[symbol] = series;

        var last = series[^1].Date;
        if (clxDate is null || last > clxDate.Value)
            clxDate = last;
    }

    if (seriesBySymbol.Count < Core.Constants.ClimaxMinConstituents)
    {
        Console.WriteLine(
            $"Only {seriesBySymbol.Count} XIU-60 names have OBV series (need >= {Core.Constants.ClimaxMinConstituents}). " +
            "Skipping CLX update -- run OBV backfill first.\n");
        return;
    }

    var asOf = clxDate!.Value;

    // XIU benchmark close on (or just before) the CLX date, for divergence analysis.
    var xiuBars = await repository.GetDailyBarsAsync("XIU", seriesStart);
    var xiuBar = xiuBars.LastOrDefault(b => b.Date.Date <= asOf.Date);
    float? xiuClose = xiuBar is null ? null : (float)xiuBar.Close;

    var entry = MarketClimaxCalculator.ComputeForDate(
        seriesBySymbol, asOf, Core.Constants.ClimaxBreakoutWindow, xiuClose);

    await climaxRepo.UpsertAsync([entry]);

    Console.WriteLine(
        $"CLX {asOf:yyyy-MM-dd}: {entry.Clx:+0;-0;0} " +
        $"({entry.UpBreakouts} up / {entry.DownBreakouts} down, covered {entry.Covered}/{entry.BasketSize}), " +
        $"fresh +{entry.FreshUp}/-{entry.FreshDown}" +
        $"{(xiuClose.HasValue ? $", XIU {xiuClose.Value:F2}" : "")}.\n");
}

static async Task BackfillAdvanceDeclineLineAsync(int months = 6)
{
    Console.WriteLine($"=== A/D Line Backfill ({months} months) ===\n");

    var repository = new QuoteRepository();
    var symbolsDb = new SymbolsRepository();
    var adRepo = new AdvanceDeclineRepository();

    var constituents = await symbolsDb.GetSymbols();
    Console.WriteLine($"Universe: {constituents.Count} symbols");

    // Backfill window: go back N months from yesterday
    var endDate = DateTime.Today.AddDays(-1);
    var backfillStart = endDate.AddMonths(-months);

    // Load bars starting 1 extra trading day before the window so that the
    // first day in the range has a prior close to compare against.
    // Using 5 calendar days covers weekends/holidays safely.
    var dataLoadStart = backfillStart.AddDays(-5);

    Console.WriteLine($"Window:   {backfillStart:yyyy-MM-dd} → {endDate:yyyy-MM-dd}");
    Console.WriteLine($"Loading bars from {dataLoadStart:yyyy-MM-dd} (extra lookback for prior close)\n");

    // Load XIU benchmark bars
    var xiuBars = await repository.GetDailyBarsAsync("XIU", dataLoadStart);
    Console.WriteLine($"XIU bars loaded: {xiuBars.Count}");

    if (xiuBars.Count == 0)
    {
        Console.WriteLine("✗ No XIU data found. Run the OHLCV backfill first.");
        return;
    }

    // Load all symbols' bars for the window (with lookback)
    var allBars = new Dictionary<string, IReadOnlyList<DailyBar>>(StringComparer.OrdinalIgnoreCase);
    int loaded = 0;
    int skipped = 0;

    foreach (var constituent in constituents)
    {
        var bars = await repository.GetDailyBarsAsync(constituent.Symbol, dataLoadStart);

        if (bars.Count >= 2) // need at least 2 bars to determine advance/decline
        {
            allBars[constituent.Symbol] = bars;
            loaded++;
        }
        else
        {
            skipped++;
        }
    }

    Console.WriteLine($"Symbols loaded: {loaded}, skipped (insufficient data): {skipped}\n");

    if (allBars.Count == 0)
    {
        Console.WriteLine("✗ No symbol data found. Run the OHLCV backfill first.");
        return;
    }

    // Compute the A/D Line from scratch (cumulative starts at 0 for a clean backfill)
    Console.WriteLine("Computing A/D Line...");
    var adLine = AdvanceDeclineCalculator.Compute(allBars, xiuBars, previousCumulative: 0);

    if (adLine.Count == 0)
    {
        Console.WriteLine("✗ No A/D Line entries computed. Check that DailyBars has data in this range.");
        return;
    }

    // Show a sample of the Granville table
    Console.WriteLine($"\nComputed {adLine.Count} trading days\n");
    Console.WriteLine($"{"Date",-12} {"Adv",5} {"Dec",5} {"Unch",5} {"Plurality",10} {"Cumulative",11} {"XIU",9}");
    Console.WriteLine(new string('─', 62));

    // First 5 + last 5
    var sample = adLine.Take(5).Concat(adLine.TakeLast(5)).Distinct().ToList();
    bool ellipsisPrinted = false;
    foreach (var entry in sample)
    {
        if (!ellipsisPrinted && entry == adLine[^5] && adLine.Count > 10)
        {
            Console.WriteLine($"  {"...",-58}");
            ellipsisPrinted = true;
        }

        Console.WriteLine(
            $"{entry.Date:yyyy-MM-dd}  {entry.Advancers,5} {entry.Decliners,5} {entry.Unchanged,5} {entry.DailyPlurality,+10} {entry.CumulativeDifferential,11} {(entry.XiuClose.HasValue ? $"{entry.XiuClose.Value,9:F2}" : "     N/A")}");
    }

    // Upsert to database
    Console.WriteLine($"\nWriting {adLine.Count} entries to [dbo].[AdvanceDeclineLine]...");
    await adRepo.UpsertAsync(adLine);

    var last = adLine[^1];
    Console.WriteLine($"\n{'=',-62}");
    Console.WriteLine("A/D Line Backfill Complete:");
    Console.WriteLine($"  Date range:   {adLine[0].Date:yyyy-MM-dd} → {last.Date:yyyy-MM-dd}");
    Console.WriteLine($"  Total days:   {adLine.Count}");
    Console.WriteLine($"  Final value:  {last.CumulativeDifferential:+#,0;-#,0;0}");
    Console.WriteLine($"  Last XIU:     {(last.XiuClose.HasValue ? $"{last.XiuClose.Value:F2}" : "N/A")}");
    Console.WriteLine($"{'=',-62}");
}

// ═══════════════════════════════════════════════════════════════════
// SECTOR INDICES
// ═══════════════════════════════════════════════════════════════════

static async Task BackfillSectorIndexHistoryAsync(TmxClient tmx)
{
    Console.WriteLine("\n── Sector Index Historical Backfill ──\n");

    var repo = new SectorIndexRepository();

    // TMX getTimeSeriesData has been validated (Sandbox probe `tmx-sector-history`)
    // to return ~754 daily bars over a 3y window for all 11 ^TT* sector symbols.
    var defaultStartDate = new DateTime(2020, 1, 1);
    var endDate = DateTime.Today.AddDays(-1);

    int totalInserted = 0;
    int symbolsProcessed = 0;
    int symbolsFailed = 0;

    foreach (var kvp in TsxSectorSymbols.All)
    {
        var symbol = kvp.Key;
        var sectorName = kvp.Value;

        try
        {
            var (count, earliest, latest) = await repo.GetCoverageAsync(symbol);

            // Decide between full backfill and incremental resume.
            //  - Full backfill: < 100 stored bars OR earliest is later than defaultStartDate + 30d
            //    (covers the case where a daily snapshot updater seeded only recent rows).
            //  - Incremental:   resume from latest + 1.
            bool needsFullBackfill =
                count < 100 ||
                !earliest.HasValue ||
                earliest.Value.Date > defaultStartDate.AddDays(30);

            DateTime startDate;
            if (needsFullBackfill)
            {
                startDate = defaultStartDate;
            }
            else
            {
                startDate = latest!.Value.Date.AddDays(1);
                if (startDate > endDate)
                {
                    Console.WriteLine($"  {symbol,-6} {sectorName,-24} ✓ Up-to-date ({latest:yyyy-MM-dd}, {count} bars)");
                    symbolsProcessed++;
                    continue;
                }
            }

            var bars = await tmx.GetHistoricalTimeSeriesAsync(
                symbol: symbol,
                freq: "day",
                startDate: startDate.ToString("yyyy-MM-dd"),
                endDate: endDate.ToString("yyyy-MM-dd"));

            if (bars == null || bars.Count == 0)
            {
                Console.WriteLine($"  {symbol,-6} {sectorName,-24} ⚠️  No bars returned (start={startDate:yyyy-MM-dd})");
                symbolsProcessed++;
                continue;
            }

            // Sort ascending and seed previous-close from DB to compute change/%change
            // continuously across the splice point.
            var ordered = bars.OrderBy(b => b.TimestampUtc).ToList();
            var prevClose = await repo.GetLatestCloseBeforeAsync(symbol, ordered[0].TimestampUtc);

            var snapshots = new List<SectorIndexSnapshot>(ordered.Count);
            foreach (var b in ordered)
            {
                var close = b.Close;
                decimal priceChange = prevClose.HasValue ? close - prevClose.Value : 0m;
                decimal percentChange = prevClose is { } p && p != 0m
                    ? (close - p) / p * 100m
                    : 0m;

                snapshots.Add(new SectorIndexSnapshot(
                    Symbol: symbol,
                    SectorName: sectorName,
                    Price: close,
                    PriceChange: priceChange,
                    PercentChange: percentChange,
                    Date: b.TimestampUtc.Date));

                prevClose = close;
            }

            await repo.UpsertAsync(snapshots);
            totalInserted += snapshots.Count;
            symbolsProcessed++;

            var mode = needsFullBackfill ? "FULL" : "incr";
            Console.WriteLine(
                $"  {symbol,-6} {sectorName,-24} ✓ {snapshots.Count,4} bars [{mode}] " +
                $"({ordered[0].TimestampUtc:yyyy-MM-dd}..{ordered[^1].TimestampUtc:yyyy-MM-dd})");

            await Task.Delay(500); // ~2 req/sec, matches constituent backfill
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {symbol,-6} {sectorName,-24} ✗ {ex.Message}");
            symbolsFailed++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Sector backfill: {symbolsProcessed}/{TsxSectorSymbols.All.Count} symbols, " +
                      $"{totalInserted:N0} bars inserted/updated, {symbolsFailed} failed.\n");
}

static async Task UpdateSectorIndicesAsync(TmxClient tmx)
{
    Console.WriteLine("\n── Sector Index Update ──\n");

    var repo = new SectorIndexRepository();
    var lastDate = await repo.GetLatestDateAsync();

    if (lastDate.HasValue)
        Console.WriteLine($"Last stored sector data: {lastDate.Value:yyyy-MM-dd}");
    else
        Console.WriteLine("No existing sector index data.");

    // Determine trading date from the latest bar in the quote DB
    // (the backfill just ran, so this reflects the most recent trading day)
    var quoteRepo = new QuoteRepository();
    var latestBarDate = await quoteRepo.GetLatestDailyBarDateAsync("XIU");
    var tradingDate = latestBarDate ?? DateTime.Today;

    // Skip if already collected for this trading date
    if (lastDate.HasValue && lastDate.Value.Date >= tradingDate.Date)
    {
        Console.WriteLine($"Sector indices already up-to-date for {tradingDate:yyyy-MM-dd}. ✓\n");
        return;
    }

    try
    {
        var snapshots = await tmx.GetSectorIndicesAsync(tradingDate: tradingDate);

        if (snapshots.Count == 0)
        {
            Console.WriteLine("⚠️  No sector index data returned from TMX.\n");
            return;
        }

        Console.WriteLine($"{"Sector",-16} {"Symbol",-8} {"Price",10} {"Change",8} {"%Change",8}");
        Console.WriteLine(new string('─', 54));
        foreach (var s in snapshots)
        {
            Console.WriteLine($"{s.SectorName,-16} {s.Symbol,-8} {s.Price,10:F2} {s.PriceChange,8:+0.00;-0.00} {s.PercentChange,7:+0.00;-0.00}%");
        }

        await repo.UpsertAsync(snapshots);
        Console.WriteLine($"\nSector indices stored: {snapshots.Count} entries for {tradingDate:yyyy-MM-dd} ✓\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Sector index collection failed: {ex.Message}\n");
    }
}

// ═══════════════════════════════════════════════════════════════════
// Call from RunBackfillAsync() — run weekly or on-demand since
// sector metadata rarely changes:
//   await RefreshStockSectorMapAsync(tmx);
// ═══════════════════════════════════════════════════════════════════

static async Task RefreshStockSectorMapAsync(TmxClient tmx)
{
    Console.WriteLine("\n── Stock → Sector Map Refresh ──\n");

    var symbolsDb = new SymbolsRepository();
    var sectorRepo = new StockSectorRepository();
    var constituents = await symbolsDb.GetEquitiesAsync();

    Console.WriteLine($"Refreshing sector metadata for {constituents.Count} equities...\n");

    var mappings = new List<StockSectorMapping>();
    int processed = 0;
    int failed = 0;
    int unmapped = 0;

    foreach (var stock in constituents)
    {
        try
        {
            Console.Write($"[{processed + 1}/{constituents.Count}] {stock.Symbol,-10} ");

            var detail = await tmx.GetQuoteDetailAsync(stock.Symbol);
            var sector = detail.sector?.Trim() ?? "";
            var industry = detail.industry?.Trim();

            TsxSectorMap.TryGetSectorIndex(sector, out var sectorIndexSymbol);

            mappings.Add(new StockSectorMapping(
                Symbol: stock.Symbol,
                Sector: string.IsNullOrEmpty(sector) ? "Unknown" : sector,
                Industry: string.IsNullOrEmpty(industry) ? null : industry,
                SectorIndexSymbol: sectorIndexSymbol,
                LastUpdated: DateTime.UtcNow));

            if (sectorIndexSymbol == null)
            {
                Console.WriteLine($"⚠️  {sector,-25} → (unmapped)");
                unmapped++;
            }
            else
            {
                Console.WriteLine($"✓ {sector,-25} → {sectorIndexSymbol}");
            }

            processed++;
            await Task.Delay(500); // respect rate limits
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ {ex.Message}");
            failed++;
        }
    }

    if (mappings.Count > 0)
        await sectorRepo.UpsertAsync(mappings);

    Console.WriteLine($"\n{'=',-60}");
    Console.WriteLine("Sector Map Refresh Complete:");
    Console.WriteLine($"  Processed: {processed}");
    Console.WriteLine($"  Mapped:    {processed - unmapped - failed}");
    Console.WriteLine($"  Unmapped:  {unmapped} (sectors with no TSX index)");
    Console.WriteLine($"  Failed:    {failed}");
    Console.WriteLine($"{'=',-60}\n");

    // Report unmapped sectors for manual review
    if (unmapped > 0)
    {
        var unmappedSectors = mappings
            .Where(m => m.SectorIndexSymbol == null)
            .Select(m => m.Sector)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s);

        Console.WriteLine("Unmapped sectors (add to TsxSectorMap if an index exists):");
        foreach (var s in unmappedSectors)
            Console.WriteLine($"  • {s}");
        Console.WriteLine();
    }
}

static async Task RefreshStockSectorMapIfStaleAsync(TmxClient tmx, TimeSpan maxAge)
{
    Console.WriteLine("\n── Stock → Sector Map Staleness Check ──\n");

    var sectorRepo = new StockSectorRepository();
    var lastRefresh = await sectorRepo.GetLatestRefreshDateAsync();

    if (!lastRefresh.HasValue)
    {
        Console.WriteLine("No existing stock-sector map found. Running full refresh.\n");
        await RefreshStockSectorMapAsync(tmx);
        return;
    }

    var age = DateTime.UtcNow - lastRefresh.Value;

    Console.WriteLine($"Last stock-sector refresh: {lastRefresh.Value:yyyy-MM-dd HH:mm:ss} UTC");
    Console.WriteLine($"Age: {age.TotalDays:F1} days");

    if (age < maxAge)
    {
        Console.WriteLine($"Stock-sector map is fresh (< {maxAge.TotalDays:0} days). Skipping refresh. ✓\n");
        return;
    }

    Console.WriteLine($"Stock-sector map is stale (>= {maxAge.TotalDays:0} days). Refreshing...\n");
    await RefreshStockSectorMapAsync(tmx);
}

// ═══════════════════════════════════════════════════════════════════
// LEADERSHIP DATA (Granville #7–#10)
// ═══════════════════════════════════════════════════════════════════

static async Task UpdateLeadershipDataAsync(
    TmxClient tmx,
    QuoteRepository repository,
    List<SymbolInfo> constituents)
{
    Console.WriteLine("\n── Leadership Data Update ──\n");

    var leadershipRepo = new LeadershipRepository();

    // ─── Layer 1: New Highs / New Lows from stored OHLCV ───

    // We need 252+ trading days of lookback per symbol.
    // Load bars from ~14 months ago to cover the 252-day window
    // plus a few recent days to compute.
    var dataLoadStart = DateTime.Today.AddMonths(-14);

    Console.WriteLine($"Loading bars from {dataLoadStart:yyyy-MM-dd} for 52-week high/low calculation...");

    var allBars = new Dictionary<string, IReadOnlyList<DailyBar>>(StringComparer.OrdinalIgnoreCase);
    int loaded = 0;

    foreach (var constituent in constituents)
    {
        var bars = await repository.GetDailyBarsAsync(constituent.Symbol, dataLoadStart);
        if (bars.Count >= NewHighLowCalculator.LookbackDays + 1)
        {
            allBars[constituent.Symbol] = bars;
            loaded++;
        }
    }

    Console.WriteLine($"Symbols with sufficient history (≥ {NewHighLowCalculator.LookbackDays + 1} bars): {loaded}");

    if (loaded == 0)
    {
        Console.WriteLine("⚠️  No symbols have enough history for 52-week high/low. Skipping leadership update.\n");
        return;
    }

    // Only compute new-high/low counts for recent dates we don't already have
    var lastStored = await leadershipRepo.GetLatestDateAsync();
    var computeFrom = lastStored.HasValue
        ? lastStored.Value.AddDays(1)
        : DateTime.Today.AddDays(-30); // first run: seed last 30 days

    if (computeFrom > DateTime.Today.AddDays(-1))
    {
        Console.WriteLine("Leadership data is already up-to-date. ✓\n");
        return;
    }

    Console.WriteLine($"Computing new highs/lows from {computeFrom:yyyy-MM-dd}...");

    var highLowCounts = NewHighLowCalculator.Compute(allBars, computeFrom);
    Console.WriteLine($"Computed {highLowCounts.Count} trading days of new-high/new-low data");

    if (highLowCounts.Count == 0)
    {
        Console.WriteLine("No new trading days to process. ✓\n");
        return;
    }

    // ─── Layer 2: Active-stock breadth (top-N by dollar volume) ───

    Console.WriteLine("Fetching top-50 most active by dollar volume...");

    int activeAdvancers = 0;
    int activeDecliners = 0;
    int activeN = 0;

    try
    {
        var movers = await tmx.GetMarketMoversAsync(
            sortOrder: "dollarvolume",
            statExchange: "tsx",
            limit: 50);

        activeN = movers.Length;
        activeAdvancers = movers.Count(m => m.priceChange > 0);
        activeDecliners = movers.Count(m => m.priceChange < 0);

        Console.WriteLine($"  Active stocks: {activeN} (↑ {activeAdvancers}, ↓ {activeDecliners}, → {activeN - activeAdvancers - activeDecliners})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ⚠️  Market movers fetch failed: {ex.Message}");
        Console.WriteLine("  Using zero for active breadth (will be updated on next run).");
    }

    // ─── Layer 3: Benchmark index closes (XIU = TSX 60, ^TXCE = Composite Equal Weight) ───

    Console.WriteLine("Fetching benchmark index quotes (XIU, ^TXCE)...");

    decimal? tsx60Close = null;
    decimal? equalWeightClose = null;

    // XIU close from stored bars (already backfilled)
    var xiuBars = await repository.GetDailyBarsAsync(TsxBenchmarkSymbols.Xiu, DateTime.Today.AddDays(-5));
    if (xiuBars.Count > 0)
    {
        tsx60Close = (decimal)xiuBars[^1].Close;
        Console.WriteLine($"  XIU (TSX 60 proxy):    {tsx60Close:F2}");
    }
    else
    {
        Console.WriteLine("  ⚠️  No recent XIU data.");
    }

    // ^TXCE from TMX API
    try
    {
        var benchmarks = await tmx.GetBenchmarkIndicesAsync();
        var txce = benchmarks.FirstOrDefault(b =>
            b.Symbol.Equals(TsxBenchmarkSymbols.TsxCompositeEqualWeight, StringComparison.OrdinalIgnoreCase));

        if (txce != null)
        {
            equalWeightClose = txce.Price;
            Console.WriteLine($"  ^TXCE (Composite EW):  {equalWeightClose:F2}");
        }
        else
        {
            Console.WriteLine("  ⚠️  ^TXCE not found in benchmark response.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ⚠️  Benchmark index fetch failed: {ex.Message}");
    }

    // ─── Build LeadershipSnapshot entries ───
    //
    // New-high/new-low data is per-day (historical), but active breadth and
    // benchmark closes are real-time (today only). For historical backfill days,
    // we store the NHNL data with zero active breadth and null benchmark closes.
    // Hermes will fill in today's active breadth and closes on each daily run,
    // building up the series over time.

    var snapshots = new List<LeadershipSnapshot>(highLowCounts.Count);

    foreach (var hlc in highLowCounts)
    {
        bool isToday = hlc.Date.Date >= DateTime.Today.AddDays(-1); // yesterday or today (market close)

        snapshots.Add(new LeadershipSnapshot
        {
            Date = hlc.Date,
            NewHighs = hlc.NewHighs,
            NewLows = hlc.NewLows,
            IssuesTraded = hlc.IssuesTraded,
            ActiveAdvancers = isToday ? activeAdvancers : 0,
            ActiveDecliners = isToday ? activeDecliners : 0,
            ActiveN = isToday ? activeN : 0,
            Tsx60Close = isToday ? tsx60Close : null,
            EqualWeightClose = isToday ? equalWeightClose : null,
        });
    }

    // Preview
    Console.WriteLine($"\n{"Date",-12} {"NH",4} {"NL",4} {"Issues",7} {"ActAdv",7} {"ActDec",7} {"XIU",9} {"TXCE",9}");
    Console.WriteLine(new string('─', 65));
    foreach (var s in snapshots.TakeLast(10))
    {
        Console.WriteLine(
            $"{s.Date:yyyy-MM-dd}  {s.NewHighs,4} {s.NewLows,4} {s.IssuesTraded,7} " +
            $"{s.ActiveAdvancers,7} {s.ActiveDecliners,7} " +
            $"{(s.Tsx60Close.HasValue ? $"{s.Tsx60Close.Value,9:F2}" : "      N/A")} " +
            $"{(s.EqualWeightClose.HasValue ? $"{s.EqualWeightClose.Value,9:F2}" : "      N/A")}");
    }

    await leadershipRepo.UpsertAsync(snapshots);
    Console.WriteLine($"\nLeadership data stored: {snapshots.Count} entries ✓\n");
}
