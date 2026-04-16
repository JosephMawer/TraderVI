namespace Core.Trader.Gates;

/// <summary>
/// Vetoes longs when P(down ≤ -4% in 10d) is too high.
/// </summary>
public sealed class DownProbabilityGate(double maxDownProb = 0.20) : ITradeGate
{
    public string Name => "DownProbability";

    public GateResult Evaluate(GateContext context)
    {
        if (context.DownProb >= maxDownProb)
            return GateResult.Block($"P(down) {context.DownProb:P0} ≥ {maxDownProb:P0}");

        return GateResult.Pass();
    }
}