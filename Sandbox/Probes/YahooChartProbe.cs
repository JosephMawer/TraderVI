using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Probes Yahoo Finance's <c>v7/finance/chart</c> JSON endpoint for the US
/// confirming indices used by Granville's Genuity group (#17–#20).
/// Pulls ~30 daily bars per symbol and prints the last 10.
/// </summary>
public sealed class YahooChartProbe : IProbe
{
    public string Slug => "yahoo";
    public string Description => "Yahoo chart API — last ~30 daily bars for ^GSPC / ^NYA / ^DJI.";

    // Yahoo accepts the canonical symbols directly — no mapping table needed.
    private static readonly (string Symbol, string Description)[] YahooIndices =
    [
        ("^GSPC", "S&P 500"),
        ("^NYA",  "NYSE Composite"),
        ("^DJI",  "Dow Jones Industrial Average"),
    ];

    public async Task RunAsync()
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
    private static IEnumerable<YahooBar> ParseYahooChart(string json)
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
