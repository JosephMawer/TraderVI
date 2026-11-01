using Core.Indicators.Models;
using Core.ML;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Indicators
{
    public static class ObvCalculator
    {
        /// <summary>
        /// Computes Granville's On-Balance Volume (OBV) over a <see cref="DailyBar"/> series.
        ///
        /// OBV is a running cumulative: add the day's volume when the close is higher than the
        /// prior close, subtract it when lower, leave it unchanged when flat. The absolute value
        /// is anchor-relative (depends on where the chain started) — only its trend and breakouts
        /// are meaningful.
        /// </summary>
        /// <param name="bars">The bar series (any order — sorted ascending by date internally).</param>
        /// <param name="seedObv">
        /// Cumulative OBV to continue from (e.g. the last value stored in dbo.SymbolObv). 0 starts a fresh chain.
        /// </param>
        /// <param name="seedPrevClose">
        /// The close that precedes the first bar in <paramref name="bars"/> (e.g. the last stored bar's close),
        /// so the first new session is compared correctly. Null means the first bar has no predecessor (delta 0).
        /// </param>
        /// <param name="unchangedEpsilon">
        /// Closes within ±this value are treated as "unchanged" (no contribution). Guards against
        /// REAL/float noise on equal closes. Default 0.0001 (far below a one-cent tick).
        /// </param>
        public static List<OBV> CalculateOBV(this IEnumerable<DailyBar> bars,
            long seedObv = 0, decimal? seedPrevClose = null, decimal unchangedEpsilon = 0.0001m)
            => ComputeObv(
                bars.OrderBy(b => b.Date).Select(b => (b.Date, Close: (decimal)b.Close, b.Volume)),
                seedObv, seedPrevClose, unchangedEpsilon);

        /// <summary>
        /// <see cref="IStockInfo"/> overload of <see cref="CalculateOBV(IEnumerable{DailyBar}, long, decimal?, decimal)"/>,
        /// for consistency with the other indicator extensions (SMA/EMA). Shares the same core.
        /// </summary>
        public static List<OBV> CalculateOBV(this IEnumerable<IStockInfo> stockInfo,
            long seedObv = 0, decimal? seedPrevClose = null, decimal unchangedEpsilon = 0.0001m)
            => ComputeObv(
                stockInfo.OrderBy(s => s.TimeOfRequest).Select(s => (Date: s.TimeOfRequest, s.Close, s.Volume)),
                seedObv, seedPrevClose, unchangedEpsilon);

        /// <summary>
        /// Core OBV accumulation over a normalized (Date, Close, Volume) sequence that is already
        /// ordered ascending by date. Walks sessions forward, carrying the running cumulative.
        /// </summary>
        private static List<OBV> ComputeObv(
            IEnumerable<(DateTime Date, decimal Close, long Volume)> bars,
            long seedObv, decimal? seedPrevClose, decimal unchangedEpsilon)
        {
            var result = new List<OBV>();
            long cumulative = seedObv;
            decimal? prevClose = seedPrevClose;

            foreach (var (date, close, volume) in bars)
            {
                long delta = 0;
                if (prevClose is decimal pc)
                {
                    decimal diff = close - pc;
                    if (diff > unchangedEpsilon) delta = volume;         // higher close -> add volume
                    else if (diff < -unchangedEpsilon) delta = -volume;  // lower close  -> subtract volume
                    // otherwise unchanged -> contributes 0
                }

                cumulative += delta;
                result.Add(new OBV(date, cumulative, delta));
                prevClose = close;
            }

            return result;
        }

    }
}
