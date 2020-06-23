using ConsoleTables;
using Core.Indicators.Models;
using Core.Math;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Indicators
{
    /// <summary>
    /// A collection of extension methods which extend <see cref="IStockInfo"/>
    /// </summary>
    public static partial class Indicators
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

        /// <summary>
        /// Get's a percentage increase or decrease table of the stock for a given time period
        /// </summary>
        /// <param name="StartDate"></param>
        /// <param name="EndDate"></param>
        public static DifferenceTable GetPercentageGain(this List<IStockInfo> stock, DateRange range, bool printTable = false)
        {
            var startDate = range.StartDate.ToShortDateString();
            var endDate = range.EndDate.ToShortDateString();

            if (stock.Count > range.DifferenceInDays)
            {
                var diff = stock.CalculateDifference(startDate, endDate);
                var max = stock.Where(x => IsInRange(x, startDate, endDate))
                               .Max(f => f.Close);
                var joined = (from f in stock
                              select new DifferenceTable
                              {
                                  StartDate = endDate,
                                  EndDate = startDate,
                                  Symbol = f.Ticker,
                                  Close = f.Close,
                                  Max = max,
                                  Difference = diff.ToString("P")
                              }).First();
                if (printTable)
                {
                    Console.WriteLine($"stock data for the past {range} days");
                    var data = new List<DifferenceTable>
                    {
                        joined
                    };
                    ConsoleTable.From(data).Write();
                }

                return joined;
            }

            // means there was not enough points in the list to calculate the difference 
            return default;
        }

        /// <summary>
        /// Get's the difference between two dates
        /// </summary>
        /// <param name="StartDate"></param>
        /// <param name="EndDate"></param>
        public static void GetPercentageGain(this List<List<IStockInfo>> stockData, DateRange range, PriceRange filter, int take, bool printTable = false)
        {
            List<DifferenceTable> lst = new List<DifferenceTable>();
            var startDate = range.StartDate.ToShortDateString();
            var endDate = range.EndDate.ToShortDateString();
            foreach (var stock in stockData)
            {
                if (stock.Count > range.DifferenceInDays)
                {
                    var diff = stock.CalculateDifference(startDate, endDate);
                    var max = stock.Where(x => IsInRange(x, startDate, endDate))
                                   .Max(f => f.Close);
                    var joined = (from f in stock
                                  select new DifferenceTable
                                  {
                                      StartDate = endDate,
                                      EndDate = startDate,
                                      Symbol = f.Ticker,
                                      Close = f.Close,
                                      Max = max,
                                      Difference = diff.ToString("P")
                                  }).First();
                    lst.Add(joined);
                    //Console.WriteLine($"{stock.First().Ticker} price change:p {diff:P}");
                }
                // create anonymous types that can be inserted into consoletables
                // todo - provide better support for anonymous types in consoletables
            }

            var topLst = lst.Where(p => p.Close > filter.Low && p.Close < filter.High)
                    .OrderByDescending(x => decimal.Parse(x.Difference.Replace("%", "")))
                    .Take(take);
            if (printTable)
            {
                Console.WriteLine($"stock data for the past {range.DifferenceInDays} days");
                ConsoleTable.From(topLst).Write();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="StockData"></param>
        /// <param name="take"></param>
        /// <param name="printTable"></param>
        /// <returns></returns>
        public static IEnumerable<TopPercentage> GetTopPriceMovers(this List<List<IStockInfo>> StockData, int take, bool printTable = false)
        {
            List<TopPercentage> table = new List<TopPercentage>();
            foreach (var stock in StockData)
            {
                if (stock.Count == 0 || stock is null) continue;
                var priceToday = stock[0].Close;
                var priceYesterday = stock[1].Close;
                var priceIncrease = priceToday - priceYesterday;
                var percentageIncrease = priceIncrease / 100m;
                //var ie2 = ie.Select(x => new { x.Foo, x.Bar, Sum = x.Abc + x.Def });
                var ret = stock.Select(x => new TopPercentage
                { Ticker = x.Ticker, Name = x.Name, Close = x.Close, PriceIncrease = priceIncrease })
                               .First();
                table.Add(ret);
            }

            //var topTen = table.Where(c => c.Close < 10).OrderByDescending(f => f.PriceIncrease).Take(10);
            var topTen = table.OrderByDescending(f => f.PriceIncrease).Take(take);
            if (printTable)
            {
                ConsoleTable.From(topTen).Write();
            }

            return topTen;
        }


       

        

        private static bool IsInRange(IStockInfo x, string StartDate, string EndDate)
        {
            return DateTime.Parse(x.TimeOfRequest) >= DateTime.Parse(EndDate) &&
                                             DateTime.Parse(x.TimeOfRequest) <= DateTime.Parse(StartDate);
        }


    }
}
