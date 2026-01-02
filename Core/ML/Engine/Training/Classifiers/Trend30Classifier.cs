using Microsoft.ML;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Training.Classifiers
{
    public static class Trend30Classifier
    {
        public static void Train(List<DailyBar> bars, int lookback = 30, string modelPath = "trend30_classifier.zip")
        {
            var mlContext = new MLContext(seed: 123);

            var allWindows = Trend30Utilities.BuildTrend30Dataset(bars, lookback);

            int n = allWindows.Count;
            if (n < 200)
                Console.WriteLine($"[WARN] Trend30 dataset is small (n={n}). Results may be unstable.");

            int split = (int)(n * 0.8);
            var trainList = allWindows.Take(split).ToList();
            var testList = allWindows.Skip(split).ToList();

            IDataView trainData = mlContext.Data.LoadFromEnumerable(trainList);
            IDataView testData = mlContext.Data.LoadFromEnumerable(testList);

            var pipeline = mlContext.Transforms
                .CopyColumns("Label", nameof(PatternWindow.IsTrendUp))
                .Append(mlContext.Transforms.Concatenate(
                    "Features",
                    nameof(PatternWindow.PriceNorm),
                    nameof(PatternWindow.VolumeNorm)))
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(mlContext.BinaryClassification.Trainers.LightGbm(
                    labelColumnName: "Label",
                    featureColumnName: "Features"));

            Console.WriteLine("Training Trend30 classifier...");
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
}