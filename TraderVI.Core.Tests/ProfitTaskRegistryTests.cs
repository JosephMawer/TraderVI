using Core.ML.Engine.Patterns.Features;
using Core.ML.Engine.Profit;
using System;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class ProfitTaskRegistryTests
{
    [Theory]
    [InlineData("BinaryUp10", 30, SignalRole.DirectionUp, 0.25f, typeof(AtrVolatilityBreakoutFeatureBuilder), typeof(BinaryUpLabeler))]
    [InlineData("BinaryDown10", 30, SignalRole.Veto, -0.20f, typeof(AtrVolatilityBreakoutFeatureBuilder), typeof(BinaryDownLabeler))]
    [InlineData("VolExpansionRelative10", 30, SignalRole.Confirmation, 0.15f, typeof(AtrVolatilityBreakoutFeatureBuilder), typeof(RelativeVolatilityExpansionLabeler))]
    [InlineData("BreakoutEnhanced", 55, SignalRole.Setup, 0.40f, typeof(EnhancedFeatureBuilder), typeof(BreakoutAbovePriorHighLabeler))]
    public void ExistingProductionModelSettingsAndFeatureBuildersArePreserved(
        string taskType, int lookback, SignalRole role, float weight, Type featureType, Type labelType)
    {
        var model = ProfitModelRegistry.GetByTaskType(taskType);
        Assert.NotNull(model);
        Assert.Equal(lookback, model.Lookback);
        Assert.Equal(10, model.HorizonBars);
        Assert.Equal(ProfitModelKind.BinaryClassification, model.ModelKind);
        Assert.Equal(role, model.Role);
        Assert.Equal(weight, model.CompositeWeight);
        Assert.Equal(2.0f, model.BuyThresholdPercent);
        Assert.Equal(-2.0f, model.SellThresholdPercent);
        Assert.Equal(0.25f, model.RegressionReturnClamp);
        Assert.Equal(featureType, model.FeatureBuilder.GetType());
        Assert.Equal(labelType, model.Labeler.GetType());
    }

    [Fact]
    public void TrainingAndEvaluationShareTheSameEnabledTasksAndLabelInstances()
    {
        string[] expected = ["BinaryUp10", "BinaryDown10", "VolExpansionRelative10", "BreakoutEnhanced"];
        Assert.Equal(expected, ProfitModelRegistry.All.Select(model => model.TaskType));
        Assert.Equal(expected, ProfitTaskRegistry.All.Select(task => task.TaskType));
        foreach (var task in ProfitTaskRegistry.All)
        {
            var model = ProfitModelRegistry.GetByTaskType(task.TaskType);
            Assert.Same(task.Labeler, model.Labeler);
            Assert.Equal(task.HorizonBars, model.HorizonBars);
        }
    }
}
