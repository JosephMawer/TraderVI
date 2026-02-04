using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns.Features;

/// <summary>
/// "Starter Pack" feature builder with proven alpha factors for momentum/trend following:
/// 
/// TREND (6 features):
/// - MA20 slope (normalized)
/// - MA50 slope (normalized)
/// - Price / MA20
/// - Price / MA50
/// - MA20 / MA50 (golden/death cross proximity)
/// - Trend alignment score (1 if price > MA20 > MA50, -1 if opposite, 0 mixed)
/// 
/// BREAKOUT (6 features):
/// - Close / 20-day high (proximity to breakout)
/// - Close / 55-day high (proximity to breakout)
/// - Pullback depth from 20-day high / ATR
/// - Days since 20-day high
/// - Close / 20-day low
/// - Range compression (current ATR / ATR 20 bars ago)
/// 
/// MOMENTUM (6 features):
/// - RSI(14)
/// - RSI above 50 (binary)
/// - RSI above 70 (overbought binary)
/// - RSI below 30 (oversold binary)
/// - Rate of change 10d
/// - Rate of change 20d
/// 
/// VOLUME (4 features):
/// - Volume / 20-day SMA volume
/// - Volume trend (slope of volume over 10 bars)
/// - Up-day volume ratio (avg vol on up days / avg vol on down days)
/// - Volume breakout (volume > 2x average)
/// 
/// RELATIVE STRENGTH (4 features):
/// - RelPerf_10 vs XIU (if market bars provided)
/// - RelPerf_20 vs XIU
/// - Up-day fraction over 10 days
/// - Up-day fraction over 20 days
/// 
/// Total: 26 features
/// </summary>
public sealed class TrendMomentumFeatureBuilder : IFeatureBuilder
{
    public string Name => "TrendMomentum";

    private const int FeatureCountFixed = 26;

    public int FeatureCount(int lookback) => FeatureCountFixed;

    /// <summary>
    /// Optional: Market bars (XIU) for relative strength features.
    /// </summary>
    public IReadOnlyList<DailyBar>? MarketBars { get; set; }

    public float[] Build(IReadOnlyList<DailyBar> windowBars)
    {
        var features = new float[FeatureCountFixed];

        if (windowBars.Count < 2)
            return features;

        int n = windowBars.Count;
        double lastClose = windowBars[^1].Close == 0 ? 1.0 : windowBars[^1].Close;

        // ═══════════════════════════════════════════════════════════════
        // TREND FEATURES (indices 0-5)
        // ═══════════════════════════════════════════════════════════════

        double ma20 = SimpleMovingAverage(windowBars, 20);
        double ma50 = SimpleMovingAverage(windowBars, 50);

        // MA slopes (change in MA over last 5 bars, normalized by price)
        double ma20Slope = MaSlope(windowBars, 20, 5) / lastClose;
        double ma50Slope = MaSlope(windowBars, 50, 5) / lastClose;

        // Price position relative to MAs
        double priceOverMa20 = ma20 > 0 ? lastClose / ma20 : 1.0;
        double priceOverMa50 = ma50 > 0 ? lastClose / ma50 : 1.0;

        // MA relationship
        double ma20OverMa50 = ma50 > 0 ? ma20 / ma50 : 1.0;

        // Trend alignment: +1 if price > MA20 > MA50, -1 if price < MA20 < MA50, 0 otherwise
        double trendAlignment = 0;
        if (lastClose > ma20 && ma20 > ma50) trendAlignment = 1;
        else if (lastClose < ma20 && ma20 < ma50) trendAlignment = -1;

        features[0] = (float)ma20Slope;
        features[1] = (float)ma50Slope;
        features[2] = (float)priceOverMa20;
        features[3] = (float)priceOverMa50;
        features[4] = (float)ma20OverMa50;
        features[5] = (float)trendAlignment;

        // ═══════════════════════════════════════════════════════════════
        // BREAKOUT FEATURES (indices 6-11)
        // ═══════════════════════════════════════════════════════════════

        double high20 = windowBars.TakeLast(System.Math.Min(20, n)).Max(b => (double)b.High);
        double high55 = windowBars.TakeLast(System.Math.Min(55, n)).Max(b => (double)b.High);
        double low20 = windowBars.TakeLast(System.Math.Min(20, n)).Min(b => (double)b.Low);

        double closeOverHigh20 = high20 > 0 ? lastClose / high20 : 1.0;
        double closeOverHigh55 = high55 > 0 ? lastClose / high55 : 1.0;
        double closeOverLow20 = low20 > 0 ? lastClose / low20 : 1.0;

        // Pullback depth from 20-day high, normalized by ATR
        double atr14 = CalculateAtr(windowBars, 14);
        double pullbackDepth = high20 > 0 && atr14 > 0 ? (high20 - lastClose) / atr14 : 0;

        // Days since 20-day high (normalized)
        int daysSinceHigh20 = DaysSinceHigh(windowBars, 20);
        double daysSinceHigh20Norm = daysSinceHigh20 / 20.0;

        // Range compression: current ATR vs ATR 20 bars ago
        double atrRecent = CalculateAtr(windowBars.TakeLast(System.Math.Min(7, n)).ToList(), 7);
        double atrOlder = n > 20 ? CalculateAtr(windowBars.Skip(n - 27).Take(7).ToList(), 7) : atrRecent;
        double rangeCompression = atrOlder > 0 ? atrRecent / atrOlder : 1.0;

        features[6] = (float)closeOverHigh20;
        features[7] = (float)closeOverHigh55;
        features[8] = (float)System.Math.Clamp(pullbackDepth, 0, 10);
        features[9] = (float)daysSinceHigh20Norm;
        features[10] = (float)closeOverLow20;
        features[11] = (float)System.Math.Clamp(rangeCompression, 0.1, 10);

        // ═══════════════════════════════════════════════════════════════
        // MOMENTUM FEATURES (indices 12-17)
        // ═══════════════════════════════════════════════════════════════

        double rsi14 = CalculateRsi(windowBars, 14);
        double rsiNorm = rsi14 / 100.0;  // Normalize to 0-1
        double rsiAbove50 = rsi14 > 50 ? 1.0 : 0.0;
        double rsiOverbought = rsi14 > 70 ? 1.0 : 0.0;
        double rsiOversold = rsi14 < 30 ? 1.0 : 0.0;

        double roc10 = ReturnOverDays(windowBars, 10);
        double roc20 = ReturnOverDays(windowBars, 20);

        features[12] = (float)rsiNorm;
        features[13] = (float)rsiAbove50;
        features[14] = (float)rsiOverbought;
        features[15] = (float)rsiOversold;
        features[16] = (float)System.Math.Clamp(roc10, -0.5, 0.5);
        features[17] = (float)System.Math.Clamp(roc20, -0.5, 0.5);

        // ═══════════════════════════════════════════════════════════════
        // VOLUME FEATURES (indices 18-21)
        // ═══════════════════════════════════════════════════════════════

        double volSma20 = windowBars.TakeLast(System.Math.Min(20, n)).Average(b => (double)b.Volume);
        double lastVolume = windowBars[^1].Volume;
        double relativeVolume = volSma20 > 0 ? lastVolume / volSma20 : 1.0;

        double volumeSlope = VolumeSlope(windowBars, 10);

        double upDayVolRatio = UpDayVolumeRatio(windowBars, 20);

        double volumeBreakout = relativeVolume > 2.0 ? 1.0 : 0.0;

        features[18] = (float)System.Math.Clamp(relativeVolume, 0, 10);
        features[19] = (float)System.Math.Clamp(volumeSlope, -1, 1);
        features[20] = (float)System.Math.Clamp(upDayVolRatio, 0, 5);
        features[21] = (float)volumeBreakout;

        // ═══════════════════════════════════════════════════════════════
        // RELATIVE STRENGTH FEATURES (indices 22-25)
        // ═══════════════════════════════════════════════════════════════

        double relPerf10 = 0;
        double relPerf20 = 0;

        if (MarketBars != null && MarketBars.Count > 0)
        {
            var alignedMarket = AlignMarketBars(windowBars, MarketBars);
            if (alignedMarket.Count >= 10)
            {
                double stockRet10 = ReturnOverDays(windowBars, 10);
                double mktRet10 = ReturnOverDays(alignedMarket, 10);
                relPerf10 = stockRet10 - mktRet10;

                double stockRet20 = ReturnOverDays(windowBars, 20);
                double mktRet20 = ReturnOverDays(alignedMarket, 20);
                relPerf20 = stockRet20 - mktRet20;
            }
        }

        double upDayFrac10 = UpDayFraction(windowBars, 10);
        double upDayFrac20 = UpDayFraction(windowBars, 20);

        features[22] = (float)System.Math.Clamp(relPerf10, -0.5, 0.5);
        features[23] = (float)System.Math.Clamp(relPerf20, -0.5, 0.5);
        features[24] = (float)upDayFrac10;
        features[25] = (float)upDayFrac20;

        return features;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════════

    private static double SimpleMovingAverage(IReadOnlyList<DailyBar> bars, int period)
    {
        if (bars.Count == 0) return 0;
        int p = System.Math.Min(period, bars.Count);
        return bars.TakeLast(p).Average(b => (double)b.Close);
    }

    private static double MaSlope(IReadOnlyList<DailyBar> bars, int maPeriod, int slopePeriod)
    {
        int n = bars.Count;
        if (n < maPeriod + slopePeriod) return 0;

        // Calculate MA at current bar and slopePeriod bars ago
        double maNow = bars.TakeLast(maPeriod).Average(b => (double)b.Close);
        double maOld = bars.Skip(n - maPeriod - slopePeriod).Take(maPeriod).Average(b => (double)b.Close);

        return maNow - maOld;
    }

    private static double CalculateAtr(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        if (n < 2) return 0;

        int p = System.Math.Min(period, n - 1);
        double sumTr = 0;

        for (int i = n - p; i < n; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];

            double prevClose = prev.Close == 0 ? 1.0 : prev.Close;
            double tr = System.Math.Max(cur.High - cur.Low,
                        System.Math.Max(System.Math.Abs(cur.High - prevClose),
                                 System.Math.Abs(cur.Low - prevClose)));
            sumTr += tr;
        }

        return sumTr / p;
    }

    private static double CalculateRsi(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        if (n < period + 1) return 50; // neutral default

        double avgGain = 0;
        double avgLoss = 0;

        // Initial average
        for (int i = n - period; i < n; i++)
        {
            double change = bars[i].Close - bars[i - 1].Close;
            if (change > 0) avgGain += change;
            else avgLoss += System.Math.Abs(change);
        }

        avgGain /= period;
        avgLoss /= period;

        if (avgLoss == 0) return 100;

        double rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    private static double ReturnOverDays(IReadOnlyList<DailyBar> bars, int daysBack)
    {
        if (bars.Count == 0) return 0;

        int n = bars.Count;
        int i0 = System.Math.Max(0, n - 1 - daysBack);

        double p0 = bars[i0].Close == 0 ? 1.0 : bars[i0].Close;
        double p1 = bars[^1].Close == 0 ? 1.0 : bars[^1].Close;

        return (p1 - p0) / p0;
    }

    private static int DaysSinceHigh(IReadOnlyList<DailyBar> bars, int lookback)
    {
        int n = bars.Count;
        int period = System.Math.Min(lookback, n);

        double maxHigh = double.MinValue;
        int daysSince = 0;

        for (int i = n - period; i < n; i++)
        {
            if (bars[i].High > maxHigh)
            {
                maxHigh = bars[i].High;
                daysSince = n - 1 - i;
            }
        }

        return daysSince;
    }

    private static double VolumeSlope(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        int p = System.Math.Min(period, n);
        if (p < 2) return 0;

        var volumes = bars.TakeLast(p).Select(b => (double)b.Volume).ToArray();
        double avgVol = volumes.Average();
        if (avgVol == 0) return 0;

        // Simple linear regression slope
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < p; i++)
        {
            sx += i;
            sy += volumes[i];
            sxx += i * i;
            sxy += i * volumes[i];
        }

        double denom = (p * sxx) - (sx * sx);
        if (denom == 0) return 0;

        double slope = ((p * sxy) - (sx * sy)) / denom;
        return slope / avgVol; // Normalize by average volume
    }

    private static double UpDayVolumeRatio(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        int p = System.Math.Min(period, n - 1);
        if (p < 2) return 1.0;

        double upVol = 0, downVol = 0;
        int upCount = 0, downCount = 0;

        for (int i = n - p; i < n; i++)
        {
            double change = bars[i].Close - bars[i - 1].Close;
            if (change > 0)
            {
                upVol += bars[i].Volume;
                upCount++;
            }
            else if (change < 0)
            {
                downVol += bars[i].Volume;
                downCount++;
            }
        }

        double avgUp = upCount > 0 ? upVol / upCount : 0;
        double avgDown = downCount > 0 ? downVol / downCount : 1;

        return avgDown > 0 ? avgUp / avgDown : 1.0;
    }

    private static double UpDayFraction(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        int p = System.Math.Min(period, n - 1);
        if (p < 1) return 0.5;

        int upDays = 0;
        for (int i = n - p; i < n; i++)
        {
            if (bars[i].Close > bars[i - 1].Close)
                upDays++;
        }

        return (double)upDays / p;
    }

    private static List<DailyBar> AlignMarketBars(IReadOnlyList<DailyBar> stockBars, IReadOnlyList<DailyBar> marketBars)
    {
        if (stockBars.Count == 0) return [];

        var windowEnd = stockBars[^1].Date;
        var windowStart = stockBars[0].Date;

        return marketBars
            .Where(b => b.Date >= windowStart && b.Date <= windowEnd)
            .OrderBy(b => b.Date)
            .ToList();
    }
}