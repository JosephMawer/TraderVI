using System.Collections.Generic;
using Core.TMX.Models.Domain;

namespace Core.Indicators.Granville;

/// <summary>
/// Market data context shared across all Granville indicator groups for a single evaluation.
/// Add fields here as new indicator groups require additional data sources.
/// </summary>
public sealed class GranvilleMarketContext
{
    // ── Advance/Decline data (from AdvanceDeclineRepository) ──

    /// <summary>Today's A/D line entry.</summary>
    public required ADLineEntry Today { get; init; }

    /// <summary>Yesterday's A/D line entry (for XIU direction comparison).</summary>
    public required ADLineEntry Yesterday { get; init; }

    /// <summary>Recent A/D line history (up to 200 days, ascending by date).</summary>
    public required IReadOnlyList<ADLineEntry> RecentHistory { get; init; }

    // ── Sector index data (for Disparity indicators) ──

    /// <summary>
    /// Recent sector index snapshots (all sectors, up to ~10 trading days, ascending by date).
    /// Used by <see cref="DisparityIndicators"/> for single-day and rolling-window comparisons.
    /// Null if sector data is not yet available (graceful degradation).
    /// </summary>
    public IReadOnlyList<SectorIndexSnapshot>? SectorSnapshots { get; init; }

    /// <summary>
    /// Optional stock → sector-index mapping loaded from <c>[dbo].[StockSectorMap]</c>.
    /// Not required by current Disparity logic, but passed through now so future
    /// Granville groups (Leadership, Weighting, sector-relative rules) can consume it
    /// without changing the Delphi integration again.
    /// </summary>
    public IReadOnlyList<StockSectorMapping>? StockSectorMappings { get; init; }

    // ── Leadership data (for Leadership indicators #7–#10) ──

    /// <summary>
    /// Recent leadership snapshots (up to ~50 trading days, ascending by date).
    /// Used by <see cref="LeadershipIndicators"/> to compute smoothed series and
    /// determine leadership state (upswing/downswing) and quality (improving/deteriorating).
    /// Null if leadership data is not yet available (graceful degradation).
    /// </summary>
    public IReadOnlyList<LeadershipSnapshot>? LeadershipHistory { get; init; }

    // ── Future data sources will be added here as we implement more groups ──
    // e.g., gold prices, odd-lot data, volume breakdowns, etc.
}