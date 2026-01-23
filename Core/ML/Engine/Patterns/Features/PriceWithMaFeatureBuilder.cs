using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns.Features;

/// <summary>
/// Builds features including normalized price, volume, and moving average ratios.
/// Output: [priceNorm, volumeNorm, shortMaRatio, longMaRatio] per bar + summary MAs
/// </summary>
public class PriceWithMaFeatureBuilder : IFeatureBuilder
{
    public string Name => "PriceWithMA";

    private readonly int _shortMaPeriod;
    private readonly int _longMaPeriod;

    public PriceWithMaFeatureBuilder(int shortMaPeriod = 10, int longMaPeriod = 30)
    {
        _shortMaPeriod = shortMaPeriod;
        _longMaPeriod = longMaPeriod;
    }

    public int FeatureCount(int lookback) => lookback * 2 + 4; // price + volume + 4 MA features

    public float[] Build(IReadOnlyList<DailyBar> windowBars)
    {
        int n = windowBars.Count;

        float firstClose = windowBars[0].Close;
        if (firstClose == 0) firstClose = 1f;

        float avgVol = (float)windowBars.Average(b => (double)b.Volume);
        if (avgVol == 0) avgVol = 1f;

        var closes = windowBars.Select(b => (double)b.Close).ToList();

        // Calculate MAs
        double shortMa = closes.TakeLast(_shortMaPeriod).Average();
        double longMa = closes.TakeLast(_longMaPeriod).Average();
        double currentPrice = closes.Last();

        var features = new float[n * 2 + 4];

        // Price and volume normalized (same as PriceVolumeFeatureBuilder)
        for (int i = 0; i < n; i++)
        {
            features[i] = windowBars[i].Close / firstClose;
            features[n + i] = windowBars[i].Volume / avgVol;
        }

        // MA features (normalized ratios)
        int offset = n * 2;
        features[offset] = (float)(shortMa / currentPrice);           // short MA relative to price
        features[offset + 1] = (float)(longMa / currentPrice);        // long MA relative to price
        features[offset + 2] = (float)(shortMa / longMa);             // short/long MA ratio
        features[offset + 3] = shortMa > longMa ? 1f : 0f;            // crossover flag

        return features;
    }
}