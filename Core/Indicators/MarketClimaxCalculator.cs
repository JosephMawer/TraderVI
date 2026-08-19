using Core.Indicators.Models;
using System;
using System.Collections.Generic;

namespace Core.Indicators;

/// <summary>
/// A single daily row of Granville's market-wide Climax (CLX): the net count of
/// OBV breakouts across the S&amp;P/TSX 60 leaders.
///
/// CLX is a standalone market-regime signal, a sibling to the Advance-Decline Line.
/// Where the A/D Line counts advancing vs declining <em>prices</em>, CLX counts names
/// whose On-Balance Volume (OBV) field trend currently designates UP vs DOWN — a
/// volume-flavoured breadth read. <see cref="Clx"/> = <see cref="UpBreakouts"/> −
/// <see cref="DownBreakouts"/> is the signal; <see cref="FreshUp"/>/<see cref="FreshDown"/>
/// are a flow diagnostic (breakouts that actually fired on <see cref="Date"/>).
/// </summary>
public sealed record MarketClimaxEntry
{
    /// <summary>The session this CLX reading represents.</summary>
    public DateTime Date { get; init; }

    /// <summary>Standing tally: # XIU-60 names whose latest OBV designation is UP.</summary>
    public int UpBreakouts { get; init; }

    /// <summary>Standing tally: # XIU-60 names whose latest OBV designation is DOWN.</summary>
    public int DownBreakouts { get; init; }

    /// <summary>The signal: <see cref="UpBreakouts"/> − <see cref="DownBreakouts"/>.</summary>
    public int Clx { get; init; }

    /// <summary>Flow diagnostic: # names whose UP breakout fired on <see cref="Date"/>.</summary>
    public int FreshUp { get; init; }

    /// <summary>Flow diagnostic: # names whose DOWN breakout fired on <see cref="Date"/>.</summary>
    public int FreshDown { get; init; }

    /// <summary># names with a directional (UP/DOWN) designation (<see cref="UpBreakouts"/> + <see cref="DownBreakouts"/>).</summary>
    public int Covered { get; init; }

    /// <summary># names that produced a classifiable OBV series (excludes Indeterminate).</summary>
    public int BasketSize { get; init; }

    /// <summary>XIU benchmark close on <see cref="Date"/>, for divergence analysis.</summary>
    public float? XiuClose { get; init; }
}

/// <summary>
/// The market-regime verdict from comparing CLX against the XIU benchmark over a window.
/// </summary>
public enum ClimaxRegime
{
    /// <summary>Not enough CLX history to judge.</summary>
    Insufficient,
    /// <summary>CLX is moving in the same direction as XIU — breadth confirms price.</summary>
    Confirming,
    /// <summary>XIU is rising but CLX is falling hard — an advance on narrowing volume breadth.</summary>
    BearishDivergence,
    /// <summary>XIU is falling but CLX is rising hard — a decline on improving volume breadth.</summary>
    BullishDivergence,
    /// <summary>No clear confirmation or divergence.</summary>
    Neutral
}

/// <summary>
/// Outcome of classifying the Climax regime, with diagnostics for reporting.
/// </summary>
/// <param name="Regime">The classified regime.</param>
/// <param name="ClxNow">CLX value at the end of the window (most recent entry).</param>
/// <param name="ClxThen">CLX value at the start of the window.</param>
/// <param name="ClxChange">ClxNow − ClxThen.</param>
/// <param name="XiuChangePct">XIU percentage change across the window (null if unavailable).</param>
/// <param name="Description">Human-readable, machine-friendly explanation of the verdict.</param>
public sealed record ClimaxRegimeResult(
    ClimaxRegime Regime,
    int ClxNow,
    int ClxThen,
    int ClxChange,
    double? XiuChangePct,
    string Description);

/// <summary>
/// Computes Granville's market-wide Climax (CLX) from the per-symbol OBV series of the
/// S&amp;P/TSX 60 basket, and classifies its confirmation/divergence against XIU.
///
/// CLX records <em>both</em> tallies: a standing net (each name's current UP vs DOWN OBV
/// designation, the signal) and a fresh flow (breakouts that fired on the as-of date, a
/// diagnostic). The classifier is pure — it bakes in no trading score; the soft-signal
/// mapping (if any) happens downstream once calibrated. v1 is diagnostic-only.
/// </summary>
public static class MarketClimaxCalculator
{
    /// <summary>
    /// Computes a single <see cref="MarketClimaxEntry"/> for <paramref name="asOf"/> from
    /// each symbol's stored OBV series.
    /// </summary>
    /// <param name="seriesBySymbol">Per-symbol OBV points ordered oldest → newest.</param>
    /// <param name="asOf">The session to evaluate. Series are truncated to Date ≤ asOf.</param>
    /// <param name="breakoutWindow">N-session lookback used to detect OBV breakouts.</param>
    /// <param name="xiuClose">XIU benchmark close on <paramref name="asOf"/> (null if unavailable).</param>
    public static MarketClimaxEntry ComputeForDate(
        IReadOnlyDictionary<string, IReadOnlyList<OBV>> seriesBySymbol,
        DateTime asOf,
        int breakoutWindow,
        float? xiuClose)
    {
        int up = 0, down = 0, freshUp = 0, freshDown = 0, basketSize = 0;

        foreach (var (_, series) in seriesBySymbol)
        {
            if (series is null || series.Count == 0)
                continue;

            // Truncate the series to the as-of date. Fast-path: when the series already
            // ends on/before asOf, classify it directly without copying.
            IReadOnlyList<OBV> view;
            if (series[^1].Date.Date <= asOf.Date)
            {
                view = series;
            }
            else
            {
                var truncated = new List<OBV>(series.Count);
                foreach (var point in series)
                {
                    if (point.Date.Date > asOf.Date) break; // ascending → stop at first future point
                    truncated.Add(point);
                }
                if (truncated.Count == 0)
                    continue;
                view = truncated;
            }

            var result = ObvFieldTrendCalculator.Classify(view, breakoutWindow);

            // Not enough history to classify → don't count it in the basket.
            if (result.Trend == ObvFieldTrend.Indeterminate)
                continue;

            basketSize++;

            switch (result.LatestDesignation)
            {
                case ObvDesignation.Up:
                    up++;
                    if (result.LatestDesignationDate?.Date == asOf.Date) freshUp++;
                    break;
                case ObvDesignation.Down:
                    down++;
                    if (result.LatestDesignationDate?.Date == asOf.Date) freshDown++;
                    break;
            }
        }

        return new MarketClimaxEntry
        {
            Date = asOf.Date,
            UpBreakouts = up,
            DownBreakouts = down,
            Clx = up - down,
            FreshUp = freshUp,
            FreshDown = freshDown,
            Covered = up + down,
            BasketSize = basketSize,
            XiuClose = xiuClose
        };
    }

    /// <summary>
    /// Computes a CLX entry for every trading day in the union of all symbols' OBV dates.
    /// Used by the Sandbox backfill probe to seed history so the first live read has context.
    /// </summary>
    /// <param name="seriesBySymbol">Per-symbol OBV points ordered oldest → newest.</param>
    /// <param name="xiuCloseByDate">XIU close indexed by date (missing dates → null XiuClose).</param>
    /// <param name="breakoutWindow">N-session lookback used to detect OBV breakouts.</param>
    /// <param name="fromDate">Optional inclusive lower bound; days before it are skipped.</param>
    public static List<MarketClimaxEntry> ComputeSeries(
        IReadOnlyDictionary<string, IReadOnlyList<OBV>> seriesBySymbol,
        IReadOnlyDictionary<DateTime, float> xiuCloseByDate,
        int breakoutWindow,
        DateTime? fromDate = null)
    {
        var dateSet = new SortedSet<DateTime>();
        foreach (var (_, series) in seriesBySymbol)
        {
            if (series is null) continue;
            foreach (var point in series)
                dateSet.Add(point.Date.Date);
        }

        var results = new List<MarketClimaxEntry>(dateSet.Count);
        foreach (var date in dateSet)
        {
            if (fromDate.HasValue && date < fromDate.Value.Date)
                continue;

            float? xiu = xiuCloseByDate.TryGetValue(date, out var close) ? close : null;
            results.Add(ComputeForDate(seriesBySymbol, date, breakoutWindow, xiu));
        }

        return results;
    }

    /// <summary>
    /// Classifies the Climax regime by comparing the most recent CLX reading to the reading
    /// <paramref name="window"/> sessions earlier, and confirming/diverging against XIU.
    /// </summary>
    /// <param name="recent">Recent CLX entries, sorted ascending by date.</param>
    /// <param name="window">How many sessions back to compare (needs ≥ window+1 entries).</param>
    /// <param name="threshold">Minimum CLX change magnitude to flag a divergence.</param>
    public static ClimaxRegimeResult ClassifyRegime(
        IReadOnlyList<MarketClimaxEntry> recent,
        int window,
        int threshold)
    {
        if (recent is null || recent.Count < window + 1)
        {
            return new ClimaxRegimeResult(
                ClimaxRegime.Insufficient, 0, 0, 0, null,
                $"Need at least {window + 1} CLX entries (have {recent?.Count ?? 0}) to judge the {window}-day regime.");
        }

        var now = recent[^1];
        var then = recent[recent.Count - 1 - window];
        int clxChange = now.Clx - then.Clx;

        double? xiuChangePct = null;
        if (now.XiuClose.HasValue && then.XiuClose.HasValue && then.XiuClose.Value != 0f)
            xiuChangePct = (now.XiuClose.Value - then.XiuClose.Value) / (double)then.XiuClose.Value;

        bool xiuUp = xiuChangePct is > 0;
        bool xiuDown = xiuChangePct is < 0;

        string clxText = $"CLX {then.Clx:+0;-0;0}\u2192{now.Clx:+0;-0;0} (\u0394{clxChange:+0;-0;0})";
        string xiuText = xiuChangePct.HasValue ? $"XIU {xiuChangePct.Value:+0.0%;-0.0%}" : "XIU n/a";

        ClimaxRegime regime;
        string description;

        if (!xiuChangePct.HasValue)
        {
            regime = ClimaxRegime.Neutral;
            description = $"{clxText} over {window}d, but no XIU reference to confirm \u2014 neutral.";
        }
        else if (xiuUp && clxChange <= -threshold)
        {
            regime = ClimaxRegime.BearishDivergence;
            description = $"{xiuText} but {clxText} over {window}d \u2192 unconfirmed advance (bearish divergence).";
        }
        else if (xiuDown && clxChange >= threshold)
        {
            regime = ClimaxRegime.BullishDivergence;
            description = $"{xiuText} but {clxText} over {window}d \u2192 improving breadth (bullish divergence).";
        }
        else if ((xiuUp && clxChange > 0) || (xiuDown && clxChange < 0))
        {
            regime = ClimaxRegime.Confirming;
            description = $"{xiuText} and {clxText} agree over {window}d \u2192 confirming.";
        }
        else
        {
            regime = ClimaxRegime.Neutral;
            description = $"{xiuText}, {clxText} over {window}d \u2192 no clear confirmation or divergence (neutral).";
        }

        return new ClimaxRegimeResult(regime, now.Clx, then.Clx, clxChange, xiuChangePct, description);
    }
}
