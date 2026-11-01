using System;

namespace Core.Indicators.Granville;

/// <summary>
/// A single day's leadership measurement across three layers:
///   1. New-High/New-Low breadth (NHNL)
///   2. Active-stock breadth (top-N by dollar volume)
///   3. Large-cap relative strength (TSX 60 vs equal-weight)
///
/// Raw values are stored daily; smoothed series (EMA-10, 20-day returns)
/// are computed by <see cref="LeadershipCalculator"/> from the history.
/// </summary>
public sealed record LeadershipSnapshot
{
    public DateTime Date { get; init; }

    // ── Layer 1: New-High / New-Low breadth ──

    /// <summary>Number of TSX Composite stocks at a 52-week high today.</summary>
    public int NewHighs { get; init; }

    /// <summary>Number of TSX Composite stocks at a 52-week low today.</summary>
    public int NewLows { get; init; }

    /// <summary>Total issues traded (denominator for normalization).</summary>
    public int IssuesTraded { get; init; }

    /// <summary>Raw ratio: (NewHighs − NewLows) / IssuesTraded.</summary>
    public double NhnlRaw => IssuesTraded > 0
        ? (double)(NewHighs - NewLows) / IssuesTraded
        : 0.0;

    // ── Layer 2: Active-stock breadth (top-N by dollar volume) ──

    /// <summary>
    /// Number of top-N most-active stocks (by dollar volume) that advanced.
    /// Null when the movers source was unavailable for this session.
    /// </summary>
    public int? ActiveAdvancers { get; init; }

    /// <summary>
    /// Number of top-N most-active stocks (by dollar volume) that declined.
    /// Null when the movers source was unavailable for this session.
    /// </summary>
    public int? ActiveDecliners { get; init; }

    /// <summary>
    /// The N used for the active-stock basket. Null when no basket was reported.
    /// A reported basket always has a positive value, even when its breadth is neutral.
    /// </summary>
    public int? ActiveN { get; init; }

    /// <summary>
    /// Whether this snapshot contains a reported active-stock basket.
    /// Persistence validation guarantees that the three active-breadth fields are
    /// either all present and valid or all null.
    /// </summary>
    public bool HasActiveBreadth =>
        ActiveAdvancers is int advancers
        && ActiveDecliners is int decliners
        && ActiveN is int basketSize
        && advancers >= 0
        && decliners >= 0
        && basketSize > 0
        && (long)advancers + decliners <= basketSize;

    /// <summary>
    /// Raw ratio: (ActiveAdvancers − ActiveDecliners) / N, or null when the
    /// movers source was unavailable. A genuine tied basket remains exactly zero.
    /// </summary>
    public double? ActiveBreadthRaw => HasActiveBreadth
        ? (double)(ActiveAdvancers!.Value - ActiveDecliners!.Value) / ActiveN!.Value
        : null;

    // ── Layer 3: Large-cap relative strength ──

    /// <summary>TSX 60 (XIU) close price.</summary>
    public decimal? Tsx60Close { get; init; }

    /// <summary>TSX Composite Equal-Weight (or Completion) close price.</summary>
    public decimal? EqualWeightClose { get; init; }
}
