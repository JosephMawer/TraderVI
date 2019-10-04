using System;
using System.Collections.Generic;
using System.Linq;

namespace Core
{
    /// <summary>
    /// A collection of extension methods which extend <see cref="IStockInfo"/>
    /// </summary>
    public static class StockExtensions
    {

        /// <summary>
        /// Calculates the SMA for a time period
        /// </summary>
        /// <param name="stockInfo"></param>
        /// <param name="period">The period of time to calculate the SMA for</param>
        /// <returns>A list of <see cref="SMA"/></returns>
        public static List<SMA> CalculateSMA(this List<IStockInfo> stockInfo, int period)
        {
            if (stockInfo.Count < period) throw new Exception("Not enough data to calculate SMA for " + period + " days");
            var sma = new List<SMA>();
            for (int i = 0; i < stockInfo.Count - period + 1; i++) {
                var x = new SMA(stockInfo.Skip(i).Take(period).Average(stock => stock.Close), 
                    DateTime.Parse(stockInfo[i].TimeOfRequest));
                sma.Add(x);
            }
            return sma;
        }


        public static List<OBV> CalculateOBV(this List<IStockInfo> stockInfo, int period)
        {
            var obv = new List<OBV>();
            // todo : calculate obv
            return obv;
        }
    }
}
