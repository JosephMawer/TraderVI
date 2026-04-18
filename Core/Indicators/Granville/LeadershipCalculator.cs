using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Indicators.Granville;

/// <summary>
/// Computes smoothed leadership series from raw <see cref="LeadershipSnapshot"/> history
/// and determines the current leadership state (upswing / downswing).
///
/// Formulas (per design):
///   NHNL_10         = EMA10((NewHighs − NewLows) / Issues)
///   ActiveBreadth_10 = EMA10((AdvancersTopN − DeclinersTopN) / N)
///   LargeCapRS_20    = Return(TSX60, 20) − Return(EqualWeight, 20)
///
/// Leadership upswing:  ≥ 2 of 3 series rising AND none deeply negative.
/// Leadership downswing: ≥ 2 of 3 series falling AND NHNL_10 &lt; 0.
/// </summary>
public sealed class LeadershipCalculator
{
    /// <summary>EMA period for NHNL and Active Breadth.</summary>
    public int EmaPeriod { get; init; } = 10;

    /// <summary>Lookback for large-cap relative strength return comparison.</summary>
    public int LargeCapReturnDays { get; init; } = 20;

    /// <summary>Threshold below which a series is considered "deeply negative."</summary>
    public double DeeplyNegativeThreshold { get; init; } = -0.10;

    /// <summary>
    /// Compute the current leadership state from a history of snapshots.
    /// Requires at least <see cref="LargeCapReturnDays"/> + 1 snapshots for full evaluation.
    /// </summary>
    /// <param name="history">Snapshots ascending by date. Must have ≥ 2 entries.</param>
    /// <returns>The computed state for the most recent day.</returns>
    public LeadershipState Compute(IReadOnlyList<LeadershipSnapshot> history)
    {
        if (history.Count < 2)
            return LeadershipState.Indeterminate;

        // ── NHNL EMA-10 ──
        var nhnlRawSeries = history.Select(s => s.NhnlRaw).ToArray();
        var nhnlEma = ComputeEma(nhnlRawSeries, EmaPeriod);

        // ── Active Breadth EMA-10 ──
        var activeSeries = history.Select(s => s.ActiveBreadthRaw).ToArray();
        var activeEma = ComputeEma(activeSeries, EmaPeriod);

        // ── Large-Cap RS (20-day return differential) ──
        double? largeCapRs = null;
        bool largeCapRising = false;
        if (history.Count > LargeCapReturnDays)
        {
            var today = history[^1];
            var pastDay = history[^(LargeCapReturnDays + 1)];

            if (today.Tsx60Close.HasValue && pastDay.Tsx60Close.HasValue && pastDay.Tsx60Close > 0
                && today.EqualWeightClose.HasValue && pastDay.EqualWeightClose.HasValue && pastDay.EqualWeightClose > 0)
            {
                double tsx60Return = (double)(today.Tsx60Close.Value / pastDay.Tsx60Close.Value - 1m);
                double ewReturn = (double)(today.EqualWeightClose.Value / pastDay.EqualWeightClose.Value - 1m);
                largeCapRs = tsx60Return - ewReturn;

                // Check if rising: compare to yesterday's RS
                if (history.Count > LargeCapReturnDays + 1)
                {
                    var yesterday = history[^2];
                    var pastDayYesterday = history[^(LargeCapReturnDays + 2)];
                    if (yesterday.Tsx60Close.HasValue && pastDayYesterday.Tsx60Close.HasValue && pastDayYesterday.Tsx60Close > 0
                        && yesterday.EqualWeightClose.HasValue && pastDayYesterday.EqualWeightClose.HasValue && pastDayYesterday.EqualWeightClose > 0)
                    {
                        double prevTsx60Return = (double)(yesterday.Tsx60Close.Value / pastDayYesterday.Tsx60Close.Value - 1m);
                        double prevEwReturn = (double)(yesterday.EqualWeightClose.Value / pastDayYesterday.EqualWeightClose.Value - 1m);
                        double prevRs = prevTsx60Return - prevEwReturn;
                        largeCapRising = largeCapRs.Value > prevRs;
                    }
                }
            }
        }

        // ── Determine direction of each series ──
        double currentNhnl = nhnlEma[^1];
        double prevNhnl = nhnlEma.Length >= 2 ? nhnlEma[^2] : currentNhnl;
        bool nhnlRising = currentNhnl > prevNhnl;

        double currentActive = activeEma[^1];
        double prevActive = activeEma.Length >= 2 ? activeEma[^2] : currentActive;
        bool activeRising = currentActive > prevActive;

        int risingCount = (nhnlRising ? 1 : 0) + (activeRising ? 1 : 0) + (largeCapRising ? 1 : 0);
        int fallingCount = 3 - risingCount;

        bool anyDeeplyNegative = currentNhnl < DeeplyNegativeThreshold
                              || currentActive < DeeplyNegativeThreshold;

        // ── Leadership state determination ──
        LeadershipState state;
        if (risingCount >= 2 && !anyDeeplyNegative)
            state = LeadershipState.Upswing;
        else if (fallingCount >= 2 && currentNhnl < 0)
            state = LeadershipState.Downswing;
        else
            state = LeadershipState.Indeterminate;

        return state;
    }

    /// <summary>
    /// Compute the current leadership quality: Improving, Deteriorating, or Stable.
    /// Based on the rate of change of the leadership composite over recent days.
    /// </summary>
    public LeadershipQuality ComputeQuality(IReadOnlyList<LeadershipSnapshot> history)
    {
        if (history.Count < EmaPeriod + 2)
            return LeadershipQuality.Indeterminate;

        var nhnlEma = ComputeEma(history.Select(s => s.NhnlRaw).ToArray(), EmaPeriod);
        var activeEma = ComputeEma(history.Select(s => s.ActiveBreadthRaw).ToArray(), EmaPeriod);

        // Use the last 3 data points to determine trend in each series
        bool nhnlImproving = nhnlEma.Length >= 3
            && nhnlEma[^1] > nhnlEma[^2] && nhnlEma[^2] > nhnlEma[^3];
        bool nhnlDeteriorating = nhnlEma.Length >= 3
            && nhnlEma[^1] < nhnlEma[^2] && nhnlEma[^2] < nhnlEma[^3];

        bool activeImproving = activeEma.Length >= 3
            && activeEma[^1] > activeEma[^2] && activeEma[^2] > activeEma[^3];
        bool activeDeteriorating = activeEma.Length >= 3
            && activeEma[^1] < activeEma[^2] && activeEma[^2] < activeEma[^3];

        int improvingCount = (nhnlImproving ? 1 : 0) + (activeImproving ? 1 : 0);
        int deterioratingCount = (nhnlDeteriorating ? 1 : 0) + (activeDeteriorating ? 1 : 0);

        if (deterioratingCount >= 2)
            return LeadershipQuality.Deteriorating;
        if (improvingCount >= 2)
            return LeadershipQuality.Improving;

        return LeadershipQuality.Stable;
    }

    /// <summary>
    /// Computes an EMA series from raw values.
    /// </summary>
    internal static double[] ComputeEma(double[] values, int period)
    {
        if (values.Length == 0) return [];

        double k = 2.0 / (period + 1);
        var ema = new double[values.Length];
        ema[0] = values[0];

        for (int i = 1; i < values.Length; i++)
            ema[i] = values[i] * k + ema[i - 1] * (1 - k);

        return ema;
    }
}

/// <summary>
/// The directional leg of the leadership composite.
/// </summary>
public enum LeadershipState
{
    /// <summary>Leadership series trending up (≥ 2 of 3 rising, none deeply negative).</summary>
    Upswing,

    /// <summary>Leadership series trending down (≥ 2 of 3 falling, NHNL &lt; 0).</summary>
    Downswing,

    /// <summary>Mixed signals or insufficient data.</summary>
    Indeterminate
}

/// <summary>
/// Whether the quality of market leadership is improving, deteriorating, or stable.
/// Determined by the rate of change (slope) of the smoothed leadership series.
/// </summary>
public enum LeadershipQuality
{
    /// <summary>Leadership series consistently strengthening over recent days.</summary>
    Improving,

    /// <summary>Leadership series consistently weakening over recent days.</summary>
    Deteriorating,

    /// <summary>No clear directional trend in leadership quality.</summary>
    Stable,

    /// <summary>Insufficient history to determine quality.</summary>
    Indeterminate
}