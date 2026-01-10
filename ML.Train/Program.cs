using Core.Db;
using Core.ML.Engine.Training.Classifiers;
using System;
using System.IO;
using System.Threading.Tasks;

Console.WriteLine("=== ML Training Pipeline ===\n");

const string symbol = "CEU";
const int lookback = 30;

var modelsRoot = @"C:\Users\joseph.mawer\OneDrive\Joseph\Programming\ML\_models";
Directory.CreateDirectory(modelsRoot);

var modelPath = Path.Combine(modelsRoot, "trend30_classifier.zip");

Console.WriteLine($"Loading daily bars for {symbol}...");
var bars = await new QuoteRepository().GetDailyBarsAsync(symbol);

Console.WriteLine($"Bars loaded: {bars.Count}");
if (bars.Count < lookback + 5)
{
    Console.WriteLine($"Not enough bars to train Trend30. Need at least {lookback + 5}.");
    return;
}

Console.WriteLine("Training Trend30 classifier...");
Trend30Classifier.Train(bars, lookback, modelPath);

Console.WriteLine($"Model saved: {modelPath}");

Console.WriteLine("Inserting model into registry...");
var registry = new ModelRegistryRepository();
await registry.InsertModel(
    name: "Trend30 (Daily) - v1",
    taskType: "Trend30",
    modelKind: "BinaryClassification",
    family: "Structure",
    timeFrame: "Daily",
    lookbackBars: 30,
    horizonBars: 0,
    inputSchema: "PatternWindow30_v1",
    featureSet: null,
    zipPath: modelPath,
    thresholdBuy: 0.60,
    thresholdSell: 0.40,
    isEnabled: true,
    trainedFromUtc: null,
    trainedToUtc: null,
    notes: $"Trend direction (slope > 0) over a 30-day window. Trained on {symbol}.");

Console.WriteLine("Done.");

////using Core.ML;
////using Core.ML.Engine.Training.Classifiers;
////using System;
////using System.Collections.Generic;

////Console.WriteLine("Offline Training for prediction models");

////var dailyBars = new System.Collections.Generic.List<Core.ML.DailyBar>();
////var labels = new Dictionary<DateTime, bool>(); 
////var lookback = 30;

////var options = new ClassificationTrainerOptions(dailyBars, labels, lookback);
////ClassificationTrainerFactory.Train(ClassificationPattern.HeadAndShoulders, options);


//using Core.Db;
//using Core.ML;
//using Core.ML.Engine.Training.Classifiers;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;

//Console.WriteLine("=== ML Training Pipeline ===\n");

//// Step 1: Load historical data for a symbol
//Console.WriteLine("Loading historical data...");
//var bars = await LoadHistoricalBars("AAPL", startDate: new DateTime(2020, 1, 1));

//// Step 2: Generate labels (for now, using synthetic or rule-based)
//Console.WriteLine("Generating labels...");
//var labels = GenerateHeadAndShouldersLabels(bars);

//// Step 3: Build pattern dataset
//Console.WriteLine($"Building pattern dataset with {labels.Count} labeled windows...");
//int lookback = 30;
//var dataset = ClassificationUtilities.BuildPatternDataset(bars, labels, lookback);

//// Step 4: Train classifier
//Console.WriteLine("Training Head & Shoulders classifier...");
//var result = HeadAndShoulders.TrainClassifier(bars, labels, lookback);

//Console.WriteLine($"\nTraining complete!");
//Console.WriteLine($"Model saved to: hs_classifier.zip");
//Console.WriteLine($"Latest prediction: {result.PredictedLabel} (Prob: {result.Probability:P2})");

//// Helper methods
//static async Task<List<DailyBar>> LoadHistoricalBars(string symbol, DateTime startDate)
//{
//    var bars = await new QuoteRepository().GetDailyBarsAsync(symbol, startDate);
//    return bars;
//}

//static Dictionary<DateTime, bool> GenerateHeadAndShouldersLabels(List<DailyBar> bars)
//{
//    // TODO: Option A - Use rule-based detector from Core.Indicators
//    // TODO: Option B - Load from CSV (manual labels)
//    // TODO: Option C - Start with synthetic, gradually add real labels

//    var labels = new Dictionary<DateTime, bool>();
//    // Placeholder: mark every 50th day as having pattern
//    for (int i = 0; i < bars.Count; i += 50)
//    {
//        labels[bars[i].Date.Date] = true;
//    }
//    return labels;
//}