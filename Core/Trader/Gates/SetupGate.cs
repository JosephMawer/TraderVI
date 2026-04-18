namespace Core.Trader.Gates;

/// <summary>
/// Requires a minimum breakout probability (setup filter).
/// </summary>
public sealed class SetupGate(double minBreakoutProb = 0.30) : ITradeGate
{
    public string Name => "Setup";

    public GateResult Evaluate(GateContext context)
    {
        if (context.BreakoutProb < minBreakoutProb)
            return GateResult.Block($"Breakout {context.BreakoutProb:P0} < {minBreakoutProb:P0}");

        return GateResult.Pass();
    }
}