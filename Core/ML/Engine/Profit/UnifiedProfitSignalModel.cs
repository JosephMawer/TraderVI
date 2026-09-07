using Core.Db;
using Core.ML.Engine.Patterns;
using Core.Calibration;
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
public class UnifiedProfitSignalModel : IStockSignalModel, IProfitSignalModel
{
    private static readonly MLContext MlContext = new();

    private readonly ProfitModelDefinition _model;

    private readonly PredictionEngine<ProfitWindow, RegressionPrediction>? _regressionEngine;
    private readonly PredictionEngine<ProfitWindow, ThreeWayPrediction>? _threeWayEngine;
    private readonly PredictionEngine<ProfitWindow, BinaryPrediction>? _binaryEngine;

    private readonly float _thresholdBuy;
    private readonly float _thresholdSell;

    public string Name => _model.TaskType;
    public ProfitModelKind ModelKind => _model.ModelKind;
    public SignalRole Role => _model.Role;
    public float CompositeWeight => _model.CompositeWeight;
    public ModelArtifactProvenance? ArtifactProvenance { get; }

    public UnifiedProfitSignalModel(
        ProfitModelDefinition model,
        string modelZipPath,
        float? thresholdBuy = null,
        float? thresholdSell = null)
        : this(model, MlContext.Model.Load(modelZipPath, out _), null, thresholdBuy, thresholdSell)
    {
    }

    private UnifiedProfitSignalModel(
        ProfitModelDefinition model,
        ITransformer loadedModel,
        ModelArtifactProvenance? provenance,
        float? thresholdBuy,
        float? thresholdSell)
    {
        _model = model;
        ArtifactProvenance = provenance;

        // Use registry thresholds if provided, otherwise fall back to model definition defaults.
        _thresholdBuy = thresholdBuy ?? (model.BuyThresholdPercent / 100f);
        _thresholdSell = thresholdSell ?? (model.SellThresholdPercent / 100f);

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
            if (_model.FeatureBuilder is DatedProfitFeatureBuilder)
                throw new ProfitFeatureInputException("StockFeatureHistoryMissing", false,
                    history.Count == 0 ? System.DateTime.MinValue : history[^1].Date);
            return new SignalResult(
                Name,
                Score: 0,
                Hint: TradeDirection.Hold,
                Notes: $"Insufficient history (need {lookback} bars, got {history.Count})");
        }

        var input = BuildPredictionInput(_model, history);

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

        var hint = expectedReturn >= _thresholdBuy ? TradeDirection.Buy
                 : expectedReturn <= _thresholdSell ? TradeDirection.Sell
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

        // Use registry-provided threshold instead of hardcoded 0.50.
        var hint = p >= _thresholdBuy ? TradeDirection.Buy : TradeDirection.Hold;

        return new SignalResult(
            Name,
            Score: p,
            Hint: hint,
            Notes: $"EventProbability={p:P1}, ThresholdBuy={_thresholdBuy:P1}, Horizon={_model.HorizonBars}d");
    }

    internal static ProfitWindow BuildPredictionInput(ProfitModelDefinition model, IReadOnlyList<DailyBar> history) => new()
    {
        Features = model.FeatureBuilder is DatedProfitFeatureBuilder dated
            ? dated.Build(history.TakeLast(model.Lookback).ToArray(), DatedProfitFeatureBuilder.DuplicateSessions(history))
            : model.FeatureBuilder.Build(history.TakeLast(model.Lookback).ToArray()),
        ForwardReturn = 0,
        ThreeWayLabel = 1,
        IsEvent = false
    };

    public static UnifiedProfitSignalModel? FromRegistryInfo(ModelRegistryInfo info,
        ProfitFeatureInputs? inputs = null, System.DateTime? predictionSession = null,
        ReviewedProfitInputBinding? binding = null)
    {
        var model = ProfitModelRegistry.GetByTaskType(info.TaskType);
        if (model == null)
            return null;

        if (inputs is null && info.FeatureSet?.EndsWith(".XiuDatedV1", System.StringComparison.Ordinal) == true)
            throw new System.InvalidOperationException("A dated candidate model requires the reviewed corrected-input path.");

        bool correctedBinding = binding?.InputContract == ProfitFeatureInputs.Contract;
        if ((inputs is not null) != correctedBinding || (inputs is not null && predictionSession is null) ||
            (binding is not null && !correctedBinding && binding.InputContract != ProfitFeatureInputs.LegacyContract))
            throw new System.InvalidOperationException("Corrected inference requires both reviewed model compatibility and an exact prediction session.");
        if (inputs is not null) model = inputs.Bind(model, predictionSession);

        return LoadedModelArtifact.Load(info, (stream, provenance) =>
        {
            binding?.ValidateArtifact(provenance);
            return new UnifiedProfitSignalModel(model, MlContext.Model.Load(stream, out _), provenance,
                thresholdBuy: (float)info.ThresholdBuy, thresholdSell: (float)info.ThresholdSell);
        });
    }
}
