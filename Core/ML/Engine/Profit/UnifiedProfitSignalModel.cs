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
/// Handles both regression and 3-way classification.
/// </summary>
public class UnifiedProfitSignalModel : IStockSignalModel
{
    private static readonly MLContext MlContext = new();

    private readonly ProfitModelDefinition _model;
    private readonly PredictionEngine<ProfitWindow, RegressionPrediction>? _regressionEngine;
    private readonly PredictionEngine<ProfitWindow, ThreeWayPrediction>? _threeWayEngine;

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

        if (model.ModelKind == ProfitModelKind.Regression)
        {
            _regressionEngine = MlContext.Model.CreatePredictionEngine<ProfitWindow, RegressionPrediction>(
                loadedModel, inputSchemaDefinition: schemaDefinition);
        }
        else
        {
            _threeWayEngine = MlContext.Model.CreatePredictionEngine<ProfitWindow, ThreeWayPrediction>(
                loadedModel, inputSchemaDefinition: schemaDefinition);
        }
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

        var features = _model.FeatureBuilder.Build(windowBars);

        var input = new ProfitWindow
        {
            Features = features,
            ForwardReturn = 0,
            ThreeWayLabel = 1
        };

        if (_model.ModelKind == ProfitModelKind.Regression)
        {
            return EvaluateRegression(input);
        }
        else
        {
            return EvaluateThreeWay(input);
        }
    }

    private SignalResult EvaluateRegression(ProfitWindow input)
    {
        var prediction = _regressionEngine!.Predict(input);
        float expectedReturn = prediction.Score;

        // Convert expected return to hint (using labeler thresholds)
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

        // PredictedLabel: 0=Sell, 1=Hold, 2=Buy (as trained)
        var hint = prediction.PredictedLabel switch
        {
            0 => TradeDirection.Sell,
            2 => TradeDirection.Buy,
            _ => TradeDirection.Hold
        };

        // Score = confidence in predicted class
        float confidence = prediction.Score?.Length > 0
            ? prediction.Score.Max()
            : 0f;

        return new SignalResult(
            Name,
            Score: confidence,
            Hint: hint,
            Notes: $"Class={hint}, Confidence={confidence:P1}, Horizon={_model.HorizonBars}d");
    }

    public static UnifiedProfitSignalModel? FromRegistryInfo(ModelRegistryInfo info)
    {
        var model = ProfitModelRegistry.GetByTaskType(info.TaskType);
        if (model == null)
            return null;

        return new UnifiedProfitSignalModel(model, info.ZipPath);
    }
}