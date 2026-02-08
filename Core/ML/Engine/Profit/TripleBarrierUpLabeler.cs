using System;
using System.Collections.Generic;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Triple-barrier labeling: which barrier is hit first?
/// - Upper barrier: +K ATRs (profit target)
/// - Lower barrier: -K ATRs (stop loss)
/// - Time barrier: max bars (timeout)
/// 
/// Label = 1 (Buy) if upper hit first, 0 (Hold) otherwise.
/// This is path-aware and more learnable than end-of-horizon labels.
/// </summary>
public sealed class TripleBarrierUpLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _upperAtrMultiple;
    private readonly float _lowerAtrMultiple;
    private readonly int _atrPeriod;

    /// <summary>
    /// Creates a triple-barrier labeler for upward moves.
    /// </summary>
    /// <param name="horizonBars">Maximum bars before timeout (time barrier).</param>
    /// <param name="upperAtrMultiple">ATR multiple for profit target (e.g., 1.0 = +1 ATR).</param>
    /// <param name="lowerAtrMultiple">ATR multiple for stop loss (e.g., 1.0 = -1 ATR). Positive number.</param>
    /// <param name="atrPeriod">Period for ATR calculation.</param>
    public TripleBarrierUpLabeler(
        int horizonBars = 10,
        float upperAtrMultiple = 1.0f,
        float lowerAtrMultiple = 1.0f,
        int atrPeriod = 14)
    {
        HorizonBars = horizonBars;
        _upperAtrMultiple = upperAtrMultiple;
        _lowerAtrMultiple = System.Math.Abs(lowerAtrMultiple);
        _atrPeriod = atrPeriod;
        Name = $"TripleBarrierUp_{horizonBars}d_+{_upperAtrMultiple:0.#}/-{_lowerAtrMultiple:0.#}atr";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < _atrPeriod + 1)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float entryPrice = windowBars[^1].Close;
        if (entryPrice <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float atr = CalculateAtr(windowBars, _atrPeriod);
        if (atr <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // Calculate barriers
        float upperBarrier = entryPrice + (_upperAtrMultiple * atr);
        float lowerBarrier = entryPrice - (_lowerAtrMultiple * atr);

        // Walk forward and check which barrier is hit first
        for (int i = 0; i < HorizonBars && i < futureBars.Count; i++)
        {
            var bar = futureBars[i];

            // Check upper barrier (using High for intraday touch)
            if (bar.High >= upperBarrier)
            {
                return new LabelResult(0, ThreeWayLabel.Buy, IsValid: true);
            }

            // Check lower barrier (using Low for intraday touch)
            if (bar.Low <= lowerBarrier)
            {
                return new LabelResult(0, ThreeWayLabel.Hold, IsValid: true);
            }
        }

        // Time barrier hit (no barrier touched) → Hold
        return new LabelResult(0, ThreeWayLabel.Hold, IsValid: true);
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
/// Triple-barrier labeler for downward moves (risk/veto signal).
/// Label = 1 if lower barrier hit first.
/// </summary>
public sealed class TripleBarrierDownLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _upperAtrMultiple;
    private readonly float _lowerAtrMultiple;
    private readonly int _atrPeriod;

    public TripleBarrierDownLabeler(
        int horizonBars = 10,
        float upperAtrMultiple = 1.0f,
        float lowerAtrMultiple = 1.0f,
        int atrPeriod = 14)
    {
        HorizonBars = horizonBars;
        _upperAtrMultiple = System.Math.Abs(upperAtrMultiple);
        _lowerAtrMultiple = System.Math.Abs(lowerAtrMultiple);
        _atrPeriod = atrPeriod;
        Name = $"TripleBarrierDown_{horizonBars}d_+{_upperAtrMultiple:0.#}/-{_lowerAtrMultiple:0.#}atr";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < _atrPeriod + 1)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float entryPrice = windowBars[^1].Close;
        if (entryPrice <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float atr = CalculateAtr(windowBars, _atrPeriod);
        if (atr <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float upperBarrier = entryPrice + (_upperAtrMultiple * atr);
        float lowerBarrier = entryPrice - (_lowerAtrMultiple * atr);

        for (int i = 0; i < HorizonBars && i < futureBars.Count; i++)
        {
            var bar = futureBars[i];

            // Lower barrier hit first → label as "positive" (down risk realized)
            if (bar.Low <= lowerBarrier)
            {
                return new LabelResult(0, ThreeWayLabel.Buy, IsValid: true);
            }

            // Upper barrier hit first → no down risk
            if (bar.High >= upperBarrier)
            {
                return new LabelResult(0, ThreeWayLabel.Hold, IsValid: true);
            }
        }

        // Timeout → no significant down move
        return new LabelResult(0, ThreeWayLabel.Hold, IsValid: true);
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