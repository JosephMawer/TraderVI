using Microsoft.ML;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Training.Classifiers
{
    internal static class HeadAndShoulders
    {
        public static PatternPredictionResult TrainClassifier(
            List<DailyBar> bars,
            Dictionary<DateTime, bool> labels,
            int lookback)
            {
                var mlContext = new MLContext(seed: 123);

                // 1. Build dataset (only windows with labels)
                var allWindows = ClassificationUtilities.BuildPatternDataset(bars, labels, lookback);

                // Time-based split: 80% train, 20% test
                int n = allWindows.Count;
                int split = (int)(n * 0.8);
                var trainList = allWindows.Take(split).ToList();
                var testList = allWindows.Skip(split).ToList();

                IDataView trainData = mlContext.Data.LoadFromEnumerable(trainList);
                IDataView testData = mlContext.Data.LoadFromEnumerable(testList);

                // 2. Build pipeline
                var pipeline = BuildPipeline(mlContext);

                // 3. Train
                Console.WriteLine("Training head-and-shoulders classifier...");
                var model = pipeline.Fit(trainData);

                // 4. Evaluate
                var predictions = model.Transform(testData);
                var metrics = mlContext.BinaryClassification.Evaluate(
                    predictions, labelColumnName: "Label", scoreColumnName: "Score");

                Console.WriteLine($"Accuracy: {metrics.Accuracy:0.####}");
                Console.WriteLine($"AUC: {metrics.AreaUnderRocCurve:0.####}");
                Console.WriteLine($"F1: {metrics.F1Score:0.####}");

                // 5. Save model
                mlContext.Model.Save(model, trainData.Schema, "hs_classifier.zip");

                // 6. Example: classify the latest window
                var engine = mlContext.Model.CreatePredictionEngine<PatternWindow, PatternPredictionResult>(model);

                var latestDataset = ClassificationUtilities.BuildPatternDataset(bars, labels: new Dictionary<DateTime, bool>(), lookback);
                var latestWindow = latestDataset.Last(); // or build specifically for last 30 days

                var pred = engine.Predict(latestWindow);
                Console.WriteLine($"Head-and-shoulders? {pred.PredictedLabel}, " +
                                  $"Prob={pred.Probability:0.###}");

                return pred;
            }


        private static IEstimator<ITransformer> BuildPipeline(MLContext mlContext)
        {
            var pipeline =
                // 1. Create a Label column from HasHeadAndShoulders
                // mlContext.Transforms is a TransformsCatalog.

                mlContext.Transforms

                // CopyColumns(...) returns a ColumnCopyingEstimator (an estimator chain), not a TransformsCatalog.
                .CopyColumns("Label", nameof(PatternWindow.HasHeadAndShoulders))

                // 2. Append concatenation of your two window vectors into Features
                .Append(mlContext.Transforms.Concatenate(
                    "Features",
                    nameof(PatternWindow.PriceNorm),
                    nameof(PatternWindow.VolumeNorm)))

                // 3. Optional normalization
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))

                // 4. LightGBM binary classifier
                .Append(mlContext.BinaryClassification.Trainers.LightGbm(
                    labelColumnName: "Label",
                    featureColumnName: "Features"));

                // Mental model for ML.NET pipelines
                // mlContext.Transforms.Something(...)
                // → starts a transform chain(returns an estimator).
                // After that, you always use:
                // .Append(mlContext.Transforms.OtherThing(...))
                // .Append(trainer)

                // to keep adding steps.
                // If you later bump into a similar error on your regression pipeline, the exact same pattern applies: first transform via mlContext.Transforms.X, then.Append(...) for all subsequent ones.

            return pipeline;

        }

        /// <summary>
        /// What this classifier is doing:
        /// It looks at the shape of the normalized price window(the WindowPrices vector).
        /// 
        /// It learns to approximate the decision boundary: “this looks like the kind of shape that the rule-based detector calls head-and-shoulders.”
        /// 
        /// You can later:
        /// Add more windows with manual corrections(where you disagree with the rule).
        /// Retrain so it better matches your idea of the pattern, instead of a brittle heuristic.
        /// </summary>
        /// <param name="bars"></param>
        /// <param name="lookback"></param>
        //private static void TrainHeadAndShouldersClassifier(List<DailyBar> bars, int lookback)
        //{
        //    var mlContext = new MLContext(seed: 123);

        //    // 1. Build pattern dataset
        //    var allWindows = ClassificationUtilities.BuildPatternDataset(bars, lookback);

        //    // Optional: filter out all-negative cases if your detector almost never fires
        //    // or rebalance. For now we'll keep it simple.

        //    // 2. Train/test split by time (no shuffling)
        //    int n = allWindows.Count;
        //    int split = (int)(n * 0.8);
        //    var trainList = allWindows.Take(split).ToList();
        //    var testList = allWindows.Skip(split).ToList();

        //    var trainData = mlContext.Data.LoadFromEnumerable(trainList);
        //    var testData = mlContext.Data.LoadFromEnumerable(testList);

        //    // 3. Build pipeline
        //    var pipeline = mlContext.Transforms
        //        // map bool label into "Label" column if needed
        //        .CopyColumns("Label", nameof(PatternWindow.HasHeadAndShoulders))
        //        // normalize price vector
        //        .Append(mlContext.Transforms.NormalizeMinMax("WindowPrices"))
        //        // train LightGBM binary classifier
        //        .Append(mlContext.BinaryClassification.Trainers.LightGbm(
        //            labelColumnName: "Label",
        //            featureColumnName: "WindowPrices"));

        //    // 4. Train
        //    var model = pipeline.Fit(trainData);

        //    // 5. Evaluate
        //    var pred = model.Transform(testData);
        //    var metrics = mlContext.BinaryClassification.Evaluate(
        //        pred,
        //        labelColumnName: "Label",
        //        scoreColumnName: "Score");

        //    Console.WriteLine($"Accuracy: {metrics.Accuracy:0.####}");
        //    Console.WriteLine($"AUC: {metrics.AreaUnderRocCurve:0.####}");
        //    Console.WriteLine($"F1: {metrics.F1Score:0.####}");

        //    // 6. Use the classifier
        //    var engine = mlContext.Model.CreatePredictionEngine<PatternWindow, PatternPredictionResult>(model);

        //    var latestBars = bars.TakeLast(lookback).ToList();
        //    var latestWindow = ClassificationUtilities.BuildPatternDataset(bars, lookback).Last(); // or construct directly

        //    var prediction = engine.Predict(latestWindow);
        //    Console.WriteLine($"Pattern present? {prediction.PredictedLabel}, " +
        //                      $"Prob={prediction.Probability:0.###}");
        //}
    }
}
