using System;

namespace Core.Indicators.Models;

/// <summary>
/// A single On-Balance Volume (OBV) point for one trading session.
///
/// OBV is Granville's cumulative volume gauge: a running total that ADDS the day's
/// volume on an up-close, SUBTRACTS it on a down-close, and stays flat on an unchanged
/// close. The cumulative <see cref="Value"/> is anchor-relative (it depends on where the
/// chain started), so only its TREND and BREAKOUTS carry meaning — never the raw number.
/// <para>
/// <see cref="Delta"/> retains the signed contribution for this session (+volume / -volume / 0)
/// purely for diagnostics ("why did OBV move?"); it is derived, not persisted.
/// </para>
/// </summary>
public readonly record struct OBV
{
    /// <summary>The trading session this point represents.</summary>
    public DateTime Date { get; init; }

    /// <summary>The running cumulative OBV as of <see cref="Date"/> (anchor-relative).</summary>
    public long Value { get; init; }

    /// <summary>
    /// This session's signed contribution to the cumulative: <c>+Volume</c> on an up-close,
    /// <c>-Volume</c> on a down-close, <c>0</c> on an unchanged close. For diagnostics only.
    /// </summary>
    public long Delta { get; init; }

    /// <summary>Sign of <see cref="Delta"/>: +1 up day, -1 down day, 0 unchanged.</summary>
    public int Sign => System.Math.Sign(Delta);

    public OBV(DateTime date, long value, long delta)
    {
        Date = date;
        Value = value;
        Delta = delta;
    }
}
