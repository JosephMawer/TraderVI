using Core.Db;
using Core.ML;
using Core.ML.Engine.Patterns;
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
Console.WriteLine(new string('═', 60));

// ═══════════════════════════════════════════════════════════════════
// Train all registered patterns
// ═══════════════════════════════════════════════════════════════════
foreach (var pattern in PatternRegistry.All)
{
    var modelPath = Path.Combine(modelsRoot, $"{pattern.TaskType.ToLower()}_classifier.zip");

    var result = UnifiedPatternTrainer.Train(pattern, barsBySymbol, modelPath);

    if (result.Success)
    {
        await registry.InsertModel(
            name: $"{pattern.TaskType} (Daily)",
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
            notes: $"Trained on {result.SymbolsUsed} symbols. Accuracy={result.Accuracy:0.####}, AUC={result.Auc:0.####}");
    }
}

Console.WriteLine(new string('═', 60));
Console.WriteLine("=== Training Complete ===");