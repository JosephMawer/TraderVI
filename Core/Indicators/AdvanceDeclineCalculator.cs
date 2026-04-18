using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Indicators;

/// <summary>
/// A single row in Granville's Advance-Decline table.
/// </summary>
public sealed record ADLineEntry
{
    public DateTime Date { get; init; }
    public int Advancers { get; init; }
    public int Decliners { get; init; }
    public int Unchanged { get; init; }

    /// <summary>Advancers − Decliners (the "daily plurality").</summary>
    public int DailyPlurality { get; init; }

    /// <summary>Running sum of DailyPlurality — this IS the A/D Line value.</summary>
    public int CumulativeDifferential { get; init; }

    /// <summary>XIU benchmark close for divergence analysis.</summary>
    public float? XiuClose { get; init; }
}

/// <summary>
/// Computes Granville's Advance-Decline Line from raw daily bars.
/// 
/// Granville (p. 66): "The difference between the number of advancing issues
/// and the number of declining issues is the daily plurality. The daily plurality
/// is then added or subtracted each day cumulatively to determine the advance-decline line."
/// 
/// The A/D Line reveals whether the broad market is gaining or losing strength.
/// Divergences between the A/D Line and the benchmark (XIU) often precede
/// major directional changes — weight this indicator accordingly.
/// </summary>
public static class AdvanceDeclineCalculator
{
    /// <summary>
    /// Builds the full A/D Line series from per-symbol daily bars.
    /// </summary>
    /// <param name="allSymbolBars">
    /// Dictionary of symbol → daily bars (sorted ascending by date).
    /// These come from <c>QuoteRepository.GetDailyBarsAsync</c>.
    /// </param>
    /// <param name="xiuBars">
    /// XIU benchmark bars for the same date range (sorted ascending).
    /// </param>
    /// <param name="previousCumulative">
    /// The last stored CumulativeDifferential, so we continue the running total
    /// rather than resetting to zero. Pass 0 on first-ever calculation.
    /// </param>
    /// <returns>A/D Line entries for each trading day, sorted ascending.</returns>
    public static List<ADLineEntry> Compute(
        IReadOnlyDictionary<string, IReadOnlyList<ML.DailyBar>> allSymbolBars,
        IReadOnlyList<ML.DailyBar> xiuBars,
        int previousCumulative = 0)
    {
        // Index XIU bars by date for O(1) lookup
        var xiuByDate = xiuBars.ToDictionary(b => b.Date.Date, b => b.Close);

        // Build a per-date view: for each symbol, pair (yesterday close, today close)
        // so we can count advancers/decliners.
        var priorCloseBySymbol = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var dateSet = new SortedSet<DateTime>();

        foreach (var (symbol, bars) in allSymbolBars)
        {
            foreach (var bar in bars)
                dateSet.Add(bar.Date.Date);
        }

        // Walk each date in order, tallying advancers/decliners across the universe
        var results = new List<ADLineEntry>(dateSet.Count);
        int cumulative = previousCumulative;

        foreach (var date in dateSet)
        {
            int advancers = 0;
            int decliners = 0;
            int unchanged = 0;

            foreach (var (symbol, bars) in allSymbolBars)
            {
                // Binary search would be faster, but bars are typically iterated once
                // and this runs offline in Hermes, so clarity > micro-optimization.
                var todayBar = FindBarOnDate(bars, date);
                if (todayBar is null)
                    continue;

                if (priorCloseBySymbol.TryGetValue(symbol, out float prevClose) && prevClose > 0)
                {
                    if (todayBar.Close > prevClose)
                        advancers++;
                    else if (todayBar.Close < prevClose)
                        decliners++;
                    else
                        unchanged++;
                }

                // Always update prior close for next day
                priorCloseBySymbol[symbol] = todayBar.Close;
            }

            // Skip days with no comparable data (first day for all symbols)
            if (advancers == 0 && decliners == 0 && unchanged == 0)
                continue;

            int dailyPlurality = advancers - decliners;
            cumulative += dailyPlurality;

            xiuByDate.TryGetValue(date, out float xiuClose);

            results.Add(new ADLineEntry
            {
                Date = date,
                Advancers = advancers,
                Decliners = decliners,
                Unchanged = unchanged,
                DailyPlurality = dailyPlurality,
                CumulativeDifferential = cumulative,
                XiuClose = xiuClose > 0 ? xiuClose : null
            });
        }

        return results;
    }

    // ═══════════════════════════════════════════════════════════════════
    // DERIVED HELPERS — Slope, MA, Divergence
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Slope of the A/D Line over the last <paramref name="period"/> entries.
    /// Positive = breadth expanding, negative = breadth contracting.
    /// Computed via simple linear regression on CumulativeDifferential.
    /// </summary>
    public static double Slope(IReadOnlyList<ADLineEntry> adLine, int period = 20)
    {
        if (adLine.Count < period)
            return 0;

        var recent = adLine.Skip(adLine.Count - period).ToList();

        // Simple least-squares slope: Σ((x-x̄)(y-ȳ)) / Σ((x-x̄)²)
        double xMean = (period - 1) / 2.0;
        double yMean = recent.Average(e => (double)e.CumulativeDifferential);

        double numerator = 0, denominator = 0;
        for (int i = 0; i < recent.Count; i++)
        {
            double dx = i - xMean;
            double dy = recent[i].CumulativeDifferential - yMean;
            numerator += dx * dy;
            denominator += dx * dx;
        }

        return denominator == 0 ? 0 : numerator / denominator;
    }

    /// <summary>
    /// Simple moving average of the A/D Line (CumulativeDifferential).
    /// </summary>
    public static double MovingAverage(IReadOnlyList<ADLineEntry> adLine, int period = 50)
    {
        if (adLine.Count < period)
            return adLine.Count > 0 ? adLine[^1].CumulativeDifferential : 0;

        return adLine
            .Skip(adLine.Count - period)
            .Average(e => (double)e.CumulativeDifferential);
    }

    /// <summary>
    /// Is the A/D Line above its own SMA? True = broad market participation confirming.
    /// </summary>
    public static bool IsAboveSma(IReadOnlyList<ADLineEntry> adLine, int period = 50)
    {
        if (adLine.Count == 0) return false;
        return adLine[^1].CumulativeDifferential > MovingAverage(adLine, period);
    }

    /// <summary>
    /// Detects bearish divergence: XIU making new highs while A/D Line is not.
    /// This is Granville's most powerful warning signal — the market is rising
    /// on narrowing participation.
    /// </summary>
    /// <param name="adLine">Recent A/D Line entries (sorted ascending).</param>
    /// <param name="lookback">Number of days to scan for highs.</param>
    /// <returns>True if XIU high is in the most recent 5 days but A/D Line high is not.</returns>
    public static bool HasBearishDivergence(IReadOnlyList<ADLineEntry> adLine, int lookback = 50)
    {
        if (adLine.Count < lookback) return false;

        var window = adLine.Skip(adLine.Count - lookback).ToList();
        int recentDays = System.Math.Min(5, lookback / 4);

        // Find where XIU hit its high
        float maxXiu = float.MinValue;
        int xiuHighIdx = -1;
        for (int i = 0; i < window.Count; i++)
        {
            if (window[i].XiuClose.HasValue && window[i].XiuClose.Value > maxXiu)
            {
                maxXiu = window[i].XiuClose.Value;
                xiuHighIdx = i;
            }
        }

        // Find where A/D Line hit its high
        int maxAD = int.MinValue;
        int adHighIdx = -1;
        for (int i = 0; i < window.Count; i++)
        {
            if (window[i].CumulativeDifferential > maxAD)
            {
                maxAD = window[i].CumulativeDifferential;
                adHighIdx = i;
            }
        }

        if (xiuHighIdx < 0 || adHighIdx < 0) return false;

        // Bearish divergence: XIU high is recent but A/D high was earlier
        bool xiuHighIsRecent = xiuHighIdx >= window.Count - recentDays;
        bool adHighIsOlder = adHighIdx < window.Count - recentDays;

        return xiuHighIsRecent && adHighIsOlder;
    }

    /// <summary>
    /// Detects bullish divergence: XIU making new lows while A/D Line is not.
    /// Suggests the decline is running out of steam — broad market holding up.
    /// </summary>
    public static bool HasBullishDivergence(IReadOnlyList<ADLineEntry> adLine, int lookback = 50)
    {
        if (adLine.Count < lookback) return false;

        var window = adLine.Skip(adLine.Count - lookback).ToList();
        int recentDays = System.Math.Min(5, lookback / 4);

        float minXiu = float.MaxValue;
        int xiuLowIdx = -1;
        for (int i = 0; i < window.Count; i++)
        {
            if (window[i].XiuClose.HasValue && window[i].XiuClose.Value < minXiu)
            {
                minXiu = window[i].XiuClose.Value;
                xiuLowIdx = i;
            }
        }

        int minAD = int.MaxValue;
        int adLowIdx = -1;
        for (int i = 0; i < window.Count; i++)
        {
            if (window[i].CumulativeDifferential < minAD)
            {
                minAD = window[i].CumulativeDifferential;
                adLowIdx = i;
            }
        }

        if (xiuLowIdx < 0 || adLowIdx < 0) return false;

        bool xiuLowIsRecent = xiuLowIdx >= window.Count - recentDays;
        bool adLowIsOlder = adLowIdx < window.Count - recentDays;

        return xiuLowIsRecent && adLowIsOlder;
    }

    /// <summary>
    /// Produces a breadth confirmation score for use in Delphi.
    /// Range: -1.0 (strongly bearish) to +1.0 (strongly bullish).
    /// 
    /// Components:
    ///   - A/D Line trending up (slope > 0)
    ///   - A/D Line above its SMA
    ///   - No bearish divergence vs XIU
    ///   - Bullish divergence bonus
    /// </summary>
    public static double BreadthScore(IReadOnlyList<ADLineEntry> adLine, int smaPeriod = 50, int slopePeriod = 20)
    {
        if (adLine.Count < smaPeriod) return 0;

        double score = 0;

        // Slope direction (+0.3 / -0.3)
        double slope = Slope(adLine, slopePeriod);
        score += slope > 0 ? 0.3 : -0.3;

        // Above/below SMA (+0.3 / -0.3)
        score += IsAboveSma(adLine, smaPeriod) ? 0.3 : -0.3;

        // Divergence signals (+0.4 / -0.4)
        if (HasBearishDivergence(adLine))
            score -= 0.4;
        else if (HasBullishDivergence(adLine))
            score += 0.4;

        return System.Math.Clamp(score, -1.0, 1.0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static ML.DailyBar? FindBarOnDate(IReadOnlyList<ML.DailyBar> bars, DateTime date)
    {
        // Bars are sorted ascending; for large lists a binary search would help,
        // but this is an offline batch operation and N is ~250 per symbol per year.
        for (int i = bars.Count - 1; i >= 0; i--)
        {
            if (bars[i].Date.Date == date)
                return bars[i];
            if (bars[i].Date.Date < date)
                return null; // Past it, won't find it
        }
        return null;
    }
}