using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Indicators.Granville;

namespace Core.TMX;

/// <summary>
/// <see cref="IUsIndexDataSource"/> backed by Yahoo Finance's public <c>chart</c> JSON endpoint
/// (<c>https://query1.finance.yahoo.com/v7/finance/chart/{symbol}</c>).
///
/// This is the same endpoint used by yfinance, ta-lib bridges, and many open-source tools.
/// No API key, no captcha, no cookie/crumb. See ADR-0004 for source-selection rationale.
///
/// NOTE: indices typically publish volume = 0 (especially mid-session for ^NYA).
/// That is expected; Genuity only consumes <c>Close</c>.
/// </summary>
public sealed class YahooChartUsIndexDataSource : IUsIndexDataSource, IDisposable
{
    private const string BaseUrl = "https://query1.finance.yahoo.com/v7/finance/chart/";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public YahooChartUsIndexDataSource()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // Yahoo edge nodes 401 generic HTTP clients; mimic a browser UA.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _ownsClient = true;
    }

    public YahooChartUsIndexDataSource(HttpClient http)
    {
        _http = http;
        _ownsClient = false;
    }

    public async Task<IReadOnlyList<UsIndexBar>> GetDailyBarsAsync(
        string canonicalSymbol,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default)
    {
        // Yahoo expects period1/period2 as unix-seconds (UTC). Pad end by +1 day so the
        // most recent session is always included regardless of timezone rounding.
        long period1 = ToUnixSeconds(DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc));
        long period2 = ToUnixSeconds(DateTime.SpecifyKind(endDate.Date.AddDays(1), DateTimeKind.Utc));

        string url = $"{BaseUrl}{Uri.EscapeDataString(canonicalSymbol)}" +
                     $"?period1={period1}&period2={period2}&interval=1d";

        string json;
        // Light retry for transient 429/5xx
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                json = await _http.GetStringAsync(url, ct);
                break;
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }

        return ParseChartJson(canonicalSymbol, json);
    }

    // ── Yahoo response shape (trimmed):
    //   chart.result[0].timestamp = [unix, ...]
    //   chart.result[0].indicators.quote[0].{open,high,low,close,volume} = [..., ...]
    //   chart.error = null | { code, description }
    private static IReadOnlyList<UsIndexBar> ParseChartJson(string symbol, string json)
    {
        var bars = new List<UsIndexBar>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("chart", out var chart)) return bars;

        if (chart.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
        {
            string desc = err.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            throw new InvalidOperationException($"Yahoo chart error for {symbol}: {desc}");
        }

        if (!chart.TryGetProperty("result", out var resultArr)
            || resultArr.ValueKind != JsonValueKind.Array
            || resultArr.GetArrayLength() == 0)
        {
            return bars;
        }

        var result = resultArr[0];
        if (!result.TryGetProperty("timestamp", out var tsArr) || tsArr.ValueKind != JsonValueKind.Array) return bars;
        if (!result.TryGetProperty("indicators", out var indicators)) return bars;
        if (!indicators.TryGetProperty("quote", out var quoteArr) || quoteArr.GetArrayLength() == 0) return bars;

        var quote = quoteArr[0];
        var opens   = quote.GetProperty("open");
        var highs   = quote.GetProperty("high");
        var lows    = quote.GetProperty("low");
        var closes  = quote.GetProperty("close");
        var volumes = quote.TryGetProperty("volume", out var v) ? v : default;

        int n = tsArr.GetArrayLength();
        for (int i = 0; i < n; i++)
        {
            // Skip bars where any OHLC field is null (partial prints, halted days, early-close days).
            // Yahoo occasionally publishes today's Close before Open/High/Low are finalized; next day's
            // incremental update will backfill the complete bar.
            if (opens[i].ValueKind == JsonValueKind.Null
                || highs[i].ValueKind == JsonValueKind.Null
                || lows[i].ValueKind == JsonValueKind.Null
                || closes[i].ValueKind == JsonValueKind.Null) continue;

            long unix = tsArr[i].GetInt64();
            var date = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.Date;

            double o = opens[i].GetDouble();
            double h = highs[i].GetDouble();
            double l = lows[i].GetDouble();
            double c = closes[i].GetDouble();

            long vol = 0;
            if (volumes.ValueKind == JsonValueKind.Array && volumes[i].ValueKind != JsonValueKind.Null)
                vol = volumes[i].GetInt64();

            bars.Add(new UsIndexBar(symbol, date, o, h, l, c, vol));
        }

        return bars;
    }

    private static long ToUnixSeconds(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
