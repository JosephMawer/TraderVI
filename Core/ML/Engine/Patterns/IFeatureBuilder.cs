using System.Collections.Generic;

namespace Core.ML.Engine.Patterns;

/// <summary>
/// Builds a feature vector from a window of daily bars.
/// Different patterns may use different feature representations.
/// </summary>
public interface IFeatureBuilder
{
    /// <summary>
    /// Descriptive name for this feature set (e.g., "PriceVolume", "OHLC").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns the number of features produced for a given lookback.
    /// </summary>
    int FeatureCount(int lookback);

    /// <summary>
    /// Builds a normalized feature vector from the given window of bars.
    /// </summary>
    /// <param name="windowBars">Chronologically ordered bars (oldest first).</param>
    float[] Build(IReadOnlyList<DailyBar> windowBars);
}