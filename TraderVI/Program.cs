using Core.TMX;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TraderVI
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Choose an option:");
            Console.WriteLine("1. Import stock data to TraderVI");
            Console.WriteLine("2. Ad-Hoc Analysis (on local database)");

       
            
            var tasks = new Task[]
            {
                new Market().GetMarketSummary(true),
                new Market().GetMarketIndices(true),
                new Market().GetConstituents(true)
            };
            Task.WaitAll(tasks);
            //var market = new Market();
            //await market.GetMarketSummary(true);
            //await market.GetMarketIndices(true);
            //await market.GetConstituents(true);

            Console.ReadLine();
        }
    }
}
