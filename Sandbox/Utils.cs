using AlphaVantage.Net.Stocks.TimeSeries;
using AngleSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox
{
    public static class Utils
    {
        public static async Task DownloadHTMLPage(string url, string pathToSaveFile)
        {
            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(url);  //https://web.tmxmoney.com/marketsca.php
            File.WriteAllText(pathToSaveFile, document.ToHtml());
        }


        // These import mechanisms are mostly obsolete since I manually import csv into sql server
        public static async Task Import_CSV_Constituents(string pathToCSV)
        {
            // just importing some tsx data.. so some values will be hardcoded here ... don't need this function
            // to be general purpose yet


            var lines = File.ReadAllLines(pathToCSV);
            StocksDB.Constituents _symbol = new StocksDB.Constituents();
            foreach (var line in lines) // skip the header
            {
                var info = line.Split("\t");    
                await _symbol.InsertConstituent(info[0], info[1]);
            }
        }
        public static async Task Import_CSV_Symbols(string pathToCSV)
        {
            // just importing some tsx data.. so some values will be hardcoded here ... don't need this function
            // to be general purpose yet

            
            var lines = File.ReadAllLines(pathToCSV);
            StocksDB.Symbols _symbol = new StocksDB.Symbols();
            foreach (var line in lines.Skip(1)) // skip the header
            {
                var info = line.Split("\t");    //imported data from http://www.eoddata.com/, which was a tab separated text file
                await _symbol.InsertSymbol(info[0], info[1], "TSX");
            }

        }
        public static async Task ExportToCSV(string path, string name, ICollection<StockDataPoint> data)
        {
            var sb = new StringBuilder();
            foreach (var p in data) {
                sb.Append($"{p.Time.ToShortDateString()},{p.Volume},{p.OpeningPrice},{p.ClosingPrice},{p.HighestPrice},{p.LowestPrice}{Environment.NewLine}");
            }
            File.WriteAllText(Path.Combine(path, name), sb.ToString());
        }
    }
}
