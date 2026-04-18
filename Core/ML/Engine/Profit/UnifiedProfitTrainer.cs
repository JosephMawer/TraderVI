using Core.ML.Engine.Patterns;
using Core.ML.Engine.Patterns.Features;
using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

public static class UnifiedProfitTrainer
{
    private const float MaxAbsForwardReturn = 0.50f;

    public static ProfitTrainingResult Train(
        ProfitModelDefinition model,
        Dictionary<string, List<DailyBar>> barsBySymbol,
        string modelPath,
        double trainFraction = 0.8)
    {
        var trainWindows = new List<ProfitWindow>();
        var testWindows = new List<ProfitWindow>();

        int lookback = model.Lookback;
        int horizon = model.HorizonBars;
        int symbolsUsed = 0;

        foreach (var (symbol, bars) in barsBySymbol)
        {
            if (bars.Count < lookback + horizon + 5)
                continue;

            var windows = BuildProfitWindows(
                bars,
                lookback,
                horizon,
                model.FeatureBuilder,
                model.Labeler,
                model.ModelKind,
                model.RegressionReturnClamp);

            if (windows.Count < 10)
                continue;

            int split = (int)(windows.Count * trainFraction);
            if (split <= 0 || split >= windows.Count)
                continue;

            trainWindows.AddRange(windows.Take(split));
            testWindows.AddRange(windows.Skip(split));
            symbolsUsed++;
        }

        if (trainWindows.Count < 200 || testWindows.Count < 50)
        {
            Console.WriteLine($"[SKIP] {model.TaskType}: insufficient windows (train={trainWindows.Count}, test={testWindows.Count})");
            return new ProfitTrainingResult(false, 0, 0, 0, 0, 0);
        }

        if (model.ModelKind == ProfitModelKind.BinaryClassification)
        {
            int testPositives = testWindows.Count(w => w.IsEvent);
            int testNegatives = testWindows.Count - testPositives;
            
            if (testPositives == 0 || testNegatives == 0)
            {
                Console.WriteLine($"[SKIP] {model.TaskType}: test set lacks class diversity (positives={testPositives}, negatives={testNegatives})");
                return new ProfitTrainingResult(false, 0, 0, 0, 0, 0);
            }
        }

        if (model.ModelKind == ProfitModelKind.BinaryClassification)
        {
            int testPositives = testWindows.Count(w => w.IsEvent);
            int testNegatives = testWindows.Count - testPositives;
            
            if (testPositives == 0 || testNegatives == 0)
            {
                Console.WriteLine($"[SKIP] {model.TaskType}: test set lacks class diversity (positives={testPositives}, negatives={testNegatives})");
                return new ProfitTrainingResult(false, 0, 0, 0, 0, 0);
            }
        }

        if (model.ModelKind == ProfitModelKind.BinaryClassification)
        {
            int testPositives = testWindows.Count(w => w.IsEvent);
            int testNegatives = testWindows.Count - testPositives;
            
            if (testPositives == 0 || testNegatives == 0)
            {
                Console.WriteLine($"[SKIP] {model.TaskType}: test set lacks class diversity (positives={testPositives}, negatives={testNegatives})");
                return new ProfitTrainingResult(false, 0, 0, 0, 0, 0);
            }
        }

        if (model.ModelKind == ProfitModelKind.BinaryClassification)
        {
            int testPositives = testWindows.Count(w => w.IsEvent);
            int testNegatives = testWindows.Count - testPositives;
            
            if (testPositives == 0 || testNegatives == 0)
            {
                Console.WriteLine($"[SKIP] {model.TaskType}: test set lacks class diversity (positives={testPositives}, negatives={testNegatives})");
                return new ProfitTrainingResult(false, 0, 0, 0, 0, 0);
            }
        }

        Console.WriteLine($"Labeler: {model.Labeler.Name}");
        Console.WriteLine($"FeatureSet: {model.FeatureBuilder.Name}");

        PrintReturnStats("Train", trainWindows);
        PrintReturnStats("Test ", testWindows);

        if (model.ModelKind is ProfitModelKind.ThreeWayClassification or ProfitModelKind.BinaryClassification)
        {
            PrintClassBalance("Train", trainWindows);
            PrintClassBalance("Test ", testWindows);
        }

        Console.WriteLine();

        return model.ModelKind switch
        {
            ProfitModelKind.Regression => TrainRegression(model, trainWindows, testWindows, modelPath, symbolsUsed),
            ProfitModelKind.ThreeWayClassification => TrainThreeWay(model, trainWindows, testWindows, modelPath, symbolsUsed),
            ProfitModelKind.BinaryClassification => TrainBinary(model, trainWindows, testWindows, modelPath, symbolsUsed),
            _ => new ProfitTrainingResult(false, 0, 0, 0, 0, 0)
        };
    }

    /// <summary>
    /// Train with market context (passes XIU bars to MarketContextFeatureBuilder).
    /// </summary>
    public static ProfitTrainingResult TrainWithMarketContext(
        ProfitModelDefinition model,
        Dictionary<string, List<DailyBar>> barsBySymbol,
        List<DailyBar> marketBars,
        string modelPath,
        double trainFraction = 0.8)
    {
        // Inject market bars into feature builder if it supports it
        if (model.FeatureBuilder is MarketContextFeatureBuilder mcfb)
        {
            mcfb.MarketBars = marketBars;
        }

        return Train(model, barsBySymbol, modelPath, trainFraction);
    }

    private static void PrintClassBalance(string name, List<ProfitWindow> windows)
    {
        // Uses ThreeWayLabel encoding: 0=Sell, 1=Hold, 2=Buy
        int total = windows.Count;
        int buys = windows.Count(w => w.ThreeWayLabel == 2);
        int holds = windows.Count(w => w.ThreeWayLabel == 1);
        int sells = windows.Count(w => w.ThreeWayLabel == 0);

        double pBuy = total == 0 ? 0 : (double)buys / total;
        double pHold = total == 0 ? 0 : (double)holds / total;
        double pSell = total == 0 ? 0 : (double)sells / total;

        Console.WriteLine($"  {name} class balance: Buy={buys} ({pBuy:P1}), Hold={holds} ({pHold:P1}), Sell={sells} ({pSell:P1})");
    }

    private static void PrintReturnStats(string name, List<ProfitWindow> windows)
    {
        if (windows.Count == 0)
        {
            Console.WriteLine($"  {name} return stats: n=0");
            return;
        }

        var r = windows.Select(w => (double)w.ForwardReturn).ToArray();
        double mean = r.Average();
        double min = r.Min();
        double max = r.Max();
        double std = StdDev(r);

        Console.WriteLine($"  {name} return stats: n={windows.Count:N0}, mean={mean:P2}, std={std:P2}, min={min:P2}, max={max:P2}");
    }

    private static double StdDev(double[] values)
    {
        if (values.Length <= 1) return 0;
        double mean = values.Average();
        double variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
        return System.Math.Sqrt(variance);
    }

    private static ProfitTrainingResult TrainRegression(
        ProfitModelDefinition model,
        List<ProfitWindow> trainWindows,
        List<ProfitWindow> testWindows,
        string modelPath,
        int symbolsUsed)
    {
        var mlContext = new MLContext(seed: 123);

        int featureCount = trainWindows[0].Features.Length;
        var schemaDefinition = SchemaDefinition.Create(typeof(ProfitWindow));
        schemaDefinition[nameof(ProfitWindow.Features)].ColumnType =
            new VectorDataViewType(NumberDataViewType.Single, featureCount);

        IDataView trainData = mlContext.Data.LoadFromEnumerable(trainWindows, schemaDefinition);
        IDataView testData = mlContext.Data.LoadFromEnumerable(testWindows, schemaDefinition);

        var pipeline = mlContext.Transforms
            .CopyColumns("Label", nameof(ProfitWindow.ForwardReturn))
            .Append(mlContext.Transforms.NormalizeMinMax(nameof(ProfitWindow.Features)))
            .Append(mlContext.Regression.Trainers.LightGbm(
                labelColumnName: "Label",
                featureColumnName: nameof(ProfitWindow.Features)));

        Console.WriteLine($"Training {model.TaskType} (Regression, lookback={model.Lookback}, horizon={model.HorizonBars})...");
        Console.WriteLine($"  Train: {trainWindows.Count:N0}, Test: {testWindows.Count:N0}, Features: {featureCount}");

        var trainedModel = pipeline.Fit(trainData);

        var predictions = trainedModel.Transform(testData);
        var metrics = mlContext.Regression.Evaluate(predictions, labelColumnName: "Label");

        Console.WriteLine($"  RMSE: {metrics.RootMeanSquaredError:0.####}");
        Console.WriteLine($"  MAE:  {metrics.MeanAbsoluteError:0.####}");
        Console.WriteLine($"  R²:   {metrics.RSquared:0.####}");

        PrintRegressionRankingMetrics(mlContext, predictions);

        mlContext.Model.Save(trainedModel, trainData.Schema, modelPath);
        Console.WriteLine($"  Model saved: {modelPath}\n");

        return new ProfitTrainingResult(
            Success: true,
            SymbolsUsed: symbolsUsed,
            TrainWindows: trainWindows.Count,
            TestWindows: testWindows.Count,
            PrimaryMetric: metrics.RSquared,
            SecondaryMetric: metrics.RootMeanSquaredError);
    }

    private static void PrintRegressionRankingMetrics(MLContext mlContext, IDataView predictions)
    {
        var rows = mlContext.Data.CreateEnumerable<RegressionEvalRow>(predictions, reuseRowObject: false).ToList();

        if (rows.Count == 0)
        {
            Console.WriteLine("  Ranking: n=0");
            return;
        }

        double avgLabel = rows.Average(r => (double)r.Label);
        double avgPred = rows.Average(r => (double)r.Score);

        double dirAcc = rows.Count(r => System.Math.Sign(r.Score) == System.Math.Sign(r.Label)) / (double)rows.Count;

        double spearman = SpearmanCorrelation(
            rows.Select(r => (double)r.Score).ToArray(),
            rows.Select(r => (double)r.Label).ToArray());

        int top10N = System.Math.Max(1, rows.Count / 10);
        var top10 = rows.OrderByDescending(r => r.Score).Take(top10N).ToList();
        double top10Mean = top10.Average(r => (double)r.Label);

        int top5N = System.Math.Max(1, rows.Count / 20);
        int top1N = System.Math.Max(1, rows.Count / 100);

        double top5Mean = rows.OrderByDescending(r => r.Score).Take(top5N).Average(r => (double)r.Label);
        double top1Mean = rows.OrderByDescending(r => r.Score).Take(top1N).Average(r => (double)r.Label);

        double hitRate2Pct = top10.Count == 0
            ? 0
            : top10.Count(r => r.Label >= 0.02f) / (double)top10.Count;

        Console.WriteLine($"  Ranking (Test): avgLabel={avgLabel:P2}, avgPred={avgPred:P2}");
        Console.WriteLine($"  Ranking (Test): directionalAcc={dirAcc:P1}, spearman={spearman:0.###}");
        Console.WriteLine($"  Ranking (Test): top10% realized mean={top10Mean:P2}, hitRate(Label>=+2%)={hitRate2Pct:P1} (n={top10N})");
        Console.WriteLine($"  Ranking (Test): top5% realized mean={top5Mean:P2} (n={top5N})");
        Console.WriteLine($"  Ranking (Test): top1% realized mean={top1Mean:P2} (n={top1N})");
    }

    private static double SpearmanCorrelation(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length < 2)
            return 0;

        var rx = Rank(x);
        var ry = Rank(y);

        return PearsonCorrelation(rx, ry);
    }

    private static double[] Rank(double[] values)
    {
        // Average ranks for ties
        var indexed = values
            .Select((v, i) => (Value: v, Index: i))
            .OrderBy(t => t.Value)
            .ToArray();

        var ranks = new double[values.Length];

        int i = 0;
        while (i < indexed.Length)
        {
            int j = i;
            while (j < indexed.Length && indexed[j].Value.Equals(indexed[i].Value))
                j++;

            // rank positions are 1..n
            double avgRank = (i + 1 + j) / 2.0;

            for (int k = i; k < j; k++)
                ranks[indexed[k].Index] = avgRank;

            i = j;
        }

        return ranks;
    }

    private static double PearsonCorrelation(double[] x, double[] y)
    {
        int n = x.Length;
        if (n < 2) return 0;

        double mx = x.Average();
        double my = y.Average();

        double cov = 0, vx = 0, vy = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - mx;
            double dy = y[i] - my;
            cov += dx * dy;
            vx += dx * dx;
            vy += dy * dy;
        }

        double denom = System.Math.Sqrt(vx * vy);
        return denom == 0 ? 0 : cov / denom;
    }

    private sealed class RegressionEvalRow
    {
        public float Label { get; set; }
        public float Score { get; set; }
    }

    private static ProfitTrainingResult TrainThreeWay(
        ProfitModelDefinition model,
        List<ProfitWindow> trainWindows,
        List<ProfitWindow> testWindows,
        string modelPath,
        int symbolsUsed)
    {
        var mlContext = new MLContext(seed: 123);

        int featureCount = trainWindows[0].Features.Length;
        var schemaDefinition = SchemaDefinition.Create(typeof(ProfitWindow));
        schemaDefinition[nameof(ProfitWindow.Features)].ColumnType =
            new VectorDataViewType(NumberDataViewType.Single, featureCount);

        IDataView trainData = mlContext.Data.LoadFromEnumerable(trainWindows, schemaDefinition);
        IDataView testData = mlContext.Data.LoadFromEnumerable(testWindows, schemaDefinition);

        var pipeline = mlContext.Transforms
            .Conversion.MapValueToKey("Label", nameof(ProfitWindow.ThreeWayLabel))
            .Append(mlContext.Transforms.NormalizeMinMax(nameof(ProfitWindow.Features)))
            .Append(mlContext.MulticlassClassification.Trainers.LightGbm(
                labelColumnName: "Label",
                featureColumnName: nameof(ProfitWindow.Features)))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        Console.WriteLine($"Training {model.TaskType} (3-Way, lookback={model.Lookback}, horizon={model.HorizonBars})...");
        Console.WriteLine($"  Train: {trainWindows.Count:N0}, Test: {testWindows.Count:N0}, Features: {featureCount}");

        var trainedModel = pipeline.Fit(trainData);

        var predictions = trainedModel.Transform(testData);
        var metrics = mlContext.MulticlassClassification.Evaluate(predictions, labelColumnName: "Label");

        Console.WriteLine($"  MacroAccuracy: {metrics.MacroAccuracy:0.####}");
        Console.WriteLine($"  MicroAccuracy: {metrics.MicroAccuracy:0.####}");
        Console.WriteLine($"  LogLoss:       {metrics.LogLoss:0.####}");

        PrintConfusionMatrix(metrics);

        mlContext.Model.Save(trainedModel, trainData.Schema, modelPath);
        Console.WriteLine($"  Model saved: {modelPath}\n");

        return new ProfitTrainingResult(
            Success: true,
            SymbolsUsed: symbolsUsed,
            TrainWindows: trainWindows.Count,
            TestWindows: testWindows.Count,
            PrimaryMetric: metrics.MacroAccuracy,
            SecondaryMetric: metrics.MicroAccuracy);
    }

    private static void PrintConfusionMatrix(MulticlassClassificationMetrics metrics)
    {
        // Label indices: 0=Sell, 1=Hold, 2=Buy
        var counts = metrics.ConfusionMatrix.Counts;

        if (counts.Count != 3 || counts.Any(r => r.Count != 3))
        {
            Console.WriteLine("  ConfusionMatrix: (unexpected size)");
            return;
        }

        Console.WriteLine("  ConfusionMatrix (Actual x Pred):");
        Console.WriteLine("               PredSell  PredHold  PredBuy");
        Console.WriteLine($"    ActSell   {counts[0][0],8:0} {counts[0][1],9:0} {counts[0][2],8:0}");
        Console.WriteLine($"    ActHold   {counts[1][0],8:0} {counts[1][1],9:0} {counts[1][2],8:0}");
        Console.WriteLine($"    ActBuy    {counts[2][0],8:0} {counts[2][1],9:0} {counts[2][2],8:0}");

        var labels = new[] { "Sell", "Hold", "Buy" };

        for (int c = 0; c < 3; c++)
        {
            double tp = counts[c][c];
            double fn = counts[c].Sum() - tp;

            double fp = 0;
            for (int r = 0; r < 3; r++)
                fp += counts[r][c];
            fp -= tp;

            double precision = (tp + fp) == 0 ? 0 : tp / (tp + fp);
            double recall = (tp + fn) == 0 ? 0 : tp / (tp + fn);

            Console.WriteLine($"  {labels[c],4}: precision={precision:P1}, recall={recall:P1}");
        }
    }

    private static ProfitTrainingResult TrainBinary(
        ProfitModelDefinition model,
        List<ProfitWindow> trainWindows,
        List<ProfitWindow> testWindows,
        string modelPath,
        int symbolsUsed)
    {
        var mlContext = new MLContext(seed: 123);

        int featureCount = trainWindows[0].Features.Length;
        var schemaDefinition = SchemaDefinition.Create(typeof(ProfitWindow));
        schemaDefinition[nameof(ProfitWindow.Features)].ColumnType =
            new VectorDataViewType(NumberDataViewType.Single, featureCount);

        IDataView trainData = mlContext.Data.LoadFromEnumerable(trainWindows, schemaDefinition);
        IDataView testData = mlContext.Data.LoadFromEnumerable(testWindows, schemaDefinition);

        var pipeline = mlContext.Transforms
            .CopyColumns("Label", nameof(ProfitWindow.IsEvent))
            .Append(mlContext.Transforms.NormalizeMinMax(nameof(ProfitWindow.Features)))
            .Append(mlContext.BinaryClassification.Trainers.LightGbm(
                labelColumnName: "Label",
                featureColumnName: nameof(ProfitWindow.Features)));

        Console.WriteLine($"Training {model.TaskType} (Binary, lookback={model.Lookback}, horizon={model.HorizonBars})...");
        Console.WriteLine($"  Train: {trainWindows.Count:N0}, Test: {testWindows.Count:N0}, Features: {featureCount}");

        var trainedModel = pipeline.Fit(trainData);

        var predictions = trainedModel.Transform(testData);
        var metrics = mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: "Label");

        Console.WriteLine($"  Accuracy: {metrics.Accuracy:0.####}");
        Console.WriteLine($"  AUC:      {metrics.AreaUnderRocCurve:0.####}");
        Console.WriteLine($"  F1:       {metrics.F1Score:0.####} (at default threshold=0.5)");

        // Get rows with probabilities for threshold sweep
        var rows = mlContext.Data.CreateEnumerable<BinaryEvalRow>(predictions, reuseRowObject: false).ToList();

        PrintBinaryPositiveStats(rows);

        // Threshold sweep to find optimal operating point
        var (optimalThreshold, optPrecision, optRecall, optF1) = FindOptimalThreshold(rows);

        Console.WriteLine($"  ─────────────────────────────────────────");
        Console.WriteLine($"  Threshold Sweep Results:");
        Console.WriteLine($"    Optimal threshold: {optimalThreshold:0.###}");
        Console.WriteLine($"    F1 at optimal:     {optF1:P1}");
        Console.WriteLine($"    Precision:         {optPrecision:P1}");
        Console.WriteLine($"    Recall:            {optRecall:P1}");

        // Also show what happens at a few fixed thresholds
        PrintThresholdStats(rows, 0.30, "0.30 (aggressive)");
        PrintThresholdStats(rows, 0.40, "0.40");
        PrintThresholdStats(rows, 0.50, "0.50 (default)");
        PrintThresholdStats(rows, 0.60, "0.60 (conservative)");

        PrintTopDecileLift(rows);

        mlContext.Model.Save(trainedModel, trainData.Schema, modelPath);
        Console.WriteLine($"  Model saved: {modelPath}\n");

        return new ProfitTrainingResult(
            Success: true,
            SymbolsUsed: symbolsUsed,
            TrainWindows: trainWindows.Count,
            TestWindows: testWindows.Count,
            PrimaryMetric: metrics.AreaUnderRocCurve,
            SecondaryMetric: metrics.F1Score,
            OptimalThreshold: optimalThreshold,
            PrecisionAtOptimal: optPrecision,
            RecallAtOptimal: optRecall,
            F1AtOptimal: optF1);
    }

    private static void PrintTopDecileLift(List<BinaryEvalRow> rows)
    {
        if (rows.Count < 100) return;

        double baselineRate = rows.Count(r => r.Label) / (double)rows.Count;

        // Sort by predicted probability descending
        var sorted = rows.OrderByDescending(r => r.Probability).ToList();

        int decileSize = rows.Count / 10;

        Console.WriteLine($"  ─────────────────────────────────────────");
        Console.WriteLine($"  Decile Lift Analysis (baseline={baselineRate:P1}):");

        for (int d = 0; d < 10; d++)
        {
            var decile = sorted.Skip(d * decileSize).Take(decileSize).ToList();
            double decileRate = decile.Count(r => r.Label) / (double)decile.Count;
            double lift = baselineRate > 0 ? decileRate / baselineRate : 0;
            double minProb = decile.Min(r => r.Probability);
            double maxProb = decile.Max(r => r.Probability);

            string marker = d == 0 ? " ← TOP" : (d == 9 ? " ← BOTTOM" : "");
            Console.WriteLine($"    D{d + 1}: rate={decileRate:P1}, lift={lift:0.00}x, prob=[{minProb:0.##}-{maxProb:0.##}]{marker}");
        }

        // Specifically call out top decile
        var topDecile = sorted.Take(decileSize).ToList();
        double topRate = topDecile.Count(r => r.Label) / (double)topDecile.Count;
        double topLift = baselineRate > 0 ? topRate / baselineRate : 0;

        Console.WriteLine($"  Top-decile: {topRate:P1} vs baseline {baselineRate:P1} → lift={topLift:0.00}x");

        if (topLift >= 1.3)
            Console.WriteLine($"  ✓ Usable as gate (30%+ lift in top decile)");
        else if (topLift >= 1.15)
            Console.WriteLine($"  ? Marginal signal (15-30% lift)");
        else
            Console.WriteLine($"  ✗ No actionable signal (lift < 15%)");
    }

    private static (double threshold, double precision, double recall, double f1) FindOptimalThreshold(List<BinaryEvalRow> rows)
    {
        if (rows.Count == 0)
            return (0.5, 0, 0, 0);

        double bestF1 = 0;
        double bestThreshold = 0.5;
        double bestPrecision = 0;
        double bestRecall = 0;

        // Sweep thresholds from 0.1 to 0.9 in 0.01 increments
        for (double t = 0.10; t <= 0.90; t += 0.01)
        {
            int tp = rows.Count(r => r.Label && r.Probability >= t);
            int fp = rows.Count(r => !r.Label && r.Probability >= t);
            int fn = rows.Count(r => r.Label && r.Probability < t);

            double precision = (tp + fp) == 0 ? 0 : tp / (double)(tp + fp);
            double recall = (tp + fn) == 0 ? 0 : tp / (double)(tp + fn);
            double f1 = (precision + recall) == 0 ? 0 : 2 * precision * recall / (precision + recall);

            if (f1 > bestF1)
            {
                bestF1 = f1;
                bestThreshold = t;
                bestPrecision = precision;
                bestRecall = recall;
            }
        }

        return (bestThreshold, bestPrecision, bestRecall, bestF1);
    }

    private static void PrintThresholdStats(List<BinaryEvalRow> rows, double threshold, string label)
    {
        int tp = rows.Count(r => r.Label && r.Probability >= threshold);
        int fp = rows.Count(r => !r.Label && r.Probability >= threshold);
        int fn = rows.Count(r => r.Label && r.Probability < threshold);

        double precision = (tp + fp) == 0 ? 0 : tp / (double)(tp + fp);
        double recall = (tp + fn) == 0 ? 0 : tp / (double)(tp + fn);
        double f1 = (precision + recall) == 0 ? 0 : 2 * precision * recall / (precision + recall);
        double predictedPosRate = (tp + fp) / (double)rows.Count;

        Console.WriteLine($"    @{label}: F1={f1:P1}, prec={precision:P1}, rec={recall:P1}, predPosRate={predictedPosRate:P1}");
    }

    private static void PrintBinaryPositiveStats(List<BinaryEvalRow> rows)
    {
        if (rows.Count == 0)
        {
            Console.WriteLine("  PosClass: n=0");
            return;
        }

        int tp = rows.Count(r => r.Label && r.PredictedLabel);
        int fp = rows.Count(r => !r.Label && r.PredictedLabel);
        int fn = rows.Count(r => r.Label && !r.PredictedLabel);
        int tn = rows.Count - tp - fp - fn;

        double precision = (tp + fp) == 0 ? 0 : tp / (double)(tp + fp);
        double recall = (tp + fn) == 0 ? 0 : tp / (double)(tp + fn);

        double actualPosRate = rows.Count(r => r.Label) / (double)rows.Count;
        double predictedPosRate = rows.Count(r => r.PredictedLabel) / (double)rows.Count;

        Console.WriteLine($"  PosClass (Test): precision={precision:P1}, recall={recall:P1}");
        Console.WriteLine($"  PosClass (Test): actualPosRate={actualPosRate:P1}, predictedPosRate={predictedPosRate:P1}");
        Console.WriteLine($"  PosClass (Test): TP={tp}, FP={fp}, FN={fn}, TN={tn}");
    }

    private static void PrintBinaryPositiveStats(MLContext mlContext, IDataView predictions)
    {
        var rows = mlContext.Data.CreateEnumerable<BinaryEvalRow>(predictions, reuseRowObject: false).ToList();
        if (rows.Count == 0)
        {
            Console.WriteLine("  PosClass: n=0");
            return;
        }

        int tp = rows.Count(r => r.Label && r.PredictedLabel);
        int fp = rows.Count(r => !r.Label && r.PredictedLabel);
        int fn = rows.Count(r => r.Label && !r.PredictedLabel);
        int tn = rows.Count - tp - fp - fn;

        double precision = (tp + fp) == 0 ? 0 : tp / (double)(tp + fp);
        double recall = (tp + fn) == 0 ? 0 : tp / (double)(tp + fn);

        double actualPosRate = rows.Count(r => r.Label) / (double)rows.Count;
        double predictedPosRate = rows.Count(r => r.PredictedLabel) / (double)rows.Count;

        Console.WriteLine($"  PosClass (Test): precision={precision:P1}, recall={recall:P1}");
        Console.WriteLine($"  PosClass (Test): actualPosRate={actualPosRate:P1}, predictedPosRate={predictedPosRate:P1}");
        Console.WriteLine($"  PosClass (Test): TP={tp}, FP={fp}, FN={fn}, TN={tn}");
    }

    private sealed class BinaryEvalRow
    {
        public bool Label { get; set; }
        public bool PredictedLabel { get; set; }
        public float Probability { get; set; }
        public float Score { get; set; }
    }

    private static List<ProfitWindow> BuildProfitWindows(
        List<DailyBar> bars,
        int lookback,
        int horizon,
        IFeatureBuilder featureBuilder,
        ILabeler labeler,
        ProfitModelKind modelKind,
        float? regressionReturnClamp)
    {
        var result = new List<ProfitWindow>();

        for (int windowEnd = lookback - 1; windowEnd < bars.Count - horizon; windowEnd++)
        {
            var windowBars = bars.GetRange(windowEnd - lookback + 1, lookback);
            var futureBars = bars.GetRange(windowEnd + 1, horizon);

            var label = labeler.ComputeLabel(windowBars, futureBars);
            if (!label.IsValid)
                continue;

            // Only applies to forward-return models. Event labelers generally return ForwardReturn=0.
            if (label.ForwardReturn != 0 && System.Math.Abs(label.ForwardReturn) > MaxAbsForwardReturn)
                continue;

            float forwardReturn = label.ForwardReturn;

            if (modelKind == ProfitModelKind.Regression && regressionReturnClamp.HasValue)
            {
                float c = regressionReturnClamp.Value;
                forwardReturn = System.Math.Clamp(forwardReturn, -c, c);
            }

            uint threeWayEncoded = label.ThreeWayClass switch
            {
                ThreeWayLabel.Sell => 0,
                ThreeWayLabel.Hold => 1,
                ThreeWayLabel.Buy => 2,
                _ => 1
            };

            result.Add(new ProfitWindow
            {
                Features = featureBuilder.Build(windowBars),
                ForwardReturn = forwardReturn,
                ThreeWayLabel = threeWayEncoded,
                IsEvent = label.ThreeWayClass == ThreeWayLabel.Buy
            });
        }

        return result;
    }
}

