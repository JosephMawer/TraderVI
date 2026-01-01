using Core.ML;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader
{
    public record BacktestOptions(int WarmupBars = 30, double InitialCapital = 1.0);

    public record BacktestBarDiagnostics(
        DateTime Date,
        double Close,
        TradeDirection Decision,
        double Position,
        double RealizedReturn,
        double StrategyReturn,
        double Equity,
        double Drawdown,
        IReadOnlyList<SignalResult> Signals);

    public record BacktestResult(
        double FinalEquity,
        double TotalReturn,
        double MaxDrawdown,
        IReadOnlyList<BacktestBarDiagnostics> Bars);

    /// <summary>
    /// Minimal walk-forward backtest: runs the decision engine each bar, takes a position for the next bar,
    /// and tracks equity, drawdown, and per-bar diagnostics (signals + realized return).
    /// </summary>
    public class BacktestHarness
    {
        private readonly TradeDecisionEngine _decisionEngine;
        private readonly BacktestOptions _options;

        public BacktestHarness(TradeDecisionEngine decisionEngine, BacktestOptions? options = null)
        {
            _decisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
            _options = options ?? new BacktestOptions();
        }

        public BacktestResult Run(IReadOnlyList<DailyBar> history)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (history.Count <= _options.WarmupBars + 1)
                throw new ArgumentException("Not enough bars to warm up models and compute next-bar returns.", nameof(history));

            var diagnostics = new List<BacktestBarDiagnostics>();
            double equity = _options.InitialCapital;
            double peakEquity = equity;
            double maxDrawdown = 0.0;

            // Start after warmup so models can build their own features/windows without lookahead.
            for (int i = _options.WarmupBars; i < history.Count - 1; i++)
            {
                var subHistory = history.Take(i + 1).ToList();
                var decision = _decisionEngine.Evaluate(subHistory);

                var bar = history[i];
                var nextBar = history[i + 1];

                double realizedReturn = bar.Close == 0
                    ? 0.0
                    : (double)((nextBar.Close - bar.Close) / bar.Close);

                double position = DirectionToPosition(decision.Direction);
                double strategyReturn = position * realizedReturn;

                equity *= 1 + strategyReturn;
                peakEquity = System.Math.Max(peakEquity, equity);

                double drawdown = peakEquity <= 0 ? 0.0 : (peakEquity - equity) / peakEquity;
                maxDrawdown = System.Math.Max(maxDrawdown, drawdown);

                diagnostics.Add(new BacktestBarDiagnostics(
                    bar.Date,
                    (double)bar.Close,
                    decision.Direction,
                    position,
                    realizedReturn,
                    strategyReturn,
                    equity,
                    drawdown,
                    decision.Signals));
            }

            double totalReturn = equity - _options.InitialCapital;

            return new BacktestResult(
                FinalEquity: equity,
                TotalReturn: totalReturn,
                MaxDrawdown: maxDrawdown,
                Bars: diagnostics);
        }

        private static double DirectionToPosition(TradeDirection direction) =>
            direction switch
            {
                TradeDirection.Buy => 1.0,
                TradeDirection.Sell => -1.0,
                _ => 0.0
            };
    }
}
