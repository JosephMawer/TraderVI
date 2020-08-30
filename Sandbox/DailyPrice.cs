using Abot2.Crawler;
using Abot2.Poco;
using AlphaVantage.Net.Stocks;
using AlphaVantage.Net.Stocks.TimeSeries;
using Core;
using Core.Db;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using HtmlAgilityPack;
using AngleSharp.Dom;
using ConsoleTables;

namespace Sandbox
{
    public class DailyPrice
    {
        


        // Abot web crawler
        private static IWebCrawler crawler;

        // alpha vantage API key

        private static readonly AlphaVantageStocksClient client = new AlphaVantageStocksClient(Constants.apiKey);
        private static bool CrawlComplete = false;
        static List<SymbolInfo> tickers = new List<SymbolInfo>();
        public static async Task DailyPriceMain(string[] args)
        {
            await GetDailyPercentGainers();


            string Ticker = "";
            StockTimeSeries stockTimeSeries = await client.RequestIntradayTimeSeriesAsync(Ticker, IntradayInterval.OneMin, TimeSeriesSize.Full);
            var c = await client.RequestBatchQuotesAsync(new[] { Ticker });

            await BeginAnalysis();
            //await ImportDailyTimeSeries();





            //log4net.Config.XmlConfigurator.Configure();
            //Console.Write("Enter ticker: ");
            //var ticker = Console.ReadLine();
            //var path = "https://web.tmxmoney.com/quote.php?qm_symbol=";
            //Uri uriToCrawl = new Uri($"{path}{ticker}");


            //while (true)
            //{
            //    crawler = new PoliteWebCrawler();

            //    //Subscribe to any of these asynchronous events, there are also sychronous versions of each.
            //    //This is where you process data about specific events of the crawl
            //    crawler.PageCrawlStarting += crawler_ProcessPageCrawlStarting;
            //    crawler.PageCrawlCompleted += crawler_ProcessPageCrawlCompleted;
            //    crawler.PageCrawlDisallowed += crawler_PageCrawlDisallowed;
            //    crawler.PageLinksCrawlDisallowed += crawler_PageLinksCrawlDisallowed;
            //    CrawlResult result = crawler.Crawl(uriToCrawl);
            //    Thread.Sleep(10000);    // wait 1 minute before next crawl
            //    if (CrawlComplete) CrawlComplete = false;
            //}




            //var stocks = new StockInfo(Table.TimeSeries_Intraday);
            //var stockList = await stocks.GetListOfStockInfo("ACIU", "[Time] >= '2019-03-22'");
            //stockList.Reverse();
            //Console.WriteLine("TIME         HLC     % INC   Volume");
            //foreach (var stock in stockList)
            //{
            //    await StockStreamer(stock);
            //    await Task.Delay(59000);    // wait one minute
            //}



            //string exchange = "NASDAQ";
            //string ticker = "ACIU";
            //await CollectIntradayTimeSeries(exchange, ticker);
            //await CollectDailyTimeSeries(exchange, ticker);


            // with free plan, i can basically make one request per minute; for a max of 500 requests, total day is 390 min (6.5 hours) 9am,
            //while (true)
            //{

            //    await GetCurrentStockPrice("ACIU");
            //    await Task.Delay(12000);
            //}
            // Load all symbols
            //var sym = new Db.Symbols();
            //tickers = await sym.GetSymbolsList();

            //await CollectMonthlyAdjustedTimeSeries();
            Console.WriteLine("Program completed");
            Console.ReadLine();
        }

        #region Technical Analysis
        private static async Task BeginAnalysis()
        {
            string[] symbols = { "G", "NVA", "ROXG", "ELD", "ALA", "FM", "EFN", "IMG" };
            // First get all the tickers on TSX we wish to analyse
            var stocks = await GetStockData();
            for (int i = 0; i < symbols.Length - 1; i++)
            {
                var lst = stocks.Where(stock => stock.Ticker == symbols[i]).OrderBy(s => s.Time).ToList();
                CalculateTotalPercentageGain(lst);
                var thirty = lst[lst.Count - 1].Time.Subtract(new TimeSpan(30, 0, 0, 0, 0));
                CalculateTotalPercentageGain(lst.Where(si => si.Time >= thirty).ToList());
                var sixty = lst[lst.Count - 1].Time.Subtract(new TimeSpan(60, 0, 0, 0, 0));
                CalculateTotalPercentageGain(lst.Where(si => si.Time >= sixty).ToList());
                var ninety = lst[lst.Count - 1].Time.Subtract(new TimeSpan(90, 0, 0, 0, 0));
                CalculateTotalPercentageGain(lst.Where(si => si.Time >= ninety).ToList());
            }
        }

        private static void CalculateTotalPercentageGain(List<Core.Db.StockInfo> lst)
        {
            var first = lst[lst.Count - 1];
            var last = lst[0];

            var timeSpan = first.Time.Subtract(last.Time);
            var percentIncrease = (first.Close - last.Close) / first.Close;

            Console.WriteLine($"{first.Ticker} has increased by {percentIncrease:P2} over {timeSpan.TotalDays} days");

        }

        /// <summary>
        /// Gets a list of tickers 
        /// </summary>
        /// <returns></returns>
        private static async Task<List<Core.Db.StockInfo>> GetStockData()
        {
            var stockDb = new Core.Db.StockInfo(Core.Db.Table.TimeSeries_Daily);
            var symbols = new Symbols();
            var SymbolList = await symbols.GetSymbolsList("where [Exchange]='TSX'");
            List<Core.Db.StockInfo> stocks = new List<Core.Db.StockInfo>();
            foreach (var symbol in SymbolList)
            {
                var stockList = await stockDb.GetListOfStockInfo(symbol.Symbol, Table.TimeSeries_Daily);
                foreach (var stock in stockList)
                {
                    stocks.Add(stock);
                }
            }
            return stocks;
        }


        #endregion


        private static async Task GetDailyPercentGainers()
        {
            var uri = "https://web.tmxmoney.com/marketsca.php?qm_page=99935";
            Uri uriToCrawl = new Uri(uri);
            var config = new CrawlConfiguration
            {
                MaxPagesToCrawl = 1, //Only crawl 10 pages
                MinCrawlDelayPerDomainMilliSeconds = 3000 //Wait this many millisecs between requests
            };
            crawler = new PoliteWebCrawler(config);
            crawler.PageCrawlCompleted += crawler_dailyPercentGainers;
            var result = await crawler.CrawlAsync(uriToCrawl);
        }


        /// <summary>
        /// Gets real-time info of daily percent gainers
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static async void crawler_dailyPercentGainers(object sender, PageCrawlCompletedArgs e)
        {
            //Console.WriteLine(e.CrawledPage.Content.Text);
            var h = e.CrawledPage.AngleSharpHtmlDocument;
            var tmx = new Core.TMX.Market();
            //var summary = tmx.GetMarketSummary(h);
            //var indices = tmx.GetMarketIndices(h);
            //ConsoleTable.From(summary).Write();
            //ConsoleTable.From(indices).Write();
          


            var content = h.GetElementsByClassName("row");
            var marketSummary = content.Where(x => x.InnerHtml.Contains("Market Summary"));
            foreach (var s in marketSummary)
            {
                if (s.TextContent.Contains("Canadian Market Summary")) continue;
                Console.WriteLine(s.TextContent);
            }
            




            //HtmlAgilityPack.HtmlDocument doc = new HtmlDocument();
            //var html = e.CrawledPage.Content.Text;
            //doc.LoadHtml(html);
            //Console.WriteLine(doc.ParsedText);
            //var quoteDetails = doc.DocumentNode.Descendants("table")
            //    .Where(node => node.GetAttributeValue("id", "")
            //    .Equals("qmmt_scalingMarketStatsTable")).ToList();


            //foreach (HtmlNode table in e.CrawledPage.HtmlDocument.DocumentNode.SelectNodes(@"/html/body/div[2]/div[3]/div/div[5]/table[2]/tbody/tr[2]/td/table/tbody/tr/td/table/tbody"))
            //{
            //    Console.WriteLine("Found: " + table.Id);
            //    foreach (HtmlNode row in table.SelectNodes("tr"))
            //    {
            //        Console.WriteLine("row");
            //        foreach (HtmlNode cell in row.SelectNodes("th|td"))
            //        {
            //            Console.WriteLine("cell: " + cell.InnerText);
            //        }
            //    }
            //}
            //List<List<string>> table = doc.DocumentNode.SelectNodes("/html/body/div[2]/div[3]/div/div[5]/table[2]/tbody/tr[2]/td/table/tbody/tr/td/table/tbody")
            //.Descendants("tr")
            ////.Skip(1)
            //.Where(tr => tr.Elements("td").Count() > 1)
            //.Select(tr => tr.Elements("td").Select(td => td.InnerText.Trim()).ToList())
            //.ToList();

            //foreach (var column in table)
            //{
            //    if (column.Count > 1) Console.Write($"{column[0],-12}");
            //    if (column.Count > 2) Console.Write($"{column[1],-12}");
            //    if (column.Count > 3) Console.Write($"{column[2],-12}");
            //    if (column.Count > 4) Console.Write($"{column[3],-12}");
            //    Console.WriteLine();
            //}

            //Console.ReadLine();
            //var stockInfo = new Core.Models.StockQuote();
            //foreach (var detail in quoteDetails)
            //{
            //    var quoteName = detail.SelectNodes("//*[contains(@class,'quote-name')]");
            //    var name = Regex.Replace(quoteName[0].InnerText, @"\s+", string.Empty);

            //}
        }

        private static void crawler_PageLinksCrawlDisallowed(object sender, PageLinksCrawlDisallowedArgs e)
        {
            throw new NotImplementedException();
        }

        private static void crawler_PageCrawlDisallowed(object sender, PageCrawlDisallowedArgs e)
        {
            throw new NotImplementedException();
        }

        private static async void crawler_ProcessPageCrawlCompleted(object sender, PageCrawlCompletedArgs e)
        {
            throw new NotImplementedException("fix issues with abot");
            //if (CrawlComplete)
            //    return;

            //CrawlComplete = true;

            //var quoteDetails = e.CrawledPage.HtmlDocument.DocumentNode.Descendants("Div")
            //    .Where(node => node.GetAttributeValue("class", "")
            //    .Equals("quote-details")).ToList();

            //var stockInfo = new Core.Db.StockInfo();
            //foreach (var detail in quoteDetails)
            //{
            //    var quoteName = detail.SelectNodes("//*[contains(@class,'quote-name')]");
            //    var name = Regex.Replace(quoteName[0].InnerText, @"\s+", string.Empty);

            //    var tickerNode = detail.SelectNodes("//*[contains(@class,'quote-ticker tickerLarge')]");
            //    var ticker = Regex.Replace(tickerNode[0].InnerText, @"\s+", string.Empty);
            //    stockInfo.Ticker = ticker;

            //    var currentPrice = e.CrawledPage.HtmlDocument.DocumentNode.SelectNodes("//*[contains(@class,'quote-price priceLarge')]");
            //    var price = Regex.Replace(currentPrice[0].InnerText, @"\s+", string.Empty);
            //    stockInfo.Close = decimal.Parse(price.Replace("$", ""));

            //    var currentVolume = e.CrawledPage.HtmlDocument.DocumentNode.SelectNodes("//*[contains(@class,'quote-volume volumeLarge')]");
            //    var volume = Regex.Replace(currentVolume[0].InnerText, @"\s+", string.Empty);

            //    stockInfo.Volume = long.Parse(volume.Replace("Volume:", "").Replace(",", ""));
            //    stockInfo.Time = DateTime.Now;
            //    await StockStreamer(stockInfo);
            //}
        }

        private static void crawler_ProcessPageCrawlStarting(object sender, PageCrawlStartingArgs e)
        {

        }

        static decimal highestPrice = 0;
        static decimal trailingStop = 0;
        static decimal minLoss = 0.01m; // 0.4%
        static Core.Db.StockInfo prevStock = null;
        public static async Task StockStreamer(Core.Db.StockInfo stock)
        {
            var currentStockPrice = stock.Close;

            // Always check to see if we hit a new high so we can readjust the trailing stop loss limit
            if (currentStockPrice > highestPrice)
            {
                highestPrice = currentStockPrice;
                // calculate percentage increase and subtract from the trailing loss to create a moving trailing loss
                //var percentGain = (currentStockPrice - prevStock.Close) / prevStock.Close;
                //var minLossIncrease = minLoss * percentGain;
                //minLoss = minLoss - minLossIncrease;
                //var x = currentStockPrice * minLoss;
                //trailingStop = trailingStop + x;

                // adjust the trailing stop loss
                trailingStop = currentStockPrice - (currentStockPrice * minLoss);


            }


            //if (currentStockPrice <= trailingStop)
            //{
            //    WriteToConsole($"MINIMUM EXIT POINT HAS BEEN REACHED, SELL NOW{Environment.NewLine}", ConsoleColor.Red);
            //    Console.Beep(1000, 1000);
            //    await Task.Delay(1000);
            //}

            if (prevStock == null) prevStock = stock;
            var LastPrice = prevStock.Close;  // (prevStock.High + prevStock.Low + prevStock.Close) / 3;
            var increase = currentStockPrice - LastPrice;
            var pi = increase / LastPrice;

            WriteToConsole($"{stock.Time.ToShortTimeString(),-12}");
            WriteToConsole($"{currentStockPrice,-12:C}");



            ConsoleColor color;
            if (pi < 0)
            {
                color = ConsoleColor.Red;
            }
            else if (pi > 0)
            {
                color = ConsoleColor.Green;
            }
            else
            {
                color = ConsoleColor.White;
            }
            WriteToConsole($"{pi,-12:P2}", color);
            WriteToConsole($"{stock.Volume,-12}");
            WriteToConsole($"Sell at {trailingStop:C}");
            WriteToConsole(Environment.NewLine);

            // Previous stock is always one element behind
            prevStock = stock;
        }
        public static async Task StockWatcher(List<Core.Db.StockInfo> stockList)
        {
            decimal highestPrice = 0;
            decimal traillingExit = 0;
            decimal newProfit = 0;
            Core.Db.StockInfo prevStock = null;
            stockList.Reverse();
            int counter = 0;
            foreach (var stock in stockList)
            {
                var currentHLC = stock.Close;//(stock.High + stock.Low + stock.Close) / 3;



                // Always check to see if we hit a new high so we can readjust the trailing exit
                if (currentHLC > highestPrice)
                {
                    highestPrice = currentHLC;
                    // Trailing limit should always be x percent from the highest point
                    var p = highestPrice * 0.04m;
                    traillingExit = highestPrice - p;    // price at which we must sell to avoid losing capital

                    newProfit = highestPrice + p;    // price at which we sell because we're happy to take profit
                }


                if (currentHLC <= traillingExit)
                {
                    WriteToConsole($"MINIMUM EXIT POINT HAS BEEN REACHED, SELL NOW{Environment.NewLine}", ConsoleColor.Red);
                    Console.Beep(1000, 1000);
                    await Task.Delay(1000);
                }

                if (prevStock == null) prevStock = stock;
                var previoushlc = (prevStock.High + prevStock.Low + prevStock.Close) / 3;
                var increase = currentHLC - previoushlc;
                var pi = increase / previoushlc;

                WriteToConsole($"{stock.Time.ToShortTimeString(),-12}");
                WriteToConsole($"{currentHLC,-12:C}");
                ConsoleColor color = (pi < 0) ? ConsoleColor.Red : ConsoleColor.Green;
                WriteToConsole($"{pi,-12:P2}", color);
                WriteToConsole($"{stock.Volume}");
                WriteToConsole(Environment.NewLine);

                // Previous stock is always one element behind
                prevStock = stockList[counter];
                counter++;

                await Task.Delay(58000);
            }
            Console.ReadLine();
        }

        private static async Task StartStream()
        {
            // request batch quote -- which would feed an observable list, i.e. so we get notifications everytime the list is updated
        }

        private static async Task CollectIntradayTimeSeries(string Exchange, string Ticker)
        {
            var stockDb = new Core.Db.StockInfo(Core.Db.Table.TimeSeries_Intraday);
            // retrieve the last timestamp for monthly adjusted and make sure we're not overwriting old data


            Console.WriteLine($"Collecting intraday data for: {Ticker}");
            StockTimeSeries stockTimeSeries = await client.RequestIntradayTimeSeriesAsync(Ticker, IntradayInterval.OneMin, TimeSeriesSize.Full);
            foreach (var p in stockTimeSeries.DataPoints)
            {
                Console.WriteLine($@"{p.Time},{p.Volume},{p.OpeningPrice},{p.ClosingPrice},{p.HighestPrice},{p.LowestPrice}");
                await stockDb.Insert(Exchange, Ticker, p.Time, p.Volume, p.OpeningPrice, p.ClosingPrice, p.HighestPrice, p.LowestPrice);
            }
            await Task.Delay(500);

            Console.WriteLine($"Successfully retrieved daily time series for {Ticker}");
            Console.ReadLine();
        }
        private static async Task CollectDailyTimeSeries(string Exchange, string Ticker)
        {
            var stockDb = new Core.Db.StockInfo(Table.TimeSeries_Daily);
            // retrieve the last timestamp for monthly adjusted and make sure we're not overwriting old data


            Console.WriteLine($"Collecting daily data for: {Ticker}");
            StockTimeSeries stockTimeSeries = await client.RequestDailyTimeSeriesAsync(Ticker, TimeSeriesSize.Full, false);
            foreach (var p in stockTimeSeries.DataPoints)
            {
                Console.WriteLine($@"{p.Time},{p.Volume},{p.OpeningPrice},{p.ClosingPrice},{p.HighestPrice},{p.LowestPrice}");
                await stockDb.Insert(Exchange, Ticker, p.Time, p.Volume, p.OpeningPrice, p.ClosingPrice, p.HighestPrice, p.LowestPrice);
            }
            await Task.Delay(500);

            Console.WriteLine($"Successfully retrieved daily time series for {Ticker}");
            Console.ReadLine();
        }


        // 500 requests per day; 
        // let's me pull all categories (daily, weekly, monthly, etc) for up to 71 stocks
        private static async Task CollectMonthlyAdjustedTimeSeries()
        {
            var stockDb = new Core.Db.StockInfo(Core.Db.Table.TimeSeries_MonthlyAdjusted);
            // retrieve the last timestamp for monthly adjusted and make sure we're not overwriting old data

            foreach (var ticker in tickers)
            {
                Console.WriteLine($"Collecting monthly adjusted data for: {ticker.Symbol}   -- {ticker.Name}");
                StockTimeSeries stockTimeSeries = await client.RequestMonthlyTimeSeriesAsync(ticker.Symbol, true);
                var points = stockTimeSeries.DataPoints;
                foreach (var p in points)
                {
                    //Console.WriteLine($@"{p.Time},{p.Volume},{p.OpeningPrice},{p.ClosingPrice},{p.HighestPrice},{p.LowestPrice}");
                    await stockDb.Insert(ticker.Exchange, ticker.Symbol, p.Time, p.Volume, p.OpeningPrice, p.ClosingPrice, p.HighestPrice, p.LowestPrice);
                }
                await Task.Delay(500);
            }
            Console.WriteLine("Successfully inserted all data");
            Console.ReadLine();
        }

        private static async Task GetCurrentStockPrice(string Ticker)
        {
            var currentQuote = await client.RequestBatchQuotesAsync(new string[] { Ticker });
            foreach (var info in currentQuote)
            {
                Console.WriteLine($"Current Price of {Ticker} is: {info.Price},\t{info.Volume}");
            }
        }
        public static void WriteToConsole(string text, ConsoleColor color = ConsoleColor.White)
        {
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = originalColor;
        }
        // todo - create a general work project,  migrate this method to utilities class
        private static async Task ImportSymbols()
        {
            var db = new Core.Db.SQLiteBase("Symbols", "[Symbol],[Name],[Exchange]");
            var nyse = "nyse_symbol_list.txt";
            var nasdaq = "nasdaq_symbol_list.txt";
            var tsx = "TSX.txt";
            var lines = File.ReadAllLines($@"C:\Users\sesa345094\Desktop\{tsx}");
            foreach (var line in lines)
            {
                var l = line.Split('\t');
                await db.Insert("insert into dbo.Symbols values(@symbol,@name,@exchange)",
                    new List<SQLiteParameter>()
                     {
                            new SQLiteParameter() {ParameterName = "@symbol", DbType = DbType.String, Size=10, Value=l[0]},
                            new SQLiteParameter() {ParameterName = "@name", DbType = DbType.String, Size=75, Value=l[1]},
                            new SQLiteParameter() {ParameterName = "@exchange", DbType = DbType.String, Size=10, Value="TSX"},
                     });
            }
        }


        private static async Task ImportDailyTimeSeries()
        {
            var stockDb = new Core.Db.StockInfo(Core.Db.Table.TimeSeries_Daily);
            var tsx = "tickers.txt";
            var lines = File.ReadAllLines($@"C:\Users\sesa345094\Desktop\{tsx}");
            foreach (var ticker in lines)
            {
                Console.WriteLine($"Collecting daily data for: TSX:{ticker}");
                StockTimeSeries stockTimeSeries = await client.RequestDailyTimeSeriesAsync($"TSX:{ticker}", TimeSeriesSize.Compact);
                foreach (var p in stockTimeSeries.DataPoints)
                {
                    Console.WriteLine($@"{p.Time,-5},{p.Volume,-5},{p.OpeningPrice,-5},{p.ClosingPrice,-5},{p.HighestPrice,-5},{p.LowestPrice,-5}");
                    await stockDb.Insert("TSX", ticker, p.Time, p.Volume, p.OpeningPrice, p.ClosingPrice, p.HighestPrice, p.LowestPrice);
                }

                Console.WriteLine($"Successfully retrieved daily time series for {ticker}");
                await Task.Delay(12000);
                Console.Clear();
            }

            Console.WriteLine("Finished Importing stock data");
            Console.ReadLine();
        }
        private static async Task GetDailyTimeSeriesFromScreener()
        {
            var stockDb = new Core.Db.StockInfo(Core.Db.Table.TimeSeries_Daily);
            // retrieve the last timestamp for monthly adjusted and make sure we're not overwriting old data
            var symbols = new Symbols();
            var SymbolList = await symbols.GetSymbolsList("where [Exchange]='TSX'");
            foreach (var symbol in SymbolList)
            {
                Console.WriteLine($"Collecting daily data for: TSX:{symbol.Symbol}");
                StockTimeSeries stockTimeSeries = await client.RequestDailyTimeSeriesAsync($"TSX:{symbol.Symbol}", TimeSeriesSize.Compact);
                foreach (var p in stockTimeSeries.DataPoints)
                {
                    //Console.WriteLine($@"{p.Time},{p.Volume},{p.OpeningPrice},{p.ClosingPrice},{p.HighestPrice},{p.LowestPrice}");
                    await stockDb.Insert("TSX", symbol.Symbol, p.Time, p.Volume, p.OpeningPrice, p.ClosingPrice, p.HighestPrice, p.LowestPrice);
                }

                Console.WriteLine($"Successfully retrieved daily time series for {symbol.Symbol}");

            }

            Console.WriteLine("Finished Importing stock data");
            Console.ReadLine();

        }




        //private static void findMinMax()
        //{
        //    var points = new List<int>();

        //    var minY = 10000000;
        //    var maxY = 0;
        //    for (var ix = 3; ix <= w - 3; ix++)
        //    {
        //        var height = getY(ix);
        //        if (height == null)
        //        {
        //            points.push(-1);
        //            continue;
        //        }
        //        points.push(height);

        //        if (maxY < height)
        //            maxY = height;
        //        if (minY > height)
        //            minY = height;
        //    }

        //    var lookingFor = -1;
        //    var localMaxY;
        //    var localMinY;
        //    var localMaxYx;
        //    var localMinYx;

        //    var minChange = (maxY - minY) * 0.1;
        //    var nextMaxY = points[0] + minChange;
        //    var nextMinY = points[0] - minChange;
        //    var npoints = points.length;
        //    for (var x = 0; x < npoints; x++)
        //    {
        //        var y = points[x];

        //        if (y == -1)
        //            continue;

        //        if (y > nextMaxY)
        //        {
        //            if (lookingFor == 1)
        //            {
        //                markMinPoint(localMinYx, localMinY)
        //            }
        //            nextMinY = y - minChange;
        //            nextMaxY = y + minChange;
        //            lookingFor = 0; // look for minimum, but save highest until we find it
        //            localMaxY = y;  // reset the local highest
        //            localMaxYx = x;
        //        }
        //        if (localMaxY <= y)
        //        {
        //            localMaxY = y;
        //            localMaxYx = x;
        //        }

        //        if (y < nextMinY)
        //        {
        //            if (lookingFor == 0)
        //            {
        //                markMaxPoint(localMaxYx, localMaxY)
        //            }
        //            nextMaxY = y + minChange;
        //            nextMinY = y - minChange;
        //            lookingFor = 1; // look for maximum, but save lowest until we find it
        //            localMinY = y;  // reset the local lowest
        //            localMinYx = x;
        //        }
        //        if (localMinY >= y)
        //        {
        //            localMinY = y;
        //            localMinYx = x;
        //        }
        //    }
        //}
    }
}
