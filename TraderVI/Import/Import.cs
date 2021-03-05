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
using dts = Core.Db.DailyTimeSeries;
using cts = Core.Db.Constituents;
using Core.Db;

namespace TraderVI.Import
{
    /// <summary>
    /// helper class for getting set up locally with a database of historical data.
    /// Note: it takes a while to import all stock data.
    /// </summary>
    public static class Import
    {
        private static List<string> BlackList = new List<string>();

        private static async Task<List<IStockInfo>> GetDailyTimeSeriesData(string ticker)
        {
            var client = new AlphaVantageStocksClient(Constants.apiKey);

            var stockData = new List<IStockInfo>();

            StockTimeSeries stockTimeSeries;

            stockTimeSeries = await client.RequestDailyTimeSeriesAsync($"{ticker}", TimeSeriesSize.Full, adjusted: false);
           
            var points = stockTimeSeries.DataPoints;
            //foreach (var point in points.Take(500))
            //{
            //    var s = new Core.Models.StockQuote()
            //    {
            //        TimeOfRequest = point.Time,
            //        Volume = (int)point.Volume,
            //        Open = point.OpeningPrice,
            //        Close = point.ClosingPrice,
            //        Ticker = ticker.Replace(".TO", ""),
            //        High = point.HighestPrice,
            //        Low = point.LowestPrice,
            //        Price = point.ClosingPrice
            //    };
            //    stockData.Add(s);
            //}
            return stockData;
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
        public static async Task InitializeLocalWorkspace()
        {
            await LoadAllConstituents();

            // now import stock data using recently imported constituents..
            // I don't need to do this (i.e. read from database) since I already have the
            // list of constituents... but.. it feels like this is okay.
            //var constituents = await Core.Db.Constituents.GetConstituents(); // get full list

            //foreach (var constituent in constituents)
            //{
            //    await AddStockDataToDatabase(constituent);
            //}
        
        }
        private static async Task AddStockDataToDatabase(ConstituentInfo constituent)
        {
            var symbol = constituent.Symbol;
            
            if (!BlackList.Contains(symbol))
            {
                try
                {
                    var data = await GetDailyTimeSeriesData(symbol + ".TO");
                    await dts.Insert(data);
                    
                    Debug.WriteLine($"{DateTime.Now} - Imported {symbol}");
                        
                    // we need to wait between each request because we are using the 
                    // free version of alpha vantage... experimenting with how long..
                    await Task.Delay(61000);    
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        

        private static async Task LoadAllConstituents()
        {
            // load the most recent market constituents from tmx site.
            // note: constituents are regularly updated, every 3-4 months?
            //var market = new Core.TMX.Market();
            //var constituents = await market.GetConstituents();

            //// create local database
            //Scripts.CreateDatabase();

            //// check for tickers that don't work with alpha vantage
            //foreach (var constituent in constituents)
            //    if (EnsureRequestIsValid(constituent))
            //        await cts.Insert(constituent.Name, constituent.Symbol); 
        }

        private static bool EnsureRequestIsValid(ConstituentInfo constituent)
        {
            // todo: make an alpha vantage call here to ensure the ticker is 
            // working as expected.. i.e. 200 status code is returned.  

            // In some cases, the calls would fail.
            return true;
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
