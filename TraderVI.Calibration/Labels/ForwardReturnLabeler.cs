using System.Collections.Generic;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Computes forward return and 3-way classification based on thresholds.
/// </summary>
public class ForwardReturnLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _buyThreshold;
    private readonly float _sellThreshold;

    /// <summary>
    /// Creates a forward return labeler.
    /// </summary>
    /// <param name="horizonBars">How many bars ahead to measure return.</param>
    /// <param name="buyThresholdPercent">Minimum return % to label as Buy (e.g., 2.0 for +2%).</param>
    /// <param name="sellThresholdPercent">Maximum return % to label as Sell (e.g., -2.0 for -2%).</param>
    public ForwardReturnLabeler(
        int horizonBars = 10,
        float buyThresholdPercent = 2.0f,
        float sellThresholdPercent = -2.0f)
    {
        HorizonBars = horizonBars;
        _buyThreshold = buyThresholdPercent / 100f;
        _sellThreshold = sellThresholdPercent / 100f;
        Name = $"ForwardReturn_{horizonBars}d_{buyThresholdPercent:0}_{sellThresholdPercent:0}";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float entryPrice = windowBars[^1].Close; // last bar of window
        float exitPrice = futureBars[HorizonBars - 1].Close; // bar at horizon

        if (entryPrice <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float forwardReturn = (exitPrice - entryPrice) / entryPrice;

        var threeWay = forwardReturn >= _buyThreshold ? ThreeWayLabel.Buy
                     : forwardReturn <= _sellThreshold ? ThreeWayLabel.Sell
                     : ThreeWayLabel.Hold;

        return new LabelResult(forwardReturn, threeWay, IsValid: true);
    }
}