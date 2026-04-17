using Core.Indicators.Granville;
using System.Linq;

namespace Core.Trader.Gates;

/// <summary>
/// Soft gate based on Granville's 56 day-to-day indicators.
/// 
/// - Warns on any bearish signal (logged in gate trace).
/// - Blocks only on StrongBearish (indicator #3: "decline will continue" —
///   breadth AND benchmark both falling).
/// 
/// As more indicator groups are added, the blocking logic can be
/// refined (e.g., block when N+ groups agree on bearish).
/// </summary>
public sealed class GranvilleGate : ITradeGate
{
    public string Name => "Granville";

    public GateResult Evaluate(GateContext context)
    {
        if (context.GranvilleForecast is null)
            return GateResult.Pass();

        var forecast = context.GranvilleForecast;

        // Block on StrongBearish — currently only Plurality #3 (decline will continue)
        var strongBearish = forecast.Results
            .FirstOrDefault(r => r.Signal == IndicatorSignal.StrongBearish);

        if (strongBearish is not null)
        {
            return GateResult.Block(
                $"Granville {strongBearish.Name}: {strongBearish.Description}");
        }

        // Warn (but pass) on regular bearish signals — trace will show the warning
        var bearish = forecast.Results
            .FirstOrDefault(r => r.Signal == IndicatorSignal.Bearish);

        if (bearish is not null)
        {
            return new GateResult(Passed: true,
                Reason: $"⚠ Granville warning: {bearish.Name}");
        }

        return GateResult.Pass();
    }
}