using System.Collections.Generic;
using Core.ML;

namespace Core.Indicators.Granville;

/// <summary>
/// Builds a <see cref="MarketTapeContext"/> from a list of XIU daily bars.
///
/// Inputs are expected to be ordered ASCENDING by date, with the most recent
/// session in the last position. The calculator handles graceful degradation
/// when there aren't enough prior sessions for the 20-day SMA.
/// </summary>
public static class MarketTapeCalculator
{
    /// <summary>Number of prior sessions used to average XIU volume (excludes today).</summary>
    public const int VolumeSmaPriorPeriod = 20;

    /// <summary>
    /// Build a market-tape context from ascending XIU daily bars.
    /// Returns null only if the input is empty (no bars at all).
    /// Otherwise returns a context populated with whatever facts are computable;
    /// missing inputs surface as null derived values.
    /// </summary>
    public static MarketTapeContext? Build(IReadOnlyList<DailyBar> xiuBarsAscending)
    {
        if (xiuBarsAscending is null || xiuBarsAscending.Count == 0)
            return null;

        var today = xiuBarsAscending[^1];

        decimal? prevClose = null;
        if (xiuBarsAscending.Count >= 2)
            prevClose = (decimal)xiuBarsAscending[^2].Close;

        decimal? sma20Prior = null;
        if (xiuBarsAscending.Count >= VolumeSmaPriorPeriod + 1)
        {
            // Prior 20 sessions excluding today: indices [count-21 .. count-2]
            long sum = 0;
            for (int i = xiuBarsAscending.Count - 1 - VolumeSmaPriorPeriod; i <= xiuBarsAscending.Count - 2; i++)
                sum += xiuBarsAscending[i].Volume;
            sma20Prior = sum / (decimal)VolumeSmaPriorPeriod;
        }

        return new MarketTapeContext
        {
            Date = today.Date,
            XiuVolume = today.Volume,
            XiuVolumeSma20Prior = sma20Prior,
            XiuClose = (decimal)today.Close,
            XiuPrevClose = prevClose,
        };
    }
}
