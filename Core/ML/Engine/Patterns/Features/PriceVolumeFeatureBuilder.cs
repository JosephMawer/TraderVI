using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns.Features;

/// <summary>
/// Builds normalized price and volume features.
/// Output: [priceNorm[0..n-1], volumeNorm[0..n-1]] (length = 2 * lookback)
/// </summary>
public class PriceVolumeFeatureBuilder : IFeatureBuilder
{
    public string Name => "PriceVolume";

    public int FeatureCount(int lookback) => lookback * 2;

    public float[] Build(IReadOnlyList<DailyBar> windowBars)
    {
        int n = windowBars.Count;

        float firstClose = windowBars[0].Close;
        if (firstClose == 0) firstClose = 1f;

        float avgVol = (float)windowBars.Average(b => (double)b.Volume);
        if (avgVol == 0) avgVol = 1f;

        var features = new float[n * 2];

        for (int i = 0; i < n; i++)
        {
            features[i] = windowBars[i].Close / firstClose;      // price normalized
            features[n + i] = windowBars[i].Volume / avgVol;     // volume normalized
        }

        return features;
    }
}