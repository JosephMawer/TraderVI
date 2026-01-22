using Core.Db;
using Core.ML.Engine;
using Core.ML.Engine.Training.Classifiers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("=== ML Training Pipeline (Hercules) ===\n");

var modelsRoot = @"C:\Users\joseph.mawer\OneDrive\Joseph\Programming\ML\_models";
Directory.CreateDirectory(modelsRoot);

const int lookback = 10;
const int maxSymbols = 200; // keep runs bounded while iterating
const int minBarsRequired = lookback + 5;

var quoteRepo = new QuoteRepository();
var registry = new ModelRegistryRepository();

var symbols = await new SymbolsRepository().GetSymbols();
var symbolList = symbols
    .Select(s => s.Symbol)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Take(maxSymbols)
    .ToList();

Console.WriteLine($"Loading daily bars for {symbolList.Count} tickers...\n");

var allWindows = new List<TrendWindow10>();
int loadedSymbols = 0;
int skippedSymbols = 0;

foreach (var sym in symbolList)
{
    var bars = await quoteRepo.GetDailyBarsAsync(sym);

    if (bars.Count < minBarsRequired)
    {
        skippedSymbols++;
        continue;
    }

    loadedSymbols++;

    var windows = TrendDatasetBuilder.Build<TrendWindow10>(bars, lookback);
    allWindows.AddRange(windows);
}

Console.WriteLine($"Symbols loaded: {loadedSymbols}, skipped: {skippedSymbols}");
Console.WriteLine($"Total Trend{lookback} windows: {allWindows.Count:N0}\n");

if (allWindows.Count < 200)
{
    Console.WriteLine("[SKIP] Not enough total windows to train a stable model.");
    return;
}

var modelPath = Path.Combine(modelsRoot, $"{TaskTypes.Trend10.ToLower()}_classifier.zip");

TrendClassifier.TrainFromWindows(
    allWindows,
    lookback,
    modelPath);

await registry.InsertModel(
    name: "Trend10 (Daily) - v2 (multi-symbol)",
    taskType: TaskTypes.Trend10,
    modelKind: "BinaryClassification",
    family: "Structure",
    timeFrame: "Daily",
    lookbackBars: lookback,
    horizonBars: 0,
    inputSchema: $"TrendWindow{lookback}_v1",
    featureSet: null,
    zipPath: modelPath,
    thresholdBuy: 0.60,
    thresholdSell: 0.40,
    isEnabled: true,
    trainedFromUtc: null,
    trainedToUtc: null,
    notes: $"Trend direction (slope > 0) over {lookback}-day window. Trained on {loadedSymbols} symbols.");

Console.WriteLine("\n=== Training Complete ===");