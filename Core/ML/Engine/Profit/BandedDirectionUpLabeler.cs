using System;
using System.Collections.Generic;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Labels "big up" vs "not big up" with a volatility-scaled band.
/// 
/// CORRECT BINARY BEHAVIOR:
/// - IsValid = true for ALL samples (both classes trainable)
/// - IsEvent = true when fwdRet > +band (positive class)
/// - IsEvent = false otherwise (negative class = in-band + down)
/// 
/// This is "big up vs not big up", not "up vs down".
/// </summary>
public sealed class BandedDirectionUpLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _bandAtrMultiple;
    private readonly int _atrPeriod;

    public BandedDirectionUpLabeler(int horizonBars = 5, float bandAtrMultiple = 0.5f, int atrPeriod = 14)
    {
        HorizonBars = horizonBars;
        _bandAtrMultiple = bandAtrMultiple;
        _atrPeriod = atrPeriod;
        Name = $"BandedDirUp_{horizonBars}d_{bandAtrMultiple:0.#}atr";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < _atrPeriod + 1)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float entryPrice = windowBars[^1].Close;
        float exitPrice = futureBars[HorizonBars - 1].Close;

        if (entryPrice <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float atr = CalculateAtr(windowBars, _atrPeriod);
        if (atr <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // Convert ATR to return threshold
        float bandReturn = (_bandAtrMultiple * atr) / entryPrice;
        float forwardReturn = (exitPrice - entryPrice) / entryPrice;

        // ═══════════════════════════════════════════════════════════════
        // CORRECT BINARY BEHAVIOR:
        // - IsValid = true for BOTH classes (trainable dataset)
        // - ThreeWayLabel.Buy = positive class (big up)
        // - ThreeWayLabel.Hold = negative class (not big up)
        // ═══════════════════════════════════════════════════════════════
        var direction = forwardReturn > bandReturn
            ? ThreeWayLabel.Buy   // Positive: big up move
            : ThreeWayLabel.Hold; // Negative: everything else

        return new LabelResult(forwardReturn, direction, IsValid: true);
    }

    private static float CalculateAtr(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        if (n < period + 1) return 0;

        float sum = 0;
        for (int i = n - period; i < n; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];

            float prevClose = prev.Close == 0 ? 1f : prev.Close;
            float tr = System.Math.Max(cur.High - cur.Low,
                       System.Math.Max(System.Math.Abs(cur.High - prevClose),
                                System.Math.Abs(cur.Low - prevClose)));
            sum += tr;
        }

        return sum / period;
    }
}

/// <summary>
/// Labels "big down" vs "not big down" with a volatility-scaled band.
/// Mirror of BandedDirectionUpLabeler for downside.
/// </summary>
public sealed class BandedDirectionDownLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly float _bandAtrMultiple;
    private readonly int _atrPeriod;

    public BandedDirectionDownLabeler(int horizonBars = 5, float bandAtrMultiple = 0.5f, int atrPeriod = 14)
    {
        HorizonBars = horizonBars;
        _bandAtrMultiple = bandAtrMultiple;
        _atrPeriod = atrPeriod;
        Name = $"BandedDirDown_{horizonBars}d_{bandAtrMultiple:0.#}atr";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < _atrPeriod + 1)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float entryPrice = windowBars[^1].Close;
        float exitPrice = futureBars[HorizonBars - 1].Close;

        if (entryPrice <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float atr = CalculateAtr(windowBars, _atrPeriod);
        if (atr <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float bandReturn = (_bandAtrMultiple * atr) / entryPrice;
        float forwardReturn = (exitPrice - entryPrice) / entryPrice;

        // Positive = big down move (return < -band)
        var direction = forwardReturn < -bandReturn
            ? ThreeWayLabel.Buy   // Positive: big down move
            : ThreeWayLabel.Hold; // Negative: everything else

        return new LabelResult(forwardReturn, direction, IsValid: true);
    }

    private static float CalculateAtr(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        if (n < period + 1) return 0;

        float sum = 0;
        for (int i = n - period; i < n; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];

            float prevClose = prev.Close == 0 ? 1f : prev.Close;
            float tr = System.Math.Max(cur.High - cur.Low,
                       System.Math.Max(System.Math.Abs(cur.High - prevClose),
                                System.Math.Abs(cur.Low - prevClose)));
            sum += tr;
        }

        return sum / period;
    }
}