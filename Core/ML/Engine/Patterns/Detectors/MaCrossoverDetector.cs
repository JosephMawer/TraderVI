using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns.Detectors;

/// <summary>
/// Detects a bullish moving average crossover (short MA crosses above long MA).
/// Label = true when short MA > long MA at end of window (Golden Cross condition).
/// </summary>
public class MaCrossoverDetector : IPatternDetector
{
    public string PatternName { get; }
    public int DefaultLookback { get; }

    private readonly int _shortPeriod;
    private readonly int _longPeriod;

    public MaCrossoverDetector(int shortPeriod = 10, int longPeriod = 30)
    {
        _shortPeriod = shortPeriod;
        _longPeriod = longPeriod;
        DefaultLookback = longPeriod + 5; // need enough bars for long MA + some buffer
        PatternName = $"MaCrossover_{shortPeriod}_{longPeriod}";
    }

    public bool Detect(IReadOnlyList<DailyBar> windowBars)
    {
        if (windowBars.Count < _longPeriod)
            return false;

        var closes = windowBars.Select(b => (double)b.Close).ToList();

        // Calculate MAs at the end of the window
        double shortMa = closes.TakeLast(_shortPeriod).Average();
        double longMa = closes.TakeLast(_longPeriod).Average();

        // Bullish: short MA is above long MA
        return shortMa > longMa;
    }
}