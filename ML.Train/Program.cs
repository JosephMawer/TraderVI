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

// ═══════════════════════════════════════════════════════════════════
// Load symbol universe
// ═══════════════════════════════════════════════════════════════════
var symbols = (await new SymbolsRepository().GetSymbols())
    .Select(s => s.Symbol)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Take(maxSymbols)
    .ToList();

Console.WriteLine($"Loading bars for {symbols.Count} symbols...\n");

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
// PART 1: Train Pattern Models (existing)
// ═══════════════════════════════════════════════════════════════════
Console.WriteLine(new string('═', 60));
Console.WriteLine("PATTERN DETECTION MODELS");
Console.WriteLine(new string('═', 60) + "\n");

foreach (var pattern in PatternRegistry.All)
{
    var modelPath = Path.Combine(modelsRoot, $"{pattern.TaskType.ToLower()}_classifier.zip");

    var result = UnifiedPatternTrainer.Train(pattern, barsBySymbol, modelPath);

    if (result.Success)
    {
        await registry.InsertModel(
            name: $"{pattern.TaskType} (Pattern)",
            taskType: pattern.TaskType,
            modelKind: "BinaryClassification",
            family: pattern.Category,
            timeFrame: "Daily",
            lookbackBars: pattern.Lookback,
            horizonBars: 0,
            inputSchema: $"{pattern.TaskType}_unified",
            featureSet: pattern.FeatureBuilder.Name,
            zipPath: modelPath,
            thresholdBuy: 0.60,
            thresholdSell: 0.40,
            isEnabled: true,
            trainedFromUtc: null,
            trainedToUtc: null,
            notes: $"Pattern detection. Trained on {result.SymbolsUsed} symbols. Acc={result.Accuracy:0.####}");
    }
}

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

    // Inject market context into labeler if it supports it
    if (profitModel.Labeler is RelativeStrengthContinuationLabeler rsLabeler && marketBars != null)
    {
        rsLabeler.MarketBars = marketBars;
        Console.WriteLine($"[{profitModel.TaskType}] Injecting {MarketBenchmarkSymbol} into labeler");
    }

    var result = UnifiedProfitTrainer.Train(profitModel, barsBySymbol, modelPath);

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
            thresholdBuy: thresholdBuy, // <-- uses OptimalThreshold when available
            thresholdSell: thresholdSell,
            isEnabled: true,
            trainedFromUtc: null,
            trainedToUtc: null,
            notes: notes);
    }
}

Console.WriteLine(new string('═', 60));
Console.WriteLine("=== Training Complete ===");