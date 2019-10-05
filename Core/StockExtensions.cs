﻿using System;
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
        public static IEnumerable<SMA> CalculateSMA(this List<IStockInfo> stockInfo, int period)
        {
            if (stockInfo.Count < period) throw new Exception("Not enough data to calculate SMA for " + period + " days");
            for (int i = 0; i < stockInfo.Count - period + 1; i++) 
                yield return new SMA(stockInfo.Skip(i).Take(period).Average(stock => stock.Close), 
                    DateTime.Parse(stockInfo[i].TimeOfRequest));
            
        }

        /// <summary>
        /// Gets the 52 week high for a stock
        /// </summary>
        /// <param name="stockInfo"></param>
        /// <returns>The highest closing price of the past 52 weeks</returns>
        public static decimal Calculate52WeekHigh(this List<IStockInfo> stockInfo)
            => stockInfo.Where(d => DateTime.Parse(d.TimeOfRequest) >= DateTime.Now.Date.AddDays(-364))
                        .Max(s => s.Close);
        

        public static List<OBV> CalculateOBV(this List<IStockInfo> stockInfo, int period)
        {
            var obv = new List<OBV>();
            // todo : calculate obv
            return obv;
        }
    }
}
