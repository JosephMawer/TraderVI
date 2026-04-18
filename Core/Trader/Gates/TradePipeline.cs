using System.Collections.Generic;

namespace Core.Trader.Gates;

/// <summary>
/// Runs a sequence of gates to determine Buy/Hold.
/// The first gate that blocks terminates evaluation.
/// </summary>
public sealed class TradePipeline
{
    private readonly IReadOnlyList<ITradeGate> _gates;

    public TradePipeline(IReadOnlyList<ITradeGate> gates)
    {
        _gates = gates;
    }

    /// <summary>
    /// Creates the default pipeline matching design-rules.md decision flow.
    /// </summary>
    public static TradePipeline Default() => FromConfig(StrategyConfig.Default);

    /// <summary>
    /// Creates a pipeline with gate thresholds driven by a StrategyConfig
    /// (which itself comes from the active strategy version, with fallback defaults).
    /// </summary>
    public static TradePipeline FromConfig(StrategyConfig config) => new(
    [
        new RegimeGate(),
        new BreadthGate(),
        new GranvilleGate(),
        new DownProbabilityGate(config.MaxDownProb),
        new SetupGate(config.MinBreakoutProb),
        new DirectionGate(config.MinDirectionEdge, config.MinUpProb),
        new CompositeGate(config.MinCompositeScore, config.StrongBreakoutOverride, config.StrongEdgeOverride)
    ]);

    /// <summary>
    /// Evaluates all gates in order.
    /// Returns Buy if all gates pass, Hold if any gate blocks.
    /// The context's Trace list is populated for diagnostics.
    /// </summary>
    public TradeDirection Evaluate(GateContext context)
    {
        foreach (var gate in _gates)
        {
            var result = gate.Evaluate(context);
            context.Trace.Add(new GateTraceEntry(gate.Name, result.Passed, result.Reason));

            if (!result.Passed)
                return TradeDirection.Hold;
        }

        return TradeDirection.Buy;
    }
}