using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns.Features;

/// <summary>
/// Extends AtrVolatilityBreakout features with market context:
/// - XIU (TSX index ETF) momentum and relative strength
/// - Market volatility regime (rolling std of XIU)
/// - Relative performance vs market (beta-adjusted)
/// - Relative strength features (stock vs index outperformance)
/// </summary>
public sealed class MarketContextFeatureBuilder : IFeatureBuilder
{
    public string Name => "MarketContext";

    private readonly AtrVolatilityBreakoutFeatureBuilder _baseBuilder = new();

    // Market context features: 12 base + 8 relative strength = 20 additional
    private const int MarketFeatureCount = 20;

    public int FeatureCount(int lookback) => _baseBuilder.FeatureCount(lookback) + MarketFeatureCount;

    /// <summary>
    /// Market bars (XIU) must be passed via this property before calling Build().
    /// </summary>
    public IReadOnlyList<DailyBar>? MarketBars { get; set; }

    public float[] Build(IReadOnlyList<DailyBar> windowBars)
    {
        var baseFeatures = _baseBuilder.Build(windowBars);
        var marketFeatures = BuildMarketFeatures(windowBars);

        var combined = new float[baseFeatures.Length + marketFeatures.Length];
        baseFeatures.CopyTo(combined, 0);
        marketFeatures.CopyTo(combined, baseFeatures.Length);

        return combined;
    }

    private float[] BuildMarketFeatures(IReadOnlyList<DailyBar> windowBars)
    {
        var features = new float[MarketFeatureCount];

        if (MarketBars == null || MarketBars.Count < windowBars.Count || windowBars.Count < 2)
            return features;

        // Align market bars to window bars by date
        var windowEnd = windowBars[^1].Date;
        var windowStart = windowBars[0].Date;

        var alignedMarket = MarketBars
            .Where(b => b.Date >= windowStart && b.Date <= windowEnd)
            .OrderBy(b => b.Date)
            .ToList();

        if (alignedMarket.Count < 5)
            return features;

        int n = alignedMarket.Count;

        // ═══════════════════════════════════════════════════════════════
        // ORIGINAL MARKET CONTEXT FEATURES (indices 0-11)
        // ═══════════════════════════════════════════════════════════════

        double mktRet1 = ReturnOverDays(alignedMarket, 1);
        double mktRet5 = ReturnOverDays(alignedMarket, 5);
        double mktRet10 = ReturnOverDays(alignedMarket, System.Math.Min(10, n - 1));
        double mktCumRet = ReturnOverDays(alignedMarket, n - 1);

        var mktReturns = ComputeReturns(alignedMarket);
        double mktVolatility = StdDev(mktReturns);

        double stockCumRet = CumulativeReturn(windowBars);
        double relativeStrength = stockCumRet - mktCumRet;

        var stockReturns = ComputeReturns(windowBars);
        double beta = stockReturns.Length > 5 && mktReturns.Length > 5
            ? ComputeBeta(stockReturns, mktReturns)
            : 1.0;

        double mktSma10 = SimpleMovingAverage(alignedMarket, 10);
        double mktSma20 = SimpleMovingAverage(alignedMarket, 20);
        double mktTrend = mktSma20 > 0 ? (mktSma10 - mktSma20) / mktSma20 : 0;

        double volRegime = mktVolatility > 0.015 ? 1.0 : 0.0;

        double mktHigh20 = alignedMarket.TakeLast(System.Math.Min(20, n)).Max(b => (double)b.High);
        double mktClose = alignedMarket[^1].Close;
        double mktDrawdown = mktHigh20 > 0 ? (mktClose - mktHigh20) / mktHigh20 : 0;

        features[0] = (float)mktRet1;
        features[1] = (float)mktRet5;
        features[2] = (float)mktRet10;
        features[3] = (float)mktCumRet;
        features[4] = (float)mktVolatility;
        features[5] = (float)relativeStrength;
        features[6] = (float)beta;
        features[7] = (float)mktTrend;
        features[8] = (float)volRegime;
        features[9] = (float)mktDrawdown;
        features[10] = (float)(stockCumRet > mktCumRet ? 1.0 : 0.0);
        features[11] = (float)(mktRet5 > 0 ? 1.0 : 0.0);

        // ═══════════════════════════════════════════════════════════════
        // NEW: RELATIVE STRENGTH CONTINUATION FEATURES (indices 12-19)
        // ═══════════════════════════════════════════════════════════════

        // Relative performance at multiple horizons (stock return - index return)
        double stockRet5 = ReturnOverDays(windowBars, 5);
        double stockRet10 = ReturnOverDays(windowBars, System.Math.Min(10, windowBars.Count - 1));
        double stockRet20 = ReturnOverDays(windowBars, System.Math.Min(20, windowBars.Count - 1));

        double relPerf5 = stockRet5 - mktRet5;
        double relPerf10 = stockRet10 - mktRet10;
        double relPerf20 = stockRet20 - ReturnOverDays(alignedMarket, System.Math.Min(20, n - 1));

        // Residual return (alpha) = actual return - (beta * market return)
        // This isolates stock-specific performance from market exposure
        double expectedReturnFromBeta = beta * mktCumRet;
        double residualReturn = stockCumRet - expectedReturnFromBeta;

        // Relative strength momentum (is rel perf accelerating or decelerating?)
        double relPerfMomentum = relPerf5 - (relPerf20 / 4); // recent vs avg

        // Consistency: how often did stock beat index in recent bars?
        double outperformanceRate = ComputeOutperformanceRate(windowBars, alignedMarket);

        // Relative volatility (stock vol / market vol) - low = defensive, high = aggressive
        double stockVol = StdDev(stockReturns);
        double relativeVolatility = mktVolatility > 0 ? stockVol / mktVolatility : 1.0;

        // Information ratio proxy (excess return / tracking error)
        double trackingError = ComputeTrackingError(stockReturns, mktReturns);
        double infoRatioProxy = trackingError > 0 ? relativeStrength / trackingError : 0;

        features[12] = (float)relPerf5;
        features[13] = (float)relPerf10;
        features[14] = (float)relPerf20;
        features[15] = (float)residualReturn;
        features[16] = (float)relPerfMomentum;
        features[17] = (float)outperformanceRate;
        features[18] = (float)relativeVolatility;
        features[19] = (float)System.Math.Clamp(infoRatioProxy, -5, 5); // clamp extreme values

        return features;
    }

    /// <summary>
    /// Computes how often the stock outperformed the index on a daily basis.
    /// </summary>
    private static double ComputeOutperformanceRate(
        IReadOnlyList<DailyBar> stockBars,
        IReadOnlyList<DailyBar> marketBars)
    {
        int minLen = System.Math.Min(stockBars.Count, marketBars.Count);
        if (minLen < 2) return 0.5;

        int outperformDays = 0;
        int totalDays = 0;

        for (int i = 1; i < minLen; i++)
        {
            double stockRet = (stockBars[i].Close - stockBars[i - 1].Close) / stockBars[i - 1].Close;
            double mktRet = (marketBars[i].Close - marketBars[i - 1].Close) / marketBars[i - 1].Close;

            if (stockRet > mktRet)
                outperformDays++;
            totalDays++;
        }

        return totalDays > 0 ? outperformDays / (double)totalDays : 0.5;
    }

    /// <summary>
    /// Computes tracking error (std dev of return differences).
    /// </summary>
    private static double ComputeTrackingError(double[] stockReturns, double[] marketReturns)
    {
        int len = System.Math.Min(stockReturns.Length, marketReturns.Length);
        if (len < 2) return 0;

        var diffs = new double[len];
        for (int i = 0; i < len; i++)
            diffs[i] = stockReturns[i] - marketReturns[i];

        return StdDev(diffs);
    }

    private static double ReturnOverDays(IReadOnlyList<DailyBar> bars, int days)
    {
        if (bars.Count < 2 || days < 1) return 0;
        int idx = System.Math.Max(0, bars.Count - 1 - days);
        double prev = bars[idx].Close == 0 ? 1.0 : bars[idx].Close;
        double cur = bars[^1].Close == 0 ? 1.0 : bars[^1].Close;
        return (cur - prev) / prev;
    }

    private static double CumulativeReturn(IReadOnlyList<DailyBar> bars)
    {
        if (bars.Count < 2) return 0;
        double first = bars[0].Close == 0 ? 1.0 : bars[0].Close;
        double last = bars[^1].Close == 0 ? 1.0 : bars[^1].Close;
        return (last - first) / first;
    }

    private static double[] ComputeReturns(IReadOnlyList<DailyBar> bars)
    {
        if (bars.Count < 2) return [];
        var returns = new double[bars.Count - 1];
        for (int i = 1; i < bars.Count; i++)
        {
            double prev = bars[i - 1].Close == 0 ? 1.0 : bars[i - 1].Close;
            double cur = bars[i].Close == 0 ? 1.0 : bars[i].Close;
            returns[i - 1] = (cur - prev) / prev;
        }
        return returns;
    }

    private static double ComputeBeta(double[] stockReturns, double[] marketReturns)
    {
        int len = System.Math.Min(stockReturns.Length, marketReturns.Length);
        if (len < 5) return 1.0;

        var sr = stockReturns.TakeLast(len).ToArray();
        var mr = marketReturns.TakeLast(len).ToArray();

        double covariance = Covariance(sr, mr);
        double variance = Variance(mr);

        return variance == 0 ? 1.0 : covariance / variance;
    }

    private static double Covariance(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length < 2) return 0;
        double mx = x.Average();
        double my = y.Average();
        return x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum() / (x.Length - 1);
    }

    private static double Variance(double[] values)
    {
        if (values.Length < 2) return 0;
        double mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
    }

    private static double StdDev(double[] values) => System.Math.Sqrt(Variance(values));

    private static double SimpleMovingAverage(IReadOnlyList<DailyBar> bars, int period)
    {
        int len = System.Math.Min(period, bars.Count);
        if (len == 0) return 0;
        return bars.TakeLast(len).Average(b => (double)b.Close);
    }
}