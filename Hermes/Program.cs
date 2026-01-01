//using Core.Db;
//using Core.Indicators;
//using Core.TMX;
//using GraphQL.Client.Http;
//using System;
//using System.Globalization;
//using System.IO;
//using System.Net.NetworkInformation;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;
////using static System.Runtime.InteropServices.JavaScript.JSType;

//// Hermes should pull in live stock data for whatever stocks are currently active or in the watchlist



//// TmxHarvester: harvesting daily insights from Canada’s markets.
//// Hermes.TmxHarvester
//// Hermes being your umbrella service framework, with TMX as one of many sources — NYSEHarvester, NasdaqHarvester, etc.

//Console.WriteLine("Hello, World!");

//// Toronto trading window (rough: 9:30–16:00, Mon–Fri) — adjust as needed
//const int OpenHour = 9, OpenMinute = 30;
//const int CloseHour = 16, CloseMinute = 0;

//using var cts = new CancellationTokenSource();
//await RunLoopAsync(new TMX(), new QuoteRepository(), cts.Token);

//static async Task RunLoopAsync(TMX tmx, QuoteRepository quoteRepository, CancellationToken ct)
//{
//    // pull in all active stocks
//    // pull in watchlist


//    while (!ct.IsCancellationRequested)
//    {
//        var nowLocal = TimeInToronto(DateTime.UtcNow);

//        if (IsMarketOpen(nowLocal))
//        {
//            try
//            {
//                //var quotes = await tmx.GetQuoteBySymbolsAsync(["CEU"], ct);//await FetchQuotesAsync(gql, Symbols, ct);

//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine("[WARN] " + ex.Message);
//            }
//        }
//        else
//        {
//            // Outside market hours — optional: sleep until open to save cycles

//            // backfill daily history

//            await BackFillDailyStockQuotes(tmx, quoteRepository);
//        }

//        // Sleep until the next 5-minute boundary
//        var delay = DelayUntilNext5MinuteBoundary();
//        await Task.Delay(delay, ct);
//    }
//}

//static async Task BackFillDailyStockQuotes(TMX tmx, QuoteRepository quoteRepository)
//{
//    var fromDate = new DateTime(2024, 1, 1);
//    var toDate = new DateTime(2025, 11, 1);

//    // todo: get all symbols
//    string symbol = "CEU";

//    // gets daily stock prices for a ticker
//    var dailyHistoricalQuotes = await tmx.getTimeSeriesData(symbol, "day", fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd"));
//    await quoteRepository.InsertDailyAsync(symbol, dailyHistoricalQuotes);

//    Console.WriteLine($"Inserted {dailyHistoricalQuotes.Count} bars for {symbol}.");
//}

//static DateTime TimeInToronto(DateTime utc)
//{
//    var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); // Windows ID for America/Toronto
//    return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
//}

//static bool IsMarketOpen(DateTime et)
//{
//    // Mon–Fri
//    if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;

//    var open = new TimeSpan(OpenHour, OpenMinute, 0);
//    var close = new TimeSpan(CloseHour, CloseMinute, 0);
//    var t = et.TimeOfDay;

//    return t >= open && t <= close;
//}

//static TimeSpan DelayUntilNext5MinuteBoundary()
//{
//    var now = DateTime.UtcNow;
//    var next = new DateTime(now.Year, now.Month, now.Day, now.Hour, (now.Minute / 5) * 5, 0, DateTimeKind.Utc).AddMinutes(5);
//    var delay = next - now;
//    if (delay < TimeSpan.FromSeconds(5)) delay = TimeSpan.FromSeconds(5); // safety
//    return delay;
//}

using Core.Db;
using Core.TMX;
using System;
using System.Threading.Tasks;

// 1. First Time: Backfill Historical Data
//    dotnet run --project Hermes -- backfill
// This downloads ~3-5 years of daily data for all TSX stocks (~2-5 minutes total)



// 2. Daily: Schedule for Market Close
//    dotnet run --project Hermes -- daily
// Run this once per day (e.g., via Windows Task Scheduler at 5pm ET).
Console.WriteLine("=== Hermes: Market Data Collector ===\n");

//var mode = args.Length > 0 ? args[0] : "daily";

//if (mode == "backfill")
//{
//    Console.WriteLine("[Backfill Mode] Downloading historical data...");
//    await RunBackfillAsync();
//}
//else if (mode == "daily")
//{
//    Console.WriteLine("[Daily Mode] Collecting today's end-of-day data...");
//    await RunDailyBatchCollection();
//}
//else if (mode == "live")
//{
//    Console.WriteLine("[Live Mode] Monitoring intraday quotes...");
//    await RunLiveMonitoring();
//}


Console.WriteLine("[Backfill Mode] Downloading historical data...");
await RunBackfillAsync();


static async Task RunBackfillAsync()
{
    var tmx = new TmxClient();
    var repository = new QuoteRepository();

    // Backfill parameters
    var startDate = new DateTime(2020, 1, 1);  // Adjust as needed
    var endDate = DateTime.Today.AddDays(-1);   // Up to yesterday

    // Get all TSX constituents
    var constituents = await Constituents.GetConstituents();

    // Optional: Filter to top liquid stocks for testing
    // constituents = constituents.Take(10).ToList();

    Console.WriteLine($"Backfilling {constituents.Count} symbols from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
    Console.WriteLine($"Estimated time: ~{constituents.Count * 0.5 / 60:F1} minutes\n");

    int processed = 0;
    int failed = 0;
    int totalBarsInserted = 0;

    foreach (var constituent in constituents)
    {
        try
        {
            Console.Write($"[{processed + 1}/{constituents.Count}] {constituent.Symbol,-10} ");

            // Fetch historical daily data via TMX GraphQL
            var dailyBars = await tmx.GetHistoricalTimeSeriesAsync(
                symbol: constituent.Symbol,
                freq: "day",
                startDate: startDate.ToString("yyyy-MM-dd"),
                endDate: endDate.ToString("yyyy-MM-dd")
            );
            //var dailyBars = await tmx.getTimeSeriesData(
            //    symbol: constituent.Symbol,
            //    freq: "day",
            //    startDate: startDate.ToString("yyyy-MM-dd"),
            //    endDate: endDate.ToString("yyyy-MM-dd")
            //);

            if (dailyBars == null || dailyBars.Count == 0)
            {
                Console.WriteLine("⚠️  No data");
                continue;
            }

            // Save to database
            await repository.InsertDailyBarsAsync(constituent.Symbol, dailyBars);

            totalBarsInserted += dailyBars.Count;
            Console.WriteLine($"✓ {dailyBars.Count,4} bars");
            processed++;

            // Respectful rate limiting
            await Task.Delay(500); // 2 req/sec
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine($"\n{'=',-50}");
    Console.WriteLine($"Backfill Complete:");
    Console.WriteLine($"  Symbols processed: {processed}");
    Console.WriteLine($"  Failed: {failed}");
    Console.WriteLine($"  Total bars inserted: {totalBarsInserted:N0}");
    Console.WriteLine($"{'=',-50}");
}

static async Task RunDailyBatchCollection()
{
    var tmx = new TmxClient();
    var repository = new QuoteRepository();

    var constituents = await Constituents.GetConstituents();
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

            // Get just yesterday's bar (or today if after market close)
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
    // Your existing intraday loop (commented code at top)
    Console.WriteLine("Live monitoring not yet implemented");
}