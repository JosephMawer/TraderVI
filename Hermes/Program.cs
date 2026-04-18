using Core.Db;
using Core.Indicators;
using Core.Indicators.Granville;
using Core.ML;
using Core.TMX;
using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("=== Hermes: Market Data Collector ===\n");

Console.WriteLine("[Backfill Mode] Downloading historical data...");
await RunBackfillAsync();

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

    // ═══════════════════════════════════════════════════════════════════
    // UPDATE SECTOR INDICES
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

    // Compute — the lookback days will prime priorCloseBySymbol, then the new
    // days produce actual ADLineEntry records. We pass lastCumulative so the
    // running total continues seamlessly from the stored value.
    var adLine = AdvanceDeclineCalculator.Compute(allBars, xiuBars, lastCumulative);

    // Filter out entries we already have stored (the lookback days)
    var newEntries = adLine.Where(e => e.Date > lastDate.Value).ToList();

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

static async Task UpdateSectorIndicesAsync(TmxClient tmx)
{
    Console.WriteLine("\n── Sector Index Update ──\n");

    var repo = new SectorIndexRepository();
    var lastDate = await repo.GetLatestDateAsync();

    if (lastDate.HasValue)
        Console.WriteLine($"Last stored sector data: {lastDate.Value:yyyy-MM-dd}");
    else
        Console.WriteLine("No existing sector index data.");

    // Skip if already collected today
    if (lastDate.HasValue && lastDate.Value.Date >= DateTime.Today)
    {
        Console.WriteLine("Sector indices already up-to-date for today. ✓\n");
        return;
    }

    try
    {
        var snapshots = await tmx.GetSectorIndicesAsync();

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
        Console.WriteLine($"\nSector indices stored: {snapshots.Count} entries for {DateTime.Today:yyyy-MM-dd} ✓\n");
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