using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Core.Db;
using Core.Indicators;
using Core.Indicators.Models;

namespace Sandbox.Probes;

/// <summary>
/// One-off backfill for Granville's per-symbol On-Balance Volume (OBV).
///
/// OBV is a running cumulative volume figure: add the session's volume on an
/// up-close, subtract it on a down-close, leave it unchanged on a flat close.
/// The absolute value is anchor-relative (meaningless on its own) — what we
/// care about downstream is the *shape* of the series and its UP/DOWN field
/// trend breakouts (see <see cref="Core.Indicators.ObvFieldTrendCalculator"/>).
///
/// Why this probe exists:
///   Hermes (<c>UpdateObvAsync</c>) maintains OBV incrementally going forward,
///   but it can only *continue* a series that already has a stored anchor row.
///   On first rollout <c>dbo.SymbolObv</c> is empty, so we seed it here by
///   computing the full cumulative from each symbol's stored OHLCV history.
///
/// What it does (idempotent — safe to re-run):
///   1. Loads the TSX universe via <see cref="SymbolsRepository"/> (same set Hermes uses).
///   2. For each symbol, loads daily bars from <c>now - retention</c> onward.
///   3. Computes the cumulative OBV series from a 0 anchor.
///   4. Upserts via MERGE (<see cref="SymbolObvRepository.UpsertAsync"/>).
///   5. Prunes anything older than the retention window, matching Hermes.
///
/// The anchor is the first bar in the window, so the backfilled series and the
/// Hermes-maintained series share the same convention: cumulative is relative
/// to the oldest retained bar, and field-trend breakouts are scale-invariant.
/// </summary>
public sealed class ObvBackfillProbe : IProbe
{
    public string Slug => "obv-backfill";
    public string Description => "Seed dbo.SymbolObv with cumulative OBV history for every TSX symbol (one-off).";

    public async Task RunAsync()
    {
        var retentionMonths = Core.Constants.ObvRetentionMonths;
        var retentionCutoff = DateTime.Today.AddMonths(-retentionMonths);

        Console.WriteLine("=== OBV backfill — dbo.SymbolObv ===");
        Console.WriteLine($"Retention window: {retentionMonths} months (from {retentionCutoff:yyyy-MM-dd})");
        Console.WriteLine("Anchor: first retained bar per symbol (cumulative is anchor-relative).");
        Console.WriteLine();

        var symbolsRepo = new SymbolsRepository();
        var quoteRepo = new QuoteRepository();
        var obvRepo = new SymbolObvRepository();

        var constituents = await symbolsRepo.GetSymbols();
        Console.WriteLine($"Universe: {constituents.Count} symbols.\n");

        var sw = Stopwatch.StartNew();
        int processed = 0;
        int seeded = 0;
        int empty = 0;
        long pointsWritten = 0;

        foreach (var constituent in constituents)
        {
            var symbol = constituent.Symbol;
            processed++;
            Console.Write($"[{processed}/{constituents.Count}] {symbol,-10} ");

            try
            {
                var bars = await quoteRepo.GetDailyBarsAsync(symbol, retentionCutoff);
                if (bars.Count == 0)
                {
                    empty++;
                    Console.WriteLine("— no bars in window, skipped.");
                    continue;
                }

                List<OBV> series = bars.CalculateOBV();
                if (series.Count == 0)
                {
                    empty++;
                    Console.WriteLine("— no OBV points produced, skipped.");
                    continue;
                }

                await obvRepo.UpsertAsync(symbol, series);
                seeded++;
                pointsWritten += series.Count;

                var first = series[0];
                var last = series[^1];
                Console.WriteLine(
                    $"✓ {series.Count,4} pts  " +
                    $"{first.Date:yyyy-MM-dd} → {last.Date:yyyy-MM-dd}  " +
                    $"OBV {last.Value,15:N0}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ ERROR: {ex.Message}");
            }
        }

        // Match Hermes: enforce the rolling retention window after seeding.
        int pruned = await obvRepo.PruneOlderThanAsync(retentionCutoff);

        sw.Stop();
        Console.WriteLine();
        Console.WriteLine("=== Backfill complete ===");
        Console.WriteLine($"  Seeded:        {seeded} symbols (+{pointsWritten:N0} points)");
        Console.WriteLine($"  Empty/skipped: {empty} symbols");
        Console.WriteLine($"  Pruned:        {pruned:N0} rows older than {retentionCutoff:yyyy-MM-dd}");
        Console.WriteLine($"  Elapsed:       {sw.Elapsed:hh\\:mm\\:ss}");
    }
}
