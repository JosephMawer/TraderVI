using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.TMX;

namespace Sandbox.Probes;

/// <summary>
/// Probes TMX's <c>getTimeSeriesData</c> for the TSX sector indices (^TT*) to
/// determine whether the historical-time-series GraphQL operation actually
/// returns multi-year OHLC history for sector indices — as opposed to the
/// snapshot-only <c>getQuoteForSymbols</c> path that Hermes appears to be using
/// today (which is why <c>dbo.SectorIndices</c> only has ~8 bars per symbol).
///
/// Decision-quality reconnaissance for the ADR-0011 sector-index backfill
/// question (RS composite is null because sector history is too short — see
/// Docs/reviews/open-questions.md "Sector index history backfill").
///
/// What it does:
/// 1. For each ^TT* symbol, calls <c>GetHistoricalTimeSeriesAsync(symbol,
///    "day", start, end)</c> over a multi-year window and reports bar count,
///    earliest/latest date, and a small sample of rows (head + tail).
/// 2. For comparison, calls <c>GetQuotesBySymbolsAsync</c> on the same symbol
///    set so we can see what the snapshot endpoint returns and confirm the
///    asymmetry between the two operations.
///
/// Exit signal:
///   - If history bars come back &gt;= 80 for most symbols, Option 1 (TMX
///     historical query) is viable — write the importer.
///   - If history bars are still ~0–10 across the board, Option 1 is dead and
///     we fall back to Option 2 (reconstruct sector returns from constituent
///     OHLCV in TraderDB).
/// </summary>
public sealed class TmxSectorHistoryProbe : IProbe
{
    public string Slug => "tmx-sector-history";
    public string Description => "TMX getTimeSeriesData (freq=day) for ^TT* sector indices — does historical query work?";

    public async Task RunAsync()
    {
        using var tmx = new TmxClient();

        // Wide window: ~3 trading years. Enough to satisfy RS (60d horizon +
        // 20d Z window = 80 bars) with a generous safety margin, and enough to
        // tell us whether TMX truncates at some prior date.
        var end = DateTime.Today;
        var start = end.AddYears(-3);
        string startStr = start.ToString("yyyy-MM-dd");
        string endStr = end.ToString("yyyy-MM-dd");

        Console.WriteLine("=== TMX getTimeSeriesData — TSX sector indices ===");
        Console.WriteLine($"Window: {startStr} → {endStr}");
        Console.WriteLine($"Symbols: {TsxSectorSymbols.AllSymbols.Length} ({string.Join(", ", TsxSectorSymbols.AllSymbols)})");
        Console.WriteLine();

        var summary = new List<(string Symbol, string Name, int Bars, DateTime? First, DateTime? Last, string? Error)>();

        foreach (var symbol in TsxSectorSymbols.AllSymbols)
        {
            string name = TsxSectorSymbols.GetName(symbol);
            Console.WriteLine($"--- {symbol} ({name}) ---");

            try
            {
                var bars = await tmx.GetHistoricalTimeSeriesAsync(symbol, "day", startStr, endStr);

                if (bars.Count == 0)
                {
                    Console.WriteLine("  ⚠  No bars returned.");
                    summary.Add((symbol, name, 0, null, null, "empty"));
                    continue;
                }

                var ordered = bars.OrderBy(b => b.TimestampUtc).ToList();
                var first = ordered.First();
                var last = ordered.Last();

                Console.WriteLine($"  Bars: {ordered.Count}  Range: {first.TimestampUtc:yyyy-MM-dd} → {last.TimestampUtc:yyyy-MM-dd}");

                // Show head + tail so we can eyeball whether early-window data
                // is real (multi-year) or whether TMX silently clamps to a
                // shorter window.
                Console.WriteLine($"  {"Date",-12} {"Open",10} {"High",10} {"Low",10} {"Close",10} {"Volume",14}");
                Console.WriteLine("  " + new string('─', 70));
                foreach (var b in ordered.Take(3))
                    Console.WriteLine($"  {b.TimestampUtc:yyyy-MM-dd} {b.Open,10:F2} {b.High,10:F2} {b.Low,10:F2} {b.Close,10:F2} {b.Volume,14:N0}");
                if (ordered.Count > 6)
                    Console.WriteLine("  ...");
                foreach (var b in ordered.TakeLast(3))
                    Console.WriteLine($"  {b.TimestampUtc:yyyy-MM-dd} {b.Open,10:F2} {b.High,10:F2} {b.Low,10:F2} {b.Close,10:F2} {b.Volume,14:N0}");

                summary.Add((symbol, name, ordered.Count, first.TimestampUtc, last.TimestampUtc, null));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ History query failed: {ex.Message}");
                summary.Add((symbol, name, 0, null, null, ex.GetType().Name));
            }

            Console.WriteLine();
        }

        // ── Summary table ─────────────────────────────────────────────────
        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"{"Symbol",-8} {"Sector",-25} {"Bars",6}  {"First",-12} {"Last",-12}  Status");
        Console.WriteLine(new string('─', 80));
        foreach (var r in summary)
        {
            string firstStr = r.First?.ToString("yyyy-MM-dd") ?? "—";
            string lastStr = r.Last?.ToString("yyyy-MM-dd") ?? "—";
            string status = r.Error ?? (r.Bars >= 80 ? "OK (≥80)" : r.Bars > 0 ? "SHORT" : "EMPTY");
            Console.WriteLine($"{r.Symbol,-8} {r.Name,-25} {r.Bars,6}  {firstStr,-12} {lastStr,-12}  {status}");
        }

        int viable = summary.Count(r => r.Bars >= 80);
        int total = summary.Count;
        Console.WriteLine();
        Console.WriteLine($"Verdict: {viable}/{total} sector indices returned ≥80 bars.");
        if (viable == total)
        {
            Console.WriteLine("  ✓ TMX historical query is viable for ALL sector symbols → Option 1 (write TMX backfill importer).");
        }
        else if (viable > 0)
        {
            Console.WriteLine("  ⚠ TMX historical query is partial → Option 1 for the working symbols + reconstruction (Option 2) for the gaps.");
        }
        else
        {
            Console.WriteLine("  ✗ TMX historical query returned nothing usable → Option 2 (reconstruct sector returns from constituent OHLCV).");
        }

        // ── Cross-check: what does the snapshot endpoint say? ─────────────
        Console.WriteLine();
        Console.WriteLine("=== Cross-check: getQuoteForSymbols (snapshot) ===");
        try
        {
            var quotes = await tmx.GetQuotesBySymbolsAsync(TsxSectorSymbols.AllSymbols);
            Console.WriteLine($"{"Symbol",-8} {"Price",10} {"Change",10} {"%Chg",8} {"PrevClose",10}");
            Console.WriteLine(new string('─', 50));
            foreach (var q in quotes.OrderBy(q => q.Symbol))
            {
                Console.WriteLine($"{q.Symbol,-8} {q.Price,10:F2} {q.PriceChange,10:F2} {q.PercentChange,8:F2} {q.PrevClose,10:F2}");
            }

            var returned = quotes.Select(q => q.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var s in TsxSectorSymbols.AllSymbols.Where(s => !returned.Contains(s)))
                Console.WriteLine($"⚠  {s}: NO QUOTE RETURNED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Quote probe failed: {ex.Message}");
        }
    }
}
