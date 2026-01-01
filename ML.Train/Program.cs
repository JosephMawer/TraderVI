//using Core.ML;
//using Core.ML.Engine.Training.Classifiers;
//using System;
//using System.Collections.Generic;

//Console.WriteLine("Offline Training for prediction models");

//var dailyBars = new System.Collections.Generic.List<Core.ML.DailyBar>();
//var labels = new Dictionary<DateTime, bool>(); 
//var lookback = 30;

//var options = new ClassificationTrainerOptions(dailyBars, labels, lookback);
//ClassificationTrainerFactory.Train(ClassificationPattern.HeadAndShoulders, options);


using Core.DB;
using Core.ML;
using Core.ML.Engine.Training.Classifiers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("=== ML Training Pipeline ===\n");

// Step 1: Load historical data for a symbol
Console.WriteLine("Loading historical data...");
var bars = await LoadHistoricalBars("AAPL", startDate: new DateTime(2020, 1, 1));

// Step 2: Generate labels (for now, using synthetic or rule-based)
Console.WriteLine("Generating labels...");
var labels = GenerateHeadAndShouldersLabels(bars);

// Step 3: Build pattern dataset
Console.WriteLine($"Building pattern dataset with {labels.Count} labeled windows...");
int lookback = 30;
var dataset = ClassificationUtilities.BuildPatternDataset(bars, labels, lookback);

// Step 4: Train classifier
Console.WriteLine("Training Head & Shoulders classifier...");
var result = HeadAndShoulders.TrainClassifier(bars, labels, lookback);

Console.WriteLine($"\nTraining complete!");
Console.WriteLine($"Model saved to: hs_classifier.zip");
Console.WriteLine($"Latest prediction: {result.PredictedLabel} (Prob: {result.Probability:P2})");

// Helper methods
static async Task<List<DailyBar>> LoadHistoricalBars(string symbol, DateTime startDate)
{
    // TODO: Connect to your Core.Db infrastructure
    // var stockData = await StockInfo.GetStockDataAsync(symbol, startDate);
    // return stockData.Select(s => new DailyBar { ... }).ToList();

    throw new NotImplementedException("Wire up to Core.Db");
}

static Dictionary<DateTime, bool> GenerateHeadAndShouldersLabels(List<DailyBar> bars)
{
    // TODO: Option A - Use rule-based detector from Core.Indicators
    // TODO: Option B - Load from CSV (manual labels)
    // TODO: Option C - Start with synthetic, gradually add real labels

    var labels = new Dictionary<DateTime, bool>();
    // Placeholder: mark every 50th day as having pattern
    for (int i = 0; i < bars.Count; i += 50)
    {
        labels[bars[i].Date.Date] = true;
    }
    return labels;
}