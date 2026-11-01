using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Event label: within HorizonBars, does future realized volatility exceed past volatility
/// (computed on windowBars) by a multiplier?
/// Volatility is standard deviation of daily returns.
/// </summary>
public sealed class VolatilityExpansionLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _expansionMultiple;

    public VolatilityExpansionLabeler(
        int horizonBars = 10,
        float expansionMultiple = 1.5f)
    {
        HorizonBars = horizonBars;
        _expansionMultiple = expansionMultiple;
        Name = $"VolExpansion_{horizonBars}d_x{expansionMultiple:0.##}";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < 3)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        double pastVol = ReturnStdDev(windowBars);
        if (pastVol <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        var futureSlice = futureBars.Take(HorizonBars).ToList();
        double futureVol = ReturnStdDev(futureSlice);

        bool expanded = futureVol >= (pastVol * _expansionMultiple);

        return new LabelResult(
            ForwardReturn: 0,
            ThreeWayClass: expanded ? ThreeWayLabel.Buy : ThreeWayLabel.Hold,
            IsValid: true);
    }

    private static double ReturnStdDev(IReadOnlyList<DailyBar> bars)
    {
        if (bars.Count < 2) return 0;

        var returns = new double[bars.Count - 1];
        for (int i = 1; i < bars.Count; i++)
        {
            double prev = bars[i - 1].Close <= 0 ? 1.0 : bars[i - 1].Close;
            double cur = bars[i].Close <= 0 ? 1.0 : bars[i].Close;
            returns[i - 1] = (cur - prev) / prev;
        }

        double mean = returns.Average();
        double variance = returns.Sum(r => (r - mean) * (r - mean)) / System.Math.Max(1, returns.Length - 1);
        return System.Math.Sqrt(variance);
    }
}