using Core.Db;
using Core.ML.Engine.Patterns;
using Core.Trader;
using Microsoft.ML;
using Microsoft.ML.Data;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Runtime signal model for profit predictions.
/// Handles regression, 3-way classification, and binary event models.
/// </summary>
public class UnifiedProfitSignalModel : IStockSignalModel
{
    private static readonly MLContext MlContext = new();

    private readonly ProfitModelDefinition _model;

    private readonly PredictionEngine<ProfitWindow, RegressionPrediction>? _regressionEngine;
    private readonly PredictionEngine<ProfitWindow, ThreeWayPrediction>? _threeWayEngine;
    private readonly PredictionEngine<ProfitWindow, BinaryPrediction>? _binaryEngine;

    public string Name => _model.TaskType;
    public ProfitModelKind ModelKind => _model.ModelKind;

    public UnifiedProfitSignalModel(ProfitModelDefinition model, string modelZipPath)
    {
        _model = model;

        var loadedModel = MlContext.Model.Load(modelZipPath, out _);

        int featureCount = model.FeatureBuilder.FeatureCount(model.Lookback);
        var schemaDefinition = SchemaDefinition.Create(typeof(ProfitWindow));
        schemaDefinition[nameof(ProfitWindow.Features)].ColumnType =
            new VectorDataViewType(NumberDataViewType.Single, featureCount);

        _regressionEngine = model.ModelKind == ProfitModelKind.Regression
            ? MlContext.Model.CreatePredictionEngine<ProfitWindow, RegressionPrediction>(loadedModel, inputSchemaDefinition: schemaDefinition)
            : null;

        _threeWayEngine = model.ModelKind == ProfitModelKind.ThreeWayClassification
            ? MlContext.Model.CreatePredictionEngine<ProfitWindow, ThreeWayPrediction>(loadedModel, inputSchemaDefinition: schemaDefinition)
            : null;

        _binaryEngine = model.ModelKind == ProfitModelKind.BinaryClassification
            ? MlContext.Model.CreatePredictionEngine<ProfitWindow, BinaryPrediction>(loadedModel, inputSchemaDefinition: schemaDefinition)
            : null;
    }

    public SignalResult Evaluate(IReadOnlyList<DailyBar> history)
    {
        int lookback = _model.Lookback;

        if (history.Count < lookback)
        {
            return new SignalResult(
                Name,
                Score: 0,
                Hint: TradeDirection.Hold,
                Notes: $"Insufficient history (need {lookback} bars, got {history.Count})");
        }

        var windowBars = history
            .Skip(history.Count - lookback)
            .Take(lookback)
            .ToList();

        var input = new ProfitWindow
        {
            Features = _model.FeatureBuilder.Build(windowBars),
            ForwardReturn = 0,
            ThreeWayLabel = 1,
            IsEvent = false
        };

        return _model.ModelKind switch
        {
            ProfitModelKind.Regression => EvaluateRegression(input),
            ProfitModelKind.ThreeWayClassification => EvaluateThreeWay(input),
            ProfitModelKind.BinaryClassification => EvaluateBinary(input),
            _ => new SignalResult(Name, 0, TradeDirection.Hold, "Unsupported model kind")
        };
    }

    private SignalResult EvaluateRegression(ProfitWindow input)
    {
        var prediction = _regressionEngine!.Predict(input);
        float expectedReturn = prediction.Score;

        var buyThreshold = _model.BuyThresholdPercent / 100f;
        var sellThreshold = _model.SellThresholdPercent / 100f;

        var hint = expectedReturn >= buyThreshold ? TradeDirection.Buy
                 : expectedReturn <= sellThreshold ? TradeDirection.Sell
                 : TradeDirection.Hold;

        return new SignalResult(
            Name,
            Score: expectedReturn,
            Hint: hint,
            Notes: $"ExpectedReturn={expectedReturn:P2}, Horizon={_model.HorizonBars}d");
    }

    private SignalResult EvaluateThreeWay(ProfitWindow input)
    {
        var prediction = _threeWayEngine!.Predict(input);

        var hint = prediction.PredictedLabel switch
        {
            0 => TradeDirection.Sell,
            2 => TradeDirection.Buy,
            _ => TradeDirection.Hold
        };

        float confidence = prediction.Score?.Length > 0
            ? prediction.Score.Max()
            : 0f;

        return new SignalResult(
            Name,
            Score: confidence,
            Hint: hint,
            Notes: $"Class={hint}, Confidence={confidence:P1}, Horizon={_model.HorizonBars}d");
    }

    private SignalResult EvaluateBinary(ProfitWindow input)
    {
        var prediction = _binaryEngine!.Predict(input);

        // Score is the probability of the "true" class (event occurred).
        float p = prediction.Probability;

        // Use Buy as the "event happened / bullish event" hint, else Hold.
        var hint = p >= 0.50f ? TradeDirection.Buy : TradeDirection.Hold;

        return new SignalResult(
            Name,
            Score: p,
            Hint: hint,
            Notes: $"EventProbability={p:P1}, Horizon={_model.HorizonBars}d");
    }

    public static UnifiedProfitSignalModel? FromRegistryInfo(ModelRegistryInfo info)
    {
        var model = ProfitModelRegistry.GetByTaskType(info.TaskType);
        if (model == null)
            return null;

        return new UnifiedProfitSignalModel(model, info.ZipPath);
    }
}