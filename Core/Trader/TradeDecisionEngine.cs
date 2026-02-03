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

        // 8. Aggregate confidence (3-way confidence)
        double confidence = threeWaySignals.Any()
            ? threeWaySignals.Average(s => s.Score)
            : 0;

        // 9. Calculate position size if sizer is configured
        PositionSizeResult? positionSize = null;
        //if (Sizer != null)
        //{
        //    positionSize = Sizer.Calculate(finalDirection, expectedReturn, confidence);
        //}

        var allSignals = patternSignals.Concat(profitSignals).ToList();

        return new TradeDecisionResult(
            Direction: finalDirection,
            ExpectedReturn: expectedReturn,
            Confidence: confidence,
            PositionSize: positionSize,
            Signals: allSignals);
    }

    private TradeDirection AggregateAllSignals(
        IReadOnlyList<SignalResult> patternSignals,
        IReadOnlyList<SignalResult> regressionSignals,
        IReadOnlyList<SignalResult> threeWaySignals,
        IReadOnlyList<SignalResult> binarySignals)
    {
        // Strategy constants (start conservative; tune with Delphi output)
        const double minDirectionBuyConfidence = 0.50;

        const double weightBreakoutPriorHigh = 1.50;   // increase (AUC 0.81)
        const double weightExpectedReturn = 0.20;      // decrease (R² negative)
        const double weightBreakoutAtr = 0.00;         // disable

        // --- Regression (ranking hint) ---
        double avgExpectedReturn = regressionSignals.Any()
            ? regressionSignals.Average(s => s.Score)
            : 0;

        // --- 3-way direction consensus + confidence ---
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

        double threeWayConfidence = threeWaySignals.Any()
            ? threeWaySignals.Average(s => s.Score)
            : 0;

        // --- Patterns (light confirmation only) ---
        var patternHints = patternSignals
            .Where(s => s.Hint.HasValue && s.Hint != TradeDirection.Hold)
            .Select(s => s.Hint!.Value)
            .ToList();

        int patternBuys = patternHints.Count(h => h == TradeDirection.Buy);
        int patternSells = patternHints.Count(h => h == TradeDirection.Sell);

        // --- Binary event hints ---
        double breakoutPriorHighProb = binarySignals
            .FirstOrDefault(s => string.Equals(s.Name, "BreakoutPriorHigh10", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        double breakoutAtrProb = binarySignals
            .FirstOrDefault(s => string.Equals(s.Name, "BreakoutAtr10", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        // --- Decision logic ---

        // Strong sell: 3-way says sell AND expected return hint is negative
        if (threeWayConsensus == TradeDirection.Sell && avgExpectedReturn < 0)
            return TradeDirection.Sell;

        // Hard gate for buy: direction model must be Buy with enough confidence.
        // (This reduces churn + avoids buying into low-quality setups.)
        if (threeWayConsensus != TradeDirection.Buy || threeWayConfidence < minDirectionBuyConfidence)
            return TradeDirection.Hold;

        // Weighted score for buy-eligible candidates.
        // Note: expected return is mapped to [0..1] using a simple clamp so it can combine with probabilities.
        double expectedReturnHint01 = Clamp01((avgExpectedReturn - 0.00) / 0.10); // 0%->0, 10%->1 cap

        double buyScore =
            (weightBreakoutPriorHigh * breakoutPriorHighProb) +
            (weightExpectedReturn * expectedReturnHint01) +
            (weightBreakoutAtr * breakoutAtrProb);

        bool patternsNotBearish = patternSells <= patternBuys;

        // Require non-bearish patterns and a small minimum score to call it a Buy.
        // The score threshold is intentionally low to start; refine after Delphi runs.
        if (patternsNotBearish && buyScore >= 0.60)
            return TradeDirection.Buy;

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
                Signals: result.Signals));
        }

        // Rank by: direction preference (Buy > Hold > Sell), then expected return, then confidence
        return picks
            .OrderByDescending(p => p.Direction == TradeDirection.Buy ? 2 : p.Direction == TradeDirection.Hold ? 1 : 0)
            .ThenByDescending(p => p.ExpectedReturn)
            .ThenByDescending(p => p.Confidence)
            .Take(topN)
            .ToList();
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

public record TradeDecisionResult(
    TradeDirection Direction,
    double ExpectedReturn,
    double Confidence,
    PositionSizeResult? PositionSize,
    IReadOnlyList<SignalResult> Signals
);

public record RankedPick(
    string Symbol,
    TradeDirection Direction,
    double ExpectedReturn,
    double Confidence,
    IReadOnlyList<SignalResult> Signals
);