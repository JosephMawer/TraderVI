using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Core.ML.Engine.Training.Classifiers
{
    public static class ClassificationUtilities
    {

        // todo: could leave this as general use for all patterns..
        // or, might be better to contain this logic in each pattern class itself?
        public static List<PatternWindow> BuildPatternDataset(
    List<DailyBar> bars,
    Dictionary<DateTime, bool> labels,
    int lookback)
        {
            var result = new List<PatternWindow>();

            for (int end = lookback - 1; end < bars.Count; end++)
            {
                var endDate = bars[end].Date.Date;

                // Only keep windows we have labels for
                if (!labels.TryGetValue(endDate, out bool hasPattern))
                    continue;

                int start = end - lookback + 1;
                var windowBars = bars.GetRange(start, lookback);

                var priceNorm = new float[lookback];
                var volNorm = new float[lookback];

                float firstClose = (float)windowBars[0].Close;
                if (firstClose == 0) firstClose = 1f;

                // average volume for normalization
                float avgVol = (float)windowBars.Average(b => (double)b.Volume);
                if (avgVol == 0) avgVol = 1f;

                for (int i = 0; i < lookback; i++)
                {
                    priceNorm[i] = (float)windowBars[i].Close / firstClose;
                    volNorm[i] = (float)windowBars[i].Volume / avgVol;
                }

                result.Add(new PatternWindow
                {
                    PriceNorm = priceNorm,
                    VolumeNorm = volNorm,
                    HasHeadAndShoulders = hasPattern
                });
            }

            return result;
        }



        //    public static List<PatternWindow> BuildPatternDataset(
        //List<DailyBar> bars,
        //int lookback)
        //    {
        //        var result = new List<PatternWindow>();

        //        for (int end = lookback - 1; end < bars.Count; end++)
        //        {
        //            int start = end - lookback + 1;
        //            var windowBars = bars.GetRange(start, lookback);

        //            // Feature: normalized closes in this window
        //            float firstClose = (float)windowBars[0].Close;
        //            var prices = new float[lookback];

        //            for (int i = 0; i < lookback; i++)
        //            {
        //                float c = (float)windowBars[i].Close;
        //                prices[i] = firstClose != 0 ? c / firstClose : 0f;  // normalize by first close
        //            }

        //            // Label from your rule-based detector
        //            //bool label = PatternDetectors.IsHeadAndShoulders(windowBars);

        //            result.Add(new PatternWindow
        //            {
        //                WindowPrices = prices,
        //                HasHeadAndShoulders = true
        //            });
        //        }

        //        return result;
        //    }

    }
}
