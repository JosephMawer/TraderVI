using Core.ML;
using Core.ML.Engine.Profit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader
{
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

            // 5. Compute aggregated decision
            var finalDirection = AggregateAllSignals(patternSignals, regressionSignals, threeWaySignals);

            // 6. Compute expected return (from regression models)
            double expectedReturn = regressionSignals.Any()
                ? regressionSignals.Average(s => s.Score)
                : 0;

            // 7. Compute confidence (from 3-way classifiers)
            double confidence = threeWaySignals.Any()
                ? threeWaySignals.Average(s => s.Score)
                : 0;

            var allSignals = patternSignals.Concat(profitSignals).ToList();

            return new TradeDecisionResult(
                Direction: finalDirection,
                ExpectedReturn: expectedReturn,
                Confidence: confidence,
                Signals: allSignals);
        }

        private TradeDirection AggregateAllSignals(
            IReadOnlyList<SignalResult> patternSignals,
            IReadOnlyList<SignalResult> regressionSignals,
            IReadOnlyList<SignalResult> threeWaySignals)
        {
            // ═══════════════════════════════════════════════════════════════
            // Multi-layer decision logic:
            // 1. If regression predicts negative expected return → Hold/Sell
            // 2. If 3-way classifier says Sell → Sell
            // 3. If patterns + regression + 3-way all agree Buy → Buy
            // 4. Otherwise → Hold
            // ═══════════════════════════════════════════════════════════════

            // Get average expected return from regression
            double avgExpectedReturn = regressionSignals.Any()
                ? regressionSignals.Average(s => s.Score)
                : 0;

            // Get dominant 3-way direction
            var threeWayHints = threeWaySignals
                .Where(s => s.Hint.HasValue)
                .Select(s => s.Hint!.Value)
                .ToList();

            TradeDirection? threeWayConsensus = null;
            if (threeWayHints.Count > 0)
            {
                var counts = threeWayHints.GroupBy(h => h).ToDictionary(g => g.Key, g => g.Count());
                threeWayConsensus = counts.OrderByDescending(kv => kv.Value).First().Key;
            }

            // Get pattern consensus
            var patternHints = patternSignals
                .Where(s => s.Hint.HasValue && s.Hint != TradeDirection.Hold)
                .Select(s => s.Hint!.Value)
                .ToList();

            int patternBuys = patternHints.Count(h => h == TradeDirection.Buy);
            int patternSells = patternHints.Count(h => h == TradeDirection.Sell);

            // ─────────────────────────────────────────────────────────────
            // Decision logic
            // ─────────────────────────────────────────────────────────────

            // Strong sell: 3-way says sell AND expected return is negative
            if (threeWayConsensus == TradeDirection.Sell && avgExpectedReturn < 0)
                return TradeDirection.Sell;

            // Strong buy: 3-way says buy AND expected return is positive AND patterns mostly bullish
            if (threeWayConsensus == TradeDirection.Buy &&
                avgExpectedReturn > 0.01 &&
                patternBuys >= patternSells)
                return TradeDirection.Buy;

            // Moderate buy: expected return strongly positive, patterns bullish
            if (avgExpectedReturn > 0.02 && patternBuys > patternSells)
                return TradeDirection.Buy;

            // Moderate sell: expected return strongly negative, patterns bearish
            if (avgExpectedReturn < -0.02 && patternSells > patternBuys)
                return TradeDirection.Sell;

            // Default: Hold
            return TradeDirection.Hold;
        }

        /// <summary>
        /// Evaluates multiple symbols and returns ranked picks.
        /// </summary>
        public List<RankedPick> EvaluateAndRank(Dictionary<string, IReadOnlyList<DailyBar>> symbolBars, int topN = 10)
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

            // Rank by: direction preference (Buy > Hold > Sell), then expected return
            return picks
                .OrderByDescending(p => p.Direction == TradeDirection.Buy ? 2 : p.Direction == TradeDirection.Hold ? 1 : 0)
                .ThenByDescending(p => p.ExpectedReturn)
                .ThenByDescending(p => p.Confidence)
                .Take(topN)
                .ToList();
        }
    }

    public record TradeDecisionResult(
        TradeDirection Direction,
        double ExpectedReturn,
        double Confidence,
        IReadOnlyList<SignalResult> Signals
    );

    public record RankedPick(
        string Symbol,
        TradeDirection Direction,
        double ExpectedReturn,
        double Confidence,
        IReadOnlyList<SignalResult> Signals
    );
}