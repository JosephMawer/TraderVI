using System;

namespace Core.RelativeStrength;

/// <summary>
/// Computed relative strength features for a single stock on a single date.
/// Covers stock-vs-sector, stock-vs-market, and sector-vs-market
/// across multiple horizons (5d, 10d, 20d, 60d).
///
/// Used both as:
/// - ML features (fed into LightGBM via Hercules training pipeline)
/// - Rule-based ranking/gating signal (consumed by Delphi decision engine)
///
/// Lifecycle:
/// - Delphi computes live for today's inference.
/// - Hermes backfills historical values to [dbo].[RelativeStrengthFeatures] for Hercules training.
/// </summary>
public sealed record RelativeStrengthRow
{
    public required string Symbol { get; init; }
    public required DateOnly Date { get; init; }
    public required string SectorIndexSymbol { get; init; }

    // ── Stock vs Sector (raw return difference) ──
    public double? RS_StockVsSector_5d { get; init; }
    public double? RS_StockVsSector_10d { get; init; }
    public double? RS_StockVsSector_20d { get; init; }
    public double? RS_StockVsSector_60d { get; init; }

    // ── Stock vs Market / XIU (raw return difference) ──
    public double? RS_StockVsMarket_5d { get; init; }
    public double? RS_StockVsMarket_10d { get; init; }
    public double? RS_StockVsMarket_20d { get; init; }
    public double? RS_StockVsMarket_60d { get; init; }

    // ── Sector vs Market / XIU (raw return difference) ──
    public double? RS_SectorVsMarket_5d { get; init; }
    public double? RS_SectorVsMarket_10d { get; init; }
    public double? RS_SectorVsMarket_20d { get; init; }
    public double? RS_SectorVsMarket_60d { get; init; }

    // ── Volatility-normalized (Z-score) variants ──
    // RS_Z = RS / rolling_std(RS, 20d) — captures whether today's RS is extreme
    // relative to its own recent history.
    public double? RS_Z_StockVsSector { get; init; }
    public double? RS_Z_StockVsMarket { get; init; }
    public double? RS_Z_SectorVsMarket { get; init; }

    /// <summary>
    /// Composite RS score for ranking. Higher = stronger relative momentum.
    /// Default: 0.5 * RS_StockVsMarket_10d + 0.3 * RS_StockVsSector_10d + 0.2 * RS_SectorVsMarket_10d
    /// Weights are initial defaults, intended to be tuned via Hercules feature importance.
    /// </summary>
    public double? CompositeScore { get; init; }
}