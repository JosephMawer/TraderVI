using AlphaVantage.Net.Stocks;
using AlphaVantage.Net.Stocks.TimeSeries;
using ConsoleTables;
using Core;
using Core.Math;
using Core.Utilities;
using GranvilleIndicator;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;


namespace Sandbox
{
    public static class DateTimeExtensions
    {
        public static string Something(this DateTime date, int PreviousDays)
        {
            return DateTime.Today.AddDays(-15).ToShortDateString();
        }
    }
    class Program
    {
        // Global/Static list of stock data used for performaning analysis on in memory
        static List<List<IStockInfo>> StockData = new List<List<IStockInfo>>();
        
        const string apiKey = "6IQSWE3D7UZHLKTB";
        static readonly AlphaVantageStocksClient client = new AlphaVantageStocksClient(apiKey);

        /// <summary>
        /// The main entry point for the program
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static async Task Main(string[] args)
        {
            // Import all data from db into memory...  todo - see how much memory this actually uses.
            StockData = await Core.Db.DailyTimeSeries.GetListOfStockData();

            
            //var today = DateTime.Today.ToShortDateString();
            //GetDifference(today, Utils.GetPastDate(-60));
            //GetDifference(today, Utils.GetPastDate(-40));
            //GetDifference(today, Utils.GetPastDate(-20));
            //GetDifference(today, Utils.GetPastDate(-12));
            //GetDifference(today, Utils.GetPastDate(-8));
            //GetDifference(today, Utils.GetPastDate(-4));



            // Get top 10 movers by volume; note the default value is 10, pass an integer to specify more constituents
            await PrintTopMoversByVolume();
            CalculateTop10MoversInPrice();
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

       


        private struct DifferenceTable
        {
            public string StartDate { get; set; }
            public string EndDate { get; set; }
            public string Symbol { get; set; }
            public decimal Close { get; set; }
            public decimal Max { get; set; }
            public string Difference { get; set; }
        }
        /// <summary>
        /// Get's the difference between two dates
        /// </summary>
        /// <param name="StartDate"></param>
        /// <param name="EndDate"></param>
        static void GetDifference(string StartDate, string EndDate)
        {
            List<DifferenceTable> lst = new List<DifferenceTable>();
            var days = (DateTime.Parse(StartDate).Subtract(DateTime.Parse(EndDate))).TotalDays;
            foreach (var stock in StockData)
            {
                if (stock.Count > days)
                {
                    var diff = stock.CalculateDifference(StartDate, EndDate);
                    var max = stock.Where(x => DateTime.Parse(x.TimeOfRequest) >= DateTime.Parse(EndDate) &&
                                               DateTime.Parse(x.TimeOfRequest) <= DateTime.Parse(StartDate))
                                   .Max(f => f.Close);
                    var joined = (from f in stock
                                 select new DifferenceTable
                                 {
                                     StartDate = EndDate,
                                     EndDate = StartDate,
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
            var topLst = lst.Where(p => p.Close > 1 && p.Close < 15)
                            .OrderByDescending(x => decimal.Parse(x.Difference.Replace("%","")))
                            .Take(10);

            Console.WriteLine($"stock data for the past {days} days");
            ConsoleTable.From(topLst).Write();
        }


        struct TopPercentage
        {
            public string Ticker { get; set; }
            public string Name { get; set; }
            public decimal Close { get; set; }
            public decimal PriceIncrease { get; set; }
        }
        static IEnumerable<TopPercentage> CalculateTop10MoversInPrice()
        {
            List<TopPercentage> table = new List<TopPercentage>();
            WriteToConsole(" Top 10 movers in price", ConsoleColor.Yellow);
            foreach (var stock in StockData)
            {
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
            var topTen = table.OrderByDescending(f => f.PriceIncrease).Take(30);
            ConsoleTable.From(topTen).Write();
            return topTen;
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
