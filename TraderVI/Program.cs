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
            Console.WriteLine("2. Ad-Hoc Analysis");

       
            var market = new Market();
            Stopwatch sw = Stopwatch.StartNew();
            var tasks = new Task[]
            {
                new Market().GetMarketSummary(),
                new Market().GetMarketIndices(),
                new Market().GetConstituents()
            };
            Task.WaitAll(tasks);
            Console.WriteLine(sw.ElapsedMilliseconds);

            sw = Stopwatch.StartNew();
            
            await market.GetMarketSummary(print: false);
            await market.GetMarketIndices(print: false);
            await market.GetConstituents(print: false);
            Console.WriteLine(sw.ElapsedMilliseconds);

            Console.ReadLine();
        }
    }
}
