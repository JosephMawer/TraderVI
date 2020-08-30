using Core.TMX;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TraderVI
{
    class Program
    {
        /// <summary>
        /// The main entry point for TraderVI.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static async Task Main(string[] args)
        {
            var option = DisplayOptions();
            
            switch (option)
            {
                case 1:
                    // initializes local machine with stock data to run ad hoc analysis on
                    Console.Clear();
                    Console.WriteLine("Initializing new local workspace.");
                    Console.WriteLine("This may take a while.");
                    await Import.Import.InitializeLocalWorkspace();
                    Console.Clear();
                    Console.WriteLine("Initialization was successful");
                    break;

                default: 
                    break;
            }
            
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

        private static int DisplayOptions()
        {
            Console.WriteLine("1. Initialize TraderVI local database.");
            Console.WriteLine("2. Ad-Hoc Analysis (on local database).");
            Console.WriteLine();
            Console.Write("Select one of the above options: ");
            var option = int.Parse(Console.ReadLine());
            return option;
        }
    }
}
