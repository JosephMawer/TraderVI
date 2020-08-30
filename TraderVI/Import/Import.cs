using AlphaVantage.Net.Stocks;
using AlphaVantage.Net.Stocks.TimeSeries;
using Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Data.SQLite;

namespace TraderVI.Import
{
    public enum TimeSeries
    {
        Daily,
        Intraday
    }

    /// <summary>
    /// helper class for getting set up locally with a database of historical data.
    /// Note: it takes a while to import all stock data.
    /// </summary>
    public static class Import
    {
        private static async Task<List<IStockInfo>> GetDailyTimeSeriesData(AlphaVantageStocksClient client, string ticker, TimeSeries timeSeries)
        {
            var stockData = new List<IStockInfo>();

            StockTimeSeries stockTimeSeries;

            if (timeSeries == TimeSeries.Daily)
            {
                stockTimeSeries = await client.RequestDailyTimeSeriesAsync($"{ticker}", TimeSeriesSize.Full, adjusted: false);
            }
            else
            {
                stockTimeSeries = await client.RequestIntradayTimeSeriesAsync(ticker, IntradayInterval.OneMin, TimeSeriesSize.Full);
            }
            var points = stockTimeSeries.DataPoints;
            foreach (var point in points.Take(500))
            {
                var s = new Core.Models.StockQuote()
                {
                    TimeOfRequest = point.Time,
                    Volume = (int)point.Volume,
                    Open = point.OpeningPrice,
                    Close = point.ClosingPrice,
                    Ticker = ticker.Replace(".TO", ""),
                    High = point.HighestPrice,
                    Low = point.LowestPrice,
                    Price = point.ClosingPrice
                };
                stockData.Add(s);
            }
            return stockData;
        }
        private static List<string> GetBlackListedConstituents()
        {
            // todo: run test to verify if the ticker request fails or not, via http

            #region old code
            //var blackList = new List<string>();
            //var path = @"C:\Users\joseph.mawer.IDEA\Desktop\blacklist.txt";
            //var blackListeSymbols = File.ReadAllLines(path);
            //blackList.AddRange(blackListeSymbols);
            //return blackList;
            #endregion

            return default;
        }

        /// <summary>
        /// Use this method to download stock data using alpha vantage API and import it into the sqlite database 
        /// this takes quite a while because we have the 'free' version / api key, so we have to wait 20+ seconds 
        /// between each request. 
        /// 
        /// It will only import the data to sqlite if there is not any data already existing 
        /// in the database. 
        /// 
        /// Consider using this the first time you want to download data and initialize db with data.
        /// </summary>
        public static async Task ImportStockData(TimeSeries timeSeries)
        {
            var client = new AlphaVantageStocksClient(Constants.apiKey);

            var db = new Core.Db.DailyTimeSeries();
            var constituents = await Core.Db.Constituents.GetConstituents(); // get full list

            // list of tickers that don't work with alpha vantage
            var blackList = GetBlackListedConstituents();

            foreach (var c in constituents)
            {
                var symbol = c.Symbol;
                var recordCount = await db.GetRecordCount(symbol);
                if (recordCount < 100 && !blackList.Contains(symbol)) // check that we haven't already gathered symbol info and that the symbol hasn't been blacklisted 
                {
                    try
                    {
                        Debug.WriteLine("Download symbol info for: " + symbol);
                        var data = await GetDailyTimeSeriesData(client, symbol + ".TO", timeSeries);
                        if (timeSeries == TimeSeries.Daily)
                        {
                            await db.InsertDailyStockList(data);
                        }
                        else
                        {
                            await db.InsertIntradayStocks(data);
                        }
                        Debug.WriteLine("Successfully downloaded data at " + DateTime.Now);
                        await Task.Delay(45000);    // wait 20 seconds
                    }
                    catch (Exception ex)
                    {
                        // todo - keep adding these to black list?
                        Console.WriteLine(ex.Message);
                        //File.AppendAllText(path, symbol + Environment.NewLine);
                    }
                }
            }
        }

        /// <summary>
        /// This was a little helper method I used to import all the stocks that compose the S&P/TSX Composite index
        /// from a .csv file.  I found all the stocks on Wikipedia: https://en.wikipedia.org/wiki/S%26P/TSX_Composite_Index
        /// </summary>
        public static void ImportTSXCompositeIndex()
        {
            var lines = File.ReadAllLines(@"C:\Users\sesa345094\Desktop\tsx_composite.csv");
            foreach (var line in lines)
            {
                var i = line.Split(',');
                var query = $@"insert into TSXCompositeIndex values ('{i[0].Replace("'", "''")}','{i[1].Replace("'", "''")}','{i[2].Replace("'", "''")}','{i[3].Replace("'", "''")}')";

                using var con = new SQLiteConnection("Data Source=.;Initial Catalog=Db;Integrated Security=True;");
                con.Open();
                using var cmd = new SQLiteCommand(query, con);
                cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
