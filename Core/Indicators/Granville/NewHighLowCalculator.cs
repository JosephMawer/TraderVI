using System;
using System.Collections.Generic;
using System.Linq;
using Core.ML;

namespace Core.Indicators.Granville;

/// <summary>
/// Computes daily new 52-week highs and lows from stored OHLCV bars.
/// A stock registers a "new high" if today's high ≥ the max high over the prior 252 trading days.
/// A "new low" if today's low ≤ the min low over the prior 252 trading days.
///
/// Computed locally from stored data rather than relying on a TMX endpoint,
/// ensuring auditability and consistency.
/// </summary>
public static class NewHighLowCalculator
{
    /// <summary>Lookback period for 52-week high/low (trading days).</summary>
    public const int LookbackDays = 252;

    /// <summary>
    /// For each trading day in the universe, count how many stocks made new
    /// 52-week highs and lows.
    /// </summary>
    /// <param name="allBars">
    /// Symbol → daily bars (ascending by date). Each symbol must have enough
    /// history for the lookback to be meaningful.
    /// </param>
    /// <param name="fromDate">
    /// Only produce counts from this date forward (earlier bars are used for lookback only).
    /// If null, produce counts for all dates that have sufficient lookback.
    /// </param>
    /// <returns>Daily counts of new highs and lows, ascending by date.</returns>
    public static IReadOnlyList<DailyHighLowCount> Compute(
        IReadOnlyDictionary<string, IReadOnlyList<DailyBar>> allBars,
        DateTime? fromDate = null)
    {
        if (allBars.Count == 0) return [];

        // Collect all unique trading dates across the universe
        var allDates = allBars.Values
            .SelectMany(bars => bars.Select(b => b.Date.Date))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // Build per-symbol indexed lists for efficient lookback
        var symbolIndexed = allBars.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var bars = kvp.Value;
                var dateIndex = new Dictionary<DateTime, int>(bars.Count);
                for (int i = 0; i < bars.Count; i++)
                    dateIndex[bars[i].Date.Date] = i;
                return (bars, dateIndex);
            },
            StringComparer.OrdinalIgnoreCase);

        var results = new List<DailyHighLowCount>(allDates.Count);

        foreach (var date in allDates)
        {
            if (fromDate.HasValue && date < fromDate.Value)
                continue;

            int newHighs = 0;
            int newLows = 0;
            int issuesTraded = 0;

            foreach (var (symbol, (bars, dateIndex)) in symbolIndexed)
            {
                if (!dateIndex.TryGetValue(date, out int todayIdx))
                    continue;

                if (todayIdx < 1) continue; // need at least 1 prior day

                issuesTraded++;

                int lookbackStart = Math.Max(0, todayIdx - LookbackDays);
                float priorHigh = float.MinValue;
                float priorLow = float.MaxValue;

                for (int i = lookbackStart; i < todayIdx; i++)
                {
                    if (bars[i].High > priorHigh) priorHigh = bars[i].High;
                    if (bars[i].Low < priorLow) priorLow = bars[i].Low;
                }

                if (priorHigh != float.MinValue && bars[todayIdx].High >= priorHigh)
                    newHighs++;

                if (priorLow != float.MaxValue && bars[todayIdx].Low <= priorLow)
                    newLows++;
            }

            if (issuesTraded > 0)
                results.Add(new DailyHighLowCount(date, newHighs, newLows, issuesTraded));
        }

        return results;
    }
}

/// <summary>
/// Daily count of stocks making new 52-week highs and lows.
/// </summary>
public sealed record DailyHighLowCount(
    DateTime Date,
    int NewHighs,
    int NewLows,
    int IssuesTraded);