using Core.ML;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Training.Classifiers;

/// <summary>
/// Builds normalized trend datasets for any lookback period.
/// </summary>
public static class TrendDatasetBuilder
{
    public static List<TWindow> Build<TWindow>(List<DailyBar> bars, int lookback)
        where TWindow : class, ITrendPatternWindow, new()
    {
        var result = new List<TWindow>();

        for (int end = lookback - 1; end < bars.Count; end++)
        {
            int start = end - lookback + 1;
            var windowBars = bars.GetRange(start, lookback);

            var close = windowBars.Select(b => (double)b.Close).ToArray();
            double slope = CalculateSlope(close);
            bool isTrendUp = slope > 0;

            float firstClose = (float)windowBars[0].Close;
            if (firstClose == 0) firstClose = 1f;

            float avgVol = (float)windowBars.Average(b => (double)b.Volume);
            if (avgVol == 0) avgVol = 1f;

            var priceNorm = new float[lookback];
            var volNorm = new float[lookback];

            for (int i = 0; i < lookback; i++)
            {
                priceNorm[i] = (float)windowBars[i].Close / firstClose;
                volNorm[i] = (float)windowBars[i].Volume / avgVol;
            }

            result.Add(new TWindow
            {
                PriceNorm = priceNorm,
                VolumeNorm = volNorm,
                IsTrendUp = isTrendUp
            });
        }

        return result;
    }

    private static double CalculateSlope(double[] y)
    {
        int n = y.Length;
        if (n < 2) return 0;

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += y[i];
            sumXY += i * y[i];
            sumX2 += i * i;
        }

        double denom = (n * sumX2) - (sumX * sumX);
        return denom == 0 ? 0 : ((n * sumXY) - (sumX * sumY)) / denom;
    }
}