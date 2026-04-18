using Core.ML.Engine.Patterns;

namespace Core.ML.Engine.Profit;

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
    SignalRole Role = SignalRole.Confirmation,
    float CompositeWeight = 0.10f,
    float BuyThresholdPercent = 2.0f,
    float SellThresholdPercent = -2.0f,
    float? RegressionReturnClamp = 0.25f);