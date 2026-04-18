using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Event label: is the future realized ATR% in the top X percentile of the stock's own
/// trailing ATR% distribution? This is a "relative to self" measure, which avoids comparing
/// apples to oranges across different volatility regimes.
/// </summary>
public sealed class RelativeVolatilityExpansionLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly int _percentileThreshold;

    /// <summary>
    /// Creates a relative volatility expansion labeler.
    /// </summary>
    /// <param name="horizonBars">How many bars ahead to measure volatility.</param>
    /// <param name="percentileThreshold">Percentile threshold (e.g., 80 = top 20%).</param>
    public RelativeVolatilityExpansionLabeler(int horizonBars = 10, int percentileThreshold = 80)
    {
        HorizonBars = horizonBars;
        _percentileThreshold = percentileThreshold;
        Name = $"VolExpansionRelative_{horizonBars}d_p{percentileThreshold}";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < 10)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // Compute trailing ATR% values for each position in the window
        // (simulating what the ATR% distribution looks like historically for this stock)
        var trailingAtrPcts = new List<double>();
        int atrPeriod = System.Math.Min(14, windowBars.Count - 1);

        for (int i = atrPeriod; i < windowBars.Count; i++)
        {
            var slice = windowBars.Skip(i - atrPeriod).Take(atrPeriod + 1).ToList();
            double atrPct = ComputeAtrPct(slice);
            if (atrPct > 0)
                trailingAtrPcts.Add(atrPct);
        }

        if (trailingAtrPcts.Count < 5)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // Compute future realized ATR%
        var futureSlice = futureBars.Take(HorizonBars).ToList();
        double futureAtrPct = ComputeAtrPct(futureSlice);

        // Find the percentile threshold from historical distribution
        var sorted = trailingAtrPcts.OrderBy(x => x).ToList();
        int idx = (int)System.Math.Floor(sorted.Count * _percentileThreshold / 100.0);
        idx = System.Math.Clamp(idx, 0, sorted.Count - 1);
        double threshold = sorted[idx];

        // Is future ATR% above the historical percentile threshold?
        bool isExpansion = futureAtrPct >= threshold;

        return new LabelResult(
            ForwardReturn: 0,
            ThreeWayClass: isExpansion ? ThreeWayLabel.Buy : ThreeWayLabel.Hold,
            IsValid: true);
    }

    private static double ComputeAtrPct(IReadOnlyList<DailyBar> bars)
    {
        if (bars.Count < 2) return 0;

        double sumTrPct = 0;
        int count = 0;

        for (int i = 1; i < bars.Count; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];

            double prevClose = prev.Close <= 0 ? 1.0 : prev.Close;
            double high = cur.High;
            double low = cur.Low;

            double tr = System.Math.Max(high - low, System.Math.Max(System.Math.Abs(high - prevClose), System.Math.Abs(low - prevClose)));
            double trPct = tr / prevClose;

            sumTrPct += trPct;
            count++;
        }

        return count == 0 ? 0 : sumTrPct / count;
    }
}