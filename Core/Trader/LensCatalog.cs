using Core.Trader.Gates;

namespace Core.Trader;

/// <summary>
/// Factory for the system's ranking lenses. Each lens is built here as a complete
/// (thesis → gate stack → ranking key) triple so the engine and Delphi never have
/// to assemble gate lists ad hoc. Adding a future lens means adding a factory here,
/// not branching an existing pipeline. See ADR-0013.
///
/// Both lenses share the market-level and capital-preservation gates
/// (Regime → Breadth → Granville → DownProbability → … → Direction → Composite).
/// They differ only in the setup-stage gate and the ranking key:
///   • Continuation: TrendConfirmationGate + RS-primary ranking (ADR-0014).
///   • Breakout:     SetupGate (BreakoutEnhanced floor) + DirectionEdge+RScomp (ADR-0011).
/// </summary>
public static class LensCatalog
{
    /// <summary>
    /// Continuations lens (executed). Gates on a confirmed multi-week uptrend
    /// (Trend30 + MaCrossover) instead of breakout probability, and ranks RS-first
    /// with DirectionEdge as confirmation. Breakout is a soft composite input only.
    /// </summary>
    public static LensDefinition Continuation(StrategyConfig config)
    {
        var pipeline = new TradePipeline(
        [
            new RegimeGate(),
            new BreadthGate(),
            new GranvilleGate(),
            new DownProbabilityGate(config.MaxDownProb),
            new TrendConfirmationGate(),
            new DirectionGate(config.MinDirectionEdge, config.MinUpProb),
            new CompositeGate(config.MinCompositeScore, config.StrongBreakoutOverride, config.StrongEdgeOverride)
        ]);

        // RS-primary: realized leadership drives the pick; DirectionEdge confirms it
        // (and the Direction gate already guarantees edge >= MinDirectionEdge).
        return new LensDefinition(
            RankingLens.Continuation,
            label: "Continuation",
            pipeline: pipeline,
            primaryKey: (_, rs) => rs);
    }

    /// <summary>
    /// Breakouts lens (journaled, not executed). Preserves the original pipeline and
    /// the equal-weight DirectionEdge + RScomp ranking key (ADR-0011), so it serves as
    /// a continuity baseline against the Continuations lens.
    /// </summary>
    public static LensDefinition Breakout(StrategyConfig config)
    {
        var pipeline = TradePipeline.FromConfig(config);

        return new LensDefinition(
            RankingLens.Breakout,
            label: "Breakout",
            pipeline: pipeline,
            primaryKey: (pick, rs) => pick.DirectionEdge + rs);
    }
}
