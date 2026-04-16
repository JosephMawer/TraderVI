using Core.ML.Engine.Patterns.Features;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

public static class ProfitModelRegistry
{
    // ════════════════════════════════════════════════════════════════
    // WORKING MODELS (keep enabled)
    // ════════════════════════════════════════════════════════════════
    private const bool EnableBinaryUp10 = true;                 // AUC 0.70, Lift 1.64x ✓
    private const bool EnableBinaryDown10 = true;               // NEW: down-tail veto signal
    private const bool EnableVolExpansionRelative10 = true;     // AUC 0.66, Lift 1.61x ✓
    private const bool EnableRelStrengthCont10_2pct = false;    // AUC 0.65, flat output, removed v3.0
    private const bool EnableBreakoutEnhanced = true;           // AUC 0.81, Lift 2.20x ✓
                                                                // All direction models disabled:
    private const bool EnableDirUp5 = false;                    // AUC 0.54 - random
    private const bool EnableBandedDirUp5 = false;              // No negatives
    private const bool EnableSetupBandedDirUp5 = false;         // AUC 0.52 - random
    private const bool EnableSetupBandedDirDown5 = false;       // AUC 0.53 - random
    private const bool EnableDirUp5_Band = false;               // Optional: with small band (reduces noise)

    // ════════════════════════════════════════════════════════════════
    // DISABLED MODELS (failed experiments)
    // ════════════════════════════════════════════════════════════════
    private const bool EnableBreakoutMeta15 = false;            // AUC 0.52 - no signal
    private const bool EnableVolumeBreakoutMeta15 = false;      // AUC 0.52 - no signal
    private const bool EnableExpectedReturn10 = false;          // R² negative
    private const bool EnableDirection10 = false;               // 40% accuracy
    private const bool EnableBreakoutPriorHigh10 = false;       // Replaced by BreakoutEnhanced
    private const bool EnableBreakoutAtr10 = false;             // AUC 0.55
    private const bool EnableVolatilityExpansion10 = false;     // Old labeler
    private const bool EnableExpectedReturn5 = false;
    private const bool EnableDirection5 = false;
    private const bool EnableRelStrengthCont10_1pct = false;    // AUC 0.60
    private const bool EnableBinaryUp10Market = false;          // AUC 0.64
    private const bool EnableBinaryUp10Enhanced = false;        // No improvement
    private const bool EnableRiskAdjustedUp10 = false;          // AUC 0.52
    private const bool EnableRiskAdjustedDown10 = false;        // AUC 0.53
    private const bool EnableTripleBarrierUp10 = false;         // AUC 0.51
    private const bool EnableTripleBarrierDown10 = false;       // AUC 0.51

    public static IReadOnlyList<ProfitModelDefinition> All { get; } = BuildAll();

    private static IReadOnlyList<ProfitModelDefinition> BuildAll()
    {
        var models = new List<ProfitModelDefinition>();

        // ════════════════════════════════════════════════════════════════
        // WORKING MODELS — each tagged with its semantic role + weight
        // ════════════════════════════════════════════════════════════════

        if (EnableBinaryUp10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "BinaryUp10",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new AtrVolatilityBreakoutFeatureBuilder(),
                Labeler: new BinaryUpLabeler(horizonBars: 10, upThresholdPercent: 4.0f),
                ModelKind: ProfitModelKind.BinaryClassification,
                Role: SignalRole.DirectionUp,
                CompositeWeight: 0.25f));
        }

        if (EnableBinaryDown10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "BinaryDown10",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new AtrVolatilityBreakoutFeatureBuilder(),
                Labeler: new BinaryDownLabeler(horizonBars: 10, downThresholdPercent: 4.0f),
                ModelKind: ProfitModelKind.BinaryClassification,
                Role: SignalRole.Veto,
                CompositeWeight: -0.20f));  // negative = penalty
        }

        if (EnableVolExpansionRelative10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "VolExpansionRelative10",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new AtrVolatilityBreakoutFeatureBuilder(),
                Labeler: new RelativeVolatilityExpansionLabeler(horizonBars: 10, percentileThreshold: 80),
                ModelKind: ProfitModelKind.BinaryClassification,
                Role: SignalRole.Confirmation,
                CompositeWeight: 0.15f));
        }

        if (EnableRelStrengthCont10_2pct)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "RelStrengthCont10_2pct",
                Lookback: 30,
                HorizonBars: 10,
                FeatureBuilder: new MarketContextFeatureBuilder(),
                Labeler: new RelativeStrengthContinuationLabeler(horizonBars: 10, outperformThresholdPercent: 2.0f),
                ModelKind: ProfitModelKind.BinaryClassification,
                Role: SignalRole.Momentum,
                CompositeWeight: 0.10f));
        }

        if (EnableBreakoutEnhanced)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "BreakoutEnhanced",
                Lookback: 55,
                HorizonBars: 10,
                FeatureBuilder: new EnhancedFeatureBuilder(),
                Labeler: new BreakoutAbovePriorHighLabeler(horizonBars: 10, priorHighLookback: 20, breakoutPercent: 1.0f),
                ModelKind: ProfitModelKind.BinaryClassification,
                Role: SignalRole.Setup,
                CompositeWeight: 0.40f));
        }

        // ════════════════════════════════════════════════════════════════
        // DISABLED MODELS (kept for reference / future experiments)
        // All disabled models below keep Role: Confirmation as a safe default.
        // ════════════════════════════════════════════════════════════════

        if (EnableDirUp5)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "DirUp5",
                Lookback: 50,
                HorizonBars: 5,
                FeatureBuilder: new DirectionFeatureBuilder(),
                Labeler: new DirectionUpLabeler(horizonBars: 5, bandPercent: 0f),
                ModelKind: ProfitModelKind.BinaryClassification,
                Role: SignalRole.DirectionUp,
                CompositeWeight: 0f));
        }

        if (EnableBandedDirUp5)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "BandedDirUp5",
                Lookback: 50,
                HorizonBars: 5,
                FeatureBuilder: new DirectionFeatureBuilder(),
                Labeler: new BandedDirectionUpLabeler(horizonBars: 5, bandAtrMultiple: 0.5f, atrPeriod: 14),
                ModelKind: ProfitModelKind.BinaryClassification,
                Role: SignalRole.DirectionUp,
                CompositeWeight: 0f));
        }

        if (EnableSetupBandedDirUp5)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "SetupDirUp5",
                Lookback: 50,
                HorizonBars: 5,
                FeatureBuilder: new DirectionFeatureBuilder(),
                Labeler: new SetupConditionalLabeler(
                    innerLabeler: new BandedDirectionUpLabeler(horizonBars: 5, bandAtrMultiple: 0.5f),
                    priorHighLookback: 20,
                    breakoutPercent: 0f),
                ModelKind: ProfitModelKind.BinaryClassification,
                Role: SignalRole.DirectionUp,
                CompositeWeight: 0f));
        }

        if (EnableSetupBandedDirDown5)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "SetupDirDown5",
                Lookback: 50,
                HorizonBars: 5,
                FeatureBuilder: new DirectionFeatureBuilder(),
                Labeler: new SetupConditionalLabeler(
                    innerLabeler: new BandedDirectionDownLabeler(horizonBars: 5, bandAtrMultiple: 0.5f),
                    priorHighLookback: 20,
                    breakoutPercent: 0f),
                ModelKind: ProfitModelKind.BinaryClassification,
                Role: SignalRole.Veto,
                CompositeWeight: 0f));
        }

        if (EnableBreakoutMeta15)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "BreakoutMeta15",
                Lookback: 55,
                HorizonBars: 15,
                FeatureBuilder: new EnhancedFeatureBuilder(),
                Labeler: new BreakoutMetaLabeler(
                    horizonBars: 15,
                    priorHighLookback: 20,
                    breakoutPercent: 0f,
                    profitAtrMultiple: 1.5f,
                    stopAtrMultiple: 1.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        if (EnableVolumeBreakoutMeta15)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "VolBreakoutMeta15",
                Lookback: 55,
                HorizonBars: 15,
                FeatureBuilder: new EnhancedFeatureBuilder(),
                Labeler: new VolumeBreakoutMetaLabeler(
                    horizonBars: 15,
                    priorHighLookback: 20,
                    volumeMultiple: 1.5f,
                    profitAtrMultiple: 1.5f,
                    stopAtrMultiple: 1.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

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

        if (EnableRiskAdjustedUp10)
        {
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
            models.Add(new ProfitModelDefinition(
                TaskType: "RiskAdjDown10",
                Lookback: 55,
                HorizonBars: 10,
                FeatureBuilder: new EnhancedFeatureBuilder(),
                Labeler: new RiskAdjustedDownLabeler(horizonBars: 10, atrThreshold: 0.75f, atrPeriod: 14),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        if (EnableTripleBarrierUp10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "TripleBarrierUp10",
                Lookback: 55,
                HorizonBars: 10,
                FeatureBuilder: new EnhancedFeatureBuilder(),
                Labeler: new TripleBarrierUpLabeler(horizonBars: 10, upperAtrMultiple: 1.0f, lowerAtrMultiple: 1.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        if (EnableTripleBarrierDown10)
        {
            models.Add(new ProfitModelDefinition(
                TaskType: "TripleBarrierDown10",
                Lookback: 55,
                HorizonBars: 10,
                FeatureBuilder: new EnhancedFeatureBuilder(),
                Labeler: new TripleBarrierDownLabeler(horizonBars: 10, upperAtrMultiple: 1.0f, lowerAtrMultiple: 1.0f),
                ModelKind: ProfitModelKind.BinaryClassification));
        }

        return models;
    }

    public static ProfitModelDefinition? GetByTaskType(string taskType)
        => All.FirstOrDefault(p => p.TaskType == taskType);

    public static IEnumerable<ProfitModelDefinition> GetByKind(ProfitModelKind kind)
        => All.Where(p => p.ModelKind == kind);
}