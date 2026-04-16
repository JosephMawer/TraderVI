namespace Core.Trader;

/// <summary>
/// Runtime configuration derived from the active StrategyVersion.
/// Every threshold has a sensible default so the system works without a DB row.
/// </summary>
public sealed record StrategyConfig
{
    // ── Composite gate ──
    public double MinCompositeScore { get; init; } = 0.35;
    public double StrongBreakoutOverride { get; init; } = 0.60;
    public double StrongEdgeOverride { get; init; } = 0.10;

    // ── Direction gate ──
    public double MinDirectionEdge { get; init; } = 0.05;
    public double MinUpProb { get; init; } = 0.25;

    // ── Setup gate ──
    public double MinBreakoutProb { get; init; } = 0.30;

    // ── Down probability gate ──
    public double MaxDownProb { get; init; } = 0.20;

    // ── Breadth gate ──
    public double BreadthVetoThreshold { get; init; } = -0.30;

    // ── Position sizing ──
    public double StopLossPercent { get; init; } = -0.10;
    public double WarningPercent { get; init; } = -0.05;
    public int MaxPositions { get; init; } = 1;

    // ── Regime ──
    public bool RequireBenchmarkUptrend { get; init; } = true;

    /// <summary>
    /// Default configuration when no strategy version is active.
    /// </summary>
    public static StrategyConfig Default => new();
}