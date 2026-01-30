using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns.Features;

/// <summary>
/// Features tuned for 5–10 day profit prediction:
/// - price/volume normalization (sequence)
/// - intraday range% (sequence)
/// - volatility (std dev of returns)
/// - ATR-like average range%
/// - breakout/breakdown distance vs prior highs/lows
/// </summary>
public sealed class AtrVolatilityBreakoutFeatureBuilder : IFeatureBuilder
{
    public string Name => "AtrVolatilityBreakout";

    // priceNorm (N) + volumeNorm (N) + rangePct (N) + summaries (4)
    public int FeatureCount(int lookback) => (lookback * 3) + 4;

    public float[] Build(IReadOnlyList<DailyBar> windowBars)
    {
        int n = windowBars.Count;
        if (n == 0)
            return [];

        float firstClose = windowBars[0].Close;
        if (firstClose == 0) firstClose = 1f;

        float avgVol = (float)windowBars.Average(b => (double)b.Volume);
        if (avgVol == 0) avgVol = 1f;

        var features = new float[(n * 3) + 4];

        // Sequences
        for (int i = 0; i < n; i++)
        {
            var b = windowBars[i];

            float close = b.Close;
            if (close == 0) close = 1f;

            features[i] = close / firstClose;               // priceNorm
            features[n + i] = b.Volume / avgVol;            // volumeNorm
            features[(2 * n) + i] = (b.High - b.Low) / close; // rangePct
        }

        // Summaries
        double atrPct = windowBars.Average(b =>
        {
            double close = b.Close == 0 ? 1.0 : b.Close;
            return (b.High - b.Low) / close;
        });

        var returns = new double[System.Math.Max(0, n - 1)];
        for (int i = 1; i < n; i++)
        {
            double prev = windowBars[i - 1].Close == 0 ? 1.0 : windowBars[i - 1].Close;
            double cur = windowBars[i].Close == 0 ? 1.0 : windowBars[i].Close;
            returns[i - 1] = (cur - prev) / prev;
        }

        double retStd = StdDev(returns);

        // Breakout distances vs prior highs/lows (exclude last bar from reference range)
        double lastClose = windowBars[^1].Close == 0 ? 1.0 : windowBars[^1].Close;

        double maxHighPrior = windowBars.Take(System.Math.Max(1, n - 1)).Max(b => (double)b.High);
        if (maxHighPrior == 0) maxHighPrior = 1.0;

        double minLowPrior = windowBars.Take(System.Math.Max(1, n - 1)).Min(b => (double)b.Low);
        if (minLowPrior == 0) minLowPrior = 1.0;

        double breakoutHighPct = (lastClose - maxHighPrior) / maxHighPrior;
        double breakdownLowPct = (lastClose - minLowPrior) / minLowPrior;

        int off = n * 3;
        features[off + 0] = (float)atrPct;
        features[off + 1] = (float)retStd;
        features[off + 2] = (float)breakoutHighPct;
        features[off + 3] = (float)breakdownLowPct;

        return features;
    }

    private static double StdDev(double[] values)
    {
        if (values.Length <= 1) return 0;
        double mean = values.Average();
        double variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
        return System.Math.Sqrt(variance);
    }
}