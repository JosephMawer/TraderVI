using Core.Db;
using Core.Indicators;
using Core.ML;
using Core.TMX;
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