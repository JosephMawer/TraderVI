namespace Core.Trader.Gates;

/// <summary>
/// Continuations-lens setup gate. Replaces the breakout <see cref="SetupGate"/>
/// for the trend-continuation thesis: a candidate is only eligible if it is in a
/// confirmed multi-week uptrend.
///
/// Confirmation = Trend30 present AND the 10/30 moving-average crossover present.
/// Trend10 is intentionally excluded — it flips during routine pullbacks even while
/// a name is still leading, so requiring it would reject healthy continuation
/// candidates. Breakout probability plays no part here (demoted to a soft composite
/// input under this lens). See ADR-0014.
/// </summary>
public sealed class TrendConfirmationGate : ITradeGate
{
    public string Name => "TrendConfirmation";

    public GateResult Evaluate(GateContext context)
    {
        if (!context.Trend30Confirmed)
            return GateResult.Block("Trend30 not confirmed (no multi-week uptrend)");

        if (!context.MaCrossoverConfirmed)
            return GateResult.Block("MaCrossover_10_30 not confirmed");

        return GateResult.Pass();
    }
}
