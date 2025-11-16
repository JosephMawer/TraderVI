using Core.Db;
using Core.Indicators;
using Core.TMX;
using GraphQL.Client.Http;
using System;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//using static System.Runtime.InteropServices.JavaScript.JSType;

// Hermes should pull in live stock data for whatever stocks are currently active or in the watchlist



// TmxHarvester: harvesting daily insights from Canada’s markets.
// Hermes.TmxHarvester
// Hermes being your umbrella service framework, with TMX as one of many sources — NYSEHarvester, NasdaqHarvester, etc.

Console.WriteLine("Hello, World!");

// Toronto trading window (rough: 9:30–16:00, Mon–Fri) — adjust as needed
const int OpenHour = 9, OpenMinute = 30;
const int CloseHour = 16, CloseMinute = 0;

using var cts = new CancellationTokenSource();
await RunLoopAsync(new TMX(), new QuoteRepository(), cts.Token);

static async Task RunLoopAsync(TMX tmx, QuoteRepository quoteRepository, CancellationToken ct)
{
    // pull in all active stocks
    // pull in watchlist


    while (!ct.IsCancellationRequested)
    {
        var nowLocal = TimeInToronto(DateTime.UtcNow);

        if (IsMarketOpen(nowLocal))
        {
            try
            {
                //var quotes = await tmx.GetQuoteBySymbolsAsync(["CEU"], ct);//await FetchQuotesAsync(gql, Symbols, ct);

            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] " + ex.Message);
            }
        }
        else
        {
            // Outside market hours — optional: sleep until open to save cycles

            // backfill daily history

            await BackFillDailyStockQuotes(tmx, quoteRepository);
        }

        // Sleep until the next 5-minute boundary
        var delay = DelayUntilNext5MinuteBoundary();
        await Task.Delay(delay, ct);
    }
}

static async Task BackFillDailyStockQuotes(TMX tmx, QuoteRepository quoteRepository)
{
    var fromDate = new DateTime(2024, 1, 1);
    var toDate = new DateTime(2025, 11, 1);

    // todo: get all symbols
    string symbol = "CEU";

    // gets daily stock prices for a ticker
    var dailyHistoricalQuotes = await tmx.getTimeSeriesData(symbol, "day", fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd"));
    await quoteRepository.InsertDailyAsync(symbol, dailyHistoricalQuotes);

    Console.WriteLine($"Inserted {dailyHistoricalQuotes.Count} bars for {symbol}.");
}

static DateTime TimeInToronto(DateTime utc)
{
    var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); // Windows ID for America/Toronto
    return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
}

static bool IsMarketOpen(DateTime et)
{
    // Mon–Fri
    if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;

    var open = new TimeSpan(OpenHour, OpenMinute, 0);
    var close = new TimeSpan(CloseHour, CloseMinute, 0);
    var t = et.TimeOfDay;

    return t >= open && t <= close;
}

static TimeSpan DelayUntilNext5MinuteBoundary()
{
    var now = DateTime.UtcNow;
    var next = new DateTime(now.Year, now.Month, now.Day, now.Hour, (now.Minute / 5) * 5, 0, DateTimeKind.Utc).AddMinutes(5);
    var delay = next - now;
    if (delay < TimeSpan.FromSeconds(5)) delay = TimeSpan.FromSeconds(5); // safety
    return delay;
}