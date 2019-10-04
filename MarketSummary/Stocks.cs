using AngleSharp;
using Core;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TMX.Market
{
    
    public static class Extensions
    {
        public static DateTime GetFormmatedDateTime(this DateTime dateTime)
        {
            return new DateTime(
                dateTime.Year,
                dateTime.Month,
                dateTime.Day,
                dateTime.Hour,
                dateTime.Minute,
                0,
                0,
                dateTime.Kind);
        }
    }
   

    /// <summary>
    /// Class used to scrap real-time stock data from tmx website
    /// </summary>
    public class Stocks
    {
        private readonly IConfiguration config;
        private readonly IBrowsingContext context;

        //private const string LegacySearchURL = "https://web.tmxmoney.com/legacy-charting.php?qm_page=72328&qm_symbol=";
        private const string SearchURL = "https://web.tmxmoney.com/quote.php?qm_symbol=";
        private string _ticker;
        private static AngleSharp.Dom.IDocument htmlDocument = null;

        /// <summary>
        /// Default constructor
        /// </summary>
        public Stocks()
        {
            config = Configuration.Default.WithDefaultLoader();
            context = BrowsingContext.New(config);
        }

        public Stocks(string Ticker) : base()
        {
            _ticker = Ticker;
        }
        public async Task<StockInfo> RequestTickerInfo(string Ticker, string Name = "")
        {
           

            // Send web request and receive html document
            //Console.WriteLine("Sending request for " + Ticker);
            var cts = new CancellationTokenSource();
            cts.CancelAfter(1000);
            htmlDocument = await context.OpenAsync(SearchURL + Ticker, cts.Token);

            cts.Dispose();
                
            var stock = new StockInfo();
            if (!string.IsNullOrEmpty(Name))
                stock.Name = Name;
            stock.Ticker = Ticker;
            try
            {
                stock.TimeOfRequest = DateTime.Now.ToShortDateString();  //.GetFormmatedDateTime();
                stock.Price = GetPrice(Ticker);
                TimeSpan marketClose = TimeSpan.Parse("16:00"); // 4 PM
                if (DateTime.Parse(stock.TimeOfRequest).TimeOfDay >= marketClose)
                    stock.Close = stock.Price;  // if the market has closed we can set the close price
                else stock.Close = default;


                stock.Open = GetOpen(Ticker);
                stock.Volume = GetVolume(Ticker);
                var (high, low) = GetDailyHighAndLow(Ticker);
                stock.High = high;
                stock.Low = low;
                //var (high52, low52) = Get52WeekHighAndLow(Ticker);
                //stock.High52Week = high52;
                //stock.Low52Week = low52;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }




            // clear large string from memory
            FlushHTML();

            // Return the current stock info
            return stock; 
        }

        /// <summary>
        /// Clears the in memory html
        /// </summary>
        public void FlushHTML() => htmlDocument = null;

        public decimal GetPrice(string Ticker)
        {
            // Record the time the request was sent/received (approximate is fine)
            var timeOfRequest = DateTime.Now;

            // Parse out the specific grid (market summary) that I am looking for.
            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
            var tdata = htmlDocument.QuerySelectorAll("div.labs-symbol span");

            // call to array so we can index
            var data = tdata.Select(x => x.TextContent).ToArray();
            decimal price;
            foreach (var d in data)
            {
                if (decimal.TryParse(d, out price))
                {
                    return price;
                }
            }
            throw new NullReferenceException("Unable to parse out price for " + Ticker);
        }
        public int GetVolume(string Ticker)
        {
            // Parse out the specific grid (market summary) that I am looking for.
            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
            var tdata = htmlDocument.QuerySelectorAll("div.col-4 strong");

            // call to array so we can index
            var data = tdata.Select(x => x.TextContent).ToArray();
            int volume;
            foreach (var d in data)
            {
                // Remove , before parsing string
                if (int.TryParse(d.Replace(",",""), out volume))
                {
                    return volume;
                }
            }
            throw new NullReferenceException("Unable to parse out volume for " + Ticker);
        }

        public (decimal,decimal) GetDailyHighAndLow(string Ticker)
        {
            // Parse out the specific grid (market summary) that I am looking for.
            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
            var tdata = htmlDocument.QuerySelectorAll("div.top-info");

            // call to array so we can index
            var data = tdata.Select(x => x.TextContent).ToArray();
            decimal dailyLow, dailyHigh;
            foreach (var d in data)
            {
                var textContent = Regex.Replace(d, @"\t|\n|\r", "");
                var index = textContent.IndexOf("D", 2);    //ex.  Day Low: 6:44Day High 7:22
                var low = textContent.Substring(0, index);
                var high = textContent.Substring(index);

                dailyLow = decimal.Parse(low.Replace("Day Low: ", ""));
                dailyHigh = decimal.Parse(high.Replace("Day High: ", ""));
                return (dailyHigh, dailyLow);
            }


            throw new NullReferenceException("Error parsing daily low/high values");
        }

        public decimal GetOpen(string Ticker)
        {
            var tdata = htmlDocument.QuerySelectorAll("div.tmx-panel-body");
            // call to array so we can index
            var data = tdata.Select(x => x.TextContent).ToArray();
            //Open:4.45High:4.51Beta:0.5933Listed Shares Out.1:1,013,539,861
            //Total Shares (All Classes)2:1,013,644,214Prev. Close:4.38Low:4.40VWAP:4.4552Market Cap1:4,520,387,780
            //Market Cap (All Classes)2*:4,520,853,194Dividend:N/ADiv. Frequency:N/AP/E Ratio:269.20EPS:0.02
            //Yield:N/AEx-Div Date:N/AP/B Ratio:1.991Exchange:TSX(1) 
            foreach (var d in data)
            {
                var textContent = Regex.Replace(d, @"\t|\n|\r", "");
                if (textContent.StartsWith("Open:"))
                {
                    var index = textContent.IndexOf("High:");
                    var open = textContent.Substring(5, index - 5);
                    if (decimal.TryParse(open, out decimal result))
                    {
                        return result;
                    }
                }
                
            }
            throw new Exception($"Unable to parse out 'Open' price for {Ticker}");
            //return 0f;
        }

        public (float, float) Get52WeekHighAndLow(string Ticker)
        {
            // Parse out the specific grid (market summary) that I am looking for.
            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
            var tdata = htmlDocument.QuerySelectorAll("div.top-info");

            // call to array so we can index
            var data = tdata.Select(x => x.TextContent).ToArray();
            float dailyLow, dailyHigh;
            foreach (var d in data)
            {
                var textContent = Regex.Replace(d, @"\t|\n|\r", "");
                if (textContent.Contains("52 Week"))
                {
                    var index = textContent.IndexOf("52 ", 5);    //ex.  52 Week Low: 6:4452 Week High 7:22
                    var low = textContent.Substring(0, index);
                    var high = textContent.Substring(index);

                    dailyLow = float.Parse(low.Replace("52 Week Low: ", ""));
                    dailyHigh = float.Parse(high.Replace("52 Week High: ", ""));
                    return (dailyHigh, dailyLow);
                }
            }


            throw new NullReferenceException("Error parsing daily low/high values");
        }
    }
}
