using Microsoft.ML.Data;

namespace Core.ML.Engine.Patterns;

/// <summary>
/// Unified ML input window for all pattern types.
/// Features are built dynamically by IFeatureBuilder implementations.
/// </summary>
public class PatternWindow
{
    /// <summary>
    /// Feature vector built by the appropriate IFeatureBuilder.
    /// Length varies by pattern type and lookback.
    /// </summary>
    [VectorType]
    public float[] Features { get; set; } = [];

    /// <summary>
    /// True if the pattern is present (set by IPatternDetector during training).
    /// </summary>
    public bool Label { get; set; }
}
