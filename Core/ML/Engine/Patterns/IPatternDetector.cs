using System.Collections.Generic;

namespace Core.ML.Engine.Patterns;

/// <summary>
/// Detects whether a specific pattern is present in a window of bars.
/// Used during training to generate labels.
/// </summary>
public interface IPatternDetector
{
    /// <summary>
    /// Unique name for this pattern (e.g., "TrendUp10", "HeadAndShoulders").
    /// </summary>
    string PatternName { get; }

    /// <summary>
    /// Default/recommended lookback window size for this pattern.
    /// </summary>
    int DefaultLookback { get; }

    /// <summary>
    /// Returns true if the pattern is detected in the given window of bars.
    /// </summary>
    /// <param name="windowBars">Chronologically ordered bars (oldest first).</param>
    bool Detect(IReadOnlyList<DailyBar> windowBars);
}