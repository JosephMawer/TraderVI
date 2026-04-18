using System.Collections.Generic;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Simple binary labeler: did the stock go up by at least the threshold?
/// Simplifies the 3-way classification problem to binary (Buy vs NotBuy).
/// </summary>
public class BinaryUpLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _upThreshold;

    /// <summary>
    /// Creates a binary up labeler.
    /// </summary>
    /// <param name="horizonBars">How many bars ahead to measure return.</param>
    /// <param name="upThresholdPercent">Minimum return % to label as "up" (e.g., 2.0 for +2%).</param>
    public BinaryUpLabeler(int horizonBars = 10, float upThresholdPercent = 2.0f)
    {
        HorizonBars = horizonBars;
        _upThreshold = upThresholdPercent / 100f;
        Name = $"BinaryUp_{horizonBars}d_{upThresholdPercent:0}pct";
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

        // Binary: Buy if return >= threshold, else Hold (NotBuy)
        var direction = forwardReturn >= _upThreshold ? ThreeWayLabel.Buy : ThreeWayLabel.Hold;

        return new LabelResult(forwardReturn, direction, IsValid: true);
    }
}