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

    /// <summary>
    /// Minimum contiguous active-breadth coverage required to seed the EMA and
    /// compare its latest three values without bridging an unavailable session.
    /// </summary>
    public int RequiredActiveBreadthDays => EmaPeriod + 2;

    /// <summary>Lookback for large-cap relative strength return comparison.</summary>
    public int LargeCapReturnDays { get; init; } = 20;

    /// <summary>Threshold below which a series is considered "deeply negative."</summary>
    public double DeeplyNegativeThreshold { get; init; } = -0.10;

    /// <summary>
    /// Compute the current leadership state from a history of snapshots.
    /// Unavailable or flat layers do not vote as falling. Active breadth contributes
    /// only after <see cref="RequiredActiveBreadthDays"/> contiguous observations.
    /// </summary>
    /// <param name="history">Snapshots ascending by date. Must have ≥ 2 entries.</param>
    /// <returns>The computed state for the most recent day.</returns>
    public LeadershipState Compute(IReadOnlyList<LeadershipSnapshot> history) =>
        ComputeCore(history, expectedSessionsAscending: null);

    /// <summary>
    /// Computes leadership against a canonical ascending session sequence. A session
    /// with no leadership row breaks adjacency just like a row with unavailable movers.
    /// </summary>
    public LeadershipState Compute(
        IReadOnlyList<LeadershipSnapshot> history,
        IReadOnlyList<DateTime> expectedSessionsAscending)
    {
        ArgumentNullException.ThrowIfNull(expectedSessionsAscending);
        return ComputeCore(history, expectedSessionsAscending);
    }

    private LeadershipState ComputeCore(
        IReadOnlyList<LeadershipSnapshot> history,
        IReadOnlyList<DateTime> expectedSessionsAscending)
    {
        ArgumentNullException.ThrowIfNull(history);

        IReadOnlyList<LeadershipSnapshot> evaluationHistory =
            GetTrailingCanonicalHistory(history, expectedSessionsAscending);
        IReadOnlyList<LeadershipSnapshot> activeHistory =
            GetTrailingActiveBreadthHistory(evaluationHistory);
        bool usesCanonicalSessions = expectedSessionsAscending is not null;
        if (usesCanonicalSessions && activeHistory.Count < RequiredActiveBreadthDays)
            return LeadershipState.Indeterminate;

        // NHNL and large-cap RS retain the full contiguous canonical row suffix.
        // Only active breadth is cut further by an unavailable mover observation.
        if (evaluationHistory.Count < 2)
            return LeadershipState.Indeterminate;

        // ── NHNL EMA-10 ──
        var nhnlRawSeries = evaluationHistory.Select(s => s.NhnlRaw).ToArray();
        var nhnlEma = ComputeEma(nhnlRawSeries, EmaPeriod);

        // ── Active Breadth EMA-10 ──
        // Never compress across missing sessions. An older observed value is not
        // yesterday's value when an unavailable source day sits between them.
        int activeCoverage = activeHistory.Count;
        TrendDirection activeDirection = TrendDirection.Unavailable;
        double? currentActive = null;
        if (activeCoverage >= RequiredActiveBreadthDays)
        {
            var activeSeries = activeHistory
                .Select(s => s.ActiveBreadthRaw!.Value)
                .ToArray();
            var activeEma = ComputeEma(activeSeries, EmaPeriod);
            currentActive = activeEma[^1];
            activeDirection = Compare(currentActive.Value, activeEma[^2]);
        }

        // ── Large-Cap RS (20-day return differential) ──
        TrendDirection largeCapDirection = TrendDirection.Unavailable;
        if (evaluationHistory.Count > LargeCapReturnDays + 1)
        {
            var today = evaluationHistory[^1];
            var pastDay = evaluationHistory[^(LargeCapReturnDays + 1)];
            var yesterday = evaluationHistory[^2];
            var pastDayYesterday = evaluationHistory[^(LargeCapReturnDays + 2)];

            if (today.Tsx60Close.HasValue && pastDay.Tsx60Close.HasValue && pastDay.Tsx60Close > 0
                && today.EqualWeightClose.HasValue && pastDay.EqualWeightClose.HasValue && pastDay.EqualWeightClose > 0)
            {
                double tsx60Return = (double)(today.Tsx60Close.Value / pastDay.Tsx60Close.Value - 1m);
                double ewReturn = (double)(today.EqualWeightClose.Value / pastDay.EqualWeightClose.Value - 1m);
                double largeCapRs = tsx60Return - ewReturn;

                if (yesterday.Tsx60Close.HasValue && pastDayYesterday.Tsx60Close.HasValue && pastDayYesterday.Tsx60Close > 0
                    && yesterday.EqualWeightClose.HasValue && pastDayYesterday.EqualWeightClose.HasValue && pastDayYesterday.EqualWeightClose > 0)
                {
                    double prevTsx60Return = (double)(yesterday.Tsx60Close.Value / pastDayYesterday.Tsx60Close.Value - 1m);
                    double prevEwReturn = (double)(yesterday.EqualWeightClose.Value / pastDayYesterday.EqualWeightClose.Value - 1m);
                    double prevRs = prevTsx60Return - prevEwReturn;
                    largeCapDirection = Compare(largeCapRs, prevRs);
                }
            }
        }

        // ── Determine direction of each series ──
        double currentNhnl = nhnlEma[^1];
        double prevNhnl = nhnlEma.Length >= 2 ? nhnlEma[^2] : currentNhnl;
        TrendDirection nhnlDirection = Compare(currentNhnl, prevNhnl);

        int risingCount = Count(TrendDirection.Rising, nhnlDirection, activeDirection, largeCapDirection);
        int fallingCount = Count(TrendDirection.Falling, nhnlDirection, activeDirection, largeCapDirection);

        bool anyDeeplyNegative = currentNhnl < DeeplyNegativeThreshold
                              || (currentActive.HasValue && currentActive.Value < DeeplyNegativeThreshold);

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
    public LeadershipQuality ComputeQuality(IReadOnlyList<LeadershipSnapshot> history) =>
        ComputeQualityCore(history, expectedSessionsAscending: null);

    /// <summary>
    /// Computes quality against a canonical ascending session sequence. Missing rows
    /// are coverage gaps and are never removed to make observed rows appear adjacent.
    /// </summary>
    public LeadershipQuality ComputeQuality(
        IReadOnlyList<LeadershipSnapshot> history,
        IReadOnlyList<DateTime> expectedSessionsAscending)
    {
        ArgumentNullException.ThrowIfNull(expectedSessionsAscending);
        return ComputeQualityCore(history, expectedSessionsAscending);
    }

    private LeadershipQuality ComputeQualityCore(
        IReadOnlyList<LeadershipSnapshot> history,
        IReadOnlyList<DateTime> expectedSessionsAscending)
    {
        ArgumentNullException.ThrowIfNull(history);

        IReadOnlyList<LeadershipSnapshot> canonicalHistory =
            GetTrailingCanonicalHistory(history, expectedSessionsAscending);
        IReadOnlyList<LeadershipSnapshot> alignedHistory =
            GetTrailingActiveBreadthHistory(canonicalHistory);
        if (alignedHistory.Count < RequiredActiveBreadthDays)
            return LeadershipQuality.Indeterminate;

        // Align both quality inputs to the same contiguous observed suffix so an
        // older mover value cannot be treated as adjacent to a later session.
        var nhnlEma = ComputeEma(alignedHistory.Select(s => s.NhnlRaw).ToArray(), EmaPeriod);
        var activeEma = ComputeEma(alignedHistory.Select(s => s.ActiveBreadthRaw!.Value).ToArray(), EmaPeriod);

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
    /// Counts the most recent uninterrupted run of snapshots with an observed
    /// active-stock basket. Stops at the first unavailable session.
    /// </summary>
    public int CountTrailingActiveBreadthDays(IReadOnlyList<LeadershipSnapshot> history)
        => GetTrailingActiveBreadthHistory(GetTrailingCanonicalHistory(
            history,
            expectedSessionsAscending: null)).Count;

    /// <summary>
    /// Counts the latest uninterrupted active-breadth coverage against canonical
    /// expected sessions. An entirely absent row stops the count.
    /// </summary>
    public int CountTrailingActiveBreadthDays(
        IReadOnlyList<LeadershipSnapshot> history,
        IReadOnlyList<DateTime> expectedSessionsAscending)
    {
        ArgumentNullException.ThrowIfNull(expectedSessionsAscending);
        IReadOnlyList<LeadershipSnapshot> canonicalHistory =
            GetTrailingCanonicalHistory(history, expectedSessionsAscending);
        return GetTrailingActiveBreadthHistory(canonicalHistory).Count;
    }

    private static IReadOnlyList<LeadershipSnapshot> GetTrailingCanonicalHistory(
        IReadOnlyList<LeadershipSnapshot> history,
        IReadOnlyList<DateTime> expectedSessionsAscending)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (expectedSessionsAscending is null)
            return history;

        if (expectedSessionsAscending.Count == 0)
            return Array.Empty<LeadershipSnapshot>();

        var leadershipByDate = new Dictionary<DateTime, LeadershipSnapshot>();
        foreach (LeadershipSnapshot snapshot in history)
        {
            if (!leadershipByDate.TryAdd(snapshot.Date.Date, snapshot))
                throw new ArgumentException(
                    $"Leadership history contains duplicate date {snapshot.Date:yyyy-MM-dd}.",
                    nameof(history));
        }

        DateTime previousExpectedDate = default;
        for (int i = 0; i < expectedSessionsAscending.Count; i++)
        {
            DateTime expectedDate = expectedSessionsAscending[i].Date;
            if (expectedDate == default
                || (i > 0 && expectedDate <= previousExpectedDate))
            {
                throw new ArgumentException(
                    "Expected leadership sessions must be unique, non-default dates in ascending order.",
                    nameof(expectedSessionsAscending));
            }

            previousExpectedDate = expectedDate;
        }

        var trailing = new List<LeadershipSnapshot>();
        for (int i = expectedSessionsAscending.Count - 1; i >= 0; i--)
        {
            DateTime expectedDate = expectedSessionsAscending[i].Date;
            if (!leadershipByDate.TryGetValue(expectedDate, out LeadershipSnapshot snapshot))
                break;

            trailing.Add(snapshot);
        }

        trailing.Reverse();
        return trailing;
    }

    private static IReadOnlyList<LeadershipSnapshot> GetTrailingActiveBreadthHistory(
        IReadOnlyList<LeadershipSnapshot> canonicalHistory)
    {
        int firstObservedIndex = canonicalHistory.Count;
        while (firstObservedIndex > 0
               && canonicalHistory[firstObservedIndex - 1].HasActiveBreadth)
        {
            firstObservedIndex--;
        }

        return canonicalHistory.Skip(firstObservedIndex).ToArray();
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

    private static TrendDirection Compare(double current, double previous)
    {
        if (current > previous) return TrendDirection.Rising;
        if (current < previous) return TrendDirection.Falling;
        return TrendDirection.Flat;
    }

    private static int Count(TrendDirection expected, params TrendDirection[] directions) =>
        directions.Count(direction => direction == expected);

    private enum TrendDirection
    {
        Unavailable,
        Flat,
        Rising,
        Falling
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
