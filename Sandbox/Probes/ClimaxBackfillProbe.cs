using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Core.Config;
using Core.Db;
using Core.Indicators;
using Core.Indicators.Models;

namespace Sandbox.Probes;

/// <summary>
/// One-off backfill for Granville's market-wide Climax (CLX) — dbo.MarketClimax.
///
/// CLX is the net count of OBV (On-Balance Volume) breakouts across the S&amp;P/TSX 60
/// leaders: for each name we read its current UP/DOWN field-trend designation and tally
/// <c>Clx = UP − DOWN</c>. It is a standalone market-regime signal, a sibling to the
/// Advance-Decline Line, and (in v1) purely diagnostic — Delphi reports its confirmation
/// or divergence vs XIU but does not yet let it move any gate or ranking.
///
/// Why this probe exists:
///   Hermes (<c>UpdateMarketClimaxAsync</c>) writes exactly one CLX row per run (the latest
///   session). On first rollout <c>dbo.MarketClimax</c> is empty, so the very first Delphi
///   read would have no prior CLX to diff against — no divergence context. This probe seeds
///   history by replaying every stored OBV date across the basket.
///
/// What it does (idempotent — safe to re-run):
///   1. Loads each XIU-60 name's stored OBV series (seeded by the <c>obv-backfill</c> probe).
///   2. Loads XIU daily closes and indexes them by date.
///   3. Calls <see cref="MarketClimaxCalculator.ComputeSeries"/> to emit one CLX entry per day.
///   4. Upserts via MERGE (<see cref="MarketClimaxRepository.UpsertAsync"/>).
///
/// Prerequisite: run <c>obv-backfill</c> first so the per-symbol OBV table is populated.
/// </summary>
public sealed class ClimaxBackfillProbe : IProbe
{
    public string Slug => "climax-backfill";
    public string Description => "Seed dbo.MarketClimax with historical CLX from the XIU-60 OBV series (one-off). Run obv-backfill first.";

    public async Task RunAsync()
    {
        var retentionMonths = Core.Constants.ObvRetentionMonths;
        var seriesStart = DateTime.Today.AddMonths(-retentionMonths);
        var breakoutWindow = Core.Constants.ClimaxBreakoutWindow;

        Console.WriteLine("=== Climax (CLX) backfill — dbo.MarketClimax ===");
        Console.WriteLine($"Basket: S&P/TSX 60 ({Xiu60Constituents.Symbols.Count} symbols)");
        Console.WriteLine($"OBV window: {seriesStart:yyyy-MM-dd} onward  |  Breakout window: {breakoutWindow} sessions");
        Console.WriteLine();

        var obvRepo = new SymbolObvRepository();
        var quoteRepo = new QuoteRepository();
        var climaxRepo = new MarketClimaxRepository();

        var sw = Stopwatch.StartNew();

        // 1) Load every XIU-60 name's OBV series.
        var seriesBySymbol = new Dictionary<string, IReadOnlyList<OBV>>(StringComparer.OrdinalIgnoreCase);
        int withData = 0, empty = 0;

        foreach (var symbol in Xiu60Constituents.Symbols)
        {
            var series = await obvRepo.GetSeriesFromDateAsync(symbol, seriesStart);
            if (series.Count == 0)
            {
                empty++;
                Console.WriteLine($"  {symbol,-10} — no OBV rows, skipped.");
                continue;
            }

            seriesBySymbol[symbol] = series;
            withData++;
        }

        Console.WriteLine();
        Console.WriteLine($"Loaded OBV for {withData} symbols ({empty} empty).");

        if (withData < Core.Constants.ClimaxMinConstituents)
        {
            Console.WriteLine(
                $"Only {withData} names have OBV series (need >= {Core.Constants.ClimaxMinConstituents}). " +
                "Run: dotnet run --project Sandbox -- obv-backfill");
            return;
        }

        // 2) Load XIU closes and index by date for divergence analysis.
        var xiuBars = await quoteRepo.GetDailyBarsAsync("XIU", seriesStart);
        var xiuCloseByDate = new Dictionary<DateTime, float>();
        foreach (var bar in xiuBars)
            xiuCloseByDate[bar.Date.Date] = (float)bar.Close;

        Console.WriteLine($"Loaded {xiuBars.Count} XIU closes ({seriesStart:yyyy-MM-dd} .. {(xiuBars.Count > 0 ? xiuBars[^1].Date.ToString("yyyy-MM-dd") : "—")}).");
        Console.WriteLine();

        // 3) Compute one CLX entry per trading day across the basket's date union.
        var entries = MarketClimaxCalculator.ComputeSeries(seriesBySymbol, xiuCloseByDate, breakoutWindow);

        // Drop leading days where too few names are classifiable yet (graceful-degradation floor).
        var trusted = entries.Where(e => e.BasketSize >= Core.Constants.ClimaxMinConstituents).ToList();
        int dropped = entries.Count - trusted.Count;

        if (trusted.Count == 0)
        {
            Console.WriteLine("No CLX days met the coverage floor — nothing to write.");
            return;
        }

        // 4) Persist.
        await climaxRepo.UpsertAsync(trusted);

        sw.Stop();

        var first = trusted[0];
        var last = trusted[^1];
        Console.WriteLine("=== Backfill complete ===");
        Console.WriteLine($"  Wrote:   {trusted.Count} CLX days ({first.Date:yyyy-MM-dd} .. {last.Date:yyyy-MM-dd})");
        Console.WriteLine($"  Dropped: {dropped} early days below coverage floor (< {Core.Constants.ClimaxMinConstituents} names)");
        Console.WriteLine($"  Latest:  CLX {last.Clx:+0;-0;0} ({last.UpBreakouts} up / {last.DownBreakouts} down, covered {last.Covered}/{last.BasketSize})");
        Console.WriteLine($"  Elapsed: {sw.Elapsed:hh\\:mm\\:ss}");
    }
}
