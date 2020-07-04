using AlphaVantage.Net.Stocks;
using AlphaVantage.Net.Stocks.TimeSeries;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Utilities.Import
{
    public enum TimeSeries
    {
        Daily,
        Intraday
    }
    public class Import
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
            var blackList = new List<string>();
            var path = @"C:\Users\joseph.mawer.IDEA\Desktop\blacklist.txt";
            var blackListeSymbols = File.ReadAllLines(path);
            blackList.AddRange(blackListeSymbols);
            return blackList;
        }

        /// <summary>
        /// Use this method to download stock data using alpha vantage API and import it into the sqlite database 
        /// this takes quite a while because we have the 'free' version / api key, so we have to wait 20+ seconds 
        /// between each request. It will only import the data to sqlite if there is not any data already existing 
        /// in the database. So consider using this the first time you want to download data and initialize db with data.
        /// </summary>
        public static async Task ImportStockData(TimeSeries timeSeries)
        {
            var client = new AlphaVantageStocksClient(Constants.apiKey);

            var db = new Core.Db.DailyTimeSeries();
            var constituents = await Db.Constituents.GetConstituents(); // get full list

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
    }
}
