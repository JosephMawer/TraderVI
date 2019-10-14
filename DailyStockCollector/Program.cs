using ConsoleTables;
using Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DailyStockCollector
{
    class Program
    {
        /// <summary>
        /// The main entry point for the program
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static async Task Main(string[] args)
        {
            // TODO: make sure constituents is up to date
            var db1 = new StocksDB.Constituents();
            var constituents = await db1.GetConstituents(); // get full list; user overload to get single constituent

            var saveToDatabase = false;
            await GetDailyIndiceAverages(saveToDatabase);
            await GetDailyMarketSummary(saveToDatabase);
            await GetDailyStockInfo(constituents, saveToDatabase);
        }
        /// <summary>
        /// Retreives market indices; stores values in database and prints to console
        /// </summary>
        static async Task GetDailyIndiceAverages(bool SaveToDatabase = false)
        {
            var market = new Core.TMX.Market();

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
            var market = new Core.TMX.Market();

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


        /// <summary>
        /// 
        /// </summary>
        /// <param name="constituents"></param>
        /// <returns></returns>
        private static async Task GetDailyStockInfo(IList<StocksDB.ConstituentInfo> constituents, bool saveToDatabase = false)
        {
            var dailyStats = new List<IStockInfo>();
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
            var stock = new Core.TMX.Stocks();
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
    }
}
