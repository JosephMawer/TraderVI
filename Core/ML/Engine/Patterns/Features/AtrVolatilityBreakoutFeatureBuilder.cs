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
///
/// Plus additional summary features:
/// - classic ATR% (true range) over 14 bars (or less if window is shorter)
/// - momentum returns (1d/3d/5d), cumulative return
/// - close slope over window
/// - volume anomaly (z-score, surge flag)
/// - distance to SMA20/SMA50 and SMA20-vs-SMA50
/// </summary>
public sealed class AtrVolatilityBreakoutFeatureBuilder : IFeatureBuilder
{
    public string Name => "AtrVolatilityBreakout";

    // priceNorm(N) + volumeNorm(N) + rangePct(N) + summaries(15)
    public int FeatureCount(int lookback) => (lookback * 3) + 15;

    public float[] Build(IReadOnlyList<DailyBar> windowBars)
    {
        int n = windowBars.Count;
        if (n == 0)
            return [];

        float firstClose = windowBars[0].Close;
        if (firstClose == 0) firstClose = 1f;

        float avgVol = (float)windowBars.Average(b => (double)b.Volume);
        if (avgVol == 0) avgVol = 1f;

        var features = new float[(n * 3) + 15];

        for (int i = 0; i < n; i++)
        {
            var b = windowBars[i];

            float close = b.Close;
            if (close == 0) close = 1f;

            features[i] = close / firstClose;                     // priceNorm
            features[n + i] = b.Volume / avgVol;                  // volumeNorm
            features[(2 * n) + i] = (b.High - b.Low) / close;     // rangePct
        }

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

        double lastClose = windowBars[^1].Close == 0 ? 1.0 : windowBars[^1].Close;

        double maxHighPrior = windowBars.Take(System.Math.Max(1, n - 1)).Max(b => (double)b.High);
        if (maxHighPrior == 0) maxHighPrior = 1.0;

        double minLowPrior = windowBars.Take(System.Math.Max(1, n - 1)).Min(b => (double)b.Low);
        if (minLowPrior == 0) minLowPrior = 1.0;

        double breakoutHighPct = (lastClose - maxHighPrior) / maxHighPrior;
        double breakdownLowPct = (lastClose - minLowPrior) / minLowPrior;

        double classicAtrPct14 = CalculateClassicAtrPct(windowBars, period: 14);

        double ret1 = ReturnOverDays(windowBars, 1);
        double ret3 = ReturnOverDays(windowBars, 3);
        double ret5 = ReturnOverDays(windowBars, 5);
        double cumRet = ReturnOverDays(windowBars, n - 1);

        double slope = CalculateSlope(windowBars);

        double volZ = ZScore(windowBars.Select(b => (double)b.Volume).ToArray(), windowBars[^1].Volume);
        double volSurge = volZ >= 2.0 ? 1.0 : 0.0;

        double sma20 = SimpleMovingAverage(windowBars, 20);
        double sma50 = SimpleMovingAverage(windowBars, 50);

        double distSma20 = sma20 <= 0 ? 0 : (lastClose - sma20) / sma20;
        double distSma50 = sma50 <= 0 ? 0 : (lastClose - sma50) / sma50;
        double sma20vs50 = sma50 <= 0 ? 0 : (sma20 - sma50) / sma50;

        int off = n * 3;

        features[off + 0] = (float)atrPct;
        features[off + 1] = (float)retStd;
        features[off + 2] = (float)breakoutHighPct;
        features[off + 3] = (float)breakdownLowPct;

        features[off + 4] = (float)classicAtrPct14;
        features[off + 5] = (float)ret1;
        features[off + 6] = (float)ret3;
        features[off + 7] = (float)ret5;
        features[off + 8] = (float)cumRet;

        features[off + 9] = (float)slope;

        features[off + 10] = (float)volZ;
        features[off + 11] = (float)volSurge;

        features[off + 12] = (float)distSma20;
        features[off + 13] = (float)distSma50;
        features[off + 14] = (float)sma20vs50;

        return features;
    }

    private static double CalculateClassicAtrPct(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        if (n < 2)
            return 0;

        int p = System.Math.Min(period, n - 1);
        int start = n - p;

        double sumTrPct = 0;

        for (int i = start; i < n; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];

            double prevClose = prev.Close == 0 ? 1.0 : prev.Close;
            double high = cur.High;
            double low = cur.Low;

            double tr = System.Math.Max(high - low, System.Math.Max(System.Math.Abs(high - prevClose), System.Math.Abs(low - prevClose)));
            double trPct = prevClose == 0 ? 0 : tr / prevClose;

            sumTrPct += trPct;
        }

        return sumTrPct / p;
    }

    private static double ReturnOverDays(IReadOnlyList<DailyBar> bars, int daysBack)
    {
        if (bars.Count == 0)
            return 0;

        int n = bars.Count;
        int i0 = System.Math.Max(0, n - 1 - daysBack);

        double p0 = bars[i0].Close == 0 ? 1.0 : bars[i0].Close;
        double p1 = bars[^1].Close == 0 ? 1.0 : bars[^1].Close;

        return (p1 - p0) / p0;
    }

    private static double CalculateSlope(IReadOnlyList<DailyBar> bars)
    {
        int n = bars.Count;
        if (n < 2)
            return 0;

        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++)
        {
            double x = i;
            double y = bars[i].Close;
            sx += x;
            sy += y;
            sxx += x * x;
            sxy += x * y;
        }

        double denom = (n * sxx) - (sx * sx);
        if (denom == 0)
            return 0;

        double slope = ((n * sxy) - (sx * sy)) / denom;

        double lastClose = bars[^1].Close == 0 ? 1.0 : bars[^1].Close;
        return slope / lastClose;
    }

    private static double SimpleMovingAverage(IReadOnlyList<DailyBar> bars, int period)
    {
        if (bars.Count == 0)
            return 0;

        int n = bars.Count;
        int p = System.Math.Min(period, n);
        return bars.Skip(n - p).Average(b => (double)b.Close);
    }

    private static double ZScore(double[] values, double current)
    {
        if (values.Length == 0)
            return 0;

        double mean = values.Average();
        double std = StdDev(values.Select(v => v - mean).ToArray());
        if (std == 0)
            return 0;

        return (current - mean) / std;
    }

    private static double StdDev(double[] values)
    {
        if (values.Length <= 1) return 0;
        double mean = values.Average();
        double variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
        return System.Math.Sqrt(variance);
    }
}