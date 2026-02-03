using Core.ML.Engine.Patterns.Features;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

public static class ProfitModelRegistry
{
    // Toggle flags (edit these during iteration)
    private const bool EnableExpectedReturn10 = true;           // ranking hint
    private const bool EnableDirection10 = true;               // primary signal

    private const bool EnableBreakoutPriorHigh10 = true;
    private const bool EnableBreakoutAtr10 = false;      // AUC 0.55 - too weak
    private const bool EnableVolatilityExpansion10 = false; // F1 0.03 - unusable

    private const bool EnableExpectedReturn5 = false;
    private const bool EnableDirection5 = false;

    public static IReadOnlyList<ProfitModelDefinition> All { get; } = BuildAll();

    private static IReadOnlyList<ProfitModelDefinition> BuildAll()
    {
        var models = new List<ProfitModelDefinition>();

        if (EnableExpectedReturn10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "ExpectedReturn10",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new AtrVolatilityBreakoutFeatureBuilder(),
                Labeler: new ForwardReturnLabeler(horizonBars: 10, buyThresholdPercent: 2.0f, sellThresholdPercent: -2.0f),
                ModelKind: ProfitModelKind.Regression));
        }

        if (EnableDirection10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "Direction10",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new AtrVolatilityBreakoutFeatureBuilder(),
                Labeler: new ForwardReturnLabeler(horizonBars: 10, buyThresholdPercent: 2.0f, sellThresholdPercent: -2.0f),
                ModelKind: ProfitModelKind.ThreeWayClassification));
        }

        if (EnableBreakoutPriorHigh10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "BreakoutPriorHigh10",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new AtrVolatilityBreakoutFeatureBuilder(),
                Labeler: new BreakoutAbovePriorHighLabeler(horizonBars: 10, priorHighLookback: 20, breakoutPercent: 1.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        if (EnableBreakoutAtr10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "BreakoutAtr10",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new AtrVolatilityBreakoutFeatureBuilder(),
                Labeler: new BreakoutAtrMultipleLabeler(horizonBars: 10, atrPeriod: 14, atrMultiple: 1.5f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        if (EnableVolatilityExpansion10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "VolatilityExpansion10",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new AtrVolatilityBreakoutFeatureBuilder(),
                Labeler: new VolatilityExpansionLabeler(horizonBars: 10, expansionMultiple: 1.5f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        if (EnableExpectedReturn5)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "ExpectedReturn5",
                Lookback: 20,
                HorizonBars: 5,
                FeatureBuilder: new PriceVolumeFeatureBuilder(),
                Labeler: new ForwardReturnLabeler(horizonBars: 5, buyThresholdPercent: 1.5f, sellThresholdPercent: -1.5f),
                ModelKind: ProfitModelKind.Regression));
        }

        if (EnableDirection5)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "Direction5",
                Lookback: 20,
                HorizonBars: 5,
                FeatureBuilder: new PriceVolumeFeatureBuilder(),
                Labeler: new ForwardReturnLabeler(horizonBars: 5, buyThresholdPercent: 1.5f, sellThresholdPercent: -1.5f),
                ModelKind: ProfitModelKind.ThreeWayClassification));
        }

        return models;
    }

    public static ProfitModelDefinition? GetByTaskType(string taskType)
        => All.FirstOrDefault(p => p.TaskType == taskType);

    public static IEnumerable<ProfitModelDefinition> GetByKind(ProfitModelKind kind)
        => All.Where(p => p.ModelKind == kind);
}