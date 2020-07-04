using AlphaVantage.Net.Stocks;
using AlphaVantage.Net.Stocks.TimeSeries;
using ConsoleTables;
using Core;
using Core.Db;
using Core.Indicators;
using Core.Indicators.Models;
using Core.Indicators.PricePatterns;
using Core.Math;
using Core.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;


namespace Sandbox
{
    partial class Program
    {
        // initialize members
        static List<List<IStockInfo>> StockData = new List<List<IStockInfo>>();
        private static List<ConstituentInfo> Constituents;
        private static DailyTimeSeries TimeSeries = new DailyTimeSeries();
        private static readonly AlphaVantageStocksClient client = new AlphaVantageStocksClient(Constants.apiKey);
        private static DateTime Today = DateTime.Today;

        /// <summary>
        /// The main entry point for the program
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static async Task Main(string[] args)
        {

            //Core.TMX.Stocks tmx = new Core.TMX.Stocks();
            //var cve = await tmx.RequestTickerInfo("CVE");
            //await DailyPrice.DailyPriceMain(null);

            // this shows how to download historical stock data into a local sqlite database which
            // can then be used for further analysis with the library. This would typically be the first
            // thing you run when you initially try to set up this library.
            //var request = Core.Utilities.Import.TimeSeries.Daily;
            //await Core.Utilities.Import.Import.ImportStockData(request);



            // load all constituents into memory
            Constituents = await Core.Db.Constituents.GetConstituents();


            // searching all stocks for the head and shoulders pattern using various input
            // parameters to define the size and sample frequency of how often we look for the pattern
            //foreach (var constituent in Constituents)
            //{
            //    Console.WriteLine($"{constituent.Symbol} : Searching for {constituent.Name}...");
            //    var stocks = await TimeSeries.GetAllStockDataFor(constituent.Symbol);

            //    Console.WriteLine("Searching for pattern: Head And Shoulders");
            //    var result = stocks.RunWindowBasedSampling(SearchPatterns.HeadAndShoulders, windowSize: 7);
            //    if (result != null)
            //    {
            //        ConsoleTable.From(result).Write();
            //    }
 
            //    Console.ReadLine();
            //    Console.Clear();
            //}
         

            // Import all data from db into memory (yea.. that's probably a lot) using the static
            // helper method in the TimeSeries (db) class
            StockData = await DailyTimeSeries.GetAllStocks();

            // shows how to calculate percentage gains table on the entire stock data collection
            //StockData.GetPercentageGain(new DateRange(Today, daysAgo: 30), new PriceRange(low: 1, high: 10), take: 3, printTable: true);

            // shows how to calculate percentage gains for individual stocks
            foreach (var stock in StockData)
            {
                if (stock.Count > 0)
                {
                    Console.WriteLine($"looking for support line for: {stock[0].Ticker}");
                    
       
                    for (int i = 1; i < 100; i++)
                    {
                        var startingDate = Utils.GetBusinessDay(Today.AddDays(-i));
                        for (int endDate = 2; endDate < 100; endDate++)
                        {
                            // checks if a support level exists between two points by forming a straight line
                            // between the two points and checking if it forms a support
                            var support = stock.GetSupportLevel(startingDate, endDate);
                            if (support.SupportLineFound)
                                ConsoleTable.From(support.DataPoint).Write();
                        }
                    }
                    

                    

                    //Console.WriteLine($"Support level at {x.Support} between {x.Range.StartDate.ToShortDateString()} and {x.Range.EndDate.ToShortDateString()});
                    // percentage 
                    Console.WriteLine($"Collecting percentage gains for: {stock[0].Ticker}");
                    stock.GetPercentageGain(new DateRange(Today, daysAgo: 60), printTable: true);
                    stock.GetPercentageGain(new DateRange(Today, daysAgo: 30), printTable: true);
                    stock.GetPercentageGain(new DateRange(Today, daysAgo: 10), printTable: true);
                    stock.GetPercentageGain(new DateRange(Today, daysAgo: 5),  printTable: true);
                }
            }

            // shows how to print/get a list of the top X movers in terms of price
            StockData.GetTopPriceMovers(take: 10, printTable: true);

           

            // Get top 10 movers by volume; note the default value is 10, pass an integer to specify more constituents
            await PrintTopMoversByVolume();
            //CalculateTop10MoversInPrice();
            await GetTheAdvanceDeclineLine();

            #region table for showing EMA data
            //var path = @"C:\src\#Projects\TraderVI\tmp\CSV_FILES\ema-are.csv";
            //try
            //{
            //    var db = new Db.DailyStock();
            //    //var high52 = await db.Get52WeekHigh("ARE");
            //    List<IStockInfo> stock = await db.GetAllStockDataFor("ARE");
            //    var hmm = stock.Calculate52WeekHigh();


            //    var ema9 = stock.CalculateEMA(9);
            //    var ema22 = stock.CalculateEMA(22);

            //    var joined = from f in stock
            //                 join r in ema22
            //                 on f.TimeOfRequest equals r.Date.ToShortDateString()
            //                 into joinResult
            //                 from r in joinResult.DefaultIfEmpty()
            //                 select new
            //                 {
            //                     Date = f.TimeOfRequest,
            //                     Symbol = f.Ticker,
            //                     f.Close,
            //                     EMA = r.Price
            //                 };

            //    var joined2 = from f in joined
            //                  join r in ema9
            //                  on f.Date equals r.Date.ToShortDateString()
            //                  into joinResult
            //                  from r in joinResult.DefaultIfEmpty()
            //                  select new
            //                  {
            //                      f.Date,
            //                      f.Symbol,
            //                      f.Close,
            //                      EMA22 = f.EMA,
            //                      EMA9 = r.Price
            //                  };

            //    ConsoleTable.From(joined2).Write();
            //    //foreach (var value in joined2)
            //    //    File.AppendAllText(path, $"{value.Date},{value.Symbol},{value.Close},{value.EMA22},{value.EMA9}{Environment.NewLine}");

            //}
            //catch (Exception ex)
            //{
            //    var msg = ex.Message;
            //}
            #endregion

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




            //var granville = new Granville();
            //var points = await granville.GetDailyMarketForecast();
            //foreach (var point in points)
            //{
            //    Console.WriteLine($@"Name: {point.Name},    Value: {point.Value}");
            //}

            //await Utils.DownloadHTMLPage("https://web.tmxmoney.com/marketsca.php", @"C:\src\#Projects\alphaVantageDemo\tmp\html\markets_dump.html");







            // Once the data gathering/import is done, we can start to scan the data for alerts
            //await CheckForAlerts(constituents);


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



        

        /// <summary>
        /// Gets the top volume movers
        /// </summary>
        /// <param name="count">The number of constituents to display</param>
        /// <returns>A task</returns>
        static async Task<List<IStockInfo>> PrintTopMoversByVolume(int count = 10)
        {
            var db = new Core.Db.DailyTimeSeries();

            // get top x in volume for today
            var topMovers = await db.GetTopMoversByVolume(count);
            ConsoleTable.From(topMovers).Write();

            return topMovers;
        }
     
        static void WriteToConsole(string msg, ConsoleColor? color = null)
        {
            var original = Console.ForegroundColor;    // get the current console color
            Console.ForegroundColor = color ?? original;
            Console.WriteLine(msg);
            Console.ForegroundColor = original;
        }

        static async Task CheckForAlerts(IList<Core.Db.ConstituentInfo> constituents)
        {
            var db = new Core.Db.DailyTimeSeries();  
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

        /// <summary>
        /// The purpose of the advance -decline line is to inform you in the broadest sense whether the market as a whole is actually gaining or losing strength
        /// </summary>
        /// <returns></returns>
        static async Task GetTheAdvanceDeclineLine()
        {
            var adLine = await Granville.GetAdvanceDeclineLine();
            ConsoleTable.From(adLine.OrderByDescending(orderBy => orderBy.Date)).Write(); 
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
            => $"{MathHelpers.GetDifference(v1.ClosingPrice, v2.ClosingPrice).ToString("P", CultureInfo.InvariantCulture)}";

        private static void PrintPercentageDifference(string ticker, DateTime startDate, TimeSeriesSize size = TimeSeriesSize.Compact)
        {
            var stock = client.RequestDailyTimeSeriesAsync($"{ticker}", size).Result;
            var data = stock.DataPoints as List<StockDataPoint>;    // convert to a list 

            // Select two points (v1 and v2)
            var v1 = data.Where(x => x.Time >= DateTime.Today).First();
            var v2 = data.Where(x => x.Time == startDate).First();

            // Calculate the percentage difference between the two points
            var diff = MathHelpers.GetDifference(v1.ClosingPrice, v2.ClosingPrice);

            // Print info
            Console.WriteLine($"Price at {v1.Time.ToShortDateString()}: {v1.ClosingPrice}");
            Console.WriteLine($"Price at {v2.Time.ToShortDateString()}: {v2.ClosingPrice}");
            Console.WriteLine($"Percentage Difference: {diff.ToString("P", CultureInfo.InvariantCulture)}");
        }

        
        #endregion

        //public static void ImportCSV(string path)
        //{
        //    //string path = @"C:\Users\joseph.mawer.IDEA\Downloads\TSX.txt";
        //    Utils.Import_CSV_Symbols(path).Wait();
        //    Console.WriteLine("Import successful");
        //    Console.ReadLine();
        //}
    }
}
