using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Event label: within the next HorizonBars, does price break above the prior N-bar high
/// by a threshold percent?
/// </summary>
public sealed class BreakoutAbovePriorHighLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly int _priorHighLookback;
    private readonly float _breakoutPercent;

    public BreakoutAbovePriorHighLabeler(
        int horizonBars = 10,
        int priorHighLookback = 20,
        float breakoutPercent = 1.0f)
    {
        HorizonBars = horizonBars;
        _priorHighLookback = priorHighLookback;
        _breakoutPercent = breakoutPercent / 100f;

        Name = $"BreakoutAbovePriorHigh_{horizonBars}d_lb{priorHighLookback}_pct{breakoutPercent:0.##}";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < 2)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        int lb = System.Math.Min(_priorHighLookback, windowBars.Count);
        var prior = windowBars.Skip(windowBars.Count - lb);

        float priorHigh = prior.Max(b => b.High);
        if (priorHigh <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float trigger = priorHigh * (1 + _breakoutPercent);

        bool hit = futureBars
            .Take(HorizonBars)
            .Any(b => b.High >= trigger);

        return new LabelResult(
            ForwardReturn: 0,
            ThreeWayClass: hit ? ThreeWayLabel.Buy : ThreeWayLabel.Hold,
            IsValid: true);
    }
}