using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Training.Classifiers
{
    // todo: rename this something more fitting like, TrendLineClassifier ; 30 days is variable as it is a parameter
    
    /// <summary>
    /// Provides utility methods for constructing datasets of normalized price and volume patterns over a fixed lookback
    /// period, and for classifying short-term price trends in financial time series data.
    /// </summary>
    /// <remarks>This class is intended for use in scenarios where pattern recognition or trend classification
    /// is required on sequences of daily financial bars. All methods are static and the class cannot be
    /// instantiated.</remarks>
    public static class Trend30Utilities
    {
        public static List<PatternWindow> BuildTrend30Dataset(List<DailyBar> bars, int lookback = 30)
        {
            if (lookback != 30)
                throw new ArgumentOutOfRangeException(nameof(lookback), "PatternWindow vectors are currently fixed at 30.");

            var result = new List<PatternWindow>();

            for (int end = lookback - 1; end < bars.Count; end++)
            {
                int start = end - lookback + 1;
                var windowBars = bars.GetRange(start, lookback);

                var close = windowBars.Select(b => (double)b.Close).ToArray();
                var vol = windowBars.Select(b => (double)b.Volume).ToArray();

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

                result.Add(new PatternWindow
                {
                    PriceNorm = priceNorm,
                    VolumeNorm = volNorm,
                    IsTrendUp = isTrendUp
                });
            }

            return result;
        }

        // Simple linear regression slope of y over x=0..n-1
        private static double CalculateSlope(double[] y)
        {
            int n = y.Length;
            if (n < 2) return 0;

            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

            for (int i = 0; i < n; i++)
            {
                double x = i;
                sumX += x;
                sumY += y[i];
                sumXY += x * y[i];
                sumX2 += x * x;
            }

            double denom = (n * sumX2) - (sumX * sumX);
            if (denom == 0) return 0;

            return ((n * sumXY) - (sumX * sumY)) / denom;
        }
    }
}