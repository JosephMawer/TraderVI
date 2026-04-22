using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML
{
    /// <summary>
    /// Legacy single-series tuning prototype. Retained for reference only.
    /// Production training lives in <c>Core.ML.Engine.Profit.UnifiedProfitTrainer</c>.
    /// See <c>docs/ml-pipeline.md</c>.
    /// </summary>
    [Obsolete("Prototype. Use UnifiedProfitTrainer for production training.")]
    public static class Trainer
    {
        /// <summary>
        /// Embargo (in bars) between train/val and val/test to prevent label leakage
        /// across the boundary when the target uses future bars.
        /// </summary>
        private const int EmbargoBars = 1;

        public static void Entry() { /* no-op placeholder */ }

        /// <summary>
        /// Time-based 60/20/20 split with embargo + LightGBM hyperparameter search
        /// selected by Spearman rank correlation on the validation slice.
        /// </summary>
        public static void TrainData(List<DailyBar> data)
        {
            var mlContext = new MLContext(seed: 123);

            List<FeatureRow> allRows = TimeSeriesUtils.BuildFeatures(data);
            allRows = allRows.OrderBy(r => r.Date).ToList();

            int n = allRows.Count;
            if (n < 200)
            {
                Console.WriteLine("Not enough data for train/val/test split.");
                return;
            }

            int trainEnd = (int)(n * 0.6);
            int validEnd = (int)(n * 0.8);

            // Apply embargo at each boundary so the trained model never sees labels
            // whose forward window overlaps with validation/test features.
            var trainList = allRows.Take(trainEnd - EmbargoBars).ToList();
            var validList = allRows.Skip(trainEnd).Take(validEnd - trainEnd - EmbargoBars).ToList();
            var testList = allRows.Skip(validEnd).ToList();

            Console.WriteLine($"Train={trainList.Count}, Val={validList.Count}, Test={testList.Count}, embargo={EmbargoBars}");

            IDataView trainData = mlContext.Data.LoadFromEnumerable(trainList);
            IDataView validData = mlContext.Data.LoadFromEnumerable(validList);
            IDataView testData = mlContext.Data.LoadFromEnumerable(testList);

            // Shrunk grid — early stopping handles iteration count.
            var learningRates = new[] { 0.02, 0.05 };
            var numLeaves = new[] { 15, 31 };
            var minPerLeaf = new[] { 50, 100 };
            var l2 = new[] { 0.5, 1.0 };

            double bestScore = double.NegativeInfinity;
            LightGbmRegressionTrainer.Options bestOptions = null!;

            Console.WriteLine("Starting hyperparameter search (ranked by Spearman on validation)...");

            foreach (var lr in learningRates)
            foreach (var leaves in numLeaves)
            foreach (var mpl in minPerLeaf)
            foreach (var l2reg in l2)
            {
                var options = new LightGbmRegressionTrainer.Options
                {
                    LabelColumnName = nameof(FeatureRow.TargetRet1d),
                    FeatureColumnName = "Features",
                    LearningRate = lr,
                    NumberOfLeaves = leaves,
                    NumberOfIterations = 2000,
                    MinimumExampleCountPerLeaf = mpl,
                    EarlyStoppingRound = 50,
                    Booster = new GradientBooster.Options
                    {
                        L2Regularization = l2reg,
                        L1Regularization = 0.0,
                    },
                };

                var pipeline = TimeSeriesUtils.BuildPipeline(mlContext, options);
                var model = pipeline.Fit(trainData);

                var validPred = model.Transform(validData);
                var metrics = mlContext.Regression.Evaluate(
                    data: validPred,
                    labelColumnName: nameof(FeatureRow.TargetRet1d),
                    scoreColumnName: "Score");

                double spearman = SpearmanOnPredictions(mlContext, validPred);
                double dirAcc = DirectionalAccuracy(mlContext, validPred);

                Console.WriteLine(
                    $"lr={lr}, leaves={leaves}, minLeaf={mpl}, l2={l2reg} => " +
                    $"spearman={spearman:0.###}, dirAcc={dirAcc:P1}, " +
                    $"R^2={metrics.RSquared:0.####}, RMSE={metrics.RootMeanSquaredError:0.####}");

                if (spearman > bestScore)
                {
                    bestScore = spearman;
                    bestOptions = options;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Best Spearman = {bestScore:0.###}");
            Console.WriteLine($"  lr={bestOptions.LearningRate}, leaves={bestOptions.NumberOfLeaves}, " +
                              $"minLeaf={bestOptions.MinimumExampleCountPerLeaf}, l2={bestOptions.L2CategoricalRegularization}");

            // Refit on train+valid (respecting embargo before test).
            var trainPlusValidList = allRows.Take(validEnd - EmbargoBars).ToList();
            var trainPlusValidData = mlContext.Data.LoadFromEnumerable(trainPlusValidList);

            var finalPipeline = TimeSeriesUtils.BuildPipeline(mlContext, bestOptions);

            Console.WriteLine("Training final model on Train+Validation...");
            var finalModel = finalPipeline.Fit(trainPlusValidData);

            Console.WriteLine("Evaluating final model on Test set...");
            var testPred = finalModel.Transform(testData);
            var finalMetrics = mlContext.Regression.Evaluate(
                data: testPred,
                labelColumnName: nameof(FeatureRow.TargetRet1d),
                scoreColumnName: "Score");

            double testSpearman = SpearmanOnPredictions(mlContext, testPred);
            double testDirAcc = DirectionalAccuracy(mlContext, testPred);

            Console.WriteLine($"TEST R^2      = {finalMetrics.RSquared:0.####}");
            Console.WriteLine($"TEST RMSE     = {finalMetrics.RootMeanSquaredError:0.####}");
            Console.WriteLine($"TEST Spearman = {testSpearman:0.###}");
            Console.WriteLine($"TEST DirAcc   = {testDirAcc:P1}");

            string modelPath = "stock_lightgbm_tuned.zip";
            mlContext.Model.Save(finalModel, trainPlusValidData.Schema, modelPath);
            Console.WriteLine($"Final model saved to: {modelPath}");
        }

        // --- helpers ---

        private sealed class EvalRow
        {
            public float TargetRet1d { get; set; }
            public float Score { get; set; }
        }

        private static double SpearmanOnPredictions(MLContext mlContext, IDataView predictions)
        {
            var rows = mlContext.Data.CreateEnumerable<EvalRow>(predictions, reuseRowObject: false).ToList();
            if (rows.Count < 2) return 0;

            var x = rows.Select(r => (double)r.Score).ToArray();
            var y = rows.Select(r => (double)r.TargetRet1d).ToArray();
            return Correlation(Rank(x), Rank(y));
        }

        private static double DirectionalAccuracy(MLContext mlContext, IDataView predictions)
        {
            var rows = mlContext.Data.CreateEnumerable<EvalRow>(predictions, reuseRowObject: false).ToList();
            if (rows.Count == 0) return 0;
            return rows.Count(r => System.Math.Sign(r.Score) == System.Math.Sign(r.TargetRet1d)) / (double)rows.Count;
        }

        private static double[] Rank(double[] values)
        {
            var indexed = values.Select((v, i) => (Value: v, Index: i))
                                .OrderBy(t => t.Value).ToArray();
            var ranks = new double[values.Length];
            int i = 0;
            while (i < indexed.Length)
            {
                int j = i;
                while (j < indexed.Length && indexed[j].Value.Equals(indexed[i].Value)) j++;
                double avgRank = (i + 1 + j) / 2.0;
                for (int k = i; k < j; k++) ranks[indexed[k].Index] = avgRank;
                i = j;
            }
            return ranks;
        }

        private static double Correlation(double[] x, double[] y)
        {
            int n = x.Length;
            if (n < 2) return 0;
            double mx = x.Average(), my = y.Average();
            double cov = 0, vx = 0, vy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - mx, dy = y[i] - my;
                cov += dx * dy; vx += dx * dx; vy += dy * dy;
            }
            double denom = System.Math.Sqrt(vx * vy);
            return denom == 0 ? 0 : cov / denom;
        }
    }
}
