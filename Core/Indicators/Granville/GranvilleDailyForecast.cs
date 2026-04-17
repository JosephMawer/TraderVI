using System.Collections.Generic;

namespace Core.Indicators.Granville;

/// <summary>
/// Aggregated output from all evaluated Granville indicator groups.
/// </summary>
/// <param name="Results">Individual indicator results.</param>
/// <param name="BullishCount">Number of bullish signals.</param>
/// <param name="BearishCount">Number of bearish signals.</param>
/// <param name="NetPoints">Sum of Granville points (positive = net bullish).</param>
/// <param name="CompositeAdjustment">Normalized adjustment to apply to the trade composite score.</param>
public sealed record GranvilleDailyForecast(
    IReadOnlyList<GranvilleResult> Results,
    int BullishCount,
    int BearishCount,
    int NetPoints,
    double CompositeAdjustment);