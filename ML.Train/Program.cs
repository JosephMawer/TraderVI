using Core.Db;
using Core.ML;
using Core.ML.Engine.Patterns;
using Core.ML.Engine.Profit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("=== ML Training Pipeline (Hercules) ===\n");

var modelsRoot = @"C:\Users\joseph.mawer\OneDrive\Joseph\Programming\ML\_models";
Directory.CreateDirectory(modelsRoot);

const int maxSymbols = 200;

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
// PART 2: Train Profit Prediction Models (new)
// ═══════════════════════════════════════════════════════════════════
Console.WriteLine(new string('═', 60));
Console.WriteLine("PROFIT PREDICTION MODELS");
Console.WriteLine(new string('═', 60) + "\n");

foreach (var profitModel in ProfitModelRegistry.All)
{
    var suffix = profitModel.ModelKind == ProfitModelKind.Regression ? "regression" : "3way";
    var modelPath = Path.Combine(modelsRoot, $"{profitModel.TaskType.ToLower()}_{suffix}.zip");

    var result = UnifiedProfitTrainer.Train(profitModel, barsBySymbol, modelPath);

    if (result.Success)
    {
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
            thresholdBuy: profitModel.BuyThresholdPercent / 100.0,
            thresholdSell: profitModel.SellThresholdPercent / 100.0,
            isEnabled: true,
            trainedFromUtc: null,
            trainedToUtc: null,
            notes: $"Profit prediction. Horizon={profitModel.HorizonBars}d. Trained on {result.SymbolsUsed} symbols.");
    }
}

Console.WriteLine(new string('═', 60));
Console.WriteLine("=== Training Complete ===");