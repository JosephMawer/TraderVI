using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Core.ML
{

    // train
    // test
    // use



    public static class Trainer
    {

        public static void Entry()
        {
            //Train a “head - and - shoulders present ?” classifier, and
            //Later plug its output into your price/return prediction model as a feature.


            // todo
            //If you’d like, I can next show a tiny example of how to pipe the classifier into the regression feature
            //builder so it all runs in one C# pipeline (classifier → features → regression).


            // Now your regression model can learn things like:
            // “When head-and - shoulders probability is high, expected next-day return tends to be X…”
            // instead of relying on a brittle yes / no rule.


        }

        /// <summary>
        /// How to tweak / extend this
        /// Change the grid size
        ///     Start small(like above). If training is quick, widen:
        ///         More learningRates(e.g. 0.005, 0.02, 0.03…)
        ///         More NumberOfLeaves(15, 31, 63, 127, 255)
        ///         More NumberOfIterations(100–1000+)
        /// Switch metric
        ///     If you care more about minimizing error, use if (metrics.RootMeanSquaredError<bestRmse).
        ///     For ranking decisions(long/short), you might later build a custom metric.
        /// Avoid leakage
        ///     The important bit is: all splits are by date order, no shuffling.
        /// Add more options
        ///     You can also grid search over MinimumExampleCountPerLeaf, L2Regularization, etc. once this is working.
        /// </summary>
        /// <param name="data"></param>
        static void TrainData(List<DailyBar> data)
        {
            var mlContext = new MLContext(seed: 123);

            // 1. Load your fully-prepared FeatureRow list (with TargetRet1d etc.)
            List<FeatureRow> allRows = TimeSeriesUtils.BuildFeatures(data);
            allRows = allRows.OrderBy(r => r.Date).ToList();

            int n = allRows.Count;
            if (n < 100)
            {
                Console.WriteLine("Not enough data for train/val/test split.");
                return;
            }

            // 2. Time-based splits: 60% train, 20% val, 20% test
            int trainEnd = (int)(n * 0.6);
            int validEnd = (int)(n * 0.8);

            var trainList = allRows.Take(trainEnd).ToList();
            var validList = allRows.Skip(trainEnd).Take(validEnd - trainEnd).ToList();
            var testList = allRows.Skip(validEnd).ToList();

            IDataView trainData = mlContext.Data.LoadFromEnumerable(trainList);
            IDataView validData = mlContext.Data.LoadFromEnumerable(validList);
            IDataView testData = mlContext.Data.LoadFromEnumerable(testList);

            // 3. Feature columns (same as before)
            string[] featureColumns = GetFeatureColumns();

            // 4. Hyperparameter grids
            var learningRates = new[] { 0.01f, 0.05f, 0.1f };
            var numLeaves = new[] { 31, 63, 127 };
            var numIterations = new[] { 200, 400, 800 };

            double bestR2 = double.NegativeInfinity;
            LightGbmRegressionTrainer.Options bestOptions = null;
            ITransformer bestModel = null;

            Console.WriteLine("Starting hyperparameter search...");

            foreach (var lr in learningRates)
            {
                foreach (var leaves in numLeaves)
                {
                    foreach (var iters in numIterations)
                    {
                        var options = new LightGbmRegressionTrainer.Options
                        {
                            LabelColumnName = nameof(FeatureRow.TargetRet1d),
                            FeatureColumnName = "Features",

                            LearningRate = lr,
                            NumberOfLeaves = leaves,
                            NumberOfIterations = iters,

                            // Some sensible defaults you can tweak later:
                            MinimumExampleCountPerLeaf = 20,
                            UseCategoricalSplit = false
                            
                            //UseHybridPreciseL1Threshold = false,
                            //L2Regularization = 0.0,
                            //L1Regularization = 0.0,
                        };

                        // 5. Build pipeline for this configuration
                        var pipeline = mlContext.Transforms
                            .Concatenate("Features", featureColumns)
                            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                            .Append(mlContext.Regression.Trainers.LightGbm(options));

                        // 6. Train on TRAIN portion only
                        var model = pipeline.Fit(trainData);

                        // 7. Evaluate on VALIDATION portion
                        var validPred = model.Transform(validData);
                        var metrics = mlContext.Regression.Evaluate(
                            data: validPred,
                            labelColumnName: nameof(FeatureRow.TargetRet1d),
                            scoreColumnName: "Score");

                        Console.WriteLine($"lr={lr}, leaves={leaves}, iters={iters} => " +
                                          $"R^2={metrics.RSquared:0.####}, RMSE={metrics.RootMeanSquaredError:0.####}");

                        // 8. Track best by R^2 (you could flip to RMSE if you prefer)
                        if (metrics.RSquared > bestR2)
                        {
                            bestR2 = metrics.RSquared;
                            bestOptions = options;
                            bestModel = model;
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("Best config found:");
            Console.WriteLine($"R^2={bestR2:0.####}");
            Console.WriteLine($"LearningRate={bestOptions.LearningRate}, " +
                              $"NumberOfLeaves={bestOptions.NumberOfLeaves}, " +
                              $"NumberOfIterations={bestOptions.NumberOfIterations}");

            // 9. Retrain best config on TRAIN+VALID (all data up to test period)
            var trainPlusValidList = allRows.Take(validEnd).ToList();
            var trainPlusValidData = mlContext.Data.LoadFromEnumerable(trainPlusValidList);

            var finalPipeline = mlContext.Transforms
                .Concatenate("Features", featureColumns)
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(mlContext.Regression.Trainers.LightGbm(bestOptions));

            Console.WriteLine("Training final model on Train+Validation...");
            var finalModel = finalPipeline.Fit(trainPlusValidData);

            // 10. Final evaluation on TEST
            Console.WriteLine("Evaluating final model on Test set...");
            var testPred = finalModel.Transform(testData);
            var finalMetrics = mlContext.Regression.Evaluate(
                data: testPred,
                labelColumnName: nameof(FeatureRow.TargetRet1d),
                scoreColumnName: "Score");

            Console.WriteLine($"TEST R^2 = {finalMetrics.RSquared:0.####}");
            Console.WriteLine($"TEST RMSE = {finalMetrics.RootMeanSquaredError:0.####}");

            // 11. Save the final model
            string modelPath = "stock_lightgbm_tuned.zip";
            mlContext.Model.Save(finalModel, trainPlusValidData.Schema, modelPath);
            Console.WriteLine($"Final model saved to: {modelPath}");
        }

        private static string[] GetFeatureColumns()
        {
            string[] featureColumns = new[]
          {
            nameof(FeatureRow.LagClose1),
            nameof(FeatureRow.LagClose2),
            nameof(FeatureRow.LagClose5),
            nameof(FeatureRow.Ret1d),
            nameof(FeatureRow.Ret5d),
            nameof(FeatureRow.Ma5),
            nameof(FeatureRow.Ma20),
            nameof(FeatureRow.Vol5),
            nameof(FeatureRow.Vol20),
            nameof(FeatureRow.PriceOverMa20),
            nameof(FeatureRow.Vol),
            nameof(FeatureRow.VolMa5),
            nameof(FeatureRow.VolRatio5),
            nameof(FeatureRow.RangePct),
            nameof(FeatureRow.CloseToOpen),
            nameof(FeatureRow.DayOfWeek),
            nameof(FeatureRow.Month),
            nameof(FeatureRow.IsMonthEnd)
            };
            return featureColumns;
        }

    }
}
