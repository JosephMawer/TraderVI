using Core.Trader;

namespace Core.Runtime;

/// <summary>Trading analysis stays outside the shared market-input validation library.</summary>
public static class DailyBenchmarkRegime
{
    public static MarketRegime ComputeRegime(this DailyBenchmarkSnapshot snapshot) =>
        TradeDecisionEngine.ComputeRegime(snapshot.Xiu, snapshot.Spy);
}
