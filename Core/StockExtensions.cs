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
        public static IEnumerable<SMA> CalculateSMA(this List<IStockInfo> stockInfo, int period)
        {
            if (stockInfo.Count < period) throw new Exception("Not enough data to calculate SMA for " + period + " days");
            for (int i = 0; i < stockInfo.Count - period + 1; i++) 
                yield return new SMA(stockInfo.Skip(i).Take(period).Average(stock => stock.Close), 
                    DateTime.Parse(stockInfo[i].TimeOfRequest));
        }

        public static IEnumerable<EMA> CalculateEMA(this List<IStockInfo> stockInfo, int period)
        {
            //https://www.iexplain.org/ema-how-to-calculate/
            // 
            // todo: calculate the entire EMA for a specific stock, but only return the period requested - this might give more accurate numbers

            var range = stockInfo.GetRange(0, period);
            decimal alpha = 2m / (period + 1m);

       
            var emaList = new List<EMA>();
            var count = 0;
            bool firstPass = true;

            #region original version
            foreach (var stock in Enumerable.Reverse(stockInfo))    // reverse date as it is from newest to oldest
            {
                var previousEMA = (firstPass) ?
                    CalculateSMA(stockInfo, 50).ToList()[period + 1].Price :
                    emaList[count - 1].Price;

                firstPass = false;

                var priceToday = stock.Close;
                var price = priceToday * alpha + previousEMA * (1 - alpha);
                var ema = new EMA(price, DateTime.Parse(stock.TimeOfRequest));
                emaList.Add(ema);
                count++;
            }
            #endregion

            //// linq version!!
            //foreach (var (stock, previousEMA) in from stock in Enumerable.Reverse(range)// reverse date as it is from newest to oldest
            //                                     let previousEMA = (firstPass) ?
            //                                                         CalculateSMA(stockInfo, 50).ToList()[period + 1].Price :
            //                                                         emaList[count - 1].Price
            //                                     select (stock, previousEMA))
            //{
            //    firstPass = false;
            //    var priceToday = stock.Close;
            //    var price = priceToday * alpha + previousEMA * (1 - alpha);
            //    var ema = new EMA(price, DateTime.Parse(stock.TimeOfRequest));
            //    emaList.Add(ema);
            //    count++;
            //}

            return emaList;
        }

        /// <summary>
        /// The MACD is calculated by subtracting the 26-period Exponential Moving Average (EMA) 
        /// from the 12-period EMA. The result of that calculation is the MACD line.
        /// </summary>
        /// <param name="stockInfo"></param>
        /// <param name="periodOne"></param>
        /// <param name="periodTwo"></param>
        /// <returns></returns>
        public static IEnumerable<decimal> CalculateMACD(
            this List<IStockInfo> stockInfo,
            int periodOne,
            int periodTwo)
        {

            var p1 = stockInfo.CalculateEMA(periodOne).ToList();
            var p2 = stockInfo.CalculateEMA(periodTwo).ToList();
            var retLst = new List<decimal>();
            for (int i = 0; i < 10; i++)
            {
                var x = p1[i].Price - p2[i].Price;
                retLst.Add(x);
            }
            return retLst;
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
