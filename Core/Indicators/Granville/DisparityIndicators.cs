using System.Collections.Generic;
using System.Linq;
using Core.TMX;
using Core.TMX.Models.Domain;

namespace Core.Indicators.Granville;

/// <summary>
/// Granville's Disparity indicators (#5–#6), adapted for the TSX.
///
/// Original Granville concept (Dow Theory): compare the transportation average
/// against the industrial average to detect real-economy divergence from the
/// financial market.
///
/// TSX adaptation: There is no dedicated TSX transportation sub-index. Instead,
/// we compare a "cyclical basket" (Energy + Industrials + Materials — the TSX's
/// most economically-sensitive sectors) against the broad benchmark.
///
/// Benchmark decision: We use XIU (iShares S&P/TSX 60 ETF) rather than ^TTEN
/// (the raw index) because XIU prices are already stored in ADLineEntry and used
/// throughout the system. XIU tracks the TSX 60 with negligible tracking error.
/// If purity is needed later, switch to ^TTEN fetched via GetSectorIndicesAsync.
///
/// Each rule is evaluated on two timeframes:
///   • Single-day percent change (responsive, noisy)
///   • 5-day rolling return (smoother, more reliable)
/// Both fire independently — up to 4 results per evaluation.
///
/// Basket weighting: Currently equal-weight (simple average of sector % changes).
/// TODO: Consider market-cap weighting if sector size disparity distorts signal.
///
/// Reference: Granville, "A Strategy of Daily Stock Market Timing", Disparity section.
/// </summary>
public sealed class DisparityIndicators : IGranvilleIndicatorGroup
{
    public IndicatorCategory Category => IndicatorCategory.Disparity;
    public string Name => "Disparity";

    /// <summary>Number of trading days for the rolling window comparison.</summary>
    private const int RollingWindowDays = 5;

    public IReadOnlyList<GranvilleResult> Evaluate(GranvilleMarketContext context)
    {
        var results = new List<GranvilleResult>(4);

        if (context.SectorSnapshots is not { Count: > 0 })
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 0,
                Category: IndicatorCategory.Disparity,
                Name: "Disparity: No Data",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: "Sector index data not available. Skipping disparity analysis."));
            return results;
        }

        // ── Single-day comparison ──
        var todaySingleDay = EvaluateSingleDay(context);
        if (todaySingleDay != null)
            results.Add(todaySingleDay);

        // ── 5-day rolling window comparison ──
        var rollingResult = EvaluateRollingWindow(context);
        if (rollingResult != null)
            results.Add(rollingResult);

        if (results.Count == 0)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 0,
                Category: IndicatorCategory.Disparity,
                Name: "Disparity: Neutral",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: "Cyclical basket and XIU moved in similar magnitude. No disparity signal."));
        }

        return results;
    }

    /// <summary>
    /// Compares today's single-day percent change of the cyclical basket vs XIU.
    /// </summary>
    private static GranvilleResult? EvaluateSingleDay(GranvilleMarketContext context)
    {
        var today = context.Today;
        var yesterday = context.Yesterday;

        if (!today.XiuClose.HasValue || !yesterday.XiuClose.HasValue || yesterday.XiuClose.Value == 0)
            return null;

        decimal xiuReturn = ((decimal)today.XiuClose.Value - (decimal)yesterday.XiuClose.Value)
                          / (decimal)yesterday.XiuClose.Value * 100m;

        // Get today's cyclical basket snapshots
        var latestDate = context.SectorSnapshots!
            .Max(s => s.Date);

        var todayBasket = context.SectorSnapshots!
            .Where(s => s.Date == latestDate && TsxSectorSymbols.CyclicalBasket.Contains(s.Symbol))
            .ToList();

        if (todayBasket.Count == 0)
            return null;

        // Equal-weight average percent change of cyclical sectors
        // TODO: Consider market-cap weighting if sector size disparity distorts signal.
        decimal basketReturn = todayBasket.Average(s => s.PercentChange);

        return ClassifyDisparity(
            basketReturn, xiuReturn,
            timeframe: "1-day",
            basketDetail: FormatBasketDetail(todayBasket),
            xiuDetail: $"XIU {xiuReturn:+0.00;-0.00}%");
    }

    /// <summary>
    /// Compares the 5-day rolling return of the cyclical basket vs XIU.
    /// </summary>
    private GranvilleResult? EvaluateRollingWindow(GranvilleMarketContext context)
    {
        // Need enough A/D history for XIU rolling return
        if (context.RecentHistory.Count < RollingWindowDays + 1)
            return null;

        var windowStart = context.RecentHistory[^(RollingWindowDays + 1)];
        var windowEnd = context.RecentHistory[^1];

        if (!windowStart.XiuClose.HasValue || windowStart.XiuClose.Value == 0
            || !windowEnd.XiuClose.HasValue)
            return null;

        decimal xiuReturn = ((decimal)windowEnd.XiuClose.Value - (decimal)windowStart.XiuClose.Value)
                          / (decimal)windowStart.XiuClose.Value * 100m;

        // Get sector snapshots at window boundaries
        var sortedSnapshots = context.SectorSnapshots!
            .Where(s => TsxSectorSymbols.CyclicalBasket.Contains(s.Symbol))
            .OrderBy(s => s.Date)
            .ToList();

        if (sortedSnapshots.Count == 0)
            return null;

        // Group by date, get earliest and latest dates with data
        var byDate = sortedSnapshots
            .GroupBy(s => s.Date)
            .OrderBy(g => g.Key)
            .ToList();

        if (byDate.Count < 2)
            return null;

        var startGroup = byDate[0];
        var endGroup = byDate[^1];

        // Compute per-sector return over the window, then equal-weight average
        // TODO: Consider market-cap weighting if sector size disparity distorts signal.
        var sectorReturns = new List<decimal>();
        foreach (var symbol in TsxSectorSymbols.CyclicalBasket)
        {
            var startSnap = startGroup.FirstOrDefault(s => s.Symbol == symbol);
            var endSnap = endGroup.FirstOrDefault(s => s.Symbol == symbol);

            if (startSnap != null && endSnap != null && startSnap.Price != 0)
            {
                sectorReturns.Add((endSnap.Price - startSnap.Price) / startSnap.Price * 100m);
            }
        }

        if (sectorReturns.Count == 0)
            return null;

        decimal basketReturn = sectorReturns.Average();

        return ClassifyDisparity(
            basketReturn, xiuReturn,
            timeframe: $"{RollingWindowDays}-day",
            basketDetail: $"Cyclical basket {basketReturn:+0.00;-0.00}% over {RollingWindowDays}d",
            xiuDetail: $"XIU {xiuReturn:+0.00;-0.00}% over {RollingWindowDays}d");
    }

    /// <summary>
    /// Applies the two Granville disparity rules and returns the appropriate signal.
    /// Returns null if no meaningful disparity is detected.
    /// </summary>
    /// <remarks>
    /// A minimum disparity threshold prevents noise from triggering signals when
    /// the basket and benchmark move nearly identically.
    /// </remarks>
    private static GranvilleResult? ClassifyDisparity(
        decimal basketReturn,
        decimal xiuReturn,
        string timeframe,
        string basketDetail,
        string xiuDetail)
    {
        decimal disparity = basketReturn - xiuReturn;

        // Minimum disparity threshold to avoid noise.
        // This is an initial default intended to be tuned based on historical analysis.
        const decimal minDisparity = 0.15m;

        if (System.Math.Abs(disparity) < minDisparity)
            return null;

        // ── Indicator #5: Cyclical basket more negative than XIU → near-term decline ──
        if (disparity < -minDisparity)
        {
            return new GranvilleResult(
                IndicatorNumber: 5,
                Category: IndicatorCategory.Disparity,
                Name: $"Disparity #5: Near-Term Decline ({timeframe})",
                Signal: IndicatorSignal.Bearish,
                GranvillePoints: -1,
                Description: $"Cyclical basket ({basketDetail}) moved more negatively than benchmark ({xiuDetail}). " +
                             $"Disparity: {disparity:+0.00;-0.00}%. " +
                             "Real-economy sectors weakening relative to broad market suggests near-term decline.");
        }

        // ── Indicator #6: Cyclical basket more positive than XIU → near-term advance ──
        if (disparity > minDisparity)
        {
            return new GranvilleResult(
                IndicatorNumber: 6,
                Category: IndicatorCategory.Disparity,
                Name: $"Disparity #6: Near-Term Advance ({timeframe})",
                Signal: IndicatorSignal.Bullish,
                GranvillePoints: +1,
                Description: $"Cyclical basket ({basketDetail}) moved more positively than benchmark ({xiuDetail}). " +
                             $"Disparity: {disparity:+0.00;-0.00}%. " +
                             "Real-economy sectors strengthening relative to broad market suggests near-term advance.");
        }

        return null;
    }

    private static string FormatBasketDetail(IReadOnlyList<SectorIndexSnapshot> basket)
    {
        var parts = basket.Select(s =>
            $"{TsxSectorSymbols.GetName(s.Symbol)} {s.PercentChange:+0.00;-0.00}%");
        var avg = basket.Average(s => s.PercentChange);
        return $"[{string.Join(", ", parts)}] avg={avg:+0.00;-0.00}%";
    }
}