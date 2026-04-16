namespace Core.Trader.Gates;

/// <summary>
/// Blocks longs when both XIU and SPY benchmarks are bearish.
/// </summary>
public sealed class RegimeGate : ITradeGate
{
    public string Name => "Regime";

    public GateResult Evaluate(GateContext context)
    {
        if (!context.RequireBenchmarkUptrend || context.Regime is null)
            return GateResult.Pass();

        if (context.Regime.IsBothBearish)
            return GateResult.Block("Both XIU and SPY are bearish");

        return GateResult.Pass();
    }
}