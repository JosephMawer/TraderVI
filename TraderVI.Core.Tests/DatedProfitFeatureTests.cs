#nullable enable
using Core.ML;
using Core.ML.Engine.Patterns.Features;
using Core.ML.Engine.Profit;
using Core.Runtime;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DatedProfitFeatureTests
{
    [Theory]
    [InlineData("BreakoutEnhanced")]
    [InlineData("BinaryUp10")]
    [InlineData("BinaryDown10")]
    [InlineData("VolExpansionRelative10")]
    public void ActualTrainingWindowAndPredictionInputHaveIdenticalFeatures(string task)
    {
        var stock = Bars(100, 1.01);
        var market = Bars(100, 1.005);
        var inputs = new ProfitFeatureInputs(market, market.Select(bar => bar.Date).ToArray());
        var definition = ProfitModelRegistry.GetByTaskType(task)!;
        var training = inputs.Bind(definition);
        var windows = UnifiedProfitTrainer.BuildProfitWindows(stock, training.Lookback, training.HorizonBars,
            training.FeatureBuilder, training.Labeler, training.ModelKind, training.RegressionReturnClamp);
        DateTime anchor = stock[75].Date;
        var trained = windows.Single(window => window.Date == anchor);
        var prediction = UnifiedProfitSignalModel.BuildPredictionInput(inputs.Bind(definition, anchor), stock.Take(76).ToArray());
        prediction.Features.ShouldBe(trained.Features);
        prediction.Features.Length.ShouldBe(definition.FeatureBuilder.FeatureCount(definition.Lookback));
        definition.FeatureBuilder.ShouldNotBeOfType<DatedProfitFeatureBuilder>();
    }

    [Fact]
    public void ExistingRelativeFeaturesReceiveExactNonzeroTenAndTwentySessionComparisons()
    {
        var stock = Bars(65, 1.01);
        var market = Bars(65, 1.005);
        var model = ProfitModelRegistry.GetByTaskType("BreakoutEnhanced")!;
        var inputs = new ProfitFeatureInputs(market, market.Select(bar => bar.Date).ToArray());
        float[] features = UnifiedProfitSignalModel.BuildPredictionInput(inputs.Bind(model, stock[^1].Date), stock).Features;
        int trendOffset = new AtrVolatilityBreakoutFeatureBuilder().FeatureCount(model.Lookback);
        float Expected(int sessions) => (float)(((double)stock[^1].Close - stock[^(sessions + 1)].Close) / stock[^(sessions + 1)].Close -
            ((double)market[^1].Close - market[^(sessions + 1)].Close) / market[^(sessions + 1)].Close);
        features[trendOffset + 22].ShouldBe(Expected(10));
        features[trendOffset + 23].ShouldBe(Expected(20));
        features[trendOffset + 22].ShouldBeGreaterThan(0f);
        new EnhancedFeatureBuilder().Build(stock.TakeLast(55).ToArray())[trendOffset + 22].ShouldBe(0f);
        // Complete legacy training observations give the same vector: the fix
        // changes input availability/alignment, not feature order or formulas.
        features.ShouldBe(new EnhancedFeatureBuilder { MarketBars = market }.Build(stock.TakeLast(55).ToArray()));
    }

    [Fact]
    public void FutureBenchmarkBarsAndLaterMutationCannotAlterAnEarlierInputSnapshot()
    {
        var stock = Bars(100, 1.01);
        var market = Bars(100, 1.005);
        DateTime anchor = stock[75].Date;
        var model = ProfitModelRegistry.GetByTaskType("BreakoutEnhanced")!;
        var input = new ProfitFeatureInputs(market, market.Select(bar => bar.Date).ToArray());
        var bound = input.Bind(model, anchor);
        float[] baseline = UnifiedProfitSignalModel.BuildPredictionInput(bound, stock.Take(76).ToArray()).Features;
        market[75].Close = 1; // The caller no longer owns the captured market facts.
        market[99].Close = float.NaN;
        UnifiedProfitSignalModel.BuildPredictionInput(bound, stock.Take(76).ToArray()).Features.ShouldBe(baseline);
        var historicalMarket = Bars(100, 1.005);
        historicalMarket[99].Close = float.NaN;
        var second = new ProfitFeatureInputs(historicalMarket, historicalMarket.Select(bar => bar.Date).ToArray());
        UnifiedProfitSignalModel.BuildPredictionInput(second.Bind(model, anchor), stock.Take(76).ToArray()).Features.ShouldBe(baseline);
        var failure = Should.Throw<ProfitFeatureInputException>(() => UnifiedProfitSignalModel.BuildPredictionInput(bound, stock));
        failure.SharedBenchmark.ShouldBeFalse(); // Future stock observations cannot become this prediction's inputs.
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("duplicate-at-boundary")]
    [InlineData("invalid")]
    [InlineData("reordered")]
    public void BadStockWindowIsRejectedWithoutAZeroPredictionOrShortenedDateWindow(string defect)
    {
        var stock = Bars(65, 1.01);
        var market = Bars(65, 1.005);
        var inputs = new ProfitFeatureInputs(market, market.Select(bar => bar.Date).ToArray());
        var model = inputs.Bind(ProfitModelRegistry.GetByTaskType("BreakoutEnhanced")!, market[^1].Date);
        if (defect == "missing") stock.RemoveAt(45);
        if (defect == "duplicate") stock.Insert(45, stock[45]);
        if (defect == "duplicate-at-boundary") stock.Insert(10, stock[10]);
        if (defect == "invalid") stock[45].Close = float.NaN;
        if (defect == "reordered") (stock[45], stock[46]) = (stock[46], stock[45]);
        var failure = Should.Throw<ProfitFeatureInputException>(() => UnifiedProfitSignalModel.BuildPredictionInput(model, stock));
        failure.SharedBenchmark.ShouldBeFalse();
        // Another stock with complete history still builds through the same context.
        UnifiedProfitSignalModel.BuildPredictionInput(model, Bars(65, 1.002)).Features.Length.ShouldBe(206);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    [InlineData("canonical-duplicate")]
    [InlineData("insufficient")]
    public void SharedXiuDefectFailsBeforeAnySymbolCanBeScored(string defect)
    {
        var market = Bars(65, 1.005);
        var sessions = market.Select(bar => bar.Date).ToList();
        DateTime anchor = sessions[^1];
        if (defect == "missing") market.RemoveAt(45);
        if (defect == "duplicate") market.Insert(45, market[45]);
        if (defect == "invalid") market[45].Close = 0;
        if (defect == "canonical-duplicate") sessions.Insert(45, sessions[45]);
        if (defect == "insufficient") sessions = sessions.TakeLast(54).ToList();
        var inputs = new ProfitFeatureInputs(market, sessions);
        Should.Throw<ProfitFeatureInputException>(() => inputs.ValidateBenchmark(anchor, 55)).SharedBenchmark.ShouldBeTrue();
    }

    [Fact]
    public void TrainingRejectsBadWindowsWithReasonsAndDoesNotMutateTheRegistryBuilder()
    {
        var stock = Bars(100, 1.01);
        var market = Bars(100, 1.005);
        stock.RemoveAt(60);
        var original = ProfitModelRegistry.GetByTaskType("BreakoutEnhanced")!;
        var originalMarket = ((EnhancedFeatureBuilder)original.FeatureBuilder).MarketBars;
        var model = new ProfitFeatureInputs(market, market.Select(bar => bar.Date).ToArray()).Bind(original);
        var reasons = new List<ProfitFeatureInputException>();
        var windows = UnifiedProfitTrainer.BuildProfitWindows(stock, model.Lookback, model.HorizonBars,
            model.FeatureBuilder, model.Labeler, model.ModelKind, model.RegressionReturnClamp, reasons.Add);
        windows.ShouldNotBeEmpty();
        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(failure => !failure.SharedBenchmark && failure.Reason == "StockFeatureSessionMismatch");
        windows.ShouldAllBe(window => window.Date < market[60].Date);
        ((EnhancedFeatureBuilder)original.FeatureBuilder).MarketBars.ShouldBeSameAs(originalMarket);
    }

    [Fact]
    public void TrainingRejectsDuplicateSessionEvenWhenOnlyOneCopyFitsInsideTheWindow()
    {
        var stock = Bars(100, 1.01);
        var market = Bars(100, 1.005);
        stock.Insert(10, stock[10]);
        var model = new ProfitFeatureInputs(market, market.Select(bar => bar.Date).ToArray())
            .Bind(ProfitModelRegistry.GetByTaskType("BreakoutEnhanced")!);
        var failures = new List<ProfitFeatureInputException>();
        var windows = UnifiedProfitTrainer.BuildProfitWindows(stock, model.Lookback, model.HorizonBars,
            model.FeatureBuilder, model.Labeler, model.ModelKind, model.RegressionReturnClamp, failures.Add);
        windows.ShouldNotContain(window => window.Date == market[64].Date);
        windows.ShouldContain(window => window.Date == market[65].Date);
        failures.ShouldContain(failure => failure.Reason == "StockFeatureBarDuplicate");
    }

    [Fact]
    public void ReportsExposeContractAndExcludedSymbolReasons()
    {
        var report = new DelphiReportBuilder
        {
            FeatureInputContract = ProfitFeatureInputs.Contract,
            FeatureInputExclusions = [new("SYNTHETIC", "StockFeatureSessionMismatch", new DateTime(2026, 4, 1))]
        };
        report.BuildDiagnostic().ShouldContain("SYNTHETIC: StockFeatureSessionMismatch (2026-04-01)");
        report.BuildSummary().ShouldContain(ProfitFeatureInputs.Contract);
        report.BuildSummary().ShouldContain("1 symbol(s) excluded");
    }

    internal static List<DailyBar> Bars(int count, double step)
    {
        var result = new List<DailyBar>();
        for (DateTime date = new(2026, 4, 1); result.Count < count; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            float close = (float)(100 * System.Math.Pow(step, result.Count));
            result.Add(new() { Date = date, Open = close, High = close + 1, Low = close - 1, Close = close, Volume = 100_000 + result.Count * 100 });
        }
        return result;
    }
}
