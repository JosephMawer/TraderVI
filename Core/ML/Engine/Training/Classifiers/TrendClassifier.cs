using Core.ML;
using Microsoft.ML;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Training.Classifiers;

/// <summary>
/// Unified trend classifier supporting multiple lookback periods.
/// </summary>
public static class TrendClassifier
{
    public static void Train<TWindow>(
        List<DailyBar> bars,
        int lookback,
        string modelPath,
        Func<List<DailyBar>, int, List<TWindow>> datasetBuilder)
        where TWindow : class, ITrendPatternWindow
    {
        var mlContext = new MLContext(seed: 123);

        var allWindows = datasetBuilder(bars, lookback);

        int n = allWindows.Count;
        if (n < 200)
            Console.WriteLine($"[WARN] Trend{lookback} dataset is small (n={n}). Results may be unstable.");

        int split = (int)(n * 0.8);
        var trainList = allWindows.Take(split).ToList();
        var testList = allWindows.Skip(split).ToList();

        IDataView trainData = mlContext.Data.LoadFromEnumerable(trainList);
        IDataView testData = mlContext.Data.LoadFromEnumerable(testList);

        var pipeline = mlContext.Transforms
            .CopyColumns("Label", nameof(ITrendPatternWindow.IsTrendUp))
            .Append(mlContext.Transforms.Concatenate(
                "Features",
                nameof(ITrendPatternWindow.PriceNorm),
                nameof(ITrendPatternWindow.VolumeNorm)))
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.BinaryClassification.Trainers.LightGbm(
                labelColumnName: "Label",
                featureColumnName: "Features"));

        Console.WriteLine($"Training Trend{lookback} classifier...");
        var model = pipeline.Fit(trainData);

        var predictions = model.Transform(testData);
        var metrics = mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: "Label");

        Console.WriteLine($"Accuracy: {metrics.Accuracy:0.####}");
        Console.WriteLine($"AUC: {metrics.AreaUnderRocCurve:0.####}");
        Console.WriteLine($"F1: {metrics.F1Score:0.####}");

        mlContext.Model.Save(model, trainData.Schema, modelPath);
        Console.WriteLine($"Model saved: {modelPath}");
    }
}