namespace Core.Trader.Gates;

/// <summary>
/// Blocks longs when A/D Line breadth score is at or below the veto threshold.
/// </summary>
public sealed class BreadthGate : ITradeGate
{
    public string Name => "Breadth";

    public GateResult Evaluate(GateContext context)
    {
        if (!context.BreadthScore.HasValue)
            return GateResult.Pass();

        if (context.BreadthScore.Value <= context.BreadthVetoThreshold)
            return GateResult.Block($"BreadthScore {context.BreadthScore.Value:+0.00} ≤ {context.BreadthVetoThreshold:+0.00}");

        return GateResult.Pass();
    }
}