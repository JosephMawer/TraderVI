using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns;

/// <summary>
/// Trains any pattern defined in the registry using a unified approach.
/// Supports per-symbol time-based train/test split.
/// </summary>
public static class UnifiedPatternTrainer
{
    public static TrainingResult Train(
        PatternDefinition pattern,
        Dictionary<string, List<DailyBar>> barsBySymbol,
        string modelPath,
        double trainFraction = 0.8)
    {
        var trainWindows = new List<PatternWindow>();
        var testWindows = new List<PatternWindow>();

        int lookback = pattern.Lookback;
        int symbolsUsed = 0;

        foreach (var (symbol, bars) in barsBySymbol)
        {
            if (bars.Count < lookback + 5)
                continue;

            var windows = BuildWindows(bars, lookback, pattern.Detector, pattern.FeatureBuilder);
            if (windows.Count < 10)
                continue;

            // Per-symbol time split: first N% train, last (1-N)% test
            int split = (int)(windows.Count * trainFraction);
            if (split <= 0 || split >= windows.Count)
                continue;

            trainWindows.AddRange(windows.Take(split));
            testWindows.AddRange(windows.Skip(split));
            symbolsUsed++;
        }

        if (trainWindows.Count < 200 || testWindows.Count < 50)
        {
            Console.WriteLine($"[SKIP] {pattern.TaskType}: insufficient windows (train={trainWindows.Count}, test={testWindows.Count})");
            return new TrainingResult(false, 0, 0, 0, 0, 0);
        }

        var mlContext = new MLContext(seed: 123);

        // Determine feature vector size from the first window
        int featureCount = trainWindows[0].Features.Length;

        // Create schema with explicit vector size (required by ML.NET)
        var schemaDefinition = SchemaDefinition.Create(typeof(PatternWindow));
        schemaDefinition[nameof(PatternWindow.Features)].ColumnType =
            new VectorDataViewType(NumberDataViewType.Single, featureCount);

        IDataView trainData = mlContext.Data.LoadFromEnumerable(trainWindows, schemaDefinition);
        IDataView testData = mlContext.Data.LoadFromEnumerable(testWindows, schemaDefinition);

        var pipeline = mlContext.Transforms
            .CopyColumns("Label", nameof(PatternWindow.Label))
            .Append(mlContext.Transforms.NormalizeMinMax(nameof(PatternWindow.Features)))
            .Append(mlContext.BinaryClassification.Trainers.LightGbm(
                labelColumnName: "Label",
                featureColumnName: nameof(PatternWindow.Features)));

        Console.WriteLine($"Training {pattern.TaskType} (lookback={lookback}, category={pattern.Category})...");
        Console.WriteLine($"  Train windows: {trainWindows.Count:N0}, Test windows: {testWindows.Count:N0}, Features: {featureCount}");

        var model = pipeline.Fit(trainData);

        var predictions = model.Transform(testData);
        var metrics = mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: "Label");

        Console.WriteLine($"  Accuracy: {metrics.Accuracy:0.####}");
        Console.WriteLine($"  AUC:      {metrics.AreaUnderRocCurve:0.####}");
        Console.WriteLine($"  F1:       {metrics.F1Score:0.####}");

        mlContext.Model.Save(model, trainData.Schema, modelPath);
        Console.WriteLine($"  Model saved: {modelPath}\n");

        return new TrainingResult(
            Success: true,
            SymbolsUsed: symbolsUsed,
            TrainWindows: trainWindows.Count,
            TestWindows: testWindows.Count,
            Accuracy: metrics.Accuracy,
            Auc: metrics.AreaUnderRocCurve);
    }

    private static List<PatternWindow> BuildWindows(
        List<DailyBar> bars,
        int lookback,
        IPatternDetector detector,
        IFeatureBuilder featureBuilder)
    {
        var result = new List<PatternWindow>();

        for (int end = lookback - 1; end < bars.Count; end++)
        {
            var windowBars = bars.GetRange(end - lookback + 1, lookback);

            result.Add(new PatternWindow
            {
                Features = featureBuilder.Build(windowBars),
                Label = detector.Detect(windowBars)
            });
        }

        return result;
    }
}

public record TrainingResult(
    bool Success,
    int SymbolsUsed,
    int TrainWindows,
    int TestWindows,
    double Accuracy,
    double Auc);