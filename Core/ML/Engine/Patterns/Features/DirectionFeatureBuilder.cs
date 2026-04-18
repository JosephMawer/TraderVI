using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns.Features;

/// <summary>
/// Feature builder optimized for direction prediction (not breakout detection).
/// Focus: short-term path, normalized by volatility, conditioned on regime.
/// 
/// ~30 features covering:
/// - Returns & path (momentum vs chop)
/// - Volatility normalization
/// - Trend regime context
/// - Mean-reversion pressure
/// - Candle structure
/// - Volume confirmation
/// - Relative strength (if market bars provided)
/// </summary>
public sealed class DirectionFeatureBuilder : IFeatureBuilder
{
    public string Name => "DirectionPack";

    /// <summary>
    /// Optional benchmark bars for relative strength features.
    /// </summary>
    public IReadOnlyList<DailyBar>? MarketBars { get; set; }

    /// <summary>
    /// Returns fixed feature count (30) regardless of lookback.
    /// Direction features are computed from recent history, not flattened windows.
    /// </summary>
    public int FeatureCount(int lookback) => 30;

    public float[] Build(IReadOnlyList<DailyBar> bars)
    {
        var features = new List<float>();
        int n = bars.Count;

        if (n < 50)
            return new float[FeatureCount(n)];

        var closes = bars.Select(b => (double)b.Close).ToArray();
        var highs = bars.Select(b => (double)b.High).ToArray();
        var lows = bars.Select(b => (double)b.Low).ToArray();
        var opens = bars.Select(b => (double)b.Open).ToArray();
        var volumes = bars.Select(b => (double)b.Volume).ToArray();

        // ═══════════════════════════════════════════════════════════════
        // 1️⃣ RETURNS & PATH (core signal) - 7 features
        // ═══════════════════════════════════════════════════════════════
        double r1 = LogReturn(closes, 1);
        double r5 = LogReturn(closes, 5);
        double r10 = LogReturn(closes, 10);
        double r20 = LogReturn(closes, 20);

        // 3-day drift
        double sum_r3 = LogReturn(closes, 1) + LogReturn(closes, 2, 1) + LogReturn(closes, 3, 2);

        // Fraction of up days in last 10
        double up_frac_10 = UpFraction(closes, 10);

        // Consecutive sign streak
        double sign_streak = SignStreak(closes);

        features.Add((float)r1);
        features.Add((float)r5);
        features.Add((float)r10);
        features.Add((float)r20);
        features.Add((float)sum_r3);
        features.Add((float)up_frac_10);
        features.Add((float)sign_streak);

        // ═══════════════════════════════════════════════════════════════
        // 2️⃣ VOLATILITY NORMALIZATION (mandatory) - 5 features
        // ═══════════════════════════════════════════════════════════════
        double atr14 = CalculateAtr(bars, 14);
        double atr50 = CalculateAtr(bars, System.Math.Min(50, n - 1));
        double vol20 = StdDev(closes, 20);

        double atr_ratio = atr50 > 0 ? atr14 / atr50 : 1.0;
        double z_r1 = vol20 > 0 ? r1 / vol20 : 0;
        double z_r5 = vol20 > 0 ? r5 / vol20 : 0;

        features.Add((float)atr14);
        features.Add((float)atr_ratio);
        features.Add((float)vol20);
        features.Add((float)z_r1);
        features.Add((float)z_r5);

        // ═══════════════════════════════════════════════════════════════
        // 3️⃣ TREND REGIME CONTEXT - 5 features
        // ═══════════════════════════════════════════════════════════════
        double ma20 = SMA(closes, 20);
        double ma50 = SMA(closes, 50);
        double ma200 = n >= 200 ? SMA(closes, 200) : ma50;

        double currentPrice = closes[^1];

        // Distance from MAs scaled by ATR
        double price_over_ma50 = atr14 > 0 ? (currentPrice - ma50) / atr14 : 0;
        double price_over_ma200 = atr14 > 0 ? (currentPrice - ma200) / atr14 : 0;

        // MA slopes (rate of change over 5 bars)
        double slope_ma20 = SlopeNormalized(closes, 20, 5, atr14);
        double slope_ma50 = SlopeNormalized(closes, 50, 5, atr14);

        // Binary regime
        double ma50_gt_ma200 = ma50 > ma200 ? 1.0 : 0.0;

        features.Add((float)price_over_ma50);
        features.Add((float)price_over_ma200);
        features.Add((float)slope_ma20);
        features.Add((float)slope_ma50);
        features.Add((float)ma50_gt_ma200);

        // ═══════════════════════════════════════════════════════════════
        // 4️⃣ MEAN-REVERSION PRESSURE - 4 features
        // ═══════════════════════════════════════════════════════════════
        double dist_from_ma20 = atr14 > 0 ? (currentPrice - ma20) / atr14 : 0;
        double dist_from_sma5 = atr14 > 0 ? (currentPrice - SMA(closes, 5)) / atr14 : 0;

        // Bollinger Z-score
        double boll_z = vol20 > 0 ? (currentPrice - ma20) / (vol20 * 2) : 0;

        // Range position (where close is in today's bar: 0=low, 1=high)
        var lastBar = bars[^1];
        double range = lastBar.High - lastBar.Low;
        double range_pos = range > 0 ? (lastBar.Close - lastBar.Low) / range : 0.5;

        features.Add((float)dist_from_ma20);
        features.Add((float)dist_from_sma5);
        features.Add((float)boll_z);
        features.Add((float)range_pos);

        // ═══════════════════════════════════════════════════════════════
        // 5️⃣ CANDLE STRUCTURE (scaled by ATR) - 5 features
        // ═══════════════════════════════════════════════════════════════
        double body = System.Math.Abs(lastBar.Close - lastBar.Open);
        double upper_wick = lastBar.High - System.Math.Max(lastBar.Close, lastBar.Open);
        double lower_wick = System.Math.Min(lastBar.Close, lastBar.Open) - lastBar.Low;
        double gap = opens[^1] - closes[^2];
        double true_range = System.Math.Max(lastBar.High - lastBar.Low,
                            System.Math.Max(System.Math.Abs(lastBar.High - closes[^2]),
                                     System.Math.Abs(lastBar.Low - closes[^2])));

        double body_atr = atr14 > 0 ? body / atr14 : 0;
        double upper_wick_atr = atr14 > 0 ? upper_wick / atr14 : 0;
        double lower_wick_atr = atr14 > 0 ? lower_wick / atr14 : 0;
        double gap_atr = atr14 > 0 ? gap / atr14 : 0;
        double tr_atr = atr14 > 0 ? true_range / atr14 : 0;

        features.Add((float)body_atr);
        features.Add((float)upper_wick_atr);
        features.Add((float)lower_wick_atr);
        features.Add((float)gap_atr);
        features.Add((float)tr_atr);

        // ═══════════════════════════════════════════════════════════════
        // 6️⃣ VOLUME CONFIRMATION - 2 features
        // ═══════════════════════════════════════════════════════════════
        double vol_sma20 = SMA(volumes, 20);
        double vol_ratio = vol_sma20 > 0 ? volumes[^1] / vol_sma20 : 1.0;
        double log_dollar_vol = System.Math.Log(System.Math.Max(1, volumes[^1] * currentPrice));

        features.Add((float)vol_ratio);
        features.Add((float)log_dollar_vol);

        // ═══════════════════════════════════════════════════════════════
        // 7️⃣ RELATIVE STRENGTH (if market bars provided) - 2 features
        // ═══════════════════════════════════════════════════════════════
        double rel_r5 = 0;
        double rel_r20 = 0;

        if (MarketBars != null && MarketBars.Count >= n)
        {
            var marketCloses = MarketBars.TakeLast(n).Select(b => (double)b.Close).ToArray();
            double market_r5 = LogReturn(marketCloses, 5);
            double market_r20 = LogReturn(marketCloses, 20);

            rel_r5 = r5 - market_r5;
            rel_r20 = r20 - market_r20;
        }

        features.Add((float)rel_r5);
        features.Add((float)rel_r20);

        return features.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private static double LogReturn(double[] closes, int period, int offset = 0)
    {
        int n = closes.Length;
        if (n < period + offset + 1) return 0;

        double current = closes[n - 1 - offset];
        double past = closes[n - 1 - offset - period];

        if (past <= 0) return 0;
        return System.Math.Log(current / past);
    }

    private static double UpFraction(double[] closes, int period)
    {
        int n = closes.Length;
        if (n < period + 1) return 0.5;

        int upDays = 0;
        for (int i = n - period; i < n; i++)
        {
            if (closes[i] > closes[i - 1]) upDays++;
        }
        return (double)upDays / period;
    }

    private static double SignStreak(double[] closes)
    {
        int n = closes.Length;
        if (n < 2) return 0;

        int streak = 0;
        int sign = System.Math.Sign(closes[^1] - closes[^2]);

        for (int i = n - 1; i > 0; i--)
        {
            int daySign = System.Math.Sign(closes[i] - closes[i - 1]);
            if (daySign == sign && daySign != 0)
                streak++;
            else
                break;
        }

        return sign * streak;
    }

    private static double SMA(double[] values, int period)
    {
        int n = values.Length;
        if (n < period) return values[^1];
        return values.TakeLast(period).Average();
    }

    private static double StdDev(double[] values, int period)
    {
        int n = values.Length;
        if (n < period) return 0;

        var subset = values.TakeLast(period).ToArray();
        double mean = subset.Average();
        double variance = subset.Sum(v => (v - mean) * (v - mean)) / (period - 1);
        return System.Math.Sqrt(variance);
    }

    private static double SlopeNormalized(double[] closes, int maPeriod, int slopePeriod, double atr)
    {
        int n = closes.Length;
        if (n < maPeriod + slopePeriod) return 0;

        double ma_now = SMA(closes.TakeLast(maPeriod).ToArray(), maPeriod);
        double ma_past = SMA(closes.Skip(n - maPeriod - slopePeriod).Take(maPeriod).ToArray(), maPeriod);

        if (atr <= 0) return 0;
        return (ma_now - ma_past) / atr;
    }

    private static double CalculateAtr(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        if (n < period + 1) return 0;

        double sum = 0;
        for (int i = n - period; i < n; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];

            double tr = System.Math.Max(cur.High - cur.Low,
                        System.Math.Max(System.Math.Abs(cur.High - prev.Close),
                                 System.Math.Abs(cur.Low - prev.Close)));
            sum += tr;
        }

        return sum / period;
    }
}