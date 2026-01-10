using Core.Db;
using Core.TMX;
using System;
using System.Threading.Tasks;

Console.WriteLine("=== Hermes: Market Data Collector ===\n");

Console.WriteLine("[Backfill Mode] Downloading historical data...");
await RunBackfillAsync();

static async Task RunBackfillAsync()
{
    var tmx = new TmxClient();
    var repository = new QuoteRepository();

    // Backfill parameters
    var defaultStartDate = new DateTime(2020, 1, 1); // Adjust as needed
    var endDate = DateTime.Today.AddDays(-1);        // Up to yesterday

    // Get all TSX constituents
    var db = new Constituents();
    var constituents = await db.GetConstituents();

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
}

static async Task RunDailyBatchCollection()
{
    var tmx = new TmxClient();
    var repository = new QuoteRepository();

    var constituents = await new Constituents().GetConstituents();
    Console.WriteLine($"Collecting today's data for {constituents.Count} constituents\n");

    var today = DateTime.Today;
    var yesterday = today.AddDays(-1);

    int processed = 0;
    int failed = 0;

    foreach (var constituent in constituents)
    {
        try
        {
            Console.Write($"[{processed + 1}/{constituents.Count}] {constituent.Symbol}... ");

            var dailyBars = await tmx.GetHistoricalTimeSeriesAsync(
                symbol: constituent.Symbol,
                freq: "day",
                startDate: yesterday.ToString("yyyy-MM-dd"),
                endDate: today.ToString("yyyy-MM-dd")
            );

            if (dailyBars?.Count > 0)
            {
                await repository.InsertDailyBarsAsync(constituent.Symbol, dailyBars);
                Console.WriteLine($"✓ {dailyBars.Count} bar(s)");
                processed++;
            }
            else
            {
                Console.WriteLine("⚠️  No data");
            }

            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine($"\nComplete: {processed} succeeded, {failed} failed");
}

static async Task RunLiveMonitoring()
{
    Console.WriteLine("Live monitoring not yet implemented");
}