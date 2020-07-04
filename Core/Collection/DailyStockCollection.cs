using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core
{
    public class DailyStockCollection
    {
        private readonly Core.TMX.Market _market;
        public DailyStockCollection()
        {
            _market = new Core.TMX.Market();
        }

        /// <summary>
        /// Retreives market indices; stores values in database and prints to console
        /// </summary>
        public async Task<List<IIndexSummary>> GetDailyIndiceAverages(bool SaveToDatabase = false)
        {
            throw new Exception();
            //// Get daily summary of market indices
            //var indice = await _market.GetMarketIndices();

            //if (SaveToDatabase)
            //{
            //    // Insert Index summary info to database
            //    var indexDb = new Db.IndiceSummary();
            //    await indexDb.InsertIndiceSummary(indice);
            //}

            //return indice;
        }

        /// <summary>
        /// Retrieves the daily market summary for TSX, TSX Venture, Alpha
        /// </summary>
        public  async Task<List<IMarketSummaryInfo>> GetDailyMarketSummary(bool saveToDatabase = false)
        {
            throw new NotImplementedException();
            // Get daily market summary
            //var summary = await _market.GetMarketSummary();

            //if (saveToDatabase)
            //{
            //    var db = new Db.MarketSummary();
            //    await db.InsertMarketSummary(summary);
            //}

            //return summary;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="constituents"></param>
        /// <returns></returns>
        public async Task<List<IStockInfo>> GetDailyStockInfo(IList<Db.ConstituentInfo> constituents, bool saveToDatabase = false)
        {
            var dailyStats = new List<IStockInfo>();
            // async patterns
            // https://markheath.net/post/constraining-concurrent-threads-csharp

            #region Attempt 3
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
            #endregion

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

            if (saveToDatabase)
            {
                Console.WriteLine("Saving daily stock info to database....");
                var db = new Db.DailyTimeSeries();
                await db.InsertDailyStockList(dailyStats);
            }

            return dailyStats;
        }


    }
}
