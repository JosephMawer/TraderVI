using System.Collections.Generic;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Generates forward-looking labels from price data.
/// Unlike IPatternDetector (which looks at current window),
/// ILabeler looks at what happens AFTER the window.
/// </summary>
public interface ILabeler
{
    string Name { get; }

    /// <summary>
    /// Horizon in bars for forward-looking label.
    /// </summary>
    int HorizonBars { get; }

    /// <summary>
    /// Computes the label for a window, given the bars that follow.
    /// </summary>
    /// <param name="windowBars">The lookback window (input features are built from this).</param>
    /// <param name="futureBars">Bars after the window (label is computed from this).</param>
    LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars);
}

public record LabelResult(
    float ForwardReturn,
    ThreeWayLabel ThreeWayClass,
    bool IsValid);

public enum ThreeWayLabel
{
    Sell = -1,
    Hold = 0,
    Buy = 1
}