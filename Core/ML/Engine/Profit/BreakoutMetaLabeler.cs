using System;
using System.Collections.Generic;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Meta-labeling: only creates training samples when an entry condition fires.
/// Then labels the outcome using a path-aware labeler (e.g., triple-barrier).
/// 
/// This answers: "Given a setup trigger, will it follow through?"
/// Instead of: "Will price go up?" (generic, unpredictable)
/// </summary>
public sealed class BreakoutMetaLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly int _priorHighLookback;
    private readonly float _breakoutPercent;
    private readonly float _profitAtrMultiple;
    private readonly float _stopAtrMultiple;
    private readonly int _atrPeriod;

    /// <summary>
    /// Creates a breakout meta-labeler.
    /// Entry: price breaks above prior high by X%
    /// Outcome: triple-barrier (profit target / stop loss / time)
    /// </summary>
    /// <param name="horizonBars">Max holding period (time barrier).</param>
    /// <param name="priorHighLookback">Lookback for prior high (default 20).</param>
    /// <param name="breakoutPercent">Required % above prior high to trigger (default 0 = any breakout).</param>
    /// <param name="profitAtrMultiple">Profit target in ATRs (default 1.5).</param>
    /// <param name="stopAtrMultiple">Stop loss in ATRs (default 1.0).</param>
    /// <param name="atrPeriod">ATR calculation period.</param>
    public BreakoutMetaLabeler(
        int horizonBars = 15,
        int priorHighLookback = 20,
        float breakoutPercent = 0f,
        float profitAtrMultiple = 1.5f,
        float stopAtrMultiple = 1.0f,
        int atrPeriod = 14)
    {
        HorizonBars = horizonBars;
        _priorHighLookback = priorHighLookback;
        _breakoutPercent = breakoutPercent;
        _profitAtrMultiple = profitAtrMultiple;
        _stopAtrMultiple = System.Math.Abs(stopAtrMultiple);
        _atrPeriod = atrPeriod;
        Name = $"BreakoutMeta_{horizonBars}d_lb{priorHighLookback}_+{_profitAtrMultiple:0.#}/-{_stopAtrMultiple:0.#}atr";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < System.Math.Max(_priorHighLookback, _atrPeriod) + 1)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // ═══════════════════════════════════════════════════════════════
        // ENTRY CONDITION: Is this a breakout bar?
        // ═══════════════════════════════════════════════════════════════
        var currentBar = windowBars[^1];
        float entryPrice = currentBar.Close;

        if (entryPrice <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // Find prior high (excluding current bar)
        float priorHigh = 0;
        int startIdx = System.Math.Max(0, windowBars.Count - 1 - _priorHighLookback);
        for (int i = startIdx; i < windowBars.Count - 1; i++)
        {
            if (windowBars[i].High > priorHigh)
                priorHigh = windowBars[i].High;
        }

        if (priorHigh <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // Check if breakout condition is met
        float breakoutThreshold = priorHigh * (1 + _breakoutPercent / 100f);
        bool isBreakout = currentBar.High >= breakoutThreshold;

        // ═══════════════════════════════════════════════════════════════
        // FILTER: Only include samples where setup fires
        // ═══════════════════════════════════════════════════════════════
        if (!isBreakout)
        {
            // Not a setup bar → skip this sample entirely
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);
        }

        // ═══════════════════════════════════════════════════════════════
        // OUTCOME: Triple-barrier on triggered setups
        // ═══════════════════════════════════════════════════════════════
        float atr = CalculateAtr(windowBars, _atrPeriod);
        if (atr <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float profitBarrier = entryPrice + (_profitAtrMultiple * atr);
        float stopBarrier = entryPrice - (_stopAtrMultiple * atr);

        // Walk forward and check which barrier is hit first
        for (int i = 0; i < HorizonBars && i < futureBars.Count; i++)
        {
            var bar = futureBars[i];

            // Profit target hit → successful breakout
            if (bar.High >= profitBarrier)
            {
                return new LabelResult(0, ThreeWayLabel.Buy, IsValid: true);
            }

            // Stop hit → failed breakout
            if (bar.Low <= stopBarrier)
            {
                return new LabelResult(0, ThreeWayLabel.Hold, IsValid: true);
            }
        }

        // Time barrier (neither hit) → neutral/timeout
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
/// Volume-confirmed breakout meta-labeler.
/// Entry: price breaks above prior high AND volume > 1.5x average
/// </summary>
public sealed class VolumeBreakoutMetaLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly int _priorHighLookback;
    private readonly float _volumeMultiple;
    private readonly float _profitAtrMultiple;
    private readonly float _stopAtrMultiple;
    private readonly int _atrPeriod;
    private readonly int _volumeAvgPeriod;

    public VolumeBreakoutMetaLabeler(
        int horizonBars = 15,
        int priorHighLookback = 20,
        float volumeMultiple = 1.5f,
        float profitAtrMultiple = 1.5f,
        float stopAtrMultiple = 1.0f,
        int atrPeriod = 14,
        int volumeAvgPeriod = 20)
    {
        HorizonBars = horizonBars;
        _priorHighLookback = priorHighLookback;
        _volumeMultiple = volumeMultiple;
        _profitAtrMultiple = profitAtrMultiple;
        _stopAtrMultiple = System.Math.Abs(stopAtrMultiple);
        _atrPeriod = atrPeriod;
        _volumeAvgPeriod = volumeAvgPeriod;
        Name = $"VolBreakoutMeta_{horizonBars}d_vol{_volumeMultiple:0.#}x_+{_profitAtrMultiple:0.#}/-{_stopAtrMultiple:0.#}atr";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < System.Math.Max(_priorHighLookback, System.Math.Max(_atrPeriod, _volumeAvgPeriod)) + 1)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        var currentBar = windowBars[^1];
        float entryPrice = currentBar.Close;

        if (entryPrice <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        // Prior high check
        float priorHigh = 0;
        int startIdx = System.Math.Max(0, windowBars.Count - 1 - _priorHighLookback);
        for (int i = startIdx; i < windowBars.Count - 1; i++)
        {
            if (windowBars[i].High > priorHigh)
                priorHigh = windowBars[i].High;
        }

        if (priorHigh <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        bool isBreakout = currentBar.High >= priorHigh;

        // Volume check
        float avgVolume = 0;
        int volStart = System.Math.Max(0, windowBars.Count - 1 - _volumeAvgPeriod);
        int volCount = 0;
        for (int i = volStart; i < windowBars.Count - 1; i++)
        {
            avgVolume += windowBars[i].Volume;
            volCount++;
        }
        avgVolume = volCount > 0 ? avgVolume / volCount : 0;

        bool volumeConfirmed = avgVolume > 0 && currentBar.Volume >= avgVolume * _volumeMultiple;

        // ═══════════════════════════════════════════════════════════════
        // FILTER: breakout + volume confirmation
        // ═══════════════════════════════════════════════════════════════
        if (!isBreakout || !volumeConfirmed)
        {
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);
        }

        // ═══════════════════════════════════════════════════════════════
        // OUTCOME: Triple-barrier
        // ═══════════════════════════════════════════════════════════════
        float atr = CalculateAtr(windowBars, _atrPeriod);
        if (atr <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float profitBarrier = entryPrice + (_profitAtrMultiple * atr);
        float stopBarrier = entryPrice - (_stopAtrMultiple * atr);

        for (int i = 0; i < HorizonBars && i < futureBars.Count; i++)
        {
            var bar = futureBars[i];

            if (bar.High >= profitBarrier)
                return new LabelResult(0, ThreeWayLabel.Buy, IsValid: true);

            if (bar.Low <= stopBarrier)
                return new LabelResult(0, ThreeWayLabel.Hold, IsValid: true);
        }

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