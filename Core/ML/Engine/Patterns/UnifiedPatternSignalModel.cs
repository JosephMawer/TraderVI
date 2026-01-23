using Core.Db;
using Core.Trader;
using Microsoft.ML;
using Microsoft.ML.Data;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns;

public class UnifiedPatternSignalModel : IStockSignalModel
{
    private static readonly MLContext MlContext = new();

    private readonly PatternDefinition _pattern;
    private readonly string _modelZipPath;
    private readonly double _thresholdBuy;
    private readonly double _thresholdSell;
    private readonly PredictionEngine<PatternWindow, PatternPrediction> _engine;

    public string Name => _pattern.TaskType;

    public UnifiedPatternSignalModel(
        PatternDefinition pattern,
        string modelZipPath,
        double thresholdBuy = 0.60,
        double thresholdSell = 0.40)
    {
        _pattern = pattern;
        _modelZipPath = modelZipPath;
        _thresholdBuy = thresholdBuy;
        _thresholdSell = thresholdSell;

        var model = MlContext.Model.Load(modelZipPath, out _);

        int featureCount = pattern.FeatureBuilder.FeatureCount(pattern.Lookback);
        var schemaDefinition = SchemaDefinition.Create(typeof(PatternWindow));
        schemaDefinition[nameof(PatternWindow.Features)].ColumnType =
            new VectorDataViewType(NumberDataViewType.Single, featureCount);

        _engine = MlContext.Model.CreatePredictionEngine<PatternWindow, PatternPrediction>(
            model,
            inputSchemaDefinition: schemaDefinition);
    }

    public SignalResult Evaluate(IReadOnlyList<DailyBar> history)
    {
        int lookback = _pattern.Lookback;

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

        var features = _pattern.FeatureBuilder.Build(windowBars);

        var input = new PatternWindow
        {
            Features = features,
            Label = false
        };

        var prediction = _engine.Predict(input);

        var hint = GetHint(prediction.Probability);

        return new SignalResult(
            Name,
            Score: prediction.Probability,
            Hint: hint,
            Notes: $"P({_pattern.Detector.PatternName})={prediction.Probability:0.###}, Score={prediction.Score:0.###}, Semantics={_pattern.Semantics}");
    }

    private TradeDirection GetHint(float probability)
    {
        return _pattern.Semantics switch
        {
            SignalSemantics.BullishWhenTrue =>
                probability >= _thresholdBuy ? TradeDirection.Buy : TradeDirection.Hold,

            SignalSemantics.BearishWhenTrue =>
                probability >= _thresholdBuy ? TradeDirection.Sell : TradeDirection.Hold,

            SignalSemantics.BullishBearishSymmetric =>
                probability >= _thresholdBuy ? TradeDirection.Buy :
                probability <= _thresholdSell ? TradeDirection.Sell :
                TradeDirection.Hold,

            _ => TradeDirection.Hold
        };
    }

    public static UnifiedPatternSignalModel? FromRegistryInfo(ModelRegistryInfo info)
    {
        var pattern = PatternRegistry.GetByTaskType(info.TaskType);
        if (pattern == null)
            return null;

        return new UnifiedPatternSignalModel(
            pattern,
            info.ZipPath,
            info.ThresholdBuy,
            info.ThresholdSell);
    }
}