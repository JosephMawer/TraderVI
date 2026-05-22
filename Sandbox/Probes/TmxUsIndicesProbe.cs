using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.TMX;

namespace Sandbox.Probes;

/// <summary>
/// Probes TMX's GraphQL endpoint for the US confirming indices (S&amp;P 500,
/// NYSE Composite, Dow Jones). Hits both <c>getQuoteForSymbols</c> (live quote)
/// and <c>getTimeSeriesData</c> (daily OHLCV history) to verify TMX exposes
/// these symbols at all — most of the time it does not, which is why we fall
/// back to Yahoo / Stooq for Genuity.
/// </summary>
public sealed class TmxUsIndicesProbe : IProbe
{
    public string Slug => "tmx-us";
    public string Description => "TMX getQuoteForSymbols + getTimeSeriesData for ^GSPC / ^NYA / ^DJI.";

    // Canonical → TMX symbol map for US indices.
    // Canonical IDs are what we store in our own tables; TMX symbols are what we
    // pass to the TMX GraphQL endpoint.
    private static readonly (string Canonical, string Tmx)[] UsIndices =
    [
        ("^GSPC", "^GSPC:US"),  // S&P 500
        ("^NYA",  "^NYA:US"),   // NYSE Composite
        ("^DJI",  "^DJI:US"),   // Dow Jones — bonus probe, Granville's original
    ];

    public async Task RunAsync()
    {
        using var tmx = new TmxClient();

        await ProbeQuotesAsync(tmx);
        Console.WriteLine();
        await ProbeDailyHistoryAsync(tmx);
    }

    // ── 1. Quote probe: does TMX return a non-null price for these symbols? ──
    private static async Task ProbeQuotesAsync(TmxClient tmx)
    {
        Console.WriteLine("=== TMX getQuoteForSymbols — US indices ===");
        var tmxSymbols = UsIndices.Select(x => x.Tmx).ToArray();

        try
        {
            var quotes = await tmx.GetQuotesBySymbolsAsync(tmxSymbols);

            Console.WriteLine($"{"Symbol",-12} {"Price",10} {"Change",10} {"%Chg",8} {"PrevClose",10} {"Exchange",-10} {"Name",-30}");
            Console.WriteLine(new string('─', 95));
            foreach (var q in quotes)
            {
                Console.WriteLine(
                    $"{q.Symbol,-12} {q.Price,10:F2} {q.PriceChange,10:F2} {q.PercentChange,8:F2} " +
                    $"{q.PrevClose,10:F2} {q.Exchange,-10} {q.LongName,-30}");
            }

            // Flag any symbol that came back missing or empty
            var returned = quotes.Select(q => q.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var s in tmxSymbols.Where(s => !returned.Contains(s)))
                Console.WriteLine($"⚠️  {s}: NO QUOTE RETURNED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Quote probe failed: {ex.Message}");
        }
    }

    // ── 2. Daily-history probe: does freq=day return OHLCV bars? ──
    private static async Task ProbeDailyHistoryAsync(TmxClient tmx)
    {
        Console.WriteLine("=== TMX getTimeSeriesData (freq=day) — US indices ===");

        var end = DateTime.Today;
        var start = end.AddDays(-30);
        string startStr = start.ToString("yyyy-MM-dd");
        string endStr = end.ToString("yyyy-MM-dd");

        foreach (var (canonical, tmxSymbol) in UsIndices)
        {
            Console.WriteLine($"\n--- {canonical}  (TMX: {tmxSymbol})  {startStr} → {endStr} ---");
            try
            {
                var bars = await tmx.GetHistoricalTimeSeriesAsync(tmxSymbol, "day", startStr, endStr);
                if (bars.Count == 0)
                {
                    Console.WriteLine("⚠️  No bars returned.");
                    continue;
                }

                Console.WriteLine($"{"Date",-12} {"Open",10} {"High",10} {"Low",10} {"Close",10} {"Volume",14}");
                Console.WriteLine(new string('─', 70));
                foreach (var b in bars.TakeLast(10))
                {
                    Console.WriteLine(
                        $"{b.TimestampUtc:yyyy-MM-dd} {b.Open,10:F2} {b.High,10:F2} {b.Low,10:F2} {b.Close,10:F2} {b.Volume,14:N0}");
                }

                var first = bars.First();
                var last = bars.Last();
                Console.WriteLine($"Total bars: {bars.Count}  |  Range: {first.TimestampUtc:yyyy-MM-dd} → {last.TimestampUtc:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ History probe failed for {tmxSymbol}: {ex.Message}");
            }
        }
    }
}
