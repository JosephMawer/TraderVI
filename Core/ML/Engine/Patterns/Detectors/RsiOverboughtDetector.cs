using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns.Detectors;

/// <summary>
/// Detects RSI overbought condition (RSI above threshold).
/// Label = true when RSI is above overbought level (default 70), indicating potential reversal down.
/// </summary>
public class RsiOverboughtDetector : IPatternDetector
{
    public string PatternName => "RsiOverbought";
    public int DefaultLookback { get; }

    private readonly int _rsiPeriod;
    private readonly double _overboughtThreshold;

    public RsiOverboughtDetector(int lookback = 20, int rsiPeriod = 14, double overboughtThreshold = 70.0)
    {
        DefaultLookback = lookback;
        _rsiPeriod = rsiPeriod;
        _overboughtThreshold = overboughtThreshold;
    }

    public bool Detect(IReadOnlyList<DailyBar> windowBars)
    {
        if (windowBars.Count < _rsiPeriod + 1)
            return false;

        var rsi = CalculateRsi(windowBars, _rsiPeriod);
        return rsi > _overboughtThreshold;
    }

    private static double CalculateRsi(IReadOnlyList<DailyBar> bars, int period)
    {
        if (bars.Count < period + 1)
            return 50; // neutral

        var changes = new List<double>();
        for (int i = 1; i < bars.Count; i++)
        {
            changes.Add(bars[i].Close - bars[i - 1].Close);
        }

        var recentChanges = changes.TakeLast(period).ToList();

        double avgGain = recentChanges.Where(c => c > 0).DefaultIfEmpty(0).Average();
        double avgLoss = System.Math.Abs(recentChanges.Where(c => c < 0).DefaultIfEmpty(0).Average());

        if (avgLoss == 0)
            return 100; // all gains

        double rs = avgGain / avgLoss;
        double rsi = 100 - (100 / (1 + rs));

        return rsi;
    }
}