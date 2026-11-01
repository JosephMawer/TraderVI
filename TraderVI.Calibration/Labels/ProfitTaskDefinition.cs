namespace Core.ML.Engine.Profit;

/// <summary>
/// Shared task/label settings; feature construction and ML execution stay in Core.
/// </summary>
public record ProfitTaskDefinition(
    string TaskType,
    int Lookback,
    int HorizonBars,
    ProfitFeatureKind FeatureKind,
    ILabeler Labeler,
    ProfitModelKind ModelKind,
    SignalRole Role = SignalRole.Confirmation,
    float CompositeWeight = 0.10f,
    float BuyThresholdPercent = 2.0f,
    float SellThresholdPercent = -2.0f,
    float? RegressionReturnClamp = 0.25f);
public enum ProfitFeatureKind
{
    AtrVolatilityBreakout,
    MarketContext,
    Enhanced,
    Direction,
    PriceVolume,
}
