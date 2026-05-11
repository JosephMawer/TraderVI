namespace Core.Indicators.Granville;

/// <summary>
/// Today's and yesterday's close for a single XIU constituent.
/// Consumed by <see cref="WeightingIndicators"/> (Granville #15/#16) to compute
/// the price-weighted contribution proxy described in ADR-0003.
/// </summary>
/// <param name="Symbol">TSX symbol (dot-suffix preserved for dual-class names, e.g. "BBD.B").</param>
/// <param name="TodayClose">Most recent session close.</param>
/// <param name="YesterdayClose">Prior session close.</param>
public sealed record XiuConstituentBar(string Symbol, double TodayClose, double YesterdayClose)
{
    /// <summary>Day-over-day return. Returns 0 if <see cref="YesterdayClose"/> is not positive.</summary>
    public double Return => YesterdayClose > 0 ? (TodayClose / YesterdayClose) - 1.0 : 0.0;

    /// <summary>True if both closes are usable (positive, finite).</summary>
    public bool IsUsable =>
        TodayClose > 0 && YesterdayClose > 0
        && !double.IsNaN(TodayClose) && !double.IsNaN(YesterdayClose)
        && !double.IsInfinity(TodayClose) && !double.IsInfinity(YesterdayClose);
}
