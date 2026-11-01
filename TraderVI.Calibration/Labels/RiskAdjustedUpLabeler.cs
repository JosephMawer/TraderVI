using System;
using System.Collections.Generic;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Labels moves normalized by ATR (risk-adjusted).
/// z = ForwardReturn / ATR; Buy if z >= threshold.
/// This creates consistent signal across volatility regimes.
/// </summary>
public sealed class RiskAdjustedUpLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _atrThreshold;
    private readonly int _atrPeriod;

    /// <summary>
    /// Creates a risk-adjusted up-move labeler.
    /// </summary>
    /// <param name="horizonBars">Forward bars to measure return.</param>
    /// <param name="atrThreshold">Minimum ATR multiple for positive label (e.g., 0.75 = move >= 0.75 ATRs).</param>
    /// <param name="atrPeriod">Period for ATR calculation (default 14).</param>
    public RiskAdjustedUpLabeler(int horizonBars = 10, float atrThreshold = 0.75f, int atrPeriod = 14)
    {
        HorizonBars = horizonBars;
        _atrThreshold = atrThreshold;
        _atrPeriod = atrPeriod;
        Name = $"RiskAdjUp_{horizonBars}d_{atrThreshold:0.##}atr";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < _atrPeriod + 1)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float entryPrice = windowBars[^1].Close;
        float exitPrice = futureBars[HorizonBars - 1].Close;

        if (entryPrice <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float forwardReturn = (exitPrice - entryPrice) / entryPrice;

        // Calculate ATR at entry point
        float atr = CalculateAtr(windowBars, _atrPeriod);
        if (atr <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // Normalize return by ATR (z-score in ATR units)
        float atrPercent = atr / entryPrice;
        float zScore = forwardReturn / atrPercent;

        // Label: Buy if z >= threshold
        var direction = zScore >= _atrThreshold ? ThreeWayLabel.Buy : ThreeWayLabel.Hold;

        // Return 0 for ForwardReturn (event labeler pattern) to avoid MaxAbsForwardReturn filter
        return new LabelResult(0, direction, IsValid: true);
    }

    private static float CalculateAtr(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        if (n < period + 1) return 0;

        float sumTr = 0;
        for (int i = n - period; i < n; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];

            float prevClose = prev.Close == 0 ? 1f : prev.Close;
            float tr = System.Math.Max(cur.High - cur.Low,
                       System.Math.Max(System.Math.Abs(cur.High - prevClose),
                                System.Math.Abs(cur.Low - prevClose)));
            sumTr += tr;
        }

        return sumTr / period;
    }
}

/// <summary>
/// Labels downward moves normalized by ATR (risk-adjusted).
/// z = ForwardReturn / ATR; Buy (positive class) if z <= -threshold.
/// Used as a veto/risk signal.
/// </summary>
public sealed class RiskAdjustedDownLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _atrThreshold;
    private readonly int _atrPeriod;

    /// <summary>
    /// Creates a risk-adjusted down-move labeler.
    /// </summary>
    /// <param name="horizonBars">Forward bars to measure return.</param>
    /// <param name="atrThreshold">ATR multiple for negative label (e.g., 0.75 = move <= -0.75 ATRs).</param>
    /// <param name="atrPeriod">Period for ATR calculation (default 14).</param>
    public RiskAdjustedDownLabeler(int horizonBars = 10, float atrThreshold = 0.75f, int atrPeriod = 14)
    {
        HorizonBars = horizonBars;
        _atrThreshold = atrThreshold;
        _atrPeriod = atrPeriod;
        Name = $"RiskAdjDown_{horizonBars}d_{atrThreshold:0.##}atr";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < _atrPeriod + 1)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float entryPrice = windowBars[^1].Close;
        float exitPrice = futureBars[HorizonBars - 1].Close;

        if (entryPrice <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float forwardReturn = (exitPrice - entryPrice) / entryPrice;

        // Calculate ATR at entry point
        float atr = CalculateAtr(windowBars, _atrPeriod);
        if (atr <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // Normalize return by ATR
        float atrPercent = atr / entryPrice;
        float zScore = forwardReturn / atrPercent;

        // Label: "Buy" (positive class) if z <= -threshold (i.e., big down move)
        var direction = zScore <= -_atrThreshold ? ThreeWayLabel.Buy : ThreeWayLabel.Hold;

        // Return 0 for ForwardReturn (event labeler pattern) to avoid MaxAbsForwardReturn filter
        return new LabelResult(0, direction, IsValid: true);
    }

    private static float CalculateAtr(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        if (n < period + 1) return 0;

        float sumTr = 0;
        for (int i = n - period; i < n; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];

            float prevClose = prev.Close == 0 ? 1f : prev.Close;
            float tr = System.Math.Max(cur.High - cur.Low,
                       System.Math.Max(System.Math.Abs(cur.High - prevClose),
                                System.Math.Abs(cur.Low - prevClose)));
            sumTr += tr;
        }

        return sumTr / period;
    }
}