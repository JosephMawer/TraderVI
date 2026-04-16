namespace Core.Trader.Gates;

/// <summary>
/// Requires minimum composite score, with a fallback for very strong breakouts.
/// Also requires pattern confirmation (≥1 pattern Buy when patterns exist).
/// </summary>
public sealed class CompositeGate(
    double minCompositeScore = 0.35,
    double strongBreakoutOverride = 0.60,
    double strongEdgeOverride = 0.10) : ITradeGate
{
    public string Name => "Composite";

    public GateResult Evaluate(GateContext context)
    {
        // Pattern confirmation required for all buy paths
        bool hasPatternConfirmation = context.PatternBuys > 0 || context.PatternCount == 0;
        if (!hasPatternConfirmation)
            return GateResult.Block($"No pattern confirmation ({context.PatternBuys} buys / {context.PatternCount} patterns)");

        // Standard path: composite meets minimum
        if (context.CompositeScore >= minCompositeScore)
            return GateResult.Pass();

        // Fallback: very strong breakout + clear direction edge bypasses composite threshold
        if (context.BreakoutProb >= strongBreakoutOverride &&
            context.DirectionEdge >= strongEdgeOverride)
            return GateResult.Pass();

        return GateResult.Block($"Composite {context.CompositeScore:P0} < {minCompositeScore:P0}");
    }
}