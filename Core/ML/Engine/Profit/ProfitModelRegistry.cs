using Core.ML.Engine.Patterns;
using Core.ML.Engine.Patterns.Features;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Registry of all profit prediction models.
/// </summary>
public static class ProfitModelRegistry
{
    public static IReadOnlyList<ProfitModelDefinition> All { get; } =
    [
        // ═══════════════════════════════════════════════════════════
        // Regression: predict expected return over 10 days
        // ═══════════════════════════════════════════════════════════
        new ProfitModelDefinition(
            TaskType: "ExpectedReturn10",
            Lookback: 30,
            HorizonBars: 10,
            FeatureBuilder: new PriceVolumeFeatureBuilder(),
            Labeler: new ForwardReturnLabeler(horizonBars: 10, buyThresholdPercent: 2.0f, sellThresholdPercent: -2.0f),
            ModelKind: ProfitModelKind.Regression),

        // ═══════════════════════════════════════════════════════════
        // 3-Way Classification: Buy/Hold/Sell over 10 days
        // ═══════════════════════════════════════════════════════════
        new ProfitModelDefinition(
            TaskType: "Direction10",
            Lookback: 30,
            HorizonBars: 10,
            FeatureBuilder: new PriceVolumeFeatureBuilder(),
            Labeler: new ForwardReturnLabeler(horizonBars: 10, buyThresholdPercent: 2.0f, sellThresholdPercent: -2.0f),
            ModelKind: ProfitModelKind.ThreeWayClassification),

        // ═══════════════════════════════════════════════════════════
        // Shorter horizon (5 days) for faster signals
        // ═══════════════════════════════════════════════════════════
        new ProfitModelDefinition(
            TaskType: "ExpectedReturn5",
            Lookback: 20,
            HorizonBars: 5,
            FeatureBuilder: new PriceVolumeFeatureBuilder(),
            Labeler: new ForwardReturnLabeler(horizonBars: 5, buyThresholdPercent: 1.5f, sellThresholdPercent: -1.5f),
            ModelKind: ProfitModelKind.Regression),

        new ProfitModelDefinition(
            TaskType: "Direction5",
            Lookback: 20,
            HorizonBars: 5,
            FeatureBuilder: new PriceVolumeFeatureBuilder(),
            Labeler: new ForwardReturnLabeler(horizonBars: 5, buyThresholdPercent: 1.5f, sellThresholdPercent: -1.5f),
            ModelKind: ProfitModelKind.ThreeWayClassification),
    ];

    public static ProfitModelDefinition? GetByTaskType(string taskType)
        => All.FirstOrDefault(p => p.TaskType == taskType);

    public static IEnumerable<ProfitModelDefinition> GetByKind(ProfitModelKind kind)
        => All.Where(p => p.ModelKind == kind);
}