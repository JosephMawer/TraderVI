using Core.ML;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        double Score,          // e.g. probability, expected return, etc.
        TradeDirection? Hint,  // optional "if used alone, I'd do X"
        string? Notes = null
    );

    public interface IStockSignalModel
    {
        string Name { get; }

        SignalResult Evaluate(IReadOnlyList<DailyBar> history);
    }


    public class TradeDecisionEngine
    {
        private readonly IReadOnlyList<IStockSignalModel> _models;

        public TradeDecisionEngine(IEnumerable<IStockSignalModel> models)
        {
            _models = models.ToList();
        }

        public TradeDecisionResult Evaluate(IReadOnlyList<DailyBar> history)
        {
            var signals = _models
                .Select(m => m.Evaluate(history))
                .ToList();

            // TODO: your aggregation logic (weights, voting, thresholds, etc.)
            var finalDirection = AggregateSignals(signals);

            return new TradeDecisionResult(finalDirection, signals);
        }

        private TradeDirection AggregateSignals(IReadOnlyList<SignalResult> signals)
        {
            // Simple example:
            // - Sum "buy" scores minus "sell" scores, threshold into Buy/Sell/Hold
            // (You can get more sophisticated as you go)
            double buyScore = signals.Where(s => s.Hint == TradeDirection.Buy).Sum(s => s.Score);
            double sellScore = signals.Where(s => s.Hint == TradeDirection.Sell).Sum(s => s.Score);

            if (buyScore - sellScore > 0.1) return TradeDirection.Buy;
            if (sellScore - buyScore > 0.1) return TradeDirection.Sell;
            return TradeDirection.Hold;
        }
    }

    public record TradeDecisionResult(
        TradeDirection Direction,
        IReadOnlyList<SignalResult> Signals
    );

}
