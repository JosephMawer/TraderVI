using Core.Indicators.Granville;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.Gates;

/// <summary>
/// Soft gate based on Granville's 56 day-to-day indicators.
/// 
/// - Warns on any bearish signal (logged in gate trace).
/// - Blocks only on StrongBearish (indicator #3: "decline will continue" —
///   breadth AND benchmark both falling).
/// 
/// As more indicator groups are added, the blocking logic can be
/// refined (e.g., block when N+ groups agree on bearish).
/// </summary>
public sealed class GranvilleGate : ITradeGate
{
    public string Name => "Granville";

    /// <summary>
    /// Tiebreaker priority when several bearish indicators fire on the same day.
    /// Lower number = surfaced first in the warning message.
    ///
    /// Weighting (#15/#16) leads because its trigger is an empirically calibrated
    /// narrow-advance warning gated on three conditions (ADR-0003), whereas
    /// Disparity / Plurality bearish signals can fire from a single-day divergence.
    /// Categories not listed fall to the end and are then ranked by point magnitude.
    /// </summary>
    private static readonly Dictionary<IndicatorCategory, int> CategoryPriority = new()
    {
        { IndicatorCategory.Weighting,  0 },
        { IndicatorCategory.Disparity,  1 },
        { IndicatorCategory.Plurality,  2 },
        { IndicatorCategory.Features,   3 },
        { IndicatorCategory.Leadership, 4 },
    };

    public GateResult Evaluate(GateContext context)
    {
        if (context.GranvilleForecast is null)
            return GateResult.Pass();

        var forecast = context.GranvilleForecast;

        // Block on StrongBearish — currently only Plurality #3 (decline will continue).
        // If multiple StrongBearish signals ever co-occur, prefer the one with the
        // most negative point contribution (most decisive single voice).
        var strongBearish = forecast.Results
            .Where(r => r.Signal == IndicatorSignal.StrongBearish)
            .OrderBy(r => r.GranvillePoints)
            .FirstOrDefault();

        if (strongBearish is not null)
        {
            return GateResult.Block(
                $"Granville {strongBearish.Name}: {strongBearish.Description}");
        }

        // Warn (but pass) on regular bearish signals. When several fire, surface the
        // highest-priority category first (see CategoryPriority above) and break ties
        // by most-negative point value.
        var bearish = forecast.Results
            .Where(r => r.Signal == IndicatorSignal.Bearish)
            .OrderBy(r => CategoryPriority.TryGetValue(r.Category, out var p) ? p : int.MaxValue)
            .ThenBy(r => r.GranvillePoints)
            .FirstOrDefault();

        if (bearish is not null)
        {
            return new GateResult(Passed: true,
                Reason: $"⚠ Granville warning: {bearish.Name}");
        }

        return GateResult.Pass();
    }
}
