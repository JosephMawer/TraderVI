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
           
            Console.WriteLine("1. Initialize TraderVI local database.");
            Console.WriteLine("2. Ad-Hoc Analysis (on local database).");
            Console.WriteLine();
            Console.Write("Select one of the above options: ");
            var option = int.Parse(Console.ReadLine());
       
            switch (option)
            {
                case 1:
                    // initializes local machine with stock data to run ad hoc analysis on
                    await Import.Import.InitializeLocalWorkspace();
                    break;

                default: 
                    break;
            }
            
            var tasks = new Task[]
            {
                new Market().GetMarketSummary(print: true),
                new Market().GetMarketIndices(print: true),
                new Market().GetConstituents(print: true)
            };
            Task.WaitAll(tasks);
            //var market = new Market();
            //await market.GetMarketSummary(print: true);
            //await market.GetMarketIndices(print: true);
            //await market.GetConstituents(print: true);

            Console.ReadLine();
        }
    }
}
