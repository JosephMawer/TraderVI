namespace Core.Trader.Gates;

/// <summary>
/// Requires minimum DirectionEdge (P(up) - P(down)) and P(up) confirmation.
/// </summary>
public sealed class DirectionGate(
    double minDirectionEdge = 0.05,
    double minUpProb = 0.25) : ITradeGate
{
    public string Name => "Direction";

    public GateResult Evaluate(GateContext context)
    {
        if (context.DirectionEdge < minDirectionEdge)
            return GateResult.Block($"DirectionEdge {context.DirectionEdge:P0} < {minDirectionEdge:P0}");

        if (context.UpProb < minUpProb)
            return GateResult.Block($"P(up) {context.UpProb:P0} < {minUpProb:P0}");

        return GateResult.Pass();
    }
}