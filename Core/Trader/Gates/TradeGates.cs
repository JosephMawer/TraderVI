using System.Collections.Generic;

namespace Core.Trader.Gates;

/// <summary>
/// A single step in the trade decision pipeline.
/// Gates are evaluated in order; the first Block stops evaluation.
/// </summary>
public interface ITradeGate
{
    /// <summary>
    /// Display name for logging/diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Evaluate whether this gate passes or blocks the trade.
    /// </summary>
    GateResult Evaluate(GateContext context);
}

/// <summary>
/// Result of a gate evaluation.
/// </summary>
public readonly record struct GateResult(bool Passed, string? Reason = null)
{
    public static GateResult Pass() => new(true);
    public static GateResult Block(string reason) => new(false, reason);
}

/// <summary>
/// All data a gate might need to make its decision.
/// Built once per symbol, passed through the pipeline.
/// </summary>
public sealed class GateContext
{
    // ── Market-level (same for all symbols in a run) ──
    public MarketRegime? Regime { get; init; }
    public double? BreadthScore { get; init; }
    public double BreadthVetoThreshold { get; init; } = -0.3;
    public bool RequireBenchmarkUptrend { get; init; } = true;

    // ── Symbol-level (computed per symbol) ──
    public string Symbol { get; init; } = string.Empty;
    public double BreakoutProb { get; init; }
    public double UpProb { get; init; }
    public double DownProb { get; init; }
    public double DirectionEdge { get; init; }
    public double CompositeScore { get; init; }
    public double VolExpansionProb { get; init; }
    public double RelStrengthProb { get; init; }

    // ── Pattern-level ──
    public int PatternBuys { get; init; }
    public int PatternCount { get; init; }

    /// <summary>
    /// Trace of gate evaluations for diagnostics.
    /// </summary>
    public List<GateTraceEntry> Trace { get; } = [];
}

/// <summary>
/// One gate's result, stored for diagnostics/logging.
/// </summary>
public readonly record struct GateTraceEntry(string GateName, bool Passed, string? Reason);