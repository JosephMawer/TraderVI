using AngleSharp;
using ConsoleTables;
using Core.Db;
using Core.TMX.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Core.TMX
{
    /// <summary>
    /// Gets high level information about the canadian market from the
    /// tmx website.
    /// </summary>
    public class Market : TMXBase
    {
        private readonly IConfiguration config;
        private readonly IBrowsingContext context;

        private readonly string TMX_CONSTITUENTS = "https://web.tmxmoney.com/index_constituents.php?qm_symbol=^TSX";
        private readonly string TMX_MARKETS = "https://web.tmxmoney.com/marketsca.php";

  
        /// <summary>
        /// Default constructor
        /// </summary>
        public Market() { }


        /// <summary>
        /// A constituent is a company with shares that are part of an index like the S&P 500 or Dow Jones 
        /// Industrial Average. It is a component or a member of the index. The aggregate of the shares of 
        /// all constituents are used to calculate the value of the index.
        /// </summary>
        /// <returns></returns>
        public async Task<List<ConstituentInfo>> GetConstituents(bool print = false)
        {
            await Crawler(TMX_CONSTITUENTS);
            
            var constituents = HtmlDocument.QuerySelectorAll("div.col-lg-10 td");

            // we now have an html table with each table row looking like this:
            //<tr>
            //  <td><a href="company.php?qm_symbol=RY">Royal Bank of Canada</a></td>    
            //  <td style="text-align:right;"><a href="quote.php?qm_symbol=RY">RY</a></td>
            //</tr>


            var constituentInfo = new List<ConstituentInfo>();

            var total = constituents.Length;
            var currentCount = 0;
            while (currentCount < total)
            {
                var tableData = constituents.Skip(currentCount).Take(2);
                var cf = new ConstituentInfo
                {
                    Name = tableData.ElementAt(0).TextContent,
                    Symbol = tableData.ElementAt(1).TextContent
                };

                constituentInfo.Add(cf);
                currentCount += 2;
            }
            
            if (print) ConsoleTable.From(constituentInfo).Write();

            return constituentInfo;
        }

        /// <summary>
        /// Gets the market summary from TMX
        /// </summary>
        /// <param name="printToConsole"></param>
        /// <param name="saveToFile"></param>
        /// <returns></returns>
        public async Task<List<Models.MarketSummary>> GetMarketSummary(bool print = false)
        {
            await Crawler(TMX_MARKETS);//("https://web.tmxmoney.com/marketsca.php?qm_page=99935");



            // Record the time the request was sent/received (approximate is fine)
            var timeOfRequest = DateTime.Now;

            // Parse out the specific grid (market summary) that I am looking for.
            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
            var tdata = HtmlDocument.QuerySelectorAll("div.col-12 td");

            // There should be 7 columns
            if (tdata.Length % 7 != 0)
            {
                throw new Exception("Unexpected number of column headers - please review static html and adjust method accordingly");
            }

            //The expected order of the strings is based on the column headers here:
            //Name,Total Volume,Total Value,Issues Traded,Advancers,Unchanged,Decliners


            // call to array so we can index
            var data = tdata.Select(x => x.TextContent).ToArray();

            // Group the array into subarrays of length 7 (again, equal to the amount of column headers)
            string[][] chunks = data.Select((s, i) => new { Value = s, Index = i })
                                    .GroupBy(x => x.Index / 7)
                                    .Select(grp => grp.Select(x => x.Value).ToArray())
                                    .ToArray();

            var marketSummary = new List<TMX.Models.MarketSummary>();
            foreach (var block in chunks)
            {
                try
                {
                    marketSummary.Add(new TMX.Models.MarketSummary
                    {
                        Date = timeOfRequest,
                        Name = block[0],
                        Volume = long.Parse(block[1].Replace(",", "")),
                        Value = long.Parse(block[2].Replace(",", "")),
                        IssuesTraded = int.Parse(block[3].Replace(",", "")),
                        Advancers = int.Parse(block[4].Replace(",", "")),
                        Unchanged = int.Parse(block[5].Replace(",", "")),
                        Decliners = int.Parse(block[6].Replace(",", ""))
                    });
                }
                catch (Exception ex) {
                    Console.WriteLine("Error while parsing market summary" + ex.Message);
                }
            }

            if (print) ConsoleTable.From(marketSummary).Write();

            return marketSummary;
        }

        

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<List<MarketIndices>> GetMarketIndices(bool print = false)
        {
            await Crawler(TMX_MARKETS);//("https://web.tmxmoney.com/marketsca.php?qm_page=99935");

            // Record the time the request was sent/received (approximate is fine)
            var timeOfRequest = DateTime.Now;

            // Parse out the specific grid (market summary) that I am looking for.
            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
            var tdata = HtmlDocument.QuerySelectorAll("div.col-lg-4 td");



            // Skip the first 7 td elements because we don't need that data (it's just the days of the week)
            var data = tdata.Select(x => x.TextContent).Skip(7).ToArray();

            // Group the array into subarrays of length 7 (again, equal to the amount of column headers)
            string[][] chunks = data.Select((s, i) => new { Value = s, Index = i })
                                    .GroupBy(x => x.Index / 4)
                                    .Select(grp => grp.Select(x => x.Value).ToArray())
                                    .ToArray();

            var indiceSummary = new List<MarketIndices>();
            for (int i = 0; i < chunks.Length; i++)
            {
                indiceSummary.Add(new MarketIndices
                {
                    Date = timeOfRequest,
                    // The name of the symbol, ex. TSX, ENERGY, FINANCIALS
                    Name = Regex.Replace(chunks[i][0], @"\t|\n|\r", ""),

                    // The last recorded price
                    Last = float.Parse(chunks[i][1].Replace(",", "")),
                    Change = float.Parse(chunks[i][2]),
                    PercentChange = float.Parse(chunks[i][3].Replace("%", ""))
                });
            }

            if (print) ConsoleTable.From(indiceSummary).Write();

            return indiceSummary;
        }
        
        public async Task<List<int>> GetCumulativeDifferential()
        {
            throw new NotImplementedException();
        }
    }
}
