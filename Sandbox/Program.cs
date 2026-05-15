using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox;

partial class Program
{
    // Yahoo accepts the canonical symbols directly — no mapping table needed.
    private static readonly (string Symbol, string Description)[] YahooIndices =
    [
        ("^GSPC", "S&P 500"),
        ("^NYA",  "NYSE Composite"),
        ("^DJI",  "Dow Jones Industrial Average"),
    ];

    static async Task Main(string[] args)
    {
        await ProbeYahooChartAsync();
    }

    // ── Yahoo 'chart' JSON probe: pull last ~30 days of daily bars. ──
    static async Task ProbeYahooChartAsync()
    {
        Console.WriteLine("=== Yahoo chart API — US indices ===");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // Some Yahoo edges 401 without a browser-ish UA. Mimic one.
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");

        foreach (var (symbol, description) in YahooIndices)
        {
            // range=1mo & interval=1d → ~21 daily bars. Use range=10y for a full backfill probe.
            var url = $"https://query1.finance.yahoo.com/v7/finance/chart/{Uri.EscapeDataString(symbol)}?range=1mo&interval=1d";
            Console.WriteLine($"\n--- {symbol}  ({description}) ---");
            Console.WriteLine($"GET {url}");

            try
            {
                var json = await http.GetStringAsync(url);
                var bars = ParseYahooChart(json).ToList();

                if (bars.Count == 0)
                {
                    Console.WriteLine("⚠️  No bars parsed. First 300 chars of response:");
                    Console.WriteLine(json[..Math.Min(300, json.Length)]);
                    continue;
                }

                Console.WriteLine($"{"Date",-12} {"Open",10} {"High",10} {"Low",10} {"Close",10} {"Volume",14}");
                Console.WriteLine(new string('─', 70));
                foreach (var b in bars.TakeLast(10))
                {
                    Console.WriteLine(
                        $"{b.Date:yyyy-MM-dd} {b.Open,10:F2} {b.High,10:F2} {b.Low,10:F2} {b.Close,10:F2} {b.Volume,14:N0}");
                }

                var first = bars.First();
                var last = bars.Last();
                Console.WriteLine($"Total bars: {bars.Count}  |  Range: {first.Date:yyyy-MM-dd} → {last.Date:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Yahoo probe failed for {symbol}: {ex.Message}");
            }
        }
    }

    private readonly record struct YahooBar(
        DateTime Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

    // Yahoo response shape (trimmed):
    //   chart.result[0].timestamp = [unix, unix, ...]
    //   chart.result[0].indicators.quote[0].{open,high,low,close,volume} = [..., ..., ...]
    private static System.Collections.Generic.IEnumerable<YahooBar> ParseYahooChart(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("chart", out var chart)) yield break;
        if (!chart.TryGetProperty("result", out var resultArr) || resultArr.ValueKind != JsonValueKind.Array || resultArr.GetArrayLength() == 0) yield break;

        var result = resultArr[0];
        if (!result.TryGetProperty("timestamp", out var tsArr) || tsArr.ValueKind != JsonValueKind.Array) yield break;
        if (!result.TryGetProperty("indicators", out var indicators)) yield break;
        if (!indicators.TryGetProperty("quote", out var quoteArr) || quoteArr.GetArrayLength() == 0) yield break;

        var quote = quoteArr[0];
        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.TryGetProperty("volume", out var v) ? v : default;

        int n = tsArr.GetArrayLength();
        for (int i = 0; i < n; i++)
        {
            // Skip bars Yahoo padded with nulls (rare, e.g., halted days).
            if (opens[i].ValueKind == JsonValueKind.Null || closes[i].ValueKind == JsonValueKind.Null) continue;

            long unix = tsArr[i].GetInt64();
            var date = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.Date;

            decimal o = opens[i].GetDecimal();
            decimal h = highs[i].GetDecimal();
            decimal l = lows[i].GetDecimal();
            decimal c = closes[i].GetDecimal();

            long vol = 0;
            if (volumes.ValueKind == JsonValueKind.Array
                && volumes[i].ValueKind != JsonValueKind.Null)
            {
                vol = volumes[i].GetInt64();
            }

            yield return new YahooBar(date, o, h, l, c, vol);
        }
    }
}


//using Core.TMX;
//using System;
//using System.Globalization;
//using System.IO;
//using System.Linq;
//using System.Net.Http;
//using System.Threading.Tasks;

//namespace Sandbox;

//partial class Program
//{
//    // Stooq symbol map. Canonical → Stooq.
//    // Note Stooq uses ^spx (NOT ^gspc) for the S&P 500.
//    private static readonly (string Canonical, string Stooq, string Description)[] StooqIndices =
//    [
//        ("^GSPC", "^spx", "S&P 500"),
//        ("^NYA",  "^nya", "NYSE Composite"),
//        ("^DJI",  "^dji", "Dow Jones Industrial Average"),
//    ];

//    static async Task Main(string[] args)
//    {
//        // Comment in/out as needed:
//        // using var tmx = new TmxClient();
//        // await ProbeQuotesAsync(tmx);
//        // await ProbeDailyHistoryAsync(tmx);

//        await ProbeStooqAsync();
//    }

//    // ── Stooq probe: download daily CSV for US indices and show the last 10 rows. ──
//    static async Task ProbeStooqAsync()
//    {
//        Console.WriteLine("=== Stooq daily CSV — US indices ===");

//        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
//        http.DefaultRequestHeaders.UserAgent.ParseAdd("TraderVI-Sandbox/1.0");

//        foreach (var (canonical, stooq, description) in StooqIndices)
//        {
//            // i=d → daily. Full history is returned in one CSV.
//            var url = $"https://stooq.com/q/d/l/?s={stooq}&i=d";
//            Console.WriteLine($"\n--- {canonical}  ({description})  via Stooq '{stooq}' ---");
//            Console.WriteLine($"GET {url}");

//            try
//            {
//                var csv = await http.GetStringAsync(url);
//                if (string.IsNullOrWhiteSpace(csv) || csv.StartsWith("No data", StringComparison.OrdinalIgnoreCase))
//                {
//                    Console.WriteLine("⚠️  Stooq returned no data.");
//                    continue;
//                }

//                var rows = ParseStooqCsv(csv).ToList();
//                if (rows.Count == 0)
//                {
//                    Console.WriteLine("⚠️  CSV parsed to 0 rows. First 200 chars of response:");
//                    Console.WriteLine(csv[..Math.Min(200, csv.Length)]);
//                    continue;
//                }

//                Console.WriteLine($"{"Date",-12} {"Open",10} {"High",10} {"Low",10} {"Close",10} {"Volume",14}");
//                Console.WriteLine(new string('─', 70));
//                foreach (var r in rows.TakeLast(10))
//                {
//                    Console.WriteLine(
//                        $"{r.Date:yyyy-MM-dd} {r.Open,10:F2} {r.High,10:F2} {r.Low,10:F2} {r.Close,10:F2} {r.Volume,14:N0}");
//                }

//                var first = rows.First();
//                var last = rows.Last();
//                Console.WriteLine($"Total bars: {rows.Count}  |  Range: {first.Date:yyyy-MM-dd} → {last.Date:yyyy-MM-dd}");
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"✗ Stooq probe failed for {stooq}: {ex.Message}");
//            }
//        }
//    }

//    private readonly record struct StooqBar(
//        DateTime Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

//    private static System.Collections.Generic.IEnumerable<StooqBar> ParseStooqCsv(string csv)
//    {
//        using var reader = new StringReader(csv);
//        string? line = reader.ReadLine(); // header: Date,Open,High,Low,Close,Volume
//        if (line is null) yield break;

//        while ((line = reader.ReadLine()) != null)
//        {
//            if (string.IsNullOrWhiteSpace(line)) continue;

//            var parts = line.Split(',');
//            if (parts.Length < 5) continue;

//            // Stooq sometimes returns "N/D" for missing volume on indices.
//            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
//            if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var o)) continue;
//            if (!decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var h)) continue;
//            if (!decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var l)) continue;
//            if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var c)) continue;

//            long vol = 0;
//            if (parts.Length >= 6)
//                long.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out vol);

//            yield return new StooqBar(date, o, h, l, c, vol);
//        }
//    }
//}


//using Core.TMX;
//using System;
//using System.Linq;
//using System.Threading.Tasks;

//namespace Sandbox;

//partial class Program
//{
//    // Canonical → TMX symbol map for US indices.
//    // Canonical IDs are what we store in our own tables; TMX symbols are what we
//    // pass to the TMX GraphQL endpoint.
//    private static readonly (string Canonical, string Tmx)[] UsIndices =
//    [
//        ("^GSPC", "^GSPC:US"),  // S&P 500
//        ("^NYA",  "^NYA:US"),   // NYSE Composite
//        ("^DJI",  "^DJI:US"),   // Dow Jones — bonus probe, Granville's original
//    ];

//    static async Task Main(string[] args)
//    {
//        using var tmx = new TmxClient();

//        await ProbeQuotesAsync(tmx);
//        Console.WriteLine();
//        await ProbeDailyHistoryAsync(tmx);
//    }

//    // ── 1. Quote probe: does TMX return a non-null price for these symbols? ──
//    static async Task ProbeQuotesAsync(TmxClient tmx)
//    {
//        Console.WriteLine("=== TMX getQuoteForSymbols — US indices ===");
//        var tmxSymbols = UsIndices.Select(x => x.Tmx).ToArray();

//        try
//        {
//            var quotes = await tmx.GetQuotesBySymbolsAsync(tmxSymbols);

//            Console.WriteLine($"{"Symbol",-12} {"Price",10} {"Change",10} {"%Chg",8} {"PrevClose",10} {"Exchange",-10} {"Name",-30}");
//            Console.WriteLine(new string('─', 95));
//            foreach (var q in quotes)
//            {
//                Console.WriteLine(
//                    $"{q.Symbol,-12} {q.Price,10:F2} {q.PriceChange,10:F2} {q.PercentChange,8:F2} " +
//                    $"{q.PrevClose,10:F2} {q.Exchange,-10} {q.LongName,-30}");
//            }

//            // Flag any symbol that came back missing or empty
//            var returned = quotes.Select(q => q.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
//            foreach (var s in tmxSymbols.Where(s => !returned.Contains(s)))
//                Console.WriteLine($"⚠️  {s}: NO QUOTE RETURNED");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"✗ Quote probe failed: {ex.Message}");
//        }
//    }

//    // ── 2. Daily-history probe: does freq=day return OHLCV bars? ──
//    static async Task ProbeDailyHistoryAsync(TmxClient tmx)
//    {
//        Console.WriteLine("=== TMX getTimeSeriesData (freq=day) — US indices ===");

//        var end = DateTime.Today;
//        var start = end.AddDays(-30);
//        string startStr = start.ToString("yyyy-MM-dd");
//        string endStr = end.ToString("yyyy-MM-dd");

//        foreach (var (canonical, tmxSymbol) in UsIndices)
//        {
//            Console.WriteLine($"\n--- {canonical}  (TMX: {tmxSymbol})  {startStr} → {endStr} ---");
//            try
//            {
//                var bars = await tmx.GetHistoricalTimeSeriesAsync(tmxSymbol, "day", startStr, endStr);
//                if (bars.Count == 0)
//                {
//                    Console.WriteLine("⚠️  No bars returned.");
//                    continue;
//                }

//                Console.WriteLine($"{"Date",-12} {"Open",10} {"High",10} {"Low",10} {"Close",10} {"Volume",14}");
//                Console.WriteLine(new string('─', 70));
//                foreach (var b in bars.TakeLast(10))
//                {
//                    Console.WriteLine(
//                        $"{b.TimestampUtc:yyyy-MM-dd} {b.Open,10:F2} {b.High,10:F2} {b.Low,10:F2} {b.Close,10:F2} {b.Volume,14:N0}");
//                }

//                var first = bars.First();
//                var last = bars.Last();
//                Console.WriteLine($"Total bars: {bars.Count}  |  Range: {first.TimestampUtc:yyyy-MM-dd} → {last.TimestampUtc:yyyy-MM-dd}");
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"✗ History probe failed for {tmxSymbol}: {ex.Message}");
//            }
//        }
//    }
//}