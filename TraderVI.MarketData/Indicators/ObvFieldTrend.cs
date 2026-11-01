using Core.Indicators.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Indicators;

/// <summary>
/// The direction implied by the zigzag of OBV breakouts — Granville's "field trend."
/// </summary>
public enum ObvFieldTrend
{
    /// <summary>Not enough OBV history to judge.</summary>
    Indeterminate,
    /// <summary>UP/DOWN breakout clusters are zigzagging lower (lower highs and lower lows).</summary>
    Falling,
    /// <summary>UP/DOWN breakouts are not in gear (neither clearly higher nor lower).</summary>
    Doubtful,
    /// <summary>UP/DOWN breakout clusters are zigzagging higher (higher highs and higher lows).</summary>
    Rising
}

/// <summary>
/// Granville's UP / DOWN breakout designation for an OBV session.
/// </summary>
public enum ObvDesignation
{
    None,
    /// <summary>OBV broke above its prior N-session high (upside breakout).</summary>
    Up,
    /// <summary>OBV broke below its prior N-session low (downside breakout).</summary>
    Down
}

/// <summary>
/// Outcome of classifying a symbol's OBV field trend, with diagnostics for reporting.
/// </summary>
/// <param name="Trend">The classified field trend.</param>
/// <param name="LatestDesignation">The most recent UP/DOWN breakout designation (None if no breakout fired).</param>
/// <param name="LatestDesignationDate">The session of <paramref name="LatestDesignation"/>, if any.</param>
/// <param name="AsOf">The date of the last OBV point evaluated.</param>
/// <param name="LatestObv">The cumulative OBV as of <paramref name="AsOf"/> (anchor-relative).</param>
/// <param name="BreakoutWindow">The N-session lookback used to detect breakouts.</param>
/// <param name="PivotCount">Number of alternating UP/DOWN pivots detected.</param>
/// <param name="Description">Human-readable, machine-friendly explanation of the verdict.</param>
public sealed record ObvFieldTrendResult(
    ObvFieldTrend Trend,
    ObvDesignation LatestDesignation,
    DateTime? LatestDesignationDate,
    DateTime AsOf,
    long LatestObv,
    int BreakoutWindow,
    int PivotCount,
    string Description);

/// <summary>
/// Classifies a symbol's <see cref="ObvFieldTrend"/> from its On-Balance Volume series.
///
/// Faithful to Granville: watch for upside/downside breakouts in OBV (an UP designation when
/// OBV makes a new high vs. the prior window, a DOWN designation when it makes a new low),
/// then read the zigzag of those designations. Zigzagging higher = rising field trend;
/// out of gear = doubtful; zigzagging lower = falling.
///
/// The classifier is pure: it returns the trend plus diagnostics and bakes in no trading
/// score (the soft-signal mapping happens downstream in the decision engine).
/// </summary>
public static class ObvFieldTrendCalculator
{
    /// <summary>Default rolling-window length (sessions) for breakout detection.</summary>
    public const int DefaultBreakoutWindow = 20;

    /// <summary>
    /// Classify the field trend of an ascending-by-date OBV series.
    /// </summary>
    /// <param name="series">OBV points ordered oldest → newest.</param>
    /// <param name="breakoutWindow">Sessions of prior history a breakout must exceed (default 20).</param>
    public static ObvFieldTrendResult Classify(
        IReadOnlyList<OBV> series,
        int breakoutWindow = DefaultBreakoutWindow)
    {
        if (breakoutWindow < 2) breakoutWindow = 2; // guard against degenerate windows

        if (series is null || series.Count == 0)
            return Insufficient(default, 0, breakoutWindow, "No OBV data available.");

        var asOf = series[^1].Date;
        var latestObv = series[^1].Value;

        if (series.Count < breakoutWindow + 1)
            return Insufficient(asOf, latestObv, breakoutWindow,
                $"Need at least {breakoutWindow + 1} OBV points (have {series.Count}) to detect breakouts.");

        // 1) Detect raw UP/DOWN designations vs. the prior N-session high/low.
        var designations = new List<(DateTime Date, ObvDesignation Type, long Value)>();
        for (int t = breakoutWindow; t < series.Count; t++)
        {
            long priorHigh = long.MinValue;
            long priorLow = long.MaxValue;
            for (int k = t - breakoutWindow; k < t; k++)
            {
                long v = series[k].Value;
                if (v > priorHigh) priorHigh = v;
                if (v < priorLow) priorLow = v;
            }

            long cur = series[t].Value;
            if (cur > priorHigh)
                designations.Add((series[t].Date, ObvDesignation.Up, cur));
            else if (cur < priorLow)
                designations.Add((series[t].Date, ObvDesignation.Down, cur));
        }

        if (designations.Count == 0)
            return new ObvFieldTrendResult(
                ObvFieldTrend.Doubtful, ObvDesignation.None, null,
                asOf, latestObv, breakoutWindow, 0,
                $"No OBV breakouts over the last {series.Count - breakoutWindow} sessions " +
                $"(window={breakoutWindow}) — volume is range-bound, field trend doubtful.");

        // 2) Compress consecutive same-direction designations into pivots.
        //    Each pivot is the cluster extreme (max for UP, min for DOWN). The result
        //    strictly alternates UP/DOWN because same-type runs are merged.
        var pivots = new List<(DateTime Date, ObvDesignation Type, long Value)>();
        foreach (var d in designations)
        {
            if (pivots.Count > 0 && pivots[^1].Type == d.Type)
            {
                var last = pivots[^1];
                bool moreExtreme = d.Type == ObvDesignation.Up ? d.Value > last.Value : d.Value < last.Value;
                if (moreExtreme) pivots[^1] = d;
            }
            else
            {
                pivots.Add(d);
            }
        }

        var latest = designations[^1];

        // 3) Classify the zigzag from the most recent UP and DOWN pivots.
        var ups = pivots.Where(p => p.Type == ObvDesignation.Up).Select(p => p.Value).ToList();
        var downs = pivots.Where(p => p.Type == ObvDesignation.Down).Select(p => p.Value).ToList();

        ObvFieldTrend trend;
        string why;

        if (ups.Count >= 2 && downs.Count >= 2)
        {
            bool higherHighs = ups[^1] > ups[^2];
            bool higherLows = downs[^1] > downs[^2];
            bool lowerHighs = ups[^1] < ups[^2];
            bool lowerLows = downs[^1] < downs[^2];

            if (higherHighs && higherLows)
            {
                trend = ObvFieldTrend.Rising;
                why = "UP and DOWN breakout clusters are zigzagging higher (higher highs and higher lows).";
            }
            else if (lowerHighs && lowerLows)
            {
                trend = ObvFieldTrend.Falling;
                why = "UP and DOWN breakout clusters are zigzagging lower (lower highs and lower lows).";
            }
            else
            {
                trend = ObvFieldTrend.Doubtful;
                why = "UP/DOWN breakout clusters are not in gear (mixed highs and lows) — field trend doubtful.";
            }
        }
        else if (ups.Count >= 1 && downs.Count == 0)
        {
            trend = ObvFieldTrend.Rising;
            why = "OBV keeps breaking to new highs with no downside breakout yet.";
        }
        else if (downs.Count >= 1 && ups.Count == 0)
        {
            trend = ObvFieldTrend.Falling;
            why = "OBV keeps breaking to new lows with no upside breakout yet.";
        }
        else
        {
            trend = ObvFieldTrend.Doubtful;
            why = "Too few OBV pivots to establish a zigzag — field trend doubtful.";
        }

        string latestText = $"Latest breakout: {latest.Type} on {latest.Date:yyyy-MM-dd}.";
        return new ObvFieldTrendResult(
            trend, latest.Type, latest.Date,
            asOf, latestObv, breakoutWindow, pivots.Count,
            $"{why} {latestText} (pivots={pivots.Count}, window={breakoutWindow})");
    }

    private static ObvFieldTrendResult Insufficient(
        DateTime asOf, long latestObv, int breakoutWindow, string description) =>
        new(ObvFieldTrend.Indeterminate, ObvDesignation.None, null,
            asOf, latestObv, breakoutWindow, 0, description);
}
