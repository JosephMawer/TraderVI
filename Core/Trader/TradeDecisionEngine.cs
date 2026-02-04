using Core.ML;
using Core.ML.Engine.Profit;
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

public class TradeDecisionEngine
{
    private readonly IReadOnlyList<IStockSignalModel> _patternModels;
    private readonly IReadOnlyList<UnifiedProfitSignalModel> _profitModels;

    public PositionSizer? Sizer { get; set; }

    /// <summary>
    /// Ranking mode: Probability (recommended) or ExpectedReturn (legacy).
    /// </summary>
    public RankingMode RankingMode { get; set; } = RankingMode.Probability;

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

        // Choose the best Buy if possible, else best overall
        var best = ranked.FirstOrDefault(p => p.Direction == TradeDirection.Buy) ?? ranked[0];

        var sizer = Sizer ?? new PositionSizer(availableCapital);
        sizer.AvailableCapital = availableCapital;

        var size = sizer.SizeSingleBestPick(best);

        return (best, size);
    }

    public TradeDecisionResult Evaluate(IReadOnlyList<DailyBar> history)
    {
        // 1. Evaluate pattern signals
        var patternSignals = _patternModels
            .Select(m => m.Evaluate(history))
            .ToList();

        // 2. Evaluate profit signals
        var profitSignals = _profitModels
            .Select(m => m.Evaluate(history))
            .ToList();

        // 3. Get regression predictions (expected return)
        var regressionSignals = _profitModels
            .Where(m => m.ModelKind == ProfitModelKind.Regression)
            .Select(m => m.Evaluate(history))
            .ToList();

        // 4. Get 3-way classification predictions
        var threeWaySignals = _profitModels
            .Where(m => m.ModelKind == ProfitModelKind.ThreeWayClassification)
            .Select(m => m.Evaluate(history))
            .ToList();

        // 5. Get binary event predictions (probabilities)
        var binarySignals = _profitModels
            .Where(m => m.ModelKind == ProfitModelKind.BinaryClassification)
            .Select(m => m.Evaluate(history))
            .ToList();

        // 6. Compute aggregated decision
        var finalDirection = AggregateAllSignals(patternSignals, regressionSignals, threeWaySignals, binarySignals);

        // 7. Aggregate expected return (regression hint)
        double expectedReturn = regressionSignals.Any()
            ? regressionSignals.Average(s => s.Score)
            : 0;

        // 8. Get composite breakdown for transparency
        var (composite, directionProb, breakoutProb, volProb) = GetCompositeScoreWithBreakdown(binarySignals);

        // Fallback confidence to 3-way if no binary signals
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
            PositionSize: null,
            Signals: allSignals);
    }

    /// <summary>
    /// Computes composite probability score from binary signals for ranking.
    /// Returns breakdown for transparency and gating.
    /// </summary>
    private static (double Composite, double DirectionProb, double BreakoutProb, double VolExpansionProb)
        GetCompositeScoreWithBreakdown(IReadOnlyList<SignalResult> binarySignals)
    {
        if (!binarySignals.Any())
            return (0, 0, 0, 0);

        // Primary: BreakoutPriorHigh10 (AUC 0.81) - most reliable event signal
        double breakoutProb = binarySignals
            .FirstOrDefault(s => s.Name.Equals("BreakoutPriorHigh10", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        // Direction: Prefer 4% (AUC 0.70), then 3% (0.67)
        double binaryUp4 = binarySignals
            .FirstOrDefault(s => s.Name.Contains("4pct", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        double binaryUp3 = binarySignals
            .FirstOrDefault(s => s.Name.Contains("3pct", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        // Use best available direction signal
        double directionProb = binaryUp4 > 0 ? binaryUp4
                             : binaryUp3 > 0 ? binaryUp3
                             : 0;

        // Volatility expansion (AUC 0.66) - big move expected
        double volExpansionProb = binarySignals
            .FirstOrDefault(s => s.Name.Contains("VolExpansion", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        double relStrengthProb = binarySignals
            .FirstOrDefault(s => s.Name.Contains("RelStrength", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        //// Weighted composite based on AUC strength
        //// Total weights = 0.45 + 0.30 + 0.15 + 0.10 = 1.0
        //double composite =
        //    (breakoutProb * 0.45) +         // AUC 0.81 - highest weight
        //    (directionProb * 0.30) +        // AUC 0.70 (4pct)
        //    (volExpansionProb * 0.15) +     // AUC 0.66
        //    (binarySignals.Average(s => s.Score) * 0.10);  // ensemble fallback

        // Revised weights (total = 1.0):
        double composite =
            (breakoutProb * 0.45) +         // AUC 0.81
            (directionProb * 0.25) +        // AUC 0.70
            (volExpansionProb * 0.12) +     // AUC 0.66
            (relStrengthProb * 0.08) +      // AUC 0.65 (new)
            (binarySignals.Average(s => s.Score) * 0.10);

        return (composite, directionProb, breakoutProb, volExpansionProb);
    }

    /// <summary>
    /// Legacy method for backward compatibility.
    /// </summary>
    private static double GetCompositeScore(IReadOnlyList<SignalResult> binarySignals)
        => GetCompositeScoreWithBreakdown(binarySignals).Composite;

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
        const double minDirectionProb = 0.25;        // Direction gate: must believe "up" is likely
        const double regressionVetoThreshold = -0.03; // Veto buy if regression says <-3%

        // Get composite breakdown
        var (compositeScore, directionProb, breakoutProb, volExpansionProb) =
            GetCompositeScoreWithBreakdown(binarySignals);

        // --- Regression (ranking hint + veto) ---
        double avgExpectedReturn = regressionSignals.Any()
            ? regressionSignals.Average(s => s.Score)
            : 0;

        // --- 3-way direction (fallback) ---
        var threeWayHints = threeWaySignals
            .Where(s => s.Hint.HasValue)
            .Select(s => s.Hint!.Value)
            .ToList();

        TradeDirection? threeWayConsensus = null;
        if (threeWayHints.Count > 0)
        {
            var counts = threeWayHints
                .GroupBy(h => h)
                .ToDictionary(g => g.Key, g => g.Count());

            threeWayConsensus = counts
                .OrderByDescending(kv => kv.Value)
                .First()
                .Key;
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
        // Decision logic
        // ═══════════════════════════════════════════════════════════════

        // Strong sell: 3-way says sell AND expected return is clearly negative
        if (threeWayConsensus == TradeDirection.Sell && avgExpectedReturn < -0.02)
            return TradeDirection.Sell;

        // ─────────────────────────────────────────────────────────────────
        // Direction gate: require meaningful upward probability
        // Prevents buying high-vol/breakout setups without directional confirmation
        // ─────────────────────────────────────────────────────────────────
        bool hasDirectionConfirmation = directionProb >= minDirectionProb;

        // ─────────────────────────────────────────────────────────────────
        // Regression veto: block buy if regression is strongly negative
        // ─────────────────────────────────────────────────────────────────
        bool regressionVeto = avgExpectedReturn < regressionVetoThreshold;

        if (regressionVeto)
            return TradeDirection.Hold;

        // ─────────────────────────────────────────────────────────────────
        // Primary buy gates
        // ─────────────────────────────────────────────────────────────────

        // Strong buy: high composite + direction confirmed + patterns not bearish
        if (compositeScore >= strongBuyThreshold &&
            hasDirectionConfirmation &&
            patternsNotBearish)
        {
            return TradeDirection.Buy;
        }

        // Standard buy: moderate composite + direction confirmed + non-negative regression
        if (compositeScore >= minCompositeScore &&
            hasDirectionConfirmation &&
            patternsNotBearish &&
            avgExpectedReturn >= 0)
        {
            return TradeDirection.Buy;
        }

        // Fallback buy: very strong breakout signal alone (BreakoutPriorHigh is directional by nature)
        // Only if breakout is very high AND direction is at least somewhat positive
        if (breakoutProb >= 0.70 &&
            directionProb >= 0.20 &&
            patternsNotBearish &&
            avgExpectedReturn >= -0.01)
        {
            return TradeDirection.Buy;
        }

        return TradeDirection.Hold;
    }

    private static double Clamp01(double value)
        => value <= 0 ? 0 : value >= 1 ? 1 : value;

    /// <summary>
    /// Evaluates multiple symbols and returns ranked picks.
    /// </summary>
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
                Signals: result.Signals));
        }

        // Rank based on RankingMode
        return RankingMode switch
        {
            RankingMode.Probability => picks
                .OrderByDescending(p => p.Direction == TradeDirection.Buy ? 1 : 0)
                .ThenByDescending(p => p.CompositeScore)
                .ThenByDescending(p => p.DirectionProbability)
                .ThenByDescending(p => p.ExpectedReturn)
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

    /// <summary>
    /// Evaluates multiple symbols, ranks them, and calculates position sizes.
    /// </summary>
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
}

public enum RankingMode
{
    /// <summary>
    /// Rank by composite probability score (recommended).
    /// Uses weighted average of binary model probabilities.
    /// </summary>
    Probability,

    /// <summary>
    /// Rank by expected return from regression (legacy).
    /// Note: regression R² is negative, so this provides weak signal.
    /// </summary>
    ExpectedReturn
}

public record TradeDecisionResult(
    TradeDirection Direction,
    double ExpectedReturn,
    double Confidence,
    double CompositeScore,
    double DirectionProbability,
    PositionSizeResult? PositionSize,
    IReadOnlyList<SignalResult> Signals
);

public record RankedPick(
    string Symbol,
    TradeDirection Direction,
    double ExpectedReturn,
    double Confidence,
    double CompositeScore,
    double DirectionProbability,
    IReadOnlyList<SignalResult> Signals
);