using Core.Db;
using Core.ML.Engine;
using Core.ML.Engine.Training.Classifiers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

Console.WriteLine("=== ML Training Pipeline (Hercules) ===\n");

const string symbol = "CEU";
var modelsRoot = @"C:\Users\joseph.mawer\OneDrive\Joseph\Programming\ML\_models";
Directory.CreateDirectory(modelsRoot);

Console.WriteLine($"Loading daily bars for {symbol}...");
var bars = await new QuoteRepository().GetDailyBarsAsync(symbol);
Console.WriteLine($"Bars loaded: {bars.Count}\n");

var registry = new ModelRegistryRepository();

// ═══════════════════════════════════════════════════════════════════
// Train Trend30 (medium-term trend)
// ═══════════════════════════════════════════════════════════════════
//await TrainTrendModel<TrendWindow30>(
//    bars, lookback: 30, symbol, modelsRoot, registry,
//    taskType: TaskTypes.Trend30,
//    modelName: "Trend30 (Daily) - v1");

// ═══════════════════════════════════════════════════════════════════
// Train Trend10 (short-term momentum)
// ═══════════════════════════════════════════════════════════════════
await TrainTrendModel<TrendWindow10>(
    bars, lookback: 10, symbol, modelsRoot, registry,
    taskType: TaskTypes.Trend10,
    modelName: "Trend10 (Daily) - v1");

Console.WriteLine("\n=== Training Complete ===");

// ───────────────────────────────────────────────────────────────────
static async Task TrainTrendModel<TWindow>(
    List<Core.ML.DailyBar> bars,
    int lookback,
    string symbol,
    string modelsRoot,
    ModelRegistryRepository registry,
    string taskType,
    string modelName)
    where TWindow : class, ITrendPatternWindow, new()
{
    Console.WriteLine($"─── Training {taskType} ───");

    if (bars.Count < lookback + 5)
    {
        Console.WriteLine($"[SKIP] Not enough bars for {taskType}. Need at least {lookback + 5}.\n");
        return;
    }

    var modelPath = Path.Combine(modelsRoot, $"{taskType.ToLower()}_classifier.zip");

    TrendClassifier.Train<TWindow>(
        bars,
        lookback,
        modelPath,
        TrendDatasetBuilder.Build<TWindow>);

    await registry.InsertModel(
        name: modelName,
        taskType: taskType,
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
        notes: $"Trend direction (slope > 0) over {lookback}-day window. Trained on {symbol}.");

    Console.WriteLine();
}