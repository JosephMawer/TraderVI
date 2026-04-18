namespace Core.Indicators.Granville;

/// <summary>
/// Result from a single Granville indicator evaluation.
/// </summary>
/// <param name="IndicatorNumber">The indicator number (1–56) from Granville's book.</param>
/// <param name="Category">Which group this indicator belongs to.</param>
/// <param name="Name">Human-readable name (e.g., "Plurality #1: Verge of Decline").</param>
/// <param name="Signal">Directional signal.</param>
/// <param name="GranvillePoints">Original Granville weighting: even = bullish, odd = bearish.</param>
/// <param name="Description">Explanation of what triggered the signal.</param>
public sealed record GranvilleResult(
    int IndicatorNumber,
    IndicatorCategory Category,
    string Name,
    IndicatorSignal Signal,
    int GranvillePoints,
    string Description);
