#nullable enable

using Core.ML;
using Core.ML.Engine.Profit;
using Core.Trader;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TradeDecisionEngineLensTests
{
    [Fact]
    public void MultiLensEvaluationScoresEachSymbolOnceAndReusesTheFactsAcrossLenses()
    {
        var trend30 = new CountingPatternModel("Trend30", _ => Result("Trend30", .8, TradeDirection.Buy));
        var crossover = new CountingPatternModel("MaCrossover", _ => Result("MaCrossover", .8, TradeDirection.Buy));
        var profitModels = StandardProfitModels(
            setup: _ => .25,
            up: _ => .80,
            down: _ => .10,
            confirmation: _ => .50);
        var engine = new TradeDecisionEngine(
            [trend30, crossover],
            profitModels.Cast<IProfitSignalModel>());
        var bars = new Dictionary<string, IReadOnlyList<DailyBar>>
        {
            ["AAA"] = History(10),
            ["BBB"] = History(20)
        };

        var rankings = engine.EvaluateAndRank(
            [LensCatalog.Continuation(engine.Config), LensCatalog.Breakout(engine.Config)],
            bars,
            topN: bars.Count);

        trend30.EvaluationCount.ShouldBe(bars.Count);
        crossover.EvaluationCount.ShouldBe(bars.Count);
        profitModels.ShouldAllBe(model => model.EvaluationCount == bars.Count);

        foreach (string symbol in bars.Keys)
        {
            RankedPick continuation = rankings[RankingLens.Continuation].Single(pick => pick.Symbol == symbol);
            RankedPick breakout = rankings[RankingLens.Breakout].Single(pick => pick.Symbol == symbol);

            continuation.Direction.ShouldBe(TradeDirection.Buy);
            breakout.Direction.ShouldBe(TradeDirection.Hold);
            breakout.GateTrace!.Single(entry => entry.GateName == "Setup").Passed.ShouldBeFalse();
            continuation.GateTrace!.Single(entry => entry.GateName == "TrendConfirmation").Passed.ShouldBeTrue();

            continuation.CompositeScore.ShouldBe(breakout.CompositeScore);
            continuation.DirectionProbability.ShouldBe(breakout.DirectionProbability);
            continuation.DownProbability.ShouldBe(breakout.DownProbability);
            continuation.DirectionEdge.ShouldBe(breakout.DirectionEdge);
            ReferenceEquals(continuation.Signals, breakout.Signals).ShouldBeTrue();
        }
    }

    [Fact]
    public void LensCharacterizationKeepsContinuationRsFirstAndBreakoutEdgePlusRsFirst()
    {
        var profitModels = StandardProfitModels(
            setup: _ => .80,
            up: history => history[^1].Close < 15 ? .50 : .80,
            down: _ => .10,
            confirmation: _ => .50);
        var engine = new TradeDecisionEngine(
            [
                new CountingPatternModel("Trend30", _ => Result("Trend30", .8, TradeDirection.Buy)),
                new CountingPatternModel("MaCrossover", _ => Result("MaCrossover", .8, TradeDirection.Buy))
            ],
            profitModels.Cast<IProfitSignalModel>())
        {
            RsCompositeScores = new Dictionary<string, double>
            {
                ["LEADER"] = .50,
                ["EDGE"] = .25
            }
        };
        var bars = new Dictionary<string, IReadOnlyList<DailyBar>>
        {
            ["LEADER"] = History(10),
            ["EDGE"] = History(20)
        };

        var rankings = engine.EvaluateAndRank(
            [LensCatalog.Continuation(engine.Config), LensCatalog.Breakout(engine.Config)],
            bars,
            topN: bars.Count);

        rankings[RankingLens.Continuation].Select(pick => pick.Symbol)
            .ShouldBe(["LEADER", "EDGE"]);
        rankings[RankingLens.Breakout].Select(pick => pick.Symbol)
            .ShouldBe(["EDGE", "LEADER"]);
        rankings.Values.SelectMany(picks => picks)
            .ShouldAllBe(pick => pick.Direction == TradeDirection.Buy);
    }

    private static List<CountingProfitModel> StandardProfitModels(
        Func<IReadOnlyList<DailyBar>, double> setup,
        Func<IReadOnlyList<DailyBar>, double> up,
        Func<IReadOnlyList<DailyBar>, double> down,
        Func<IReadOnlyList<DailyBar>, double> confirmation) =>
    [
        new("BreakoutEnhanced", SignalRole.Setup, .40f, setup),
        new("BinaryUp10", SignalRole.DirectionUp, .25f, up),
        new("BinaryDown10", SignalRole.Veto, -.20f, down),
        new("VolExpansionRelative10", SignalRole.Confirmation, .15f, confirmation)
    ];

    private static IReadOnlyList<DailyBar> History(float latestClose) =>
    [
        new DailyBar
        {
            Date = new DateTime(2026, 9, 1),
            Open = latestClose,
            High = latestClose,
            Low = latestClose,
            Close = latestClose,
            Volume = 100_000
        }
    ];

    private static SignalResult Result(string name, double score, TradeDirection direction) =>
        new(name, score, direction);

    private sealed class CountingPatternModel(
        string name,
        Func<IReadOnlyList<DailyBar>, SignalResult> evaluate) : IStockSignalModel
    {
        public string Name { get; } = name;
        public int EvaluationCount { get; private set; }

        public SignalResult Evaluate(IReadOnlyList<DailyBar> history)
        {
            EvaluationCount++;
            return evaluate(history);
        }
    }

    private sealed class CountingProfitModel(
        string name,
        SignalRole role,
        float compositeWeight,
        Func<IReadOnlyList<DailyBar>, double> score) : IProfitSignalModel
    {
        public string Name { get; } = name;
        public SignalRole Role { get; } = role;
        public float CompositeWeight { get; } = compositeWeight;
        public int EvaluationCount { get; private set; }

        public SignalResult Evaluate(IReadOnlyList<DailyBar> history)
        {
            EvaluationCount++;
            double value = score(history);
            return new SignalResult(Name, value, TradeDirection.Buy);
        }
    }
}
