using Core.ML.Engine.Patterns;

namespace Core.ML.Engine.Profit;

public enum ProfitModelKind
{
    Regression,
    ThreeWayClassification
}

/// <summary>
/// Defines a profit prediction model.
/// </summary>
public record ProfitModelDefinition(
    string TaskType,
    int Lookback,
    int HorizonBars,
    IFeatureBuilder FeatureBuilder,
    ILabeler Labeler,
    ProfitModelKind ModelKind,
    float BuyThresholdPercent = 2.0f,
    float SellThresholdPercent = -2.0f);