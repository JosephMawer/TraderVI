using Core.ML.Engine.Patterns.Features;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

public static class ProfitModelRegistry
{
    // Toggle flags (edit these during iteration)
    private const bool EnableExpectedReturn10 = true;           // ranking hint (weak, but keep for now)
    private const bool EnableDirection10 = false;               // DISABLED: 3-way is weak (40% accuracy)

    private const bool EnableBreakoutPriorHigh10 = true;        // AUC 0.81 - best signal
    private const bool EnableBreakoutAtr10 = false;             // AUC 0.55 - too weak
    private const bool EnableVolatilityExpansion10 = false;     // Old labeler - unusable

    private const bool EnableExpectedReturn5 = false;
    private const bool EnableDirection5 = false;

    // Relative strength continuation
    private const bool EnableRelStrengthCont10_1pct = false;    // AUC 0.60 - too weak
    private const bool EnableRelStrengthCont10_2pct = true;     // AUC 0.65 - keep as confirmation

    // Direction model (single model; replaces BinaryUp10_*pct variants)
    private const bool EnableBinaryUp10 = true;                 // Single direction score model (recommended)
    private const bool EnableBinaryUp10Market = false;          // AUC 0.64 - market context didn't help much

    // Volatility expansion (redefined) - NOW WORKING!
    private const bool EnableVolExpansionRelative10 = true;     // AUC 0.66 ✅

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

        // ════════════════════════════════════════════════════════════════
        // Direction model (single)
        // ════════════════════════════════════════════════════════════════
        if (EnableBinaryUp10)
        {
            // Keep the “4% in 10d” label because it was the most separable direction target (AUC ~0.70).
            models.Add(new ProfitModelDefinition(
                TaskType: "BinaryUp10",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new AtrVolatilityBreakoutFeatureBuilder(),
                Labeler: new BinaryUpLabeler(horizonBars: 10, upThresholdPercent: 4.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        if (EnableBinaryUp10Market)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "BinaryUp10Market",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new MarketContextFeatureBuilder(),
                Labeler: new BinaryUpLabeler(horizonBars: 10, upThresholdPercent: 4.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        // ════════════════════════════════════════════════════════════════
        // Volatility expansion (relative to own trailing ATR)
        // ════════════════════════════════════════════════════════════════
        if (EnableVolExpansionRelative10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "VolExpansionRelative10",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new AtrVolatilityBreakoutFeatureBuilder(),
                Labeler: new RelativeVolatilityExpansionLabeler(horizonBars: 10, percentileThreshold: 80),
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

        // ════════════════════════════════════════════════════════════════
        // Relative strength continuation (market-relative outperformance)
        // ════════════════════════════════════════════════════════════════
        if (EnableRelStrengthCont10_1pct)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "RelStrengthCont10_1pct",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new MarketContextFeatureBuilder(),
                Labeler: new RelativeStrengthContinuationLabeler(horizonBars: 10, outperformThresholdPercent: 1.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        if (EnableRelStrengthCont10_2pct)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "RelStrengthCont10_2pct",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new MarketContextFeatureBuilder(),
                Labeler: new RelativeStrengthContinuationLabeler(horizonBars: 10, outperformThresholdPercent: 2.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        return models;
    }

    public static ProfitModelDefinition? GetByTaskType(string taskType)
        => All.FirstOrDefault(p => p.TaskType == taskType);

    public static IEnumerable<ProfitModelDefinition> GetByKind(ProfitModelKind kind)
        => All.Where(p => p.ModelKind == kind);
}