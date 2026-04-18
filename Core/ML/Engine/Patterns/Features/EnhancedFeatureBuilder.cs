using System.Collections.Generic;

namespace Core.ML.Engine.Patterns.Features;

/// <summary>
/// Combines AtrVolatilityBreakout features with TrendMomentum "starter pack" features.
/// Total features: (lookback * 3) + 15 + 26 = (30 * 3) + 41 = 131 features @ lookback=30
/// </summary>
public sealed class EnhancedFeatureBuilder : IFeatureBuilder
{
    public string Name => "Enhanced";

    private readonly AtrVolatilityBreakoutFeatureBuilder _atrBuilder = new();
    private readonly TrendMomentumFeatureBuilder _trendBuilder = new();

    private const int TrendMomentumFeatureCount = 26;

    public int FeatureCount(int lookback) => _atrBuilder.FeatureCount(lookback) + TrendMomentumFeatureCount;

    /// <summary>
    /// Market bars (XIU) for relative strength features.
    /// </summary>
    public IReadOnlyList<DailyBar>? MarketBars
    {
        get => _trendBuilder.MarketBars;
        set => _trendBuilder.MarketBars = value;
    }

    public float[] Build(IReadOnlyList<DailyBar> windowBars)
    {
        var atrFeatures = _atrBuilder.Build(windowBars);
        var trendFeatures = _trendBuilder.Build(windowBars);

        var combined = new float[atrFeatures.Length + trendFeatures.Length];
        atrFeatures.CopyTo(combined, 0);
        trendFeatures.CopyTo(combined, atrFeatures.Length);

        return combined;
    }
}