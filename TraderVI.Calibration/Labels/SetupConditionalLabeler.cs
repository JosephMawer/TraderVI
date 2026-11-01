using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Wrapper labeler that only emits valid samples when a setup condition is present.
/// Uses rule-based setup detection (same logic as BreakoutEnhanced) to avoid circularity.
/// 
/// This aligns training data with Delphi's behavior: only care about follow-through
/// AFTER a tradable setup exists.
/// </summary>
public sealed class SetupConditionalLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars => _innerLabeler.HorizonBars;

    private readonly ILabeler _innerLabeler;
    private readonly int _priorHighLookback;
    private readonly float _breakoutPercent;
    private readonly float _minVolumeRatio;

    /// <summary>
    /// Creates a setup-conditional labeler.
    /// </summary>
    /// <param name="innerLabeler">The actual labeler to use when setup is present.</param>
    /// <param name="priorHighLookback">Days to look back for prior high (default 20).</param>
    /// <param name="breakoutPercent">Minimum % above prior high to qualify (default 0 = any break).</param>
    /// <param name="minVolumeRatio">Optional volume confirmation (default 1.0 = no filter).</param>
    public SetupConditionalLabeler(
        ILabeler innerLabeler,
        int priorHighLookback = 20,
        float breakoutPercent = 0f,
        float minVolumeRatio = 1.0f)
    {
        _innerLabeler = innerLabeler;
        _priorHighLookback = priorHighLookback;
        _breakoutPercent = breakoutPercent;
        _minVolumeRatio = minVolumeRatio;
        Name = $"Setup_{innerLabeler.Name}";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        // Check setup condition first
        if (!IsSetupPresent(windowBars))
        {
            // No setup → invalid sample (drop from training)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);
        }

        // Setup present → delegate to inner labeler
        return _innerLabeler.ComputeLabel(windowBars, futureBars);
    }

    /// <summary>
    /// Rule-based setup detection matching BreakoutEnhanced logic.
    /// Close breaks above prior N-day high.
    /// </summary>
    private bool IsSetupPresent(IReadOnlyList<DailyBar> windowBars)
    {
        int n = windowBars.Count;
        if (n < _priorHighLookback + 1)
            return false;

        var lastBar = windowBars[^1];
        float currentClose = lastBar.Close;

        // Find prior high (excluding current bar)
        float priorHigh = 0;
        for (int i = n - 1 - _priorHighLookback; i < n - 1; i++)
        {
            if (i >= 0 && windowBars[i].High > priorHigh)
                priorHigh = windowBars[i].High;
        }

        if (priorHigh <= 0)
            return false;

        // Check breakout condition
        float breakoutThreshold = priorHigh * (1 + _breakoutPercent / 100f);
        bool priceBreakout = currentClose > breakoutThreshold;

        if (!priceBreakout)
            return false;

        // Optional volume confirmation
        if (_minVolumeRatio > 1.0f)
        {
            float avgVolume = 0;
            int volCount = System.Math.Min(20, n - 1);
            for (int i = n - 1 - volCount; i < n - 1; i++)
            {
                if (i >= 0)
                    avgVolume += windowBars[i].Volume;
            }
            avgVolume /= volCount;

            if (avgVolume > 0 && lastBar.Volume / avgVolume < _minVolumeRatio)
                return false;
        }

        return true;
    }
}