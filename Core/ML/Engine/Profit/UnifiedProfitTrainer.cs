using Core.ML.Engine.Patterns;
using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

public static class UnifiedProfitTrainer
{
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

        // ─────────────────────────────────────────────────────────────
        // Logging improvements: labeler info + class balance + return stats
        // ─────────────────────────────────────────────────────────────
        Console.WriteLine($"Labeler: {model.Labeler.Name}");
        Console.WriteLine($"FeatureSet: {model.FeatureBuilder.Name}");

        PrintReturnStats("Train", trainWindows);
        PrintReturnStats("Test ", testWindows);

        if (model.ModelKind == ProfitModelKind.ThreeWayClassification)
        {
            PrintClassBalance("Train", trainWindows);
            PrintClassBalance("Test ", testWindows);
        }

        Console.WriteLine();

        return model.ModelKind switch
        {
            ProfitModelKind.Regression => TrainRegression(model, trainWindows, testWindows, modelPath, symbolsUsed),
            ProfitModelKind.ThreeWayClassification => TrainThreeWay(model, trainWindows, testWindows, modelPath, symbolsUsed),
            _ => new ProfitTrainingResult(false, 0, 0, 0, 0, 0)
        };
    }

    private static void PrintClassBalance(string name, List<ProfitWindow> windows)
    {
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
                ThreeWayLabel = threeWayEncoded
            });
        }

        return result;
    }
}

public record ProfitTrainingResult(
    bool Success,
    int SymbolsUsed,
    int TrainWindows,
    int TestWindows,
    double PrimaryMetric,
    double SecondaryMetric);