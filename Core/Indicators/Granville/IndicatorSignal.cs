namespace Core.Indicators.Granville;

/// <summary>
/// The directional signal emitted by a single Granville indicator.
/// Even-valued = bullish, odd-valued = bearish (matching Granville's original scoring).
/// </summary>
public enum IndicatorSignal
{
    /// <summary>Bullish — market advance expected or continuing.</summary>
    Bullish = 2,

    /// <summary>Bearish — market decline expected or continuing.</summary>
    Bearish = 1,

    /// <summary>Strongly bearish — decline likely to continue (warrants blocking).</summary>
    StrongBearish = 3,

    /// <summary>Strongly bullish — advance likely to continue.</summary>
    StrongBullish = 4,

    /// <summary>Insufficient data or indeterminate.</summary>
    Neutral = 0
}
