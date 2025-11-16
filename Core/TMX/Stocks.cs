//using AngleSharp;
//using Core.Models;
//using GraphQL;
//using GraphQL.Client.Http;
//using GraphQL.Client.Serializer.Newtonsoft;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Text.RegularExpressions;
//using System.Threading;
//using System.Threading.Tasks;

//namespace Core.TMX
//{

//    public static class Extensions
//    {
//        public static DateTime GetFormmatedDateTime(this DateTime dateTime)
//        {
//            return new DateTime(
//                dateTime.Year,
//                dateTime.Month,
//                dateTime.Day,
//                dateTime.Hour,
//                dateTime.Minute,
//                0,
//                0,
//                dateTime.Kind);
//        }
//    }

//    // ---------- Models ----------
//    public class QuoteBySymbolResponse
//    {
//        public QuoteDetailItem getQuoteBySymbol { get; set; } = new();
//    }

//    public class QuoteDetailItem
//    {
//        public string symbol { get; set; } = "";
//        public string name { get; set; } = "";
//        public string exchangeName { get; set; } = "";
//        public string exchangeCode { get; set; } = "";
//        public string marketPlace { get; set; } = "";
//        public string sector { get; set; } = "";
//        public string industry { get; set; } = "";
//        public string qmdescription { get; set; } = "";
//        public string website { get; set; } = "";
//        public decimal? price { get; set; }
//        public decimal? priceChange { get; set; }
//        public decimal? percentChange { get; set; }
//        public decimal? openPrice { get; set; }
//        public decimal? dayHigh { get; set; }
//        public decimal? dayLow { get; set; }
//        public decimal? prevClose { get; set; }
//        public decimal? peRatio { get; set; }
//        public decimal? dividendYield { get; set; }
//        public decimal? dividendAmount { get; set; }
//        public decimal? weeks52high { get; set; }
//        public decimal? weeks52low { get; set; }
//        public long? volume { get; set; }
//        public long? shareOutStanding { get; set; }
//        public long? totalSharesOutStanding { get; set; }
//        public decimal? MarketCap { get; set; }
//        public decimal? returnOnEquity { get; set; }

//        public override string ToString()
//            => $"{symbol} ({name}) — {price:C} ({percentChange:N2}%), Volume: {volume:N0}";
//    }

//    /// <summary>
//    /// Class used to scrap real-time stock data from tmx website
//    /// </summary>
//    public class Stocks
//    {
        

//        /// <summary>
//        /// Default constructor
//        /// </summary>
//        public Stocks()
//        {
            
//        }

//        public async Task<GetQuoteBySymbol> GetTMXQuote(string ticker)
//        {
//            try
//            {
//                var stockTickerRequest = new GraphQLRequest
//                {
//                    Query = @"query getQuoteBySymbol($symbol: String, $locale: String) {getQuoteBySymbol(symbol: $symbol, locale: $locale) {   symbol    name    price    priceChange    percentChange    exchangeName    exShortName    exchangeCode    marketPlace    sector    industry    volume    openPrice   dayHigh    dayLow    MarketCap   MarketCapAllClasses   peRatio    prevClose    dividendFrequency    dividendYield    dividendAmount    dividendCurrency    beta    eps    exDividendDate    shortDescription    longDescription   website  email    phoneNumber    fullAddress    employees    shareOutStanding    totalDebtToEquity   totalSharesOutStanding    sharesESCROW    vwap    dividendPayDate    weeks52high    weeks52low    alpha   averageVolume10D    averageVolume30D    averageVolume50D   priceToBook    priceToCashFlow    returnOnEquity    returnOnAssets    day21MovingAvg    day50MovingAvg    day200MovingAvg    dividend3Years    dividend5Years    datatype    __typename  }}",
//                    OperationName = "getQuoteBySymbol",
//                    Variables = new
//                    {
//                        symbol = $"{ticker}",
//                        locale = "en"
//                    }
//                };

//                // To use NewtonsoftJsonSerializer, add a reference to NuGet package GraphQL.Client.Serializer.Newtonsoft
//                var client = new GraphQLHttpClient("https://app-money.tmx.com/graphql", new NewtonsoftJsonSerializer());
//                var response = await client.SendQueryAsync<Data>(stockTickerRequest);

//                return response.Data.getQuoteBySymbol;
//            }
//            catch (Exception ex)
//            {
//                var msg = ex.Message;
//            }

//            return default;
//        }

//        public async Task<StockQuote> RequestTickerInfo(string Ticker, string Name = "")
//        {


//            // Send web request and receive html document
//            //Console.WriteLine("Sending request for " + Ticker);
//            var cts = new CancellationTokenSource();
//            cts.CancelAfter(3500);
//            htmlDocument = await context.OpenAsync(SearchURL + Ticker, cts.Token);

//            cts.Dispose();

//            var stock = new StockQuote();
//            if (!string.IsNullOrEmpty(Name))
//                stock.Name = Name;
//            stock.Ticker = Ticker;
//            try
//            {
//                stock.TimeOfRequest = DateTime.Now;  //.GetFormmatedDateTime();
//                stock.Price = GetPrice(Ticker);
//                stock.Close = stock.Price;
//                // -- don't need this code since I will ensure the program always runs after 4pm
//                //TimeSpan marketClose = TimeSpan.Parse("16:00"); // 4 PM
//                //if (DateTime.Parse(stock.TimeOfRequest).TimeOfDay >= marketClose)
//                //    stock.Close = stock.Price;  // if the market has closed we can set the close price
//                //else stock.Close = default;


//                stock.Open = GetOpen(Ticker);
//                stock.Volume = GetVolume(Ticker);
//                var (high, low) = GetDailyHighAndLow(Ticker);
//                stock.High = high;
//                stock.Low = low;
//                //var (high52, low52) = Get52WeekHighAndLow(Ticker);
//                //stock.High52Week = high52;
//                //stock.Low52Week = low52;
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine(ex.Message);
//            }




//            // clear large string from memory
//            FlushHTML();

//            // Return the current stock info
//            return stock;
//        }

//        /// <summary>
//        /// Clears the in memory html
//        /// </summary>
//        public void FlushHTML() => htmlDocument = null;

//        public decimal GetPrice(string Ticker)
//        {
//            // Record the time the request was sent/received (approximate is fine)
//            var timeOfRequest = DateTime.Now;

//            // Parse out the specific grid (market summary) that I am looking for.
//            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
//            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
//            var tdata = htmlDocument.QuerySelectorAll("div.labs-symbol span");

//            // call to array so we can index
//            var data = tdata.Select(x => x.TextContent).ToArray();
//            decimal price;
//            foreach (var d in data)
//            {
//                if (decimal.TryParse(d, out price))
//                {
//                    return price;
//                }
//            }
//            throw new NullReferenceException("Unable to parse out price for " + Ticker);
//        }
//        public int GetVolume(string Ticker)
//        {
//            // Parse out the specific grid (market summary) that I am looking for.
//            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
//            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
//            var tdata = htmlDocument.QuerySelectorAll("div.col-4 strong");

//            // call to array so we can index
//            var data = tdata.Select(x => x.TextContent).ToArray();
//            int volume;
//            foreach (var d in data)
//            {
//                // Remove , before parsing string
//                if (int.TryParse(d.Replace(",", ""), out volume))
//                {
//                    return volume;
//                }
//            }
//            throw new NullReferenceException("Unable to parse out volume for " + Ticker);
//        }

//        public (decimal, decimal) GetDailyHighAndLow(string Ticker)
//        {
//            // Parse out the specific grid (market summary) that I am looking for.
//            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
//            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
//            var tdata = htmlDocument.QuerySelectorAll("div.top-info");

//            // call to array so we can index
//            var data = tdata.Select(x => x.TextContent).ToArray();
//            decimal dailyLow, dailyHigh;
//            foreach (var d in data)
//            {
//                var textContent = Regex.Replace(d, @"\t|\n|\r", "");
//                var index = textContent.IndexOf("D", 2);    //ex.  Day Low: 6:44Day High 7:22
//                var low = textContent.Substring(0, index);
//                var high = textContent.Substring(index);

//                dailyLow = decimal.Parse(low.Replace("Day Low: ", ""));
//                dailyHigh = decimal.Parse(high.Replace("Day High: ", ""));
//                return (dailyHigh, dailyLow);
//            }


//            throw new NullReferenceException("Error parsing daily low/high values");
//        }

//        public decimal GetOpen(string Ticker)
//        {
//            var tdata = htmlDocument.QuerySelectorAll("div.tmx-panel-body");
//            // call to array so we can index
//            var data = tdata.Select(x => x.TextContent).ToArray();
//            //Open:4.45High:4.51Beta:0.5933Listed Shares Out.1:1,013,539,861
//            //Total Shares (All Classes)2:1,013,644,214Prev. Close:4.38Low:4.40VWAP:4.4552Market Cap1:4,520,387,780
//            //Market Cap (All Classes)2*:4,520,853,194Dividend:N/ADiv. Frequency:N/AP/E Ratio:269.20EPS:0.02
//            //Yield:N/AEx-Div Date:N/AP/B Ratio:1.991Exchange:TSX(1) 
//            foreach (var d in data)
//            {
//                var textContent = Regex.Replace(d, @"\t|\n|\r", "");
//                if (textContent.StartsWith("Open:"))
//                {
//                    var index = textContent.IndexOf("High:");
//                    var open = textContent.Substring(5, index - 5);
//                    if (decimal.TryParse(open, out decimal result))
//                    {
//                        return result;
//                    }
//                }

//            }
//            throw new Exception($"Unable to parse out 'Open' price for {Ticker}");
//            //return 0f;
//        }

//        public (float, float) Get52WeekHighAndLow(string Ticker)
//        {
//            // Parse out the specific grid (market summary) that I am looking for.
//            // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
//            // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
//            var tdata = htmlDocument.QuerySelectorAll("div.top-info");

//            // call to array so we can index
//            var data = tdata.Select(x => x.TextContent).ToArray();
//            float dailyLow, dailyHigh;
//            foreach (var d in data)
//            {
//                var textContent = Regex.Replace(d, @"\t|\n|\r", "");
//                if (textContent.Contains("52 Week"))
//                {
//                    var index = textContent.IndexOf("52 ", 5);    //ex.  52 Week Low: 6:4452 Week High 7:22
//                    var low = textContent.Substring(0, index);
//                    var high = textContent.Substring(index);

//                    dailyLow = float.Parse(low.Replace("52 Week Low: ", ""));
//                    dailyHigh = float.Parse(high.Replace("52 Week High: ", ""));
//                    return (dailyHigh, dailyLow);
//                }
//            }


//            throw new NullReferenceException("Error parsing daily low/high values");
//        }
//    }
//}
