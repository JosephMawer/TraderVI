using System;
using System.Collections.Generic;
using System.Linq;
using Core.TMX.Models.Domain;

namespace Core.Indicators.Granville;

/// <summary>
/// Computes daily new 52-week highs and lows from stored OHLCV bars.
/// A stock registers a "new high" if today's high ≥ the max high over the prior 252 trading days.
/// A "new low" if today's low ≤ the min low over the prior 252 trading days.
///
/// This is computed locally from stored data rather than relying on a TMX endpoint,
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
    /// Symbol → OHLCV bars (ascending by date). Each symbol must have enough
    /// history for the lookback to be meaningful.
    /// </param>
    /// <returns>
    /// Daily counts of new highs and lows, ascending by date.
    /// Only dates where at least one symbol had data are included.
    /// </returns>
    public static IReadOnlyList<DailyHighLowCount> Compute(
        IReadOnlyDictionary<string, IReadOnlyList<OhlcvBar>> allBars)
    {
        if (allBars.Count == 0) return [];

        // Get all unique trading dates across the universe
        var allDates = allBars.Values
            .SelectMany(bars => bars.Select(b => b.TimestampUtc.Date))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var results = new List<DailyHighLowCount>(allDates.Count);

        // For each symbol, build a date-indexed lookup
        var symbolBars = allBars.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToDictionary(b => b.TimestampUtc.Date));

        foreach (var date in allDates)
        {
            int newHighs = 0;
            int newLows = 0;
            int issuesTraded = 0;

            foreach (var (symbol, dateLookup) in symbolBars)
            {
                if (!dateLookup.TryGetValue(date, out var todayBar))
                    continue;

                issuesTraded++;

                // Find the prior 252-day window for this symbol
                // (we need the original sorted list for efficient windowing)
                var bars = allBars[symbol];
                int todayIdx = -1;
                for (int i = bars.Count - 1; i >= 0; i--)
                {
                    if (bars[i].TimestampUtc.Date == date)
                    {
                        todayIdx = i;
                        break;
                    }
                }

                if (todayIdx < 1) continue; // need at least 1 prior day

                int lookbackStart = System.Math.Max(0, todayIdx - LookbackDays);
                decimal priorHigh = decimal.MinValue;
                decimal priorLow = decimal.MaxValue;

                for (int i = lookbackStart; i < todayIdx; i++)
                {
                    if (bars[i].High > priorHigh) priorHigh = bars[i].High;
                    if (bars[i].Low < priorLow) priorLow = bars[i].Low;
                }

                if (priorHigh != decimal.MinValue && todayBar.High >= priorHigh)
                    newHighs++;

                if (priorLow != decimal.MaxValue && todayBar.Low <= priorLow)
                    newLows++;
            }

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