using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Training.Classifiers
{
    public static class ClassificationUtilities
    {
        public static List<PatternWindow> BuildPatternDataset(
            List<DailyBar> bars,
            Dictionary<DateTime, bool> labels,
            int lookback)
        {
            var result = new List<PatternWindow>();

            for (int end = lookback - 1; end < bars.Count; end++)
            {
                var endDate = bars[end].Date.Date;

                if (!labels.TryGetValue(endDate, out bool hasPattern))
                    continue;

                int start = end - lookback + 1;
                var windowBars = bars.GetRange(start, lookback);

                float firstClose = (float)windowBars[0].Close;
                if (firstClose == 0) firstClose = 1f;

                var pricesNorm = new float[lookback];
                for (int i = 0; i < lookback; i++)
                    pricesNorm[i] = (float)windowBars[i].Close / firstClose;

                result.Add(new PatternWindow
                {
                    WindowPrices = pricesNorm,
                    HasHeadAndShoulders = hasPattern
                });
            }

            return result;
        }
    }
}