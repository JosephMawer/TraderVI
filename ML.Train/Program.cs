using Core.Db;
using Core.ML;
using Core.ML.Engine.Patterns;
using Core.ML.Engine.Patterns.Features;
using Core.ML.Engine.Profit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("=== ML Training Pipeline (Hercules) ===\n");

var modelsRoot = @"C:\Users\joseph.mawer\OneDrive\Joseph\Programming\ML\_models";
Directory.CreateDirectory(modelsRoot);

//const int maxSymbols = 180; // <-- iterate fast
const int maxSymbols = 494; // <-- full training run

// XIU = iShares S&P/TSX 60 Index ETF (TSX benchmark for market context)
const string MarketBenchmarkSymbol = "XIU";

var quoteRepo = new QuoteRepository();
var registry = new ModelRegistryRepository();
var experimentRepo = new ModelExperimentRepository();

// ═══════════════════════════════════════════════════════════════════
// Load symbol universe
// ═══════════════════════════════════════════════════════════════════
var symbols = (await new SymbolsRepository().GetEquitiesAsync())
    .Select(s => s.Symbol)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Take(maxSymbols)
    .ToList();

Console.WriteLine($"Loading bars for {symbols.Count} symbols (equities only)...\n");

var barsBySymbol = new Dictionary<string, List<DailyBar>>();
foreach (var sym in symbols)
{
    var bars = await quoteRepo.GetDailyBarsAsync(sym);
    if (bars.Count > 0)
        barsBySymbol[sym] = bars;
}

Console.WriteLine($"Loaded {barsBySymbol.Count} symbols with data.\n");

// ═══════════════════════════════════════════════════════════════════
// Load market benchmark (XIU) for market context features
// ═══════════════════════════════════════════════════════════════════
List<DailyBar>? marketBars = null;
var xiuBars = await quoteRepo.GetDailyBarsAsync(MarketBenchmarkSymbol);
if (xiuBars.Count > 0)
{
    marketBars = xiuBars;
    Console.WriteLine($"Loaded {MarketBenchmarkSymbol} benchmark: {marketBars.Count} bars ({marketBars[0].Date:yyyy-MM-dd} to {marketBars[^1].Date:yyyy-MM-dd})\n");
}
else
{
    Console.WriteLine($"⚠️  Warning: {MarketBenchmarkSymbol} not found in database. Market context features will be zeros.\n");
}

// ═══════════════════════════════════════════════════════════════════
// PART 1: Rule-Based Pattern Presence Report (informational only)
//
// Patterns are deterministic detectors — they are NOT trained. This section
// just reports how often each pattern fires across the universe so we can
// sanity-check detector logic without committing anything to the registry.
// See docs/design-rules.md → "Rule-Based Pattern Signals".
// ═══════════════════════════════════════════════════════════════════
Console.WriteLine(new string('═', 60));
Console.WriteLine("RULE-BASED PATTERN PRESENCE REPORT (no training)");
Console.WriteLine(new string('═', 60) + "\n");

foreach (var pattern in PatternRegistry.All)
{
    int windowsEvaluated = 0;
    int windowsPositive = 0;

    foreach (var (sym, bars) in barsBySymbol)
    {
        if (bars.Count < pattern.Lookback) continue;

        for (int end = pattern.Lookback; end <= bars.Count; end++)
        {
            var window = bars.GetRange(end - pattern.Lookback, pattern.Lookback);
            if (pattern.Detector.Detect(window))
                windowsPositive++;
            windowsEvaluated++;
        }
    }

    double rate = windowsEvaluated > 0 ? (double)windowsPositive / windowsEvaluated : 0;
    Console.WriteLine($"  [{pattern.TaskType,-14}] lookback={pattern.Lookback,3}  windows={windowsEvaluated,8:N0}  present={windowsPositive,8:N0}  rate={rate:P2}  semantics={pattern.Semantics}");
}
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════
// PART 2: Train Profit Prediction Models
// ═══════════════════════════════════════════════════════════════════
Console.WriteLine(new string('═', 60));
Console.WriteLine("PROFIT PREDICTION MODELS");
Console.WriteLine(new string('═', 60) + "\n");

foreach (var profitModel in ProfitModelRegistry.All)
{
    var suffix = profitModel.ModelKind switch
    {
        ProfitModelKind.Regression => "regression",
        ProfitModelKind.ThreeWayClassification => "3way",
        ProfitModelKind.BinaryClassification => "binary",
        _ => "model"
    };

    var modelPath = Path.Combine(modelsRoot, $"{profitModel.TaskType.ToLower()}_{suffix}.zip");

    // Inject market context if the feature builder supports it
    if (profitModel.FeatureBuilder is MarketContextFeatureBuilder mcfb && marketBars != null)
    {
        mcfb.MarketBars = marketBars;
        Console.WriteLine($"[{profitModel.TaskType}] Injecting {MarketBenchmarkSymbol} market context ({marketBars.Count} bars)");
    }

    if (profitModel.FeatureBuilder is EnhancedFeatureBuilder efb && marketBars != null)
    {
        efb.MarketBars = marketBars;
        Console.WriteLine($"[{profitModel.TaskType}] Injecting {MarketBenchmarkSymbol} market context into EnhancedFeatureBuilder");
    }

    if (profitModel.FeatureBuilder is TrendMomentumFeatureBuilder tmfb && marketBars != null)
    {
        tmfb.MarketBars = marketBars;
        Console.WriteLine($"[{profitModel.TaskType}] Injecting {MarketBenchmarkSymbol} market context into TrendMomentumFeatureBuilder");
    }

    // Inject market context into labeler if it supports it
    if (profitModel.Labeler is RelativeStrengthContinuationLabeler rsLabeler && marketBars != null)
    {
        rsLabeler.MarketBars = marketBars;
        Console.WriteLine($"[{profitModel.TaskType}] Injecting {MarketBenchmarkSymbol} into labeler");
    }

    var result = UnifiedProfitTrainer.Train(profitModel, barsBySymbol, modelPath);

    // Determine metrics based on model kind
    double? auc = profitModel.ModelKind == ProfitModelKind.BinaryClassification ? result.PrimaryMetric : null;
    double? rmse = profitModel.ModelKind == ProfitModelKind.Regression ? result.PrimaryMetric : null;
    double? mae = profitModel.ModelKind == ProfitModelKind.Regression ? result.SecondaryMetric : null;

    // Log experiment to DB (even if failed)
    await experimentRepo.InsertExperiment(
        taskType: profitModel.TaskType,
        experimentName: $"{profitModel.TaskType} ({profitModel.ModelKind})",
        labelDefinition: profitModel.Labeler.Name,
        featureSet: profitModel.FeatureBuilder.Name,
        featureCount: profitModel.FeatureBuilder.FeatureCount(profitModel.Lookback),
        trainWindows: result.TrainWindows,
        testWindows: result.TestWindows,
        auc: auc,
        f1AtDefault: profitModel.ModelKind == ProfitModelKind.BinaryClassification ? result.SecondaryMetric : null,
        f1AtOptimal: result.F1AtOptimal,
        optimalThreshold: result.OptimalThreshold,
        precisionAtOpt: result.PrecisionAtOptimal,
        recallAtOpt: result.RecallAtOptimal,
        rmse: rmse,
        mae: mae,
        decision: result.Success ? "Keep" : "Skip",
        notes: $"Lookback={profitModel.Lookback}. Horizon={profitModel.HorizonBars}d. Symbols={result.SymbolsUsed}");

    if (result.Success)
    {
        // Use optimal threshold if available (binary models), else fall back to configured threshold.
        var thresholdBuy = profitModel.ModelKind == ProfitModelKind.BinaryClassification
            ? (result.OptimalThreshold ?? (profitModel.BuyThresholdPercent / 100.0))
            : (profitModel.BuyThresholdPercent / 100.0);

        var thresholdSell = profitModel.SellThresholdPercent / 100.0;

        var notes = $"Profit prediction. Horizon={profitModel.HorizonBars}d. Trained on {result.SymbolsUsed} symbols.";
        if (profitModel.ModelKind == ProfitModelKind.BinaryClassification && result.OptimalThreshold.HasValue)
        {
            notes = $"Horizon={profitModel.HorizonBars}d. AUC={result.PrimaryMetric:0.###}. OptThresh={result.OptimalThreshold:0.##}. F1@opt={result.F1AtOptimal:P1}";
        }

        await registry.InsertModel(
            name: $"{profitModel.TaskType} ({profitModel.ModelKind})",
            taskType: profitModel.TaskType,
            modelKind: profitModel.ModelKind.ToString(),
            family: "Profit",
            timeFrame: "Daily",
            lookbackBars: profitModel.Lookback,
            horizonBars: profitModel.HorizonBars,
            inputSchema: $"{profitModel.TaskType}_profit",
            featureSet: profitModel.FeatureBuilder.Name,
            zipPath: modelPath,
            thresholdBuy: thresholdBuy,
            thresholdSell: thresholdSell,
            isEnabled: true,
            trainedFromUtc: null,
            trainedToUtc: null,
            notes: notes);

        Console.WriteLine($"  ✓ Logged experiment and model for {profitModel.TaskType}\n");
    }
    else
    {
        Console.WriteLine($"  ✗ Logged failed experiment for {profitModel.TaskType}\n");
    }
}

Console.WriteLine(new string('═', 60));
Console.WriteLine("=== Training Complete ===");
Console.WriteLine($"All experiments logged to [dbo].[ModelExperiment]");
Console.WriteLine($"All models registered to [dbo].[ModelRegistry]");