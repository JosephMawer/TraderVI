using System;
using System.Collections.Generic;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Labels samples where forward return goes DOWN by threshold%.
/// Used as a veto signal for longs (and optionally for short signals).
/// </summary>
public sealed class BinaryDownLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _downThresholdPercent;

    /// <summary>
    /// Creates a binary down-move labeler.
    /// </summary>
    /// <param name="horizonBars">Forward bars to measure return.</param>
    /// <param name="downThresholdPercent">Minimum % drop for positive label (e.g., 4.0 = -4%).</param>
    public BinaryDownLabeler(int horizonBars = 10, float downThresholdPercent = 4.0f)
    {
        HorizonBars = horizonBars;
        _downThresholdPercent = System.Math.Abs(downThresholdPercent);
        Name = $"BinaryDown_{horizonBars}d_{_downThresholdPercent:0.#}pct";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float entryPrice = windowBars[^1].Close;
        float exitPrice = futureBars[HorizonBars - 1].Close;

        if (entryPrice <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float forwardReturn = (exitPrice - entryPrice) / entryPrice;
        float threshold = -_downThresholdPercent / 100f;

        // Positive class = big down move (for veto purposes)
        var direction = forwardReturn <= threshold ? ThreeWayLabel.Buy : ThreeWayLabel.Hold;

        return new LabelResult(forwardReturn, direction, IsValid: true);
    }
}