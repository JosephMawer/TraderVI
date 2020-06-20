using ConsoleTables;
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

            // The stock collection class may be pointless? why not just have all operations performed
            // directly in this program
            var collector = new Collection.DailyStockCollection();

            var saveToDatabase = true;

            //var indiceSummary = await collector.GetDailyIndiceAverages(saveToDatabase);
            //ConsoleTable.From(indiceSummary).Write();

            var marketSummary = await collector.GetDailyMarketSummary(saveToDatabase);
            ConsoleTable.From(marketSummary).Write();

            var stocks = await collector.GetDailyStockInfo(constituents, saveToDatabase);
            ConsoleTable.From(stocks).Write();

            System.Console.WriteLine("Successfully imported all stock and market data");
        }
    }
}
