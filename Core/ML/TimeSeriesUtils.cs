using Core.Db;
using Core.ML.Engine.Training.Classifiers;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML
{
    // Output class for PredictionEngine
    public class PredictionOutput
    {
        [ColumnName("Score")]
        public float Score { get; set; }   // predicted TargetRet1d
    }

    public class DailyBar
    {
        public DateTime Date { get; set; }
        public float Open { get; set; }
        public float High { get; set; }
        public float Low { get; set; }
        public float Close { get; set; }
        public long Volume { get; set; }
    }

    /// <summary>
    /// Scale-invariant feature row for a single bar.
    /// All features are either returns, ratios, or z-scored — no raw prices or raw volumes —
    /// so models generalize across price regimes (e.g. 2015 @ $20 vs 2026 @ $80).
    /// </summary>
    public class FeatureRow
    {
        public DateTime Date { get; set; }

        // Target: next-day return, winsorized in BuildFeatures.
        public float TargetRet1d { get; set; }

        // --- Lagged RETURNS (not prices). "Lag1" means "return from t-2 to t-1".
        public float RetLag1 { get; set; }
        public float RetLag2 { get; set; }
        public float RetLag5 { get; set; }

        // --- Momentum (cumulative returns).
        public float Ret1d { get; set; }   // today's return (t-1 -> t)
        public float Ret5d { get; set; }   // 5-day return

        // --- Scale-invariant ratios vs moving averages.
        public float CloseOverMa5 { get; set; }   // close[t]/ma5 - 1
        public float CloseOverMa20 { get; set; }  // close[t]/ma20 - 1
        public float Ma5OverMa20 { get; set; }    // ma5/ma20 - 1

        // --- Volatility (already scale-invariant because computed from returns).
        public float Vol5 { get; set; }
        public float Vol20 { get; set; }

        // --- Volume ratios (scale-invariant).
        public float VolRatio5 { get; set; }    // volume[t]/volMa5
        public float VolRatio20 { get; set; }   // volume[t]/volMa20

        // --- Intraday ratios.
        public float RangePct { get; set; }     // (high-low)/close
        public float CloseToOpen { get; set; }  // (close-open)/open

        // --- Calendar (categorical — encoded via OneHotEncoding in the pipeline).
        public float DayOfWeek { get; set; }
        public float Month { get; set; }
        public float IsMonthEnd { get; set; }
    }

    public static class TimeSeriesUtils
    {
        /// <summary>
        /// Max absolute daily return allowed for the training label. Defends against
        /// bad ticks / unadjusted splits that would otherwise dominate squared-error loss.
        /// </summary>
        public const float TargetWinsorizeAbs = 0.20f;

        /// <summary>
        /// Legacy single-split prototype. Kept for reference; production training goes through
        /// <c>UnifiedProfitTrainer</c>. See <c>docs/ml-pipeline.md</c>.
        /// </summary>
        [Obsolete("Prototype. Use UnifiedProfitTrainer for production training; use WalkForwardEvaluator for validation.")]
        public static void RunSignalStrengthEstimator(List<DailyBar> stockData)
        {
            var mlContext = new MLContext(seed: 123);

            List<FeatureRow> allRows = BuildFeatures(stockData);
            allRows = allRows.OrderBy(r => r.Date).ToList();

            int n = allRows.Count;
            if (n < 100)
            {
                Console.WriteLine("Not enough data.");
                return;
            }

            // Time-based 80/20 split with 1-day embargo (horizon=1).
            const int embargo = 1;
            int splitIndex = (int)(n * 0.8);

            var trainList = allRows.Take(splitIndex - embargo).ToList();
            var testList = allRows.Skip(splitIndex).ToList();

            Console.WriteLine($"Train rows: {trainList.Count}, Test rows: {testList.Count} (embargo={embargo})");

            IDataView trainData = mlContext.Data.LoadFromEnumerable(trainList);
            IDataView testData = mlContext.Data.LoadFromEnumerable(testList);

            var pipeline = BuildPipeline(mlContext, new LightGbmRegressionTrainer.Options
            {
                LabelColumnName = nameof(FeatureRow.TargetRet1d),
                FeatureColumnName = "Features",
                LearningRate = 0.05,
                NumberOfLeaves = 31,
                NumberOfIterations = 1000,
                MinimumExampleCountPerLeaf = 100,
                EarlyStoppingRound = 50,
                Booster = new GradientBooster.Options
                {
                    L2Regularization = 1.0,
                    L1Regularization = 0.0,
                },
            });

            Console.WriteLine("Training model...");
            var model = pipeline.Fit(trainData);

            var predictions = model.Transform(testData);
            var metrics = mlContext.Regression.Evaluate(
                data: predictions,
                labelColumnName: nameof(FeatureRow.TargetRet1d),
                scoreColumnName: "Score");

            Console.WriteLine($"R^2:  {metrics.RSquared:0.####}");
            Console.WriteLine($"RMSE: {metrics.RootMeanSquaredError:0.####}");

            string modelPath = "stock_model_lightgbm.zip";
            mlContext.Model.Save(model, trainData.Schema, modelPath);
            Console.WriteLine($"Model saved to: {modelPath}");
        }

        /// <summary>
        /// Builds the standard pipeline: one-hot calendar fields, concatenate, (optional) normalize, LightGBM.
        /// MinMax normalization is unnecessary for tree models but kept harmless for consistency with older callers.
        /// </summary>
        internal static IEstimator<ITransformer> BuildPipeline(
            MLContext mlContext,
            LightGbmRegressionTrainer.Options options)
        {
            string[] numericFeatures = GetNumericFeatureColumns();

            return mlContext.Transforms.Categorical
                .OneHotEncoding("DayOfWeekEnc", nameof(FeatureRow.DayOfWeek))
                .Append(mlContext.Transforms.Categorical.OneHotEncoding("MonthEnc", nameof(FeatureRow.Month)))
                .Append(mlContext.Transforms.Concatenate(
                    "Features",
                    numericFeatures.Concat(new[] { "DayOfWeekEnc", "MonthEnc" }).ToArray()))
                .Append(mlContext.Regression.Trainers.LightGbm(options));
        }

        internal static string[] GetNumericFeatureColumns() => new[]
        {
            nameof(FeatureRow.RetLag1),
            nameof(FeatureRow.RetLag2),
            nameof(FeatureRow.RetLag5),
            nameof(FeatureRow.Ret1d),
            nameof(FeatureRow.Ret5d),
            nameof(FeatureRow.CloseOverMa5),
            nameof(FeatureRow.CloseOverMa20),
            nameof(FeatureRow.Ma5OverMa20),
            nameof(FeatureRow.Vol5),
            nameof(FeatureRow.Vol20),
            nameof(FeatureRow.VolRatio5),
            nameof(FeatureRow.VolRatio20),
            nameof(FeatureRow.RangePct),
            nameof(FeatureRow.CloseToOpen),
            nameof(FeatureRow.IsMonthEnd),
        };

        public static List<FeatureRow> BuildFeatures(List<DailyBar> bars)
        {
            var sorted = bars.OrderBy(b => b.Date).ToList();
            int n = sorted.Count;

            var close = sorted.Select(b => b.Close).ToList();
            var volume = sorted.Select(b => (float)b.Volume).ToList();

            // 1-day simple returns. ret1d[0] is undefined; start loop past maxLag so it's never consumed.
            var ret1d = new float[n];
            for (int i = 1; i < n; i++)
            {
                ret1d[i] = close[i - 1] == 0 ? 0f : (close[i] - close[i - 1]) / close[i - 1];
            }

            const int maxLag = 21; // need 20 prior bars for MA20/Vol20 + 1 for a clean lag
            var rows = new List<FeatureRow>(System.Math.Max(0, n - maxLag - 1));

            for (int i = maxLag; i < n - 1; i++)
            {
                var bar = sorted[i];

                // --- Target: next-day return, winsorized.
                float rawTarget = close[i] == 0 ? 0f : (close[i + 1] - close[i]) / close[i];
                float target = System.Math.Clamp(rawTarget, -TargetWinsorizeAbs, TargetWinsorizeAbs);

                // --- Lagged returns (all past-only).
                float retLag1 = ret1d[i];       // return from t-1 -> t (known at EOD t, used to predict t+1)
                float retLag2 = ret1d[i - 1];
                float retLag5 = ret1d[i - 4];

                // --- Momentum.
                float ret5d = close[i - 5] == 0 ? 0f : (close[i] - close[i - 5]) / close[i - 5];

                // --- Moving averages -> ratios.
                float ma5 = RollingMean(close, i, 5);
                float ma20 = RollingMean(close, i, 20);
                float closeOverMa5 = ma5 == 0 ? 0f : close[i] / ma5 - 1f;
                float closeOverMa20 = ma20 == 0 ? 0f : close[i] / ma20 - 1f;
                float ma5OverMa20 = ma20 == 0 ? 0f : ma5 / ma20 - 1f;

                // --- Volatility (sample std for consistency).
                float vol5 = RollingStd(ret1d, i, 5);
                float vol20 = RollingStd(ret1d, i, 20);

                // --- Volume ratios.
                float volMa5 = RollingMean(volume, i, 5);
                float volMa20 = RollingMean(volume, i, 20);
                float volRatio5 = volMa5 == 0 ? 0f : volume[i] / volMa5;
                float volRatio20 = volMa20 == 0 ? 0f : volume[i] / volMa20;

                // --- Intraday ratios.
                float rangePct = bar.Close == 0 ? 0f : (bar.High - bar.Low) / bar.Close;
                float closeToOpen = bar.Open == 0 ? 0f : (bar.Close - bar.Open) / bar.Open;

                // --- Calendar.
                int dow = (int)bar.Date.DayOfWeek;
                int month = bar.Date.Month;
                int isMonthEnd = (bar.Date.AddDays(1).Month != bar.Date.Month) ? 1 : 0;

                rows.Add(new FeatureRow
                {
                    Date = bar.Date,
                    TargetRet1d = target,

                    RetLag1 = retLag1,
                    RetLag2 = retLag2,
                    RetLag5 = retLag5,
                    Ret1d = retLag1,
                    Ret5d = ret5d,

                    CloseOverMa5 = closeOverMa5,
                    CloseOverMa20 = closeOverMa20,
                    Ma5OverMa20 = ma5OverMa20,

                    Vol5 = vol5,
                    Vol20 = vol20,

                    VolRatio5 = volRatio5,
                    VolRatio20 = volRatio20,

                    RangePct = rangePct,
                    CloseToOpen = closeToOpen,

                    DayOfWeek = dow,
                    Month = month,
                    IsMonthEnd = isMonthEnd
                });
            }

            return rows;
        }

        public static PatternWindow BuildLiveWindow(List<DailyBar> last30Bars)
        {
            int lookback = last30Bars.Count;
            var pricesNorm = new float[lookback];

            float firstClose = last30Bars[0].Close;
            if (firstClose == 0) firstClose = 1f;

            for (int i = 0; i < lookback; i++)
                pricesNorm[i] = last30Bars[i].Close / firstClose;

            return new PatternWindow
            {
                WindowPrices = pricesNorm,
                HasHeadAndShoulders = false
            };
        }

        public static float RollingMean(IList<float> series, int endIndexInclusive, int window)
        {
            int start = endIndexInclusive - window + 1;
            float sum = 0f;
            for (int i = start; i <= endIndexInclusive; i++)
                sum += series[i];
            return sum / window;
        }

        /// <summary>Sample standard deviation (N-1).</summary>
        public static float RollingStd(IList<float> series, int endIndexInclusive, int window)
        {
            if (window <= 1) return 0f;

            int start = endIndexInclusive - window + 1;
            float mean = RollingMean(series, endIndexInclusive, window);
            float sumSq = 0f;

            for (int i = start; i <= endIndexInclusive; i++)
            {
                float diff = series[i] - mean;
                sumSq += diff * diff;
            }

            float variance = sumSq / (window - 1);
            return (float)System.Math.Sqrt(variance);
        }
    }
}
