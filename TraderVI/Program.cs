using Core.TMX;
using System;
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
            await market.GetMarketSummary(print: true);
            await market.GetMarketIndices(print: true);



            Console.ReadLine();
        }
    }
}
