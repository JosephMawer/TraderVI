using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns.Detectors;

/// <summary>
/// Detects an upward trend based on linear regression slope of closing prices.
/// Label = true if slope > 0.
/// </summary>
public class TrendUpDetector : IPatternDetector
{
    public string PatternName { get; }
    public int DefaultLookback { get; }

    public TrendUpDetector(int lookback)
    {
        DefaultLookback = lookback;
        PatternName = $"TrendUp{lookback}";
    }

    public bool Detect(IReadOnlyList<DailyBar> windowBars)
    {
        var closes = windowBars.Select(b => (double)b.Close).ToArray();
        return CalculateSlope(closes) > 0;
    }

    private static double CalculateSlope(double[] y)
    {
        int n = y.Length;
        if (n < 2) return 0;

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += y[i];
            sumXY += i * y[i];
            sumX2 += i * i;
        }

        double denom = (n * sumX2) - (sumX * sumX);
        return denom == 0 ? 0 : ((n * sumXY) - (sumX * sumY)) / denom;
    }
}