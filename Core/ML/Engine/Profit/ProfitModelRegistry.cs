using Core.ML.Engine.Patterns;
using Core.ML.Engine.Patterns.Features;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

/// <summary>Attach Core's feature builders to the task definitions also used by Athena.</summary>
public static class ProfitModelRegistry
{
    public static IReadOnlyList<ProfitModelDefinition> All { get; } = ProfitTaskRegistry.All
        .Select(task => new ProfitModelDefinition(task.TaskType, task.Lookback, task.HorizonBars,
            CreateFeatureBuilder(task.FeatureKind), task.Labeler, task.ModelKind, task.Role,
            task.CompositeWeight, task.BuyThresholdPercent, task.SellThresholdPercent,
            task.RegressionReturnClamp)).ToArray();

    private static IFeatureBuilder CreateFeatureBuilder(ProfitFeatureKind kind) => kind switch
    {
        ProfitFeatureKind.AtrVolatilityBreakout => new AtrVolatilityBreakoutFeatureBuilder(),
        ProfitFeatureKind.MarketContext => new MarketContextFeatureBuilder(),
        ProfitFeatureKind.Enhanced => new EnhancedFeatureBuilder(),
        ProfitFeatureKind.Direction => new DirectionFeatureBuilder(),
        ProfitFeatureKind.PriceVolume => new PriceVolumeFeatureBuilder(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported profit feature kind.")
    };

    public static ProfitModelDefinition? GetByTaskType(string taskType)
        => All.FirstOrDefault(p => p.TaskType == taskType);

    public static IEnumerable<ProfitModelDefinition> GetByKind(ProfitModelKind kind)
        => All.Where(p => p.ModelKind == kind);
}
