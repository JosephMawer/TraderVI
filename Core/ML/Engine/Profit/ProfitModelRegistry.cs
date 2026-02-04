using Core.ML.Engine.Patterns.Features;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

public static class ProfitModelRegistry
{
    // Toggle flags (edit these during iteration)
    private const bool EnableExpectedReturn10 = false;          // DISABLED: replaced by risk-adjusted
    private const bool EnableDirection10 = false;               // DISABLED: 3-way is weak (40% accuracy)

    private const bool EnableBreakoutPriorHigh10 = false;       // DISABLED: replaced by BreakoutEnhanced
    private const bool EnableBreakoutAtr10 = false;             // AUC 0.55 - too weak
    private const bool EnableVolatilityExpansion10 = false;     // Old labeler - unusable

    private const bool EnableExpectedReturn5 = false;
    private const bool EnableDirection5 = false;

    // Relative strength continuation
    private const bool EnableRelStrengthCont10_1pct = false;    // AUC 0.60 - too weak
    private const bool EnableRelStrengthCont10_2pct = true;     // AUC 0.65 - keep as confirmation

    // Direction model (single model)
    private const bool EnableBinaryUp10 = true;                 // AUC 0.70 - keep
    private const bool EnableBinaryUp10Market = false;          // AUC 0.64 - market context didn't help much

    // Volatility expansion (redefined) - NOW WORKING!
    private const bool EnableVolExpansionRelative10 = true;     // AUC 0.66 ✅

    // Enhanced models (starter pack features)
    private const bool EnableBinaryUp10Enhanced = false;        // No improvement - disabled
    private const bool EnableBreakoutEnhanced = true;           // AUC 0.814 - best breakout model ✓

    // ════════════════════════════════════════════════════════════════
    // NEW: Risk-adjusted move models (replaces regression veto)
    // ════════════════════════════════════════════════════════════════
    private const bool EnableRiskAdjustedUp10 = true;           // P(up move >= 0.75 ATR)
    private const bool EnableRiskAdjustedDown10 = true;         // P(down move >= 0.75 ATR) - veto signal

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

        // Direction model
        if (EnableBinaryUp10)
        {
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

        // Volatility expansion
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

        // Relative strength continuation
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

        // Enhanced models (starter pack features)
        if (EnableBinaryUp10Enhanced)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "BinaryUp10Enhanced",
                Lookback: 55,
                HorizonBars: 10,
                FeatureBuilder: new EnhancedFeatureBuilder(),
                Labeler: new BinaryUpLabeler(horizonBars: 10, upThresholdPercent: 4.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        if (EnableBreakoutEnhanced)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "BreakoutEnhanced",
                Lookback: 55,
                HorizonBars: 10,
                FeatureBuilder: new EnhancedFeatureBuilder(),
                Labeler: new BreakoutAbovePriorHighLabeler(horizonBars: 10, priorHighLookback: 20, breakoutPercent: 1.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        // ════════════════════════════════════════════════════════════════
        // RISK-ADJUSTED MOVE MODELS (replaces regression veto)
        // ════════════════════════════════════════════════════════════════

        if (EnableRiskAdjustedUp10)
        {
            // P(up move >= 0.75 ATR in next 10 days)
            // Use enhanced features for best signal
            models.Add(new ProfitModelDefinition(
                TaskType: "RiskAdjUp10",
                Lookback: 55,
                HorizonBars: 10,
                FeatureBuilder: new EnhancedFeatureBuilder(),
                Labeler: new RiskAdjustedUpLabeler(horizonBars: 10, atrThreshold: 0.75f, atrPeriod: 14),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        if (EnableRiskAdjustedDown10)
        {
            // P(down move >= 0.75 ATR in next 10 days) - VETO SIGNAL
            models.Add(new ProfitModelDefinition(
                TaskType: "RiskAdjDown10",
                Lookback: 55,
                HorizonBars: 10,
                FeatureBuilder: new EnhancedFeatureBuilder(),
                Labeler: new RiskAdjustedDownLabeler(horizonBars: 10, atrThreshold: 0.75f, atrPeriod: 14),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        return models;
    }

    public static ProfitModelDefinition? GetByTaskType(string taskType)
        => All.FirstOrDefault(p => p.TaskType == taskType);

    public static IEnumerable<ProfitModelDefinition> GetByKind(ProfitModelKind kind)
        => All.Where(p => p.ModelKind == kind);
}