using AlphaVantage.Net.Stocks;
using AlphaVantage.Net.Stocks.TimeSeries;
using AngleSharp;
using ConsoleTables;
using Core;
using GranvilleIndicator;
using StocksDB;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMX.Market;

namespace Sandbox
{
    class Program
    {
        private const string apiKey = "6IQSWE3D7UZHLKTB";
        private static readonly AlphaVantageStocksClient client = new AlphaVantageStocksClient(apiKey);
     
        /// <summary>
        /// The main entry point for the program
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static async Task Main(string[] args)
        {
            #region Command Line Utils "DREAM"
            // todo - command line utils to run this tool daily, manually

            // dotnet run program -c

            // -c = complete scan
            // -s --save, args: true/false      :: by default, save is set to false, you have to specify if you want it to save
            // dotnet run program -c --save true 

            // dotnet run program -a
            // -a --alert   :: checks for alerts (returns a list of stocks on the alert list)

            // dotnet run program -ad
            // -ad  --advance-decline   :: gets the advance decline line
            #endregion

            var saveToDatabase = true; // set this to true to save to database
            //await GetTheAdvanceDeclineLine();

            //var granville = new Granville();
            //var points = await granville.GetDailyMarketForecast();
            //foreach (var point in points)
            //{
            //    Console.WriteLine($@"Name: {point.Name},    Value: {point.Value}");
            //}

            //await Utils.DownloadHTMLPage("https://web.tmxmoney.com/marketsca.php", @"C:\src\#Projects\alphaVantageDemo\tmp\html\markets_dump.html");
    
            var db1 = new StocksDB.Constituents();
            var constituents = await db1.GetConstituents(); // get full list
            //var constituents = new List<ConstituentInfo>()    // get single/specified stock
            //{
            //    new ConstituentInfo() { Name = "B2Gold Corp.", Symbol = "BTO"}
            //};

            
            await GetDailyIndiceAverages(saveToDatabase);
            await GetDailyMarketSummary(saveToDatabase);
            await GetDailyStockInfo(constituents, saveToDatabase);

            // Once the data gathering/import is done, we can start to scan the data for alerts
            await CheckForAlerts(constituents);


            #region initial code
            //var ticker = "TSX:ECA";
            //var data = await GetStockDataPoints(ticker);
            //ConsoleTable.From(data).Write();
            //var diff = PrintPercentageDifference(data.First(), data.Last());
            //Console.WriteLine($"{diff}");
            //await Utils.ExportToCSV(@"C:\src\#Projects\alphaVantageDemo\CSV_FILES", "ECA.csv", data);

            //ImportCSV();
            //PrintPercentageDifference(ticker, new DateTime(2019, 5, 1));
            //PrintTimeSeriesData(ticker);
            //DoPatternStuffOverStocks();
            #endregion
        }

        static async Task CheckForAlerts(IList<ConstituentInfo> constituents)
        {
            var db = new DailyStock();  
            foreach (var constituent in constituents)
            {
                Console.WriteLine("Currently checking alerts for " + constituent.Symbol + "    " + constituent.Name);
                List<IStockInfo> stock = await db.GetAllStockDataFor(constituent.Symbol);

                // Set of base extension methods can perform technical operations
                var sma9 = stock.CalculateSMA(9);
                var sma50 = stock.CalculateSMA(50);
                var sma200 = stock.CalculateSMA(200);



                // Scan indicators and see if any alerts are generated
                // implement a type that maintains a list of active alerts - these alerts then
                // need to be actively monitored; also require ability to remove alerts manually and
                // preferably automatically if the alert signals for a particular stock went away
                // .. the alerting system will be run once a day immediately after the daily stock collection
                //    and tickers can be stored for the next day, and the daily monitoring system will kick in and
                //    watch the stocks set as alerts more frequencty to watch for further signals by monitoring intraday activity

            }
        }

        static async Task GetTheAdvanceDeclineLine()
        {
            var adLine = await Granville.GetAdvanceDeclineLine();
            ConsoleTable.From(adLine.OrderByDescending(k => k.Date)).Write();

            #region ConsoleColor
            var color = Console.ForegroundColor;    // get the current console color
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" The purpose of the advance -decline line is to inform you in the broadest sense whether the market as a whole is actually gaining or losing strength");
            Console.ForegroundColor = color;        // restore to previous console color
            #endregion
        }

        /// <summary>
        /// Retreives market indices; stores values in database and prints to console
        /// </summary>
        static async Task GetDailyIndiceAverages(bool SaveToDatabase = false)
        {
            var market = new TMX.Market.Market();

            // Get daily summary of market indices
            var indice = await market.GetMarketIndices();

            // Write to console
            ConsoleTable.From(indice).Write();

            if (SaveToDatabase)
            {
                // Insert Index summary info to database
                var indexDb = new StocksDB.IndiceSummary();
                await indexDb.InsertIndiceSummary(indice);
            }
            
        }

        /// <summary>
        /// Retrieves the daily market summary for TSX, TSX Venture, Alpha
        /// </summary>
        static async Task GetDailyMarketSummary(bool saveToDatabase = false)
        {
            var market = new TMX.Market.Market();

            // Get daily market summary
            var summary = await market.GetMarketSummary();

            // Write to console
            ConsoleTable.From(summary).Write();

            //var tsx = summary.Single(i => i.Name == "Toronto Stock Exchange");   // throws if empty/not found, throws if duplicate exists
            //if (tsx == null) throw new NullReferenceException("Market Summary");
            if (saveToDatabase)
            {
                var db = new StocksDB.MarketSummary();
                await db.InsertMarketSummary(summary);
            }
        }



        static List<IStockInfo> dailyStats = new List<IStockInfo>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="constituents"></param>
        /// <returns></returns>
        private static async Task GetDailyStockInfo(IList<StocksDB.ConstituentInfo> constituents, bool saveToDatabase = false)
        {
            // async patterns
            // https://markheath.net/post/constraining-concurrent-threads-csharp


            //var dailyStats = new List<TMX.Market.StockInfo>();
            //var tmx = new TMX.Market.Stocks();
            //Console.WriteLine("Beginning data collection...");
            //var maxThreads = 10;
            //Console.WriteLine("Max number of concurrent threads: " + maxThreads);
            //var q = new ConcurrentQueue<ConstituentInfo>(constituents);
            //var tasks = new List<Task>();
            //for (int n = 0; n < maxThreads; n++)
            //{
            //    tasks.Add(Task.Run(async () =>
            //    {
            //        while (q.TryDequeue(out ConstituentInfo constituent))
            //        {
            //            var stockInfo = await tmx.RequestTickerInfo(constituent.Symbol, constituent.Name);
            //            dailyStats.Add(stockInfo);
            //        }
            //    }));
            //}
            //await Task.WhenAll(tasks);

            #region attempt 2

            //var count = constituents.Count;
            ////var stock = new TMX.Market.Stocks();


            //Console.WriteLine("Gathering tasks...");
            //var tasks = new Task<TMX.Market.StockInfo>[count];
            //for (int i = 0; i < count; i++) {
            //    tasks[i] = TMX.Market.Stocks.RequestTickerInfo(constituents[i].Symbol, constituents[i].Name);
            //}

            ////Console.WriteLine("Waiting for 15 seconds..");
            ////await Task.Delay(15000);

            //var processingTasks = tasks.Select(async t =>
            //{
            //    var success = false;
            //    var retries = 0;
            //    while (!success && retries < 3)
            //    {
            //        StockInfo info;
            //        try
            //        {
            //            info = await t;
            //            dailyStats.Add(info);
            //            success = true;
            //            await Task.Delay(200);
            //        }
            //        catch (Exception ex)
            //        {
            //            Console.WriteLine(ex.Message + "retry #" + retries);
            //            retries++;
            //        }


            //    }

            //}).ToArray();


            //Console.WriteLine("Starting to process tasks");
            //await Task.WhenAll(processingTasks);

            #endregion

            #region attempt 1
            //////////////////////////////////////////////////////////////////

            // Get a list of all the stocks from database and randomely send
            // requests for ticker information for each stock over the duration of
            // some time period... say 10 min ?
            var retries = 0;
            var stock = new TMX.Market.Stocks();
            for (int i = 0; i < constituents.Count; i++)
            {
                try
                {
                    var s = await stock.RequestTickerInfo(constituents[i].Symbol, constituents[i].Name);
                    dailyStats.Add(s);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception occured while requesting " + constituents[i].Name + ":" + constituents[i].Symbol);
                    Console.WriteLine(ex.Message);
                    if (retries < 2)
                    {
                        i--;
                        retries++;
                        Console.WriteLine("Retry attempt: " + retries);
                    }
                    else
                    {
                        retries = 0;
                    }
                }
            }

            #endregion

            ConsoleTable.From(dailyStats).Write();

            if (saveToDatabase)
            {
                Console.WriteLine("Saving daily stock info to database....");
                var db = new StocksDB.DailyStock();
                await db.InsertDailyStockList(dailyStats);
            }

        }

        #region AlphaVantage stuff...

        /// <summary>
        /// Requests the daily time series for a given stock which includes: date, volume, open, close, high, low
        /// </summary>
        /// <param name="ticker"></param>
        /// <param name="size"></param>
        /// <returns>A collection of <see cref="StockDataPoint"/></returns>
        private static async Task<ICollection<StockDataPoint>> GetStockDataPoints(string ticker, TimeSeriesSize size = TimeSeriesSize.Compact)
            => (await client.RequestDailyTimeSeriesAsync($"{ticker}", size)).DataPoints;

        /// <summary>
        /// Writes time series data to console for a specified ticker
        /// </summary>
        /// <param name="ticker">The ticker for the stock</param>
        /// <param name="size">Compact or full</param>
        private static void PrintTimeSeriesData(string ticker, TimeSeriesSize size = TimeSeriesSize.Compact)
        {
            // Request the time series data (compact returns 100 records)
            var stock = client.RequestDailyTimeSeriesAsync($"{ticker}", size).Result;
            var dataPoints = stock.DataPoints;

            // Column headings
            var c = new[] { "Time", "Volume", "Opening", "Closing", "High", "Low", "% Diff" };

            // Space between each column
            const int a = -14;

            // Write the headings using the specified alignment
            Console.WriteLine($"{c[0],a}|{c[1],a}|{c[2],a}|{c[3],a}|{c[4],a}|{c[5],a}|{c[6],a}");
            Console.WriteLine("-------------------------------------------------------------------------------------------------");

            // Set up some variables we need
            StockDataPoint previousPoint = null;
            List<string> dataList = new List<string>();

            // Initially we iterate over the list in reverse order to easily calculate percentage difference 
            // using the previous point.  
            foreach (var p in dataPoints.Reverse())
            {
                // Calculate percentage difference
                var diff = (previousPoint == null) ? "" : PrintPercentageDifference(p, previousPoint);
                // Add formatted line item to list
                dataList.Add($"{p.Time.ToShortDateString(),a}|{p.Volume,a}|{p.OpeningPrice,a}|{p.ClosingPrice,a}|{p.HighestPrice,a}|{p.LowestPrice,a}|{diff}");
                // Set the previous point 
                previousPoint = p;
            }

            // Reverse the list once more, so we are back to iterating from newest to oldest
            foreach (var item in Enumerable.Reverse(dataList))
                Console.WriteLine(item);

            // Let user read the output before continuing
            Console.WriteLine("Press any key to continuee...");
            Console.ReadLine();
        }

        

        /// <summary>
        /// Calculates the percentage difference between two <see cref="StockDataPoint"/> using the closing price
        /// </summary>
        /// <param name="v1">First point</param>
        /// <param name="v2">Second point</param>
        /// <returns>A string formatted as a percentage</returns>
        private static string PrintPercentageDifference(StockDataPoint v1, StockDataPoint v2)
            => $"{GetDifference(v1.ClosingPrice, v2.ClosingPrice).ToString("P", CultureInfo.InvariantCulture)}";

        private static void PrintPercentageDifference(string ticker, DateTime startDate, TimeSeriesSize size = TimeSeriesSize.Compact)
        {
            var stock = client.RequestDailyTimeSeriesAsync($"{ticker}", size).Result;
            var data = stock.DataPoints as List<StockDataPoint>;    // convert to a list 

            // Select two points (v1 and v2)
            var v1 = data.Where(x => x.Time >= DateTime.Today).First();
            var v2 = data.Where(x => x.Time == startDate).First();

            // Calculate the percentage difference between the two points
            var diff = GetDifference(v1.ClosingPrice, v2.ClosingPrice);

            // Print info
            Console.WriteLine($"Price at {v1.Time.ToShortDateString()}: {v1.ClosingPrice}");
            Console.WriteLine($"Price at {v2.Time.ToShortDateString()}: {v2.ClosingPrice}");
            Console.WriteLine($"Percentage Difference: {diff.ToString("P", CultureInfo.InvariantCulture)}");
        }

        public static decimal GetDifference(decimal v1, decimal v2)
        {
            /*   Formula
             *   
             *   |V1 - V2|
             *   ---------
             * [(V1 + V2) / 2] 
             * 
             */
            var delta = v1 - v2;
            var sum = v1 + v2;
            sum /= 2;
            var diff = delta / sum;
            return diff;
        }
        #endregion

        public static void ImportCSV(string path)
        {
            //string path = @"C:\Users\joseph.mawer.IDEA\Downloads\TSX.txt";
            Utils.Import_CSV_Symbols(path).Wait();
            Console.WriteLine("Import successful");
            Console.ReadLine();
        }

    }



    public static class Utils
    {
        public static async Task DownloadHTMLPage(string url, string pathToSaveFile)
        {
            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(url);  //https://web.tmxmoney.com/marketsca.php
            File.WriteAllText(pathToSaveFile, document.ToHtml());
        }
        public static async Task Import_CSV_Constituents(string pathToCSV)
        {
            // just importing some tsx data.. so some values will be hardcoded here ... don't need this function
            // to be general purpose yet


            var lines = File.ReadAllLines(pathToCSV);
            StocksDB.Constituents _symbol = new StocksDB.Constituents();
            foreach (var line in lines) // skip the header
            {
                var info = line.Split("\t");    
                await _symbol.InsertConstituent(info[0], info[1]);
            }
        }
        public static async Task Import_CSV_Symbols(string pathToCSV)
        {
            // just importing some tsx data.. so some values will be hardcoded here ... don't need this function
            // to be general purpose yet

            
            var lines = File.ReadAllLines(pathToCSV);
            StocksDB.Symbols _symbol = new StocksDB.Symbols();
            foreach (var line in lines.Skip(1)) // skip the header
            {
                var info = line.Split("\t");    //imported data from http://www.eoddata.com/, which was a tab separated text file
                await _symbol.InsertSymbol(info[0], info[1], "TSX");
            }

        }

        public static async Task ExportToCSV(string path, string name, ICollection<StockDataPoint> data)
        {
            var sb = new StringBuilder();
            foreach (var p in data) {
                sb.Append($"{p.Time.ToShortDateString()},{p.Volume},{p.OpeningPrice},{p.ClosingPrice},{p.HighestPrice},{p.LowestPrice}{Environment.NewLine}");
            }
            File.WriteAllText(Path.Combine(path, name), sb.ToString());
        }
    }
}
