using System;
using System.Collections.Generic;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Labels general direction (not tail events).
/// y = 1 if forward return > band, else 0.
/// This is a smoother, more frequent signal than tail models.
/// </summary>
public sealed class DirectionUpLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _bandPercent;

    /// <summary>
    /// Creates a general direction labeler.
    /// </summary>
    /// <param name="horizonBars">Forward bars to measure return.</param>
    /// <param name="bandPercent">Minimum % for positive label (0 = any positive, 0.5 = +0.5%).</param>
    public DirectionUpLabeler(int horizonBars = 5, float bandPercent = 0f)
    {
        HorizonBars = horizonBars;
        _bandPercent = bandPercent;
        Name = bandPercent == 0
            ? $"DirUp_{horizonBars}d"
            : $"DirUp_{horizonBars}d_{bandPercent:0.#}pct";
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
        float threshold = _bandPercent / 100f;

        // Positive class = forward return exceeds band
        var direction = forwardReturn > threshold ? ThreeWayLabel.Buy : ThreeWayLabel.Hold;

        return new LabelResult(forwardReturn, direction, IsValid: true);
    }
}