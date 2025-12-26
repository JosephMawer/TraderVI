using Core.Db;
using Core.ML.Engine.Training.Classifiers;
using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.Intrinsics.X86;
using System.Text;
using wstrade.Models;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
    }

    public class FeatureRow
    {
        public DateTime Date { get; set; }

        // Target: next-day return
        public float TargetRet1d { get; set; }

        // Lags
        public float LagClose1 { get; set; }
        public float LagClose2 { get; set; }
        public float LagClose5 { get; set; }

        // Returns & momentum
        public float Ret1d { get; set; }
        public float Ret5d { get; set; }

        // Rolling stats
        public float Ma5 { get; set; }
        public float Ma20 { get; set; }
        public float Vol5 { get; set; }
        public float Vol20 { get; set; }
        public float PriceOverMa20 { get; set; }

        // Volume
        public float Vol { get; set; }
        public float VolMa5 { get; set; }
        public float VolRatio5 { get; set; }

        // Intraday
        public float RangePct { get; set; }
        public float CloseToOpen { get; set; }

        // Calendar
        public float DayOfWeek { get; set; }
        public float Month { get; set; }
        public float IsMonthEnd { get; set; }
        //public DateTime Date { get; set; }

        //// Target: next-day return
        //public double TargetRet1d { get; set; }

        //// Lags
        //public double LagClose1 { get; set; }
        //public double LagClose2 { get; set; }
        //public double LagClose5 { get; set; }

        //// Returns & momentum
        //public double Ret1d { get; set; }
        //public double Ret5d { get; set; }

        //// Rolling stats
        //public double Ma5 { get; set; }
        //public double Ma20 { get; set; }
        //public double Vol5 { get; set; }
        //public double Vol20 { get; set; }
        //public double PriceOverMa20 { get; set; }

        //// Volume
        //public double Vol { get; set; }
        //public double VolMa5 { get; set; }
        //public double VolRatio5 { get; set; }

        //// Intraday
        //public double RangePct { get; set; }
        //public double CloseToOpen { get; set; }

        //// Calendar
        //public int DayOfWeek { get; set; }
        //public int Month { get; set; }
        //public int IsMonthEnd { get; set; }
    }

    public static class TimeSeriesUtils
    {

        /// <summary>
        /// 4. A couple of important practical notes

        /// 1. No shuffling for time series
        /// We did an 80/20 split by date.
        /// Do not use random train/test splits for time series; that leaks future info into training.
        /// 
        /// 2. Consider walk-forward validation later
        /// Once the basic pipeline works, you can:
        /// Train on [start … t], test on [t+1 … t+k]
        /// Slide forward in time (walk-forward) to get more realistic performance estimates.
        /// 
        /// 3. Treat this as a signal strength estimator, not a magic oracle
        /// The raw R² on daily stock returns will often be tiny (market is noisy).
        /// What you care about is whether the model lets you:
        /// Rank days by expected return,
        /// Or improve your long/flat/short decisions vs baseline.
        /// </summary>
        /// <param name="stockData"></param>
        public static void RunSignalStrengthEstimator(List<DailyBar> stockData)
        {
            // 1. Create MLContext
            var mlContext = new MLContext(seed: 123);

            // 2. Build your feature rows from raw DailyBar data
            // (Use your existing BuildFeatures(bars) method, then convert to float.)
            List<FeatureRow> allRows = BuildFeatures(stockData);  // <-- implement this

            // Ensure sorted by Date ascending
            allRows = allRows.OrderBy(r => r.Date).ToList();

            // 3. Time-based train/test split (e.g., last 20% as test)
            int n = allRows.Count;
            int splitIndex = (int)(n * 0.8);

            var trainDataList = allRows.Take(splitIndex).ToList();
            var testDataList = allRows.Skip(splitIndex).ToList();

            Console.WriteLine($"Train rows: {trainDataList.Count}, Test rows: {testDataList.Count}");

            // 4. Load into IDataView
            IDataView trainData = mlContext.Data.LoadFromEnumerable(trainDataList);
            IDataView testData = mlContext.Data.LoadFromEnumerable(testDataList);

            // 5. Define feature column names (all except Date + Target)
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

            // 6. Build the pipeline
            var pipeline = mlContext.Transforms
                // a) Concatenate into a single Features vector
                .Concatenate("Features", featureColumns)
                // b) Optional: normalize features
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                // c) Choose the trainer (LightGBM regression here)
                .Append(mlContext.Regression.Trainers.LightGbm(
                    labelColumnName: nameof(FeatureRow.TargetRet1d),
                    featureColumnName: "Features"));

            // If you want FastTree instead:
            // .Append(mlContext.Regression.Trainers.FastTree(
            //     labelColumnName: nameof(FeatureRow.TargetRet1d),
            //     featureColumnName: "Features"));

            // 7. Train the model
            Console.WriteLine("Training model...");
            var model = pipeline.Fit(trainData);

            // 8. Evaluate on test set
            Console.WriteLine("Evaluating on test set...");
            var predictions = model.Transform(testData);
            var metrics = mlContext.Regression.Evaluate(
                data: predictions,
                labelColumnName: nameof(FeatureRow.TargetRet1d),
                scoreColumnName: "Score");

            Console.WriteLine($"R^2: {metrics.RSquared:0.####}");
            Console.WriteLine($"RMSE: {metrics.RootMeanSquaredError:0.####}");

            // 9. Save model to disk
            string modelPath = "stock_model_lightgbm.zip";
            mlContext.Model.Save(model, trainData.Schema, modelPath);
            Console.WriteLine($"Model saved to: {modelPath}");

            // 10. Example: use model for single prediction
            var predictionEngine = mlContext.Model.CreatePredictionEngine<FeatureRow, PredictionOutput>(model);

            var latestRow = allRows.Last();  // last available day (t)
            var prediction = predictionEngine.Predict(latestRow);  // predicts return for t+1

            Console.WriteLine($"Predicted next-day return: {prediction.Score:0.#####}");
        }

        public static List<FeatureRow> BuildFeatures(List<DailyBar> bars)
        {
            // Ensure sorted by date
            var sorted = bars.OrderBy(b => b.Date).ToList();
            int n = sorted.Count;

            // Convert to double arrays for convenience
            var close = sorted.Select(b => (float)b.Close).ToList();
            var volume = sorted.Select(b => (float)b.Volume).ToList();

            // 1-day returns (undefined for i=0; we'll start later anyway)
            var ret1d = new float[n];
            ret1d[0] = 0.0f;
            for (int i = 1; i < n; i++)
            {
                if (close[i - 1] == 0)
                    ret1d[i] = 0.0f;
                else
                    ret1d[i] = (close[i] - close[i - 1]) / close[i - 1];
            }

            int maxLag = 20;  // because of MA20 & Vol20
            var rows = new List<FeatureRow>();

            // We go up to n-2 because we need Close[i+1] for the target
            for (int i = maxLag; i < n - 1; i++)
            {
                var bar = sorted[i];
                var nextBar = sorted[i + 1];

                // Target = next-day return
                float targetRet1d = (close[i + 1] - close[i]) / close[i];

                // Returns
                float ret1dToday = ret1d[i];
                float ret5d = (close[i] - close[i - 5]) / close[i - 5];

                // Moving averages
                float ma5 = TimeSeriesUtils.RollingMean(close, i, 5);
                float ma20 = TimeSeriesUtils.RollingMean(close, i, 20);

                // Volatility
                float vol5 = TimeSeriesUtils.RollingStd(ret1d, i, 5);
                float vol20 = TimeSeriesUtils.RollingStd(ret1d, i, 20);

                // Volume stats
                float volToday = volume[i];
                float volMa5 = TimeSeriesUtils.RollingMean(volume, i, 5);
                float volRatio5 = volMa5 == 0 ? 0.0f : volToday / volMa5;

                // Intraday
                float rangePct = bar.Close == 0
                    ? 0.0f
                    : (float)((bar.High - bar.Low) / bar.Close);

                float closeToOpen = bar.Open == 0
                    ? 0.0f
                    : (float)((bar.Close - bar.Open) / bar.Open);

                // Calendar
                int dow = (int)bar.Date.DayOfWeek;
                int month = bar.Date.Month;
                int isMonthEnd = (bar.Date.AddDays(1).Month != bar.Date.Month) ? 1 : 0;

                // Assemble feature row
                var row = new FeatureRow
                {
                    Date = bar.Date,
                    TargetRet1d = targetRet1d,

                    LagClose1 = close[i],
                    LagClose2 = close[i - 1],
                    LagClose5 = close[i - 4],

                    Ret1d = ret1dToday,
                    Ret5d = ret5d,

                    Ma5 = ma5,
                    Ma20 = ma20,
                    Vol5 = vol5,
                    Vol20 = vol20,
                    PriceOverMa20 = ma20 == 0 ? 0.0f : close[i] / ma20 - 1.0f,

                    Vol = volToday,
                    VolMa5 = volMa5,
                    VolRatio5 = volRatio5,

                    RangePct = rangePct,
                    CloseToOpen = closeToOpen,

                    DayOfWeek = dow,
                    Month = month,
                    IsMonthEnd = isMonthEnd
                };

                rows.Add(row);
            }

            return rows;
        }

        public static PatternWindow BuildLiveWindow(List<DailyBar> last30Bars)
        {
            int lookback = last30Bars.Count;
            var priceNorm = new float[lookback];
            var volNorm = new float[lookback];

            float firstClose = (float)last30Bars[0].Close;
            if (firstClose == 0) firstClose = 1f;

            float avgVol = (float)last30Bars.Average(b => (double)b.Volume);
            if (avgVol == 0) avgVol = 1f;

            for (int i = 0; i < lookback; i++)
            {
                priceNorm[i] = (float)last30Bars[i].Close / firstClose;
                volNorm[i] = (float)last30Bars[i].Volume / avgVol;
            }

            return new PatternWindow
            {
                PriceNorm = priceNorm,
                VolumeNorm = volNorm,

                // Label is unknown in real time – not used for prediction
                HasHeadAndShoulders = false
            };
        }



        //At the end you’ve got a List<FeatureRow> where each row:
        //Uses only info up to time t
        //Has a target = return at t+1
        //You can then map this to whatever your model expects:

        // Example of turning into X (features) and y (targets)
        //double[][] X = rows.Select(r => new[]
        //{
        //    r.LagClose1, r.LagClose2, r.LagClose5,
        //    r.Ret1d, r.Ret5d,
        //    r.Ma5, r.Ma20, r.Vol5, r.Vol20, r.PriceOverMa20,
        //    r.Vol, r.VolMa5, r.VolRatio5,
        //    r.RangePct, r.CloseToOpen,
        //    r.DayOfWeek, r.Month, r.IsMonthEnd
        //}).ToArray();

        //double[] y = rows.Select(r => r.TargetRet1d).ToArray();

        public static float RollingMean(IList<float> series, int endIndexInclusive, int window)
        {
            int start = endIndexInclusive - window + 1;
            float sum = 0.0f;
            for (int i = start; i <= endIndexInclusive; i++)
                sum += series[i];
            return sum / window;
        }

        public static float RollingStd(IList<float> series, int endIndexInclusive, int window)
        {
            int start = endIndexInclusive - window + 1;
            float mean = RollingMean(series, endIndexInclusive, window);
            float sumSq = 0.0f;

            for (int i = start; i <= endIndexInclusive; i++)
            {
                float diff = series[i] - mean;
                sumSq += diff * diff;
            }

            // sample std (N-1) or population (N)? choose one; here population:
            float variance = sumSq / window;
            return (float)System.Math.Sqrt(variance);
        }


 

        

    }

}
