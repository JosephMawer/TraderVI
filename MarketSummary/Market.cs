using AngleSharp;
using Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TMX.Market
{
    /// <summary>
    /// 
    /// </summary>
    public partial class Market
    {
        private readonly IConfiguration config;
        private readonly IBrowsingContext context;

        private const string TMX_CONSTITUENTS = "https://web.tmxmoney.com/index_constituents.php?qm_symbol=^TSX";
        private const string TMX_MARKETS = "https://web.tmxmoney.com/marketsca.php";

        /// <summary>
        /// Default constructor
        /// </summary>
        public Market()
        {
            config = Configuration.Default.WithDefaultLoader();
            context = BrowsingContext.New(config);
        }

        
        /// <summary>
        /// A constituent is a company with shares that are part of an index like the S&P 500 or Dow Jones 
        /// Industrial Average. It is a component or a member of the index. The aggregate of the shares of 
        /// all constituents are used to calculate the value of the index.
        /// </summary>
        /// <returns></returns>
        private async Task GetConstituents()
        {
            var document = await context.OpenAsync(TMX_CONSTITUENTS);
  
            // the html defines a table that holds all constituents with class name:
            // table-responsive contituents-table 
            var constituents = document.All.Where(m => m.ClassName == "table-responsive contituents-table");
            
            // we now have an html table with each table row looking like this:
            //<tr>
            //  <td><a href="company.php?qm_symbol=RY">Royal Bank of Canada</a></td>    
            //  <td style="text-align:right;"><a href="quote.php?qm_symbol=RY">RY</a></td>
            //</tr>

            var sb = new StringBuilder();
            foreach (var c in constituents)
            {
                sb.Append(c.TextContent);
                sb.Append(Environment.NewLine);
            }
            //File.WriteAllText(@"C:\src\#Projects\alphaVantageDemo\tmp\html\constituents.html", sb.ToString());

        }

        /// <summary>
        /// Gets the market summary from TMX
        /// </summary>
        /// <param name="printToConsole"></param>
        /// <param name="saveToFile"></param>
        /// <returns></returns>
        public async Task<List<IMarketSummaryInfo>> GetMarketSummary()
        {
            // Send web request and receive html document
            var document = await context.OpenAsync(TMX_MARKETS);  //https://web.tmxmoney.com/marketsca.php

            // Record the time the request was sent/received (approximate is fine)
            var timeOfRequest = DateTime.Now;

            // Parse out the specific grid (market summary) that I am looking for.
            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
            var tdata = document.QuerySelectorAll("div.col-12 td");

            // There should be 7 columns
            if (tdata.Length % 7 != 0) {
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

            var dtos = new List<IMarketSummaryInfo>();
            for (int i = 0; i < chunks.Length; i++)
            {
                dtos.Add(new MarketSummaryInfo
                {
                    Date = timeOfRequest,
                    Name = chunks[i][0],
                    Volume = long.Parse(chunks[i][1].Replace(",", "")),
                    Value = long.Parse(chunks[i][2].Replace(",", "")),
                    IssuesTraded = int.Parse(chunks[i][3].Replace(",", "")),
                    Advancers = int.Parse(chunks[i][4].Replace(",", "")),
                    Unchanged = int.Parse(chunks[i][5].Replace(",", "")),
                    Decliners = int.Parse(chunks[i][6].Replace(",", ""))
                });
            }

            return dtos;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<List<IIndexSummary>> GetMarketIndices()
        {
            // Send web request and receive html document
            var document = await context.OpenAsync(TMX_MARKETS);  //https://web.tmxmoney.com/marketsca.php

            // Record the time the request was sent/received (approximate is fine)
            var timeOfRequest = DateTime.Now;

            // Parse out the specific grid (market summary) that I am looking for.
            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
            var tdata = document.QuerySelectorAll("div.col-lg-4 td");



            // Skip the first 7 td elements because we don't need that data (it's just the days of the week)
            var data = tdata.Select(x => x.TextContent).Skip(7).ToArray();

            // Group the array into subarrays of length 7 (again, equal to the amount of column headers)
            string[][] chunks = data.Select((s, i) => new { Value = s, Index = i })
                                    .GroupBy(x => x.Index / 4)
                                    .Select(grp => grp.Select(x => x.Value).ToArray())
                                    .ToArray();

            var dtos = new List<IIndexSummary>();
            for (int i = 0; i < chunks.Length; i++)
            {
                dtos.Add(new IndiceSummary
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
            return dtos;
        }
        public async Task<List<int>> GetCumulativeDifferential()
        {
            throw new NotImplementedException();  
        }
    }
}
