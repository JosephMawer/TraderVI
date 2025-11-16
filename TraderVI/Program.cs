using Core.Db;
using Core.TMX;
using Core.Trader;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TraderVI
{

    // todo: use 'Channels' to communicate between services locally


    class Program
    {
        /// <summary>
        /// The main entry point for TraderVI.
        /// Manages active trades via channels to stock alerts
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static async Task Main(string[] args)
        {

            var symbolRepository = new QuoteRepository();
            await symbolRepository.InsertSymbol("CEU", "Test", "TSX");

            // start off with our ticker

            var symbol = "CEU";
            var tmx = new TMX();
            var tradeManager = new TradeManager(ghost: true);
            
            
            var quote = await tmx.GetQuoteBySymbol(symbol);
            decimal previousClose = quote.prevClose.Value;



            while (true)
            {
                // really this should be running in background task that pulses in stock data for 'Active Trades'
                var stockQuote = await tmx.GetIntradayTimeSeriesData(symbol, "minute", 5, DateTime.Now);
                

                // this loop should really just be waiting for 'Alerts' from various channels that are doing the processing



            
            }

            //var option = DisplayOptions();
            
            //switch (option)
            //{
            //    case 1:
            //        // initializes local machine with stock data to run ad hoc analysis on
            //        Console.Clear();
            //        Console.WriteLine("Initializing new local workspace.");
            //        Console.WriteLine("This may take a while.");
            //        await Import.Import.InitializeLocalWorkspace();
            //        Console.Clear();
            //        Console.WriteLine("Initialization was successful");
            //        break;

            //    default: 
            //        break;
            //}
            
            //var tasks = new Task[]
            //{
            //    new Market().GetMarketSummary(print: true),
            //    new Market().GetMarketIndices(print: true),
            //    new Market().GetConstituents(print: true)
            //};
            //Task.WaitAll(tasks);
            //var market = new Market();
            //await market.GetMarketSummary(print: true);
            //await market.GetMarketIndices(print: true);
            //await market.GetConstituents(print: true);

            Console.ReadLine();
        }


    }
}
