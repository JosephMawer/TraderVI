using System.Collections.Generic;
using System.Linq;

namespace Core.Indicators.Granville;

/// <summary>
/// Aggregates all registered Granville indicator groups into a unified daily forecast.
/// 
/// Produces both Granville's original point scoring (for console display) and a
/// normalized composite adjustment that feeds into the trade decision engine.
/// 
/// As new indicator groups are implemented, register them in the constructor.
/// The composite adjustment auto-scales as more groups are added.
/// </summary>
public sealed class GranvilleComposite
{
    private readonly IReadOnlyList<IGranvilleIndicatorGroup> _groups;

    /// <summary>
    /// Maximum possible adjustment magnitude to the trade composite score.
    /// Kept small so no single rule-based system overwhelms the ML signals.
    /// Will be revisited as more of the 56 indicators come online.
    /// </summary>
    public double MaxCompositeAdjustment { get; init; } = 0.10;

    public GranvilleComposite()
    {
        // Register all implemented indicator groups here.
        // Add new groups as they are implemented.
        _groups =
        [
            new PluralityIndicators(),
            new DisparityIndicators(),
            new LeadershipIndicators(),
        ];
    }

    public GranvilleComposite(IReadOnlyList<IGranvilleIndicatorGroup> groups)
    {
        _groups = groups;
    }

    /// <summary>
    /// Evaluates all indicator groups and produces the daily forecast.
    /// </summary>
    public GranvilleDailyForecast Evaluate(GranvilleMarketContext context)
    {
        var allResults = new List<GranvilleResult>();

        foreach (var group in _groups)
        {
            var results = group.Evaluate(context);
            allResults.AddRange(results);
        }

        int bullish = allResults.Count(r => r.Signal is IndicatorSignal.Bullish or IndicatorSignal.StrongBullish);
        int bearish = allResults.Count(r => r.Signal is IndicatorSignal.Bearish or IndicatorSignal.StrongBearish);
        int netPoints = allResults.Sum(r => r.GranvillePoints);

        // Normalize to a composite adjustment in [-MaxCompositeAdjustment, +MaxCompositeAdjustment].
        int totalIndicatorsImplemented = allResults.Count(r => r.Signal != IndicatorSignal.Neutral);
        double adjustment = totalIndicatorsImplemented > 0
            ? System.Math.Clamp((double)netPoints / MaxRawPointRange(), -MaxCompositeAdjustment, MaxCompositeAdjustment)
            : 0.0;

        return new GranvilleDailyForecast(
            Results: allResults,
            BullishCount: bullish,
            BearishCount: bearish,
            NetPoints: netPoints,
            CompositeAdjustment: adjustment);
    }

    /// <summary>
    /// The theoretical max absolute raw point value across all implemented groups.
    /// Update this as new indicator groups are added so the normalization scales properly.
    /// </summary>
    private static double MaxRawPointRange()
    {
        // Plurality:   max bullish = +4 (#2 and #4), max bearish = -2 (#1 and #3)
        // Disparity:   max bullish = +2 (#6 × 2 timeframes), max bearish = -2 (#5 × 2 timeframes)
        // Leadership:  max bullish = +4 (#8 and #10), max bearish = -2 (#7 and #9)
        // Combined theoretical range: [-6, +10]
        // Use the wider absolute value for normalization headroom.
        return 10.0;
    }
}