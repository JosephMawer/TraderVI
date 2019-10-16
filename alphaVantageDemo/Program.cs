using AlphaVantage.Net.Stocks;
using AlphaVantage.Net.Stocks.BatchQuotes;
using AlphaVantage.Net.Stocks.TimeSeries;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Data.SqlClient;
using System.Diagnostics;
using Core;

namespace alphaVantageDemo
{

    /* What would be some useful stuff this program could do

           - Place orders for me..
           - Monitor securities to ensure it doesn't fall below stop limit ( perhaps even execute the trade at this point )
           = Perform trailing stop loss
           - Scan for stocks that meet certain criteria, i.e. price, volumne, trends, etc
           - Use Natural language processing to associate sentiment of a news article with movement in stock price/volume on a given day

    */


    class Program
    {
        //string apiKey = "1"; // enter your API key here
        private const string apiKey = "6IQSWE3D7UZHLKTB";
        private static readonly AlphaVantageStocksClient client = new AlphaVantageStocksClient(apiKey);


        static StockDataPoint previous = null;

        

        public static void GetYesterdaysTSXCompositeIndexStockPrices()
        {

        }

        public static async Task<List<IStockInfo>> GetRequestTimeSeriesData(string ticker)
        {
            var stockData = new List<IStockInfo>();
            StockTimeSeries stockTimeSeries = await client.RequestDailyTimeSeriesAsync($"{ticker}", TimeSeriesSize.Full, adjusted: false);
            var points = stockTimeSeries.DataPoints;
            foreach (var point in points.Take(500))
            {
                var s = new Core.StockInfo()
                {
                    TimeOfRequest = point.Time.ToShortDateString(),
                    Volume = (int)point.Volume,
                    Open = point.OpeningPrice,
                    Close = point.ClosingPrice,
                    Ticker = ticker,
                    High = point.HighestPrice,
                    Low = point.LowestPrice,
                    Price = point.ClosingPrice
                };
                stockData.Add(s);
            }
            return stockData;
        }

        public static async Task Main(string[] args)
        {
            var weed = await GetRequestTimeSeriesData("WEED");
            var db = new StocksDB.DailyStock();
            var db1 = new StocksDB.Constituents();
            var constituents = await db1.GetConstituents(); // get full list

            // list of tickers that don't work with alpha vantage
            var blackList = new List<string>() { "ACO.X", "AC", "ATD.B", "AP.UN", "APHA", "AX.UN" };//ac
            var path = @"C:\Users\joseph.mawer.IDEA\Desktop\blacklist.txt";
            var blackListeSymbols = File.ReadAllLines(path);
            blackList.AddRange(blackListeSymbols);
            foreach (var c in constituents)
            {
                var symbol = c.Symbol;
                var recordCount = await db.GetRecordCount(symbol);
                if (recordCount < 100 && !blackList.Contains(symbol)) // check that we haven't already gathered symbol info and that the symbol hasn't been blacklisted 
                {
                    try
                    {
                        Debug.WriteLine("Download symbol info for: " + symbol);
                        var data = await GetRequestTimeSeriesData(symbol);
                        await db.InsertDailyStockList(data.Skip(3));
                        Debug.WriteLine("Successfully downloaded data at " + DateTime.Now);
                        await Task.Delay(20000);    // wait 20 seconds

                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                        File.AppendAllText(path, symbol + Environment.NewLine);
                    }
                }
            }

            Console.WriteLine("Complete");
            Console.ReadLine();







            //AlphaVantage.Net.Core.AlphaVantageCoreClient c = new AlphaVantage.Net.Core.AlphaVantageCoreClient();
            //// retrieve stocks batch quoutes of Apple Inc. and Facebook Inc.:
            //var query = new Dictionary<string, string>()
            //{
            //        {"sector", "TSX"}
            //};
            //var response = await c.RequestApiAsync(apiKey, AlphaVantage.Net.Core.ApiFunction.SECTOR, query);
            //foreach (var r in response)
            //{
            //    Console.WriteLine(r.Key);
            //    Console.WriteLine(r.Value);
            //}

            //// retrieve stocks batch quoutes of Apple Inc. and Facebook Inc.:
            //query = new Dictionary<string, string>()
            //{
            //        {"TSX", ""}
            //};
            //response = await c.RequestApiAsync(apiKey, AlphaVantage.Net.Core.ApiFunction.SECTOR, query);
            //foreach (var r in response)
            //{
            //    Console.WriteLine(r.Key);
            //    Console.WriteLine(r.Value);
            //}



            ////var symbols = new Symbols();
            ////var SymbolList = await symbols.GetSymbolsList("where [Exchange]='TSX'");


            ////await AlphaVantageStocksDemo();

            //// calculate the on balance volume
            //// retrieve daily time series for stocks of Apple Inc.:
            //StockTimeSeries timeSeries = await client.RequestDailyTimeSeriesAsync("tsx:ivn", TimeSeriesSize.Compact, adjusted: true);
            //Print(timeSeries.DataPoints);
            //foreach (var current in timeSeries.DataPoints.Reverse().Take(2))
            //{
            //    var obv = GetOBV(current);

            //    Console.WriteLine($"{current.Time.ToShortDateString(),-20} {current.ClosingPrice,-20} {obv}");

            //    Console.WriteLine($"{current.Time.ToShortDateString(),-20} {current.ClosingPrice,-20} {obv}");

            //    // Set the previous data point
            //    previous = current;
            //}


            //Console.ReadLine();
        }
        /// <summary>
        /// The term employed when describing the total advances and declines of individual stocks against the average, 
        /// assigning the plurality to one or the other
        /// </summary>
        /// <returns></returns>
        private static int GetPlurality()
        {

            return -1;
        }

        private static void Print(ICollection<StockDataPoint> points)
        {
            foreach (var d in points)
            {
                Console.WriteLine($"{d.ClosingPrice} {d.Volume}");
            }
            var lst = new List<decimal>();
            foreach (var p in points)   // this returns the full history; i just want last 52 weeks
            {
                lst.Add(p.ClosingPrice);
            }

            var max = lst.Max();
            Console.WriteLine(lst.Max());

            foreach (var current in points.Take(2))
            {
                var obv = GetOBV(current);

                Console.WriteLine($"{current.Time.ToShortDateString(),-20} {current.ClosingPrice,-20} {obv}");

                Console.WriteLine($"{current.Time.ToShortDateString(),-20} {current.ClosingPrice,-20} {obv}");

                // Set the previous data point
                previous = current;
            }
        }

        private static long GetOBV(StockDataPoint current)
        {
            if (previous == null) return 0;

            if (current.ClosingPrice > previous.ClosingPrice)
            {
                // total daily volume is added to a cumulative total whenever the stock price closes higher than the day before
                return current.Volume + previous.Volume;
            }
            else if (current.ClosingPrice < previous.ClosingPrice)
            {
                //                ... is subtracted whenever stock price closes lower than day before
                return current.Volume - previous.Volume;
            }
            else 
            {
                // If the 
                return current.Volume;
            }
        }

        /// <summary>
        /// Retrieves the current <see cref="StockQuote"/> for the given ticker
        /// </summary>
        /// <param name="Ticker"></param>
        /// <returns></returns>
        public static async Task<StockQuote> GetRealTimePriceVolume(string Ticker)
            => (await client.RequestBatchQuotesAsync(new[] { Ticker })).FirstOrDefault();     
  
        
        public static async Task AlphaVantageStocksDemo(string ticker)
        {
            StockTimeSeries stockTimeSeries = await client.RequestDailyTimeSeriesAsync(ticker, TimeSeriesSize.Full);
            var points = stockTimeSeries.DataPoints;
            foreach (var p in points)
            {
                Console.WriteLine($@"{p.Time},{p.Volume},{p.OpeningPrice},{p.ClosingPrice},{p.HighestPrice},{p.LowestPrice}");
                Thread.Sleep(1000);
            }
            Console.ReadLine();
        
            // retrieve daily time series for stocks of Apple Inc.:
            //StockTimeSeries timeSeries = await client.RequestDailyTimeSeriesAsync("TSX:OGI", TimeSeriesSize.Compact, adjusted: false);
            //foreach (var dataPoint in timeSeries.DataPoints)
            //{
            //    Console.WriteLine($"{dataPoint.Time}: {dataPoint.ClosingPrice}");
            //}

        }
    }
}
