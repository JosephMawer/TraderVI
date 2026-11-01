using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Labels whether a stock continues to outperform the market over the next N days.
/// Based on the premise that "strong names keep being strong" (momentum/relative strength).
/// </summary>
public sealed class RelativeStrengthContinuationLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _outperformThresholdPercent;

    /// <summary>
    /// Market bars (XIU) must be set before calling ComputeLabel.
    /// </summary>
    public IReadOnlyList<DailyBar>? MarketBars { get; set; }

    /// <summary>
    /// Creates a relative strength continuation labeler.
    /// </summary>
    /// <param name="horizonBars">How many bars ahead to measure outperformance.</param>
    /// <param name="outperformThresholdPercent">
    /// Minimum excess return vs market to label as positive (e.g., 1.0 for +1% outperformance).
    /// </param>
    public RelativeStrengthContinuationLabeler(
        int horizonBars = 10,
        float outperformThresholdPercent = 1.0f)
    {
        HorizonBars = horizonBars;
        _outperformThresholdPercent = outperformThresholdPercent;
        Name = $"RelStrengthCont_{horizonBars}d_{outperformThresholdPercent:0}pct";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        if (MarketBars == null || MarketBars.Count == 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // Get stock return over horizon
        float stockEntry = windowBars[^1].Close;
        float stockExit = futureBars[HorizonBars - 1].Close;

        if (stockEntry <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float stockReturn = (stockExit - stockEntry) / stockEntry;

        // Find corresponding market bars for the same future period
        var windowEndDate = windowBars[^1].Date;
        var futureEndDate = futureBars[HorizonBars - 1].Date;

        // Find market entry (closest to window end)
        var marketEntryBar = MarketBars
            .Where(b => b.Date <= windowEndDate)
            .OrderByDescending(b => b.Date)
            .FirstOrDefault();

        // Find market exit (closest to future end)
        var marketExitBar = MarketBars
            .Where(b => b.Date <= futureEndDate)
            .OrderByDescending(b => b.Date)
            .FirstOrDefault();

        if (marketEntryBar == null || marketExitBar == null || marketEntryBar.Close <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float marketReturn = (marketExitBar.Close - marketEntryBar.Close) / marketEntryBar.Close;

        // Excess return = stock return - market return
        float excessReturn = stockReturn - marketReturn;

        // Label as positive if excess return exceeds threshold
        float threshold = _outperformThresholdPercent / 100f;
        bool isOutperformer = excessReturn >= threshold;

        return new LabelResult(
            ForwardReturn: excessReturn,
            ThreeWayClass: isOutperformer ? ThreeWayLabel.Buy : ThreeWayLabel.Hold,
            IsValid: true);
    }
}