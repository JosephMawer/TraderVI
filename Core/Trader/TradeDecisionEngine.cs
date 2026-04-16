using Core.ML;
using Core.ML.Engine.Profit;
using Core.Indicators;
using Core.Trader.Gates;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader;

public enum TradeDirection
{
    Buy,
    Sell,
    Hold
}

public record SignalResult(
    string Name,
    double Score,
    TradeDirection? Hint,
    string? Notes = null
);

public interface IStockSignalModel
{
    string Name { get; }
    SignalResult Evaluate(IReadOnlyList<DailyBar> history);
}

/// <summary>
/// Market regime information for filtering trades.
/// </summary>
public record MarketRegime(
    bool IsBenchmarkUptrend,        // XIU MA50 > MA200
    bool IsBenchmark20dPositive,    // XIU 20-day return > 0
    bool IsVolatilityNormal,        // Not in a vol spike
    double BenchmarkReturn20d,
    double BenchmarkMA50,
    double BenchmarkMA200
);

public class TradeDecisionEngine
{
    private readonly IReadOnlyList<IStockSignalModel> _patternModels;
    private readonly IReadOnlyList<UnifiedProfitSignalModel> _profitModels;

    public PositionSizer? Sizer { get; set; }
    public RankingMode RankingMode { get; set; } = RankingMode.Probability;

    /// <summary>
    /// Market regime for filtering. Set before evaluation.
    /// </summary>
    public MarketRegime? CurrentRegime { get; set; }

    /// <summary>
    /// If true, require benchmark uptrend to take longs.
    /// </summary>
    public bool RequireBenchmarkUptrend { get; set; } = true;

    public TradeDecisionEngine(IEnumerable<IStockSignalModel> patternModels)
        : this(patternModels, Enumerable.Empty<UnifiedProfitSignalModel>())
    {
    }

    public TradeDecisionEngine(
        IEnumerable<IStockSignalModel> patternModels,
        IEnumerable<UnifiedProfitSignalModel> profitModels)
    {
        _patternModels = patternModels.ToList();
        _profitModels = profitModels.ToList();
    }

    public (RankedPick? Pick, PositionSizeResult? Size) EvaluateBestPickAllIn(
        Dictionary<string, IReadOnlyList<DailyBar>> symbolBars,
        decimal availableCapital)
    {
        var ranked = EvaluateAndRank(symbolBars, topN: 25);

        if (ranked.Count == 0)
            return (null, null);

        var best = ranked.FirstOrDefault(p => p.Direction == TradeDirection.Buy) ?? ranked[0];

        var sizer = Sizer ?? new PositionSizer(availableCapital);
        sizer.AvailableCapital = availableCapital;

        var size = sizer.SizeSingleBestPick(best);

        return (best, size);
    }

    public TradeDecisionResult Evaluate(IReadOnlyList<DailyBar> history)
    {
        var patternSignals = _patternModels
            .Select(m => m.Evaluate(history))
            .ToList();

        var profitSignals = _profitModels
            .Select(m => m.Evaluate(history))
            .ToList();

        var regressionSignals = _profitModels
            .Where(m => m.ModelKind == ProfitModelKind.Regression)
            .Select(m => m.Evaluate(history))
            .ToList();

        var threeWaySignals = _profitModels
            .Where(m => m.ModelKind == ProfitModelKind.ThreeWayClassification)
            .Select(m => m.Evaluate(history))
            .ToList();

        var binarySignals = _profitModels
            .Where(m => m.ModelKind == ProfitModelKind.BinaryClassification)
            .Select(m => m.Evaluate(history))
            .ToList();

        var finalDirection = AggregateAllSignals(patternSignals, regressionSignals, threeWaySignals, binarySignals);

        double expectedReturn = regressionSignals.Any()
            ? regressionSignals.Average(s => s.Score)
            : 0;

        // FIX: Added dirProb (7th element) to match the updated tuple
        var (composite, directionProb, breakoutProb, volProb, downProb, directionEdge, dirProb) =
            GetCompositeScoreWithBreakdown(binarySignals);

        double confidence = composite > 0 ? composite
            : threeWaySignals.Any() ? threeWaySignals.Average(s => s.Score)
            : 0;

        var allSignals = patternSignals.Concat(profitSignals).ToList();

        return new TradeDecisionResult(
            Direction: finalDirection,
            ExpectedReturn: expectedReturn,
            Confidence: confidence,
            CompositeScore: composite,
            DirectionProbability: directionProb,
            DownProbability: downProb,
            DirectionEdge: directionEdge,
            PositionSize: null,
            Signals: allSignals);
    }

    /// <summary>
    /// Computes composite probability score from binary signals for ranking.
    /// Now includes BinaryDown10 for directional spread.
    /// </summary>
    private static (double Composite, double DirectionProb, double BreakoutProb, double VolExpansionProb, double DownProb, double DirectionEdge, double DirDrift)
        GetCompositeScoreWithBreakdown(IReadOnlyList<SignalResult> binarySignals)
    {
        if (!binarySignals.Any())
            return (0, 0, 0, 0, 0, 0, 0);

        // Breakout signal (AUC 0.81)
        double breakoutProb = binarySignals
            .FirstOrDefault(s => s.Name.Equals("BreakoutEnhanced", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        if (breakoutProb == 0)
        {
            breakoutProb = binarySignals
                .FirstOrDefault(s => s.Name.Equals("BreakoutPriorHigh10", StringComparison.OrdinalIgnoreCase))
                ?.Score ?? 0;
        }

        // Up probability (AUC 0.70)
        double upProb = binarySignals
            .FirstOrDefault(s => s.Name.Equals("BinaryUp10", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        // Down probability (NEW - veto signal)
        double downProb = binarySignals
            .FirstOrDefault(s => s.Name.Equals("BinaryDown10", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        // Setup-conditional direction (NEW - trained only on breakout bars)
        double dirProb = binarySignals
            .FirstOrDefault(s => s.Name.Equals("SetupDirUp5", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        double dirDownProb = binarySignals
            .FirstOrDefault(s => s.Name.Equals("SetupDirDown5", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        // Fallback to legacy if setup models not available
        if (dirProb == 0)
        {
            dirProb = binarySignals
                .FirstOrDefault(s => s.Name.StartsWith("BandedDirUp", StringComparison.OrdinalIgnoreCase))
                ?.Score ?? 0;
        }

        // Direction edge: combine all signals
        // Setup direction models are more reliable when available
        double directionEdge = (dirProb > 0 || dirDownProb > 0)
            ? (0.3 * dirProb) - (0.3 * dirDownProb) + (0.4 * upProb) - (0.4 * downProb)
            : upProb - downProb;  // Tail-only fallback

        // Volatility expansion (AUC 0.66)
        double volExpansionProb = binarySignals
            .FirstOrDefault(s => s.Name.Contains("VolExpansion", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        // Relative strength (AUC 0.65)
        double relStrengthProb = binarySignals
            .FirstOrDefault(s => s.Name.Contains("RelStrength", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        // Weighted composite (breakout-heavy, but penalized by down risk)
        // Note: downProb doesn't add to composite, it subtracts via directionEdge consideration
        double composite =
            (breakoutProb * 0.40) +         // AUC 0.81 - primary setup
            (upProb * 0.25) +               // AUC 0.70 - direction
            (volExpansionProb * 0.15) +     // AUC 0.66 - vol regime
            (relStrengthProb * 0.10) +      // AUC 0.65 - cross-sectional
            (binarySignals.Average(s => s.Score) * 0.10);  // ensemble

        return (composite, upProb, breakoutProb, volExpansionProb, downProb, directionEdge, dirProb);
    }

    private TradeDirection AggregateAllSignals(
        IReadOnlyList<SignalResult> patternSignals,
        IReadOnlyList<SignalResult> regressionSignals,
        IReadOnlyList<SignalResult> threeWaySignals,
        IReadOnlyList<SignalResult> binarySignals)
    {
        // ═══════════════════════════════════════════════════════════════
        // Strategy constants
        // ═══════════════════════════════════════════════════════════════
        const double minCompositeScore = 0.35;       // Minimum composite to consider Buy
        const double strongBuyThreshold = 0.50;      // Strong buy composite
        const double minBreakoutProb = 0.30;         // Setup filter: require breakout signal
        const double minUpProb = 0.25;               // Minimum P(up) for direction
        const double maxDownProb = 0.20;             // Veto if P(down) >= 20% (was 25%)
        const double minDirectionEdge = 0.05;        // Require Up - Down >= 5%

        // Get composite breakdown with direction edge
        var (compositeScore, upProb, breakoutProb, volExpansionProb, downProb, directionEdge, dirProb) =
            GetCompositeScoreWithBreakdown(binarySignals);

        // ─────────────────────────────────────────────────────────────────
        // REGIME FILTER: Don't take longs if benchmark is in downtrend
        // ─────────────────────────────────────────────────────────────────
        if (RequireBenchmarkUptrend && CurrentRegime != null)
        {
            if (!CurrentRegime.IsBenchmarkUptrend && !CurrentRegime.IsBenchmark20dPositive)
            {
                // Bearish market regime → Hold
                return TradeDirection.Hold;
            }
        }

        // --- Patterns (light confirmation only) ---
        var patternHints = patternSignals
            .Where(s => s.Hint.HasValue && s.Hint != TradeDirection.Hold)
            .Select(s => s.Hint!.Value)
            .ToList();

        int patternBuys = patternHints.Count(h => h == TradeDirection.Buy);
        int patternSells = patternHints.Count(h => h == TradeDirection.Sell);
        bool patternsNotBearish = patternSells <= patternBuys;

        // ═══════════════════════════════════════════════════════════════
        // Decision logic (new: direction edge based)
        // ═══════════════════════════════════════════════════════════════

        // ─────────────────────────────────────────────────────────────────
        // VETO: High down probability → skip even if setup looks good
        // ─────────────────────────────────────────────────────────────────
        if (downProb >= maxDownProb)
        {
            return TradeDirection.Hold;
        }

        // ─────────────────────────────────────────────────────────────────
        // SETUP FILTER: Require meaningful breakout signal
        // ─────────────────────────────────────────────────────────────────
        bool hasSetup = breakoutProb >= minBreakoutProb;

        // ─────────────────────────────────────────────────────────────────
        // DIRECTION FILTER: Use spread (Up - Down) not just Up alone
        // ─────────────────────────────────────────────────────────────────
        bool hasDirectionEdge = directionEdge >= minDirectionEdge;
        bool hasUpConfirmation = upProb >= minUpProb;

        // ─────────────────────────────────────────────────────────────────
        // BUY CONDITIONS
        // ─────────────────────────────────────────────────────────────────

        // Strong buy: high composite + setup + positive edge + patterns ok
        if (compositeScore >= strongBuyThreshold &&
            hasSetup &&
            hasDirectionEdge &&
            patternsNotBearish)
        {
            return TradeDirection.Buy;
        }

        // Standard buy: moderate composite + setup + direction confirmation
        if (compositeScore >= minCompositeScore &&
            hasSetup &&
            hasUpConfirmation &&
            hasDirectionEdge &&
            patternsNotBearish)
        {
            return TradeDirection.Buy;
        }

        // Fallback buy: very strong breakout + clear direction edge
        if (breakoutProb >= 0.60 &&
            directionEdge >= 0.10 &&
            downProb < 0.20 &&
            patternsNotBearish)
        {
            return TradeDirection.Buy;
        }

        return TradeDirection.Hold;
    }

    public List<RankedPick> EvaluateAndRank(
        Dictionary<string, IReadOnlyList<DailyBar>> symbolBars,
        int topN = 10)
    {
        var picks = new List<RankedPick>();

        foreach (var (symbol, history) in symbolBars)
        {
            var result = Evaluate(history);

            picks.Add(new RankedPick(
                Symbol: symbol,
                Direction: result.Direction,
                ExpectedReturn: result.ExpectedReturn,
                Confidence: result.Confidence,
                CompositeScore: result.CompositeScore,
                DirectionProbability: result.DirectionProbability,
                DownProbability: result.DownProbability,
                DirectionEdge: result.DirectionEdge,
                Signals: result.Signals));
        }

        // Rank by direction edge within buys, then composite
        return RankingMode switch
        {
            RankingMode.Probability => picks
                .OrderByDescending(p => p.Direction == TradeDirection.Buy ? 1 : 0)
                .ThenByDescending(p => p.DirectionEdge)  // NEW: rank by edge
                .ThenByDescending(p => p.CompositeScore)
                .Take(topN)
                .ToList(),

            RankingMode.ExpectedReturn => picks
                .OrderByDescending(p => p.Direction == TradeDirection.Buy ? 2 : p.Direction == TradeDirection.Hold ? 1 : 0)
                .ThenByDescending(p => p.ExpectedReturn)
                .ThenByDescending(p => p.Confidence)
                .Take(topN)
                .ToList(),

            _ => picks.Take(topN).ToList()
        };
    }

    public List<SizedPick> EvaluateRankAndSize(
        Dictionary<string, IReadOnlyList<DailyBar>> symbolBars,
        decimal availableCapital,
        int maxPositions = 5)
    {
        var rankedPicks = EvaluateAndRank(symbolBars, topN: maxPositions * 2);

        var sizer = Sizer ?? new PositionSizer(availableCapital);
        sizer.AvailableCapital = availableCapital;

        return sizer.SizePortfolio(rankedPicks, maxPositions);
    }

    /// <summary>
    /// Computes market regime from benchmark bars.
    /// </summary>
    public static MarketRegime ComputeRegime(IReadOnlyList<DailyBar> benchmarkBars)
    {
        if (benchmarkBars.Count < 200)
        {
            return new MarketRegime(
                IsBenchmarkUptrend: true,
                IsBenchmark20dPositive: true,
                IsVolatilityNormal: true,
                BenchmarkReturn20d: 0,
                BenchmarkMA50: 0,
                BenchmarkMA200: 0);
        }

        // MA calculations
        double ma50 = benchmarkBars.TakeLast(50).Average(b => (double)b.Close);
        double ma200 = benchmarkBars.TakeLast(200).Average(b => (double)b.Close);

        // 20-day return
        double price20Ago = benchmarkBars[^21].Close;
        double currentPrice = benchmarkBars[^1].Close;
        double return20d = (currentPrice - price20Ago) / price20Ago;

        // Volatility check (ATR spike)
        double atr14 = CalculateAtrPercent(benchmarkBars.TakeLast(15).ToList());
        double atr60 = CalculateAtrPercent(benchmarkBars.TakeLast(61).ToList());
        bool isVolNormal = atr14 < atr60 * 1.5; // Not in vol spike

        return new MarketRegime(
            IsBenchmarkUptrend: ma50 > ma200,
            IsBenchmark20dPositive: return20d > 0,
            IsVolatilityNormal: isVolNormal,
            BenchmarkReturn20d: return20d,
            BenchmarkMA50: ma50,
            BenchmarkMA200: ma200);
    }

    private static double CalculateAtrPercent(IReadOnlyList<DailyBar> bars)
    {
        if (bars.Count < 2) return 0;

        double sum = 0;
        for (int i = 1; i < bars.Count; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];
            double prevClose = prev.Close == 0 ? 1 : prev.Close;
            double tr = System.Math.Max(cur.High - cur.Low,
                        System.Math.Max(System.Math.Abs(cur.High - prevClose),
                                 System.Math.Abs(cur.Low - prevClose)));
            sum += tr / prevClose;
        }

        return sum / (bars.Count - 1);
    }
}

public enum RankingMode
{
    Probability,
    ExpectedReturn
}

public record TradeDecisionResult(
    TradeDirection Direction,
    double ExpectedReturn,
    double Confidence,
    double CompositeScore,
    double DirectionProbability,
    double DownProbability,
    double DirectionEdge,
    PositionSizeResult? PositionSize,
    IReadOnlyList<SignalResult> Signals,
    IReadOnlyList<GateTraceEntry>? GateTrace = null
);

public record RankedPick(
    string Symbol,
    TradeDirection Direction,
    double ExpectedReturn,
    double Confidence,
    double CompositeScore,
    double DirectionProbability,
    double DownProbability,
    double DirectionEdge,
    IReadOnlyList<SignalResult> Signals,
    IReadOnlyList<GateTraceEntry>? GateTrace = null
);