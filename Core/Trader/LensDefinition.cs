using System;
using System.Collections.Generic;
using Core.Trader.Gates;

namespace Core.Trader;

/// <summary>
/// Identifies a ranking lens — a self-contained way of viewing the universe.
///
/// A lens is NOT a configuration flag on a single shared pipeline. It is a
/// distinct (thesis → gate stack → ranking key) triple:
///   • <b>thesis</b>      — the trading hypothesis the lens encodes,
///   • <b>gate stack</b>  — the ordered <see cref="ITradeGate"/> list that decides
///                          which candidates are even eligible under that thesis,
///   • <b>ranking key</b> — how eligible candidates are ordered against each other.
///
/// Two lenses can share market-level inputs (regime, breadth, Granville, RS) but
/// reach different shortlists because they gate and rank differently. This keeps
/// us from collapsing back into "one big gate stack with feature flags" — each
/// new way of interpreting the tape becomes a new lens, not a new branch.
/// See ADR-0013 (multi-lens architecture).
/// </summary>
public enum RankingLens
{
    /// <summary>
    /// Trend-continuation thesis: confirmed leaders keep leading on a 5–10d
    /// horizon. Gates on confirmed uptrend (Trend30 + MaCrossover); ranks RS-first
    /// with DirectionEdge as confirmation. Breakout is a soft input, not a gate.
    /// This lens drives the executed recommendation. See ADR-0014.
    /// </summary>
    Continuation,

    /// <summary>
    /// Breakout thesis: a range-clearing event is likely in the next 10d. Gates on
    /// BreakoutEnhanced probability (Setup gate); ranks by DirectionEdge + RScomp
    /// (ADR-0011). Journaled for supplemental awareness, not executed.
    /// </summary>
    Breakout
}

/// <summary>
/// Bundles the two things that make a lens distinct: the gate stack (as a
/// <see cref="TradePipeline"/>) and the ranking-key selector applied to eligible
/// candidates. Market-level inputs and per-symbol scoring are shared across lenses;
/// only gating and ordering differ. See ADR-0013.
/// </summary>
public sealed class LensDefinition
{
    /// <summary>The lens this definition implements.</summary>
    public RankingLens Lens { get; }

    /// <summary>Short, stable label used in reports and the DailyPick discriminator.</summary>
    public string Label { get; }

    /// <summary>The ordered gate stack that decides eligibility under this lens's thesis.</summary>
    public TradePipeline Pipeline { get; }

    /// <summary>
    /// Produces the primary ranking score for a candidate (higher = better).
    /// Buy-direction precedence and tertiary tiebreakers are applied by the engine;
    /// this selector supplies the lens-specific primary (and embeds any secondary
    /// the lens cares about). Two per-symbol soft inputs are supplied to the lens:
    ///   • <c>rs</c>      — raw RScomp (relative strength), 0 when unavailable.
    ///   • <c>obvTilt</c> — On-Balance Volume field-trend tilt: +ObvSignalWeight when
    ///                      OBV confirms (rising field trend), −ObvSignalWeight when it
    ///                      contradicts (falling), 0 when doubtful/unavailable. A soft
    ///                      nudge on ordering only — it never gates a candidate.
    /// </summary>
    public Func<RankedPick, double, double, double> PrimaryKey { get; }

    public LensDefinition(
        RankingLens lens,
        string label,
        TradePipeline pipeline,
        Func<RankedPick, double, double, double> primaryKey)
    {
        Lens = lens;
        Label = label;
        Pipeline = pipeline;
        PrimaryKey = primaryKey ?? throw new ArgumentNullException(nameof(primaryKey));
    }
}
