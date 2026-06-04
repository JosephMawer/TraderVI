using System;

namespace Core.Indicators.Granville;

/// <summary>
/// "Today's tape" facts for the XIU benchmark: 1-day return and a relative-volume
/// reading that supports the Light Volume indicator group (#25–#28) and any other
/// indicators that need a same-day market-level price/volume snapshot.
///
/// Convention:
///   <see cref="XiuVolumeSma20Prior"/> is the average of the PREVIOUS 20 trading
///   sessions (t−20 .. t−1) and excludes today (t). This keeps the ratio leak-free
///   between live evaluation and backtests.
///
/// All derived properties are nullable: callers must handle missing inputs
/// gracefully (the Light Volume group degrades to Neutral).
/// </summary>
public sealed class MarketTapeContext
{
    /// <summary>Trading date this context describes (today).</summary>
    public required DateTime Date { get; init; }

    // ── Volume facts ──

    /// <summary>Today's XIU volume (shares).</summary>
    public long? XiuVolume { get; init; }

    /// <summary>
    /// SMA20 of XIU volume over the previous 20 sessions, EXCLUDING today.
    /// Null if fewer than 20 prior sessions are available.
    /// </summary>
    public decimal? XiuVolumeSma20Prior { get; init; }

    // ── Price facts ──

    /// <summary>Today's XIU close.</summary>
    public decimal? XiuClose { get; init; }

    /// <summary>Yesterday's XIU close (prior session).</summary>
    public decimal? XiuPrevClose { get; init; }

    // ── Derived ──

    /// <summary>
    /// XIU volume relative to its prior-20-session average.
    /// Values &lt; 1.0 mean below average; the Light Volume indicators use
    /// a configurable threshold (default 0.85) to decide what counts as "light."
    /// </summary>
    public decimal? XiuVolumeRatio20 =>
        XiuVolume is long v && XiuVolumeSma20Prior is decimal sma && sma > 0m
            ? v / sma
            : null;

    /// <summary>XIU 1-day return (close-to-close, as a fraction, e.g. 0.0042 = +0.42%).</summary>
    public decimal? XiuReturn1d =>
        XiuClose is decimal c && XiuPrevClose is decimal p && p > 0m
            ? c / p - 1m
            : null;
}
