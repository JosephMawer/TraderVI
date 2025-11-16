//using ConsoleTables;
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
                    stockInfo[i].TimeOfRequest);
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
                var ema = new EMA(price, stock.TimeOfRequest);
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
            => stockInfo.Where(d => d.TimeOfRequest >= DateTime.Now.Date.AddDays(-364))
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
            var startDate = range.StartDate;
            var endDate = range.EndDate;

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
  //                  ConsoleTable.From(data).Write();
                }

                return joined;
            }

            // means there was not enough points in the list to calculate the difference 
            return default;
        }

        public struct StockPoint
        {
            public string Date { get; set; }
            public decimal Price { get; set; }
            public StockPoint(string date, decimal price)
            {
                Date = date;
                Price = price;
            }
        }
        public class SupportLine
        {
            // 3 or more data points would make a valid support line
            public bool SupportLineFound => DataPoint.Count >= 3;
            
            // does a large span indicate a stronger support line?
            public int DateSpan => DateTime.Parse(DataPoint.First().Date).Subtract(DateTime.Parse(DataPoint.Last().Date)).Days;
            public List<StockPoint> DataPoint { get; set; }

            public SupportLine()
            {
                DataPoint = new List<StockPoint>();
            }
        }

        /// <summary>
        /// This method takes two stock points (via the date range) and sees if those two points
        /// form a support line by using linear interpoaltion and checking all existings stock points
        /// between the two given points to see if it is what is expected. 
        /// </summary>
        /// <param name="stockData"></param>
        /// <param name="startDate"></param>
        /// <returns></returns>
        public static SupportLine GetSupportLevel(this List<IStockInfo> stockData, DateTime startDate, int length)
        {
            if (stockData.Count == 0 || stockData is null) throw new ArgumentException("stock data");

            // since we're not always working with the 'latest' data, simply grab the first element
            // from the dataset (which happens to be the most recent one, since it is an ordered list)
            IStockInfo stock = null;
            if (startDate > stockData.First().TimeOfRequest)
            {
                // if the stockData is out of date and we don't yet have the current date, use most recent
                stock = stockData.First();
            }
            else
            {
                // if the 'date' exists in our stock data, go ahead and start from that point
                stock = stockData.Where(x => x.TimeOfRequest == startDate).Single();
            }

            // start with a fixed starting point
            var y2 = stock.Close;
            var x2 = Resample(stock.TimeOfRequest);

            // add the first data point to the support line view model thing..
            // it's basically the list of dates and prices that make up the support line
            var supportLine = new SupportLine();
            var shortDate = stock.TimeOfRequest.ToShortDateString();
            supportLine.DataPoint.Add(new StockPoint(shortDate, y2));
           
            // pick two points, use linear interpolation to find a third point, to determine if we have a support line.
            var lastElement = length - 1; // add the minus one to account for the zero based arrays, 9th element 'actually' gives us 10 elements when zero based
          
            // find the last point
            var y1 = stockData[lastElement].Close;
            var x1 = Resample(stockData[lastElement].TimeOfRequest);

            // we have two point pairs (x1,x2), (y1,y2), and can perform linear interpolation                   
            // the target date is the date we're checking for to see if the interpolated value
            // matches the actual value (between two points)
            for (int targetDate = x1 + 1; targetDate < x2; targetDate++)
            {
                // Get the interpolated value for the given day, k
                var value = GetInterpolatedValue(targetDate, x1, x2, y1, y2);

                // check if the stocks price is equal (or close to) the
                // return value...
                //var date = stockData[targetDate - x1].TimeOfRequest;//.AddDays((x2 - targetDate));
                var date = Utilities.Utils.GetBusinessDay(stockData[targetDate - x1].TimeOfRequest);
                var currentStock = stockData.Where(x => x.TimeOfRequest == date).FirstOrDefault();

                if (currentStock is null) continue;

                var actualStockPrice = currentStock.Close;

                // now compare the interpolated value against the actual value to see if it's
                // close enough to be considered a 3rd point for support levels

                var accuracy = 0.005m;    // +/- 5%
                var highValue = value + (value * accuracy);
                var lowValue = value - (value * accuracy);

                if (actualStockPrice < highValue && actualStockPrice > lowValue)
                {
                    var point = new StockPoint(date.ToShortDateString(), actualStockPrice);
                    supportLine.DataPoint.Add(point);
                }   
            }

            // add the last point (after we have looped over all the intermediary points)
            var theLastPoint = new StockPoint(date: stockData[lastElement].TimeOfRequest.ToShortDateString(), 
                                              price: stockData[lastElement].Close);
            supportLine.DataPoint.Add(theLastPoint);

            return supportLine;
        }

        private static decimal GetInterpolatedValue(int k, int x1, int x2, decimal y1, decimal y2)
        {
            // linear interpolation formula
            // https://www.datadigitization.com/dagra-in-action/linear-interpolation-with-excel/
            var result = y1 + (k - x1)*(y2 - y1) / (x2 - x1);
            return result;
        }

        /// <summary>
        /// calculates the total number of days (starting from the given year) that the given date has
        /// </summary>
        private static int Resample(DateTime date)
        {
            var year = date.Year;      // get the starting point (year)
            var months = date.Month;   // get the ending point (month)

            int totalDays = 0;
            while (year <= DateTime.Now.Year)
            {
                if (year < DateTime.Now.Year)
                {
                    // loop through all 12 months
                    for (int month = 0; month < 12; month++)
                        totalDays += LastDayOfMonthArray[month];
                }
                else
                {
                    // loop only for the required amount of months
                    for (int month = 0; month < months; month++)
                    {
                        if (month == months - 1)
                            totalDays += date.Day;
                        else
                            totalDays += LastDayOfMonthArray[month];
                    }
                        
                }
               
                year++;
            }

            return totalDays;
        }

        /// <summary>
        /// An array containing the last day of each month (Jan - Dec)
        /// </summary>
        public static readonly int[] LastDayOfMonthArray = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };


        /// <summary>
        /// Get's the percentage difference between two dates
        /// </summary>
        /// <param name="StartDate"></param>
        /// <param name="EndDate"></param>
        public static void GetPercentageGain(this List<List<IStockInfo>> stockData, DateRange range, PriceRange filter, int take, bool printTable = false)
        {
            List<DifferenceTable> lst = new List<DifferenceTable>();
            var startDate = range.StartDate;
            var endDate = range.EndDate;
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
    //            ConsoleTable.From(topLst).Write();
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
      //          ConsoleTable.From(topTen).Write();
            }

            return topTen;
        }


       

        

        private static bool IsInRange(IStockInfo x, DateTime StartDate, DateTime EndDate)
        {
            return x.TimeOfRequest >= EndDate && x.TimeOfRequest <= StartDate;
        }


    }
}
