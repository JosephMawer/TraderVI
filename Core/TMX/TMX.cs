//using AngleSharp.Browser.Dom;
//using AngleSharp.Dom.Events;
//using ConsoleTables;
using Core.TMX.Models;
using Core.TMX.Models.Market;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
//using MathNet.Numerics.Distributions;
//using MathNet.Numerics.LinearAlgebra.Factorization;
//using MathNet.Numerics.RootFinding;
//using Microsoft.VisualBasic;
//using Serilog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.TMX
{
    public class TMX
    {


        //Production Tips
        //Respect rate limits — TMX endpoints may throttle; keep your request frequency modest(e.g., once per minute).
        //Cache responses — The Market Summary changes slowly, so cache it locally for a few minutes.
        //Persist cookies — If you need long-lived sessions, serialize CookieContainer contents between runs.
        //Rotate user agents — Avoid getting flagged as a bot if making frequent requests.
        //Compliance — Always review the site’s ToS and robots.txt to ensure your usage is permitted.


        private readonly GraphQLHttpClient _graphClient;
        private readonly CookieContainer _cookieContainer;
        private static readonly Uri GraphQLEndpoint = new Uri("https://app-money.tmx.com/graphql");

        public TMX() 
        {
            // Stored automatically by CookieContainer, allowing session persistence between multiple GraphQL calls.
            _cookieContainer = new CookieContainer();

            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,

                // Handles GZip and Deflate content-encoding transparently.
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            // Shared HttpClient to reuse sockets
            var httpClient = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            httpClient.DefaultRequestHeaders.Add("Origin", "https://money.tmx.com");
            httpClient.DefaultRequestHeaders.Add("Referer", "https://money.tmx.com/");
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var options = new GraphQLHttpClientOptions { EndPoint = GraphQLEndpoint };
            var serializer = new NewtonsoftJsonSerializer();

            _graphClient = new GraphQLHttpClient(options, serializer, httpClient);
        }


        // Use this._graphClient for queries. Do NOT dispose per-call.
        // Implement IDisposable to clean up once
        public void Dispose()
        {
            _graphClient?.Dispose();
            // Missing: Dispose of HttpClient/HttpClientHandler
        }

        //public async Task<QuoteResponse> GetQuoteBySymbolsAsync(string[] symbols)
        //{
        //    // --- GraphQL request ---
        //    var request = new GraphQLRequest
        //    {
        //        OperationName = "getQuoteForSymbols",
        //        Query = @"
        //        query getQuoteForSymbols($activity: [String], $futures: [String], $commodities: [String]) {
        //          marketActivity: getQuoteForSymbols(symbols: $activity) {
        //            symbol
        //            currency
        //            exchange
        //            longname
        //            price
        //            volume
        //            openPrice
        //            priceChange
        //            percentChange
        //            dayHigh
        //            dayLow
        //            prevClose
        //            bid
        //            ask
        //            weeks52high
        //            weeks52low
        //          }
        //          futures: getQuoteForSymbols(symbols: $futures) {
        //            symbol
        //            price
        //            priceChange
        //            percentChange
        //          }
        //          commodities: getQuoteForSymbols(symbols: $commodities) {
        //            symbol
        //            shortName
        //            price
        //            priceChange
        //            percentChange
        //          }
        //        }",
        //        Variables = new
        //        {
        //            activity = new[]
        //            {
        //            "^TSX","^JX","^TTEN","^TTFS","^TTHC",
        //            "^TTIN","^TTTK","^TXBM","^TTTS","^TTUT","^SPTXBMCP"
        //        },
        //            futures = new[] { "/COA", "/CRA", "/CGZ", "/CGB", "/LGB", "/SXF", "/CGF" },
        //            commodities = new[] { "/CL:NMX", "/NG:NMX", "/GC:CMX", "/SI:CMX", "/HG:CMX" }
        //        }
        //    };

        //    var response = await _graphClient.SendQueryAsync<QuoteResponse>(request);

        //    //if (print) ConsoleTable.From(response.Data.marketActivity).Write();
        //    //if (print) ConsoleTable.From(response.Data.futures).Write();
        //    //if (print) ConsoleTable.From(response.Data.commodities).Write();
        //    return new QuoteResponse
        //    {
        //        marketActivity = response.Data.marketActivity,
        //        futures = response.Data.futures,
        //        commodities = response.Data.commodities
        //    };
        //}
        // ----------- GraphQL call -----------

        public async Task<List<Quote>> GetQuoteBySymbolsAsync(string[] symbols, CancellationToken ct)
        {
            var request = new GraphQLRequest
            {
                OperationName = "getQuoteForSymbols",
                Query = @"
              query getQuoteForSymbols($activity: [String]) {
                marketActivity: getQuoteForSymbols(symbols: $activity) {
                  symbol
                  currency
                  exchange
                  longname
                  price
                  priceChange
                  percentChange
                  volume
                  openPrice
                  dayHigh
                  dayLow
                  prevClose
                  bid
                  ask
                  weeks52high
                  weeks52low
                }
              }",
                Variables = new { activity = symbols }
            };

            // basic retry on transient failures
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var resp = await _graphClient.SendQueryAsync<QuoteResponse>(request, ct);
                    if (resp.Errors is { Length: > 0 })
                        throw new Exception("GraphQL: " + string.Join(" | ", Array.ConvertAll(resp.Errors, e => e.Message)));
                    return resp.Data?.marketActivity ?? new List<Quote>();
                }
                catch (HttpRequestException) when (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                }
            }

            // Last attempt without catching to surface error
            var final = await _graphClient.SendQueryAsync<QuoteResponse>(request, ct);
            if (final.Errors is { Length: > 0 })
                throw new Exception("GraphQL: " + string.Join(" | ", Array.ConvertAll(final.Errors, e => e.Message)));
            return final.Data?.marketActivity ?? new List<Quote>();
        }

        public async Task<QuoteDetailItem> GetQuoteBySymbol(string symbol)
        {

            // --- Build request ---
            var request = new GraphQLRequest
            {
                OperationName = "getQuoteBySymbol",
                Query = @"
            query getQuoteBySymbol($symbol: String, $locale: String) {
                getQuoteBySymbol(symbol: $symbol, locale: $locale) {
                symbol
                name
                price
                priceChange
                percentChange
                exchangeName
                exShortName
                exchangeCode
                marketPlace
                sector
                industry
                volume
                openPrice
                dayHigh
                dayLow
                MarketCap
                MarketCapAllClasses
                peRatio
                prevClose
                dividendFrequency
                dividendYield
                dividendAmount
                dividendCurrency
                beta
                eps
                exDividendDate
                longDescription
                fulldescription
                website
                email
                phoneNumber
                fullAddress
                employees
                shareOutStanding
                totalDebtToEquity
                totalSharesOutStanding
                sharesESCROW
                vwap
                dividendPayDate
                weeks52high
                weeks52low
                alpha
                averageVolume10D
                averageVolume20D
                averageVolume30D
                averageVolume50D
                priceToBook
                priceToCashFlow
                returnOnEquity
                returnOnAssets
                day21MovingAvg
                day50MovingAvg
                day200MovingAvg
                dividend3Years
                dividend5Years
                datatype
                issueType
                secType
                close
                qmdescription
                }
            }",
                Variables = new { symbol = $"{symbol}", locale = "en" }
            };

            var response = await _graphClient.SendQueryAsync<QuoteBySymbolResponse>(request);

            if (response.Errors?.Length > 0)
            {
                throw new Exception($"GraphQL Errors: {string.Join(", ", response.Errors.Select(e => e.Message))}");
            }

            return response.Data.getQuoteBySymbol;
        }

        public async Task<MarketMoverItem[]> GetMarketMovers(bool print = false)
        {
            // --- Build GraphQL request ---
            var request = new GraphQLRequest
            {
                OperationName = "getMarketMovers",
                Query = @"
                query getMarketMovers($sortOrder: String!, $statExchange: String!, $marketId: Int, $limit: Int, $statCountry: String) {
                  getMarketMovers(
                    sortOrder: $sortOrder
                    statExchange: $statExchange
                    marketId: $marketId
                    limit: $limit
                    statCountry: $statCountry
                  ) {
                    symbol
                    name
                    exchangeName
                    exchangeCode
                    price
                    priceChange
                    percentChange
                    volume
                    tradeVolume
                    open
                    high
                    low
                    weeks52low
                    weeks52high
                  }
                }",
                Variables = new
                {
                    sortOrder = "dollarvolume",
                    statExchange = "tsx",
                    marketId = 11,
                    limit = 50
                }
            };

            // Execute the query
            var response = await _graphClient.SendQueryAsync<MarketMoversData>(request);

            //if (print) ConsoleTable.From(response.Data.getMarketMovers).Write();

            return response.Data.getMarketMovers;//marketMovers;
        }


        /// <summary>
        /// Gets the market summary from TMX
        /// </summary>
        /// <param name="printToConsole"></param>
        /// <param name="saveToFile"></param>
        /// <returns></returns>
       // public async Task<List<Models.MarketMover>> GetMarketSummary(bool print = false)
        public async Task<MarketSummaryItem[]> GetMarketSummary(bool print = false)
        {
            // Build the query
            var request = new GraphQLRequest
            {
                OperationName = "getMarketSummary",
                Query = @"
                query getMarketSummary($market: String!) {
                  getMarketSummary(market: $market) {
                    exchange
                    totalVolume
                    advancers
                    decliners
                    unchanged
                  }
                }",
                Variables = new { market = "caMarket" }
            };

            // Execute the query
            var response = await _graphClient.SendQueryAsync<MarketSummaryData>(request);

            //// --- Send with retries ---
            //const int maxRetries = 3;
            //int attempt = 0;
            //while (attempt < maxRetries)
            //{
            //    try
            //    {
            //        var response = await client.SendQueryAsync<MarketSummaryData>(request);

            //        if (response.Errors != null && response.Errors.Any())
            //        {
            //            Console.WriteLine("GraphQL returned errors:");
            //            foreach (var e in response.Errors)
            //                Console.WriteLine($"- {e.Message}");
            //            return;
            //        }

            //        foreach (var item in response.Data.getMarketSummary)
            //        {
            //            Console.WriteLine(
            //                $"{item.exchange}: Vol={item.totalVolume:N0}, Adv={item.advancers}, " +
            //                $"Dec={item.decliners}, Unch={item.unchanged}");
            //        }

            //        break; // success
            //    }
            //    catch (HttpRequestException ex) when (attempt < maxRetries - 1)
            //    {
            //        attempt++;
            //        Console.WriteLine($"[Retry {attempt}] Network error: {ex.Message}");
            //        await Task.Delay(TimeSpan.FromSeconds(2 * attempt)); // backoff
            //    }
            //}

            //// --- Inspect cookies (optional) ---
            //var cookies = cookieContainer.GetCookies(endpoint);
            //foreach (Cookie c in cookies)
            //{
            //    Console.WriteLine($"Cookie: {c.Name}={c.Value}");
            //}


            //if (print) ConsoleTable.From(response.Data.getMarketSummary).Write();

            return response.Data.getMarketSummary;//marketMovers;
        }

        ///// <summary>
        ///// A constituent is a company with shares that are part of an index like the S&P 500 or Dow Jones 
        ///// Industrial Average. It is a component or a member of the index. The aggregate of the shares of 
        ///// all constituents are used to calculate the value of the index.
        ///// </summary>
        ///// <returns></returns>
        //public async Task<List<ConstituentInfo>> GetConstituents(bool print = false)
        //{
        //    await Crawler(TMX_CONSTITUENTS);

        //    var constituents = HtmlDocument.QuerySelectorAll("div.col-lg-10 td");

        //    // we now have an html table with each table row looking like this:
        //    //<tr>
        //    //  <td><a href="company.php?qm_symbol=RY">Royal Bank of Canada</a></td>    
        //    //  <td style="text-align:right;"><a href="quote.php?qm_symbol=RY">RY</a></td>
        //    //</tr>


        //    var constituentInfo = new List<ConstituentInfo>();

        //    var total = constituents.Length;
        //    var currentCount = 0;
        //    while (currentCount < total)
        //    {
        //        var tableData = constituents.Skip(currentCount).Take(2);
        //        var cf = new ConstituentInfo
        //        {
        //            Name = tableData.ElementAt(0).TextContent,
        //            Symbol = tableData.ElementAt(1).TextContent
        //        };

        //        constituentInfo.Add(cf);
        //        currentCount += 2;
        //    }

        //    if (print) ConsoleTable.From(constituentInfo).Write();

        //    return constituentInfo;
        //}


        ///// <summary>
        ///// 
        ///// </summary>
        ///// <returns></returns>
        //public async Task<List<MarketIndices>> GetMarketIndices(bool print = false)
        //{
        //    await Crawler(TMX_MARKETS);//("https://web.tmxmoney.com/marketsca.php?qm_page=99935");

        //    // Record the time the request was sent/received (approximate is fine)
        //    var timeOfRequest = DateTime.Now;

        //    // Parse out the specific grid (market summary) that I am looking for.
        //    // div.col-12 td means get all divs with a css class of col-12 and then all td's in it.
        //    // The syntax for query selectors is basically the CSS Selector syntax: https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Selectors
        //    var tdata = HtmlDocument.QuerySelectorAll("div.col-lg-4 td");



        //    // Skip the first 7 td elements because we don't need that data (it's just the days of the week)
        //    var data = tdata.Select(x => x.TextContent).Skip(7).ToArray();

        //    // Group the array into subarrays of length 7 (again, equal to the amount of column headers)
        //    string[][] chunks = data.Select((s, i) => new { Value = s, Index = i })
        //                            .GroupBy(x => x.Index / 4)
        //                            .Select(grp => grp.Select(x => x.Value).ToArray())
        //                            .ToArray();

        //    var indiceSummary = new List<MarketIndices>();
        //    for (int i = 0; i < chunks.Length; i++)
        //    {
        //        indiceSummary.Add(new MarketIndices
        //        {
        //            Date = timeOfRequest,
        //            // The name of the symbol, ex. TSX, ENERGY, FINANCIALS
        //            Name = Regex.Replace(chunks[i][0], @"\t|\n|\r", ""),

        //            // The last recorded price
        //            Last = float.Parse(chunks[i][1].Replace(",", "")),
        //            Change = float.Parse(chunks[i][2]),
        //            PercentChange = float.Parse(chunks[i][3].Replace("%", ""))
        //        });
        //    }

        //    if (print) ConsoleTable.From(indiceSummary).Write();

        //    return indiceSummary;
        //}

        public async Task<List<int>> GetCumulativeDifferential()
        {
            throw new NotImplementedException();
        }

     


        /// <summary>
        /// Intraday: For data from today or recent sessions
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="freq"></param>
        /// <param name="interval"></param>
        /// <param name="startDateTime"></param>
        /// <param name="endDateTime"></param>
        /// <returns></returns>
        public async Task<List<TimeSeriesPointItem>> GetIntradayTimeSeriesData(string symbol, string freq, int interval, DateTime startDateTime, DateTime? endDateTime = null)
        {
            // todo - ensure startDateTime and endDateTime are in UTC
            // Normalize to UTC (TMX comparisons are safest in UTC)
            if (startDateTime.Kind != DateTimeKind.Utc) startDateTime = startDateTime.ToUniversalTime();
            if (endDateTime.HasValue && endDateTime.Value.Kind != DateTimeKind.Utc) endDateTime = endDateTime.Value.ToUniversalTime();


            var request = new GraphQLRequest
            {
                OperationName = "getTimeSeriesData",
                Query = @"
                query getTimeSeriesData(
                    $symbol: String!,
                    $freq: String,
                    $interval: Int,
                    $startDateTime: Int,
                    $endDateTime: Int
                ) {
                  getTimeSeriesData(
                    symbol: $symbol
                    freq: $freq
                    interval: $interval
                    startDateTime: $startDateTime
                    endDateTime: $endDateTime
                  ) {
                    dateTime
                    open
                    high
                    low
                    close
                    volume
                  }
                }",
                Variables = new
                {
                    symbol = $"{symbol}",//"BCE:US",
                    freq = $"{freq}",//"minute",
                    interval = interval,
                    startDateTime = ToUnixSeconds(startDateTime),
                    endDateTime = endDateTime.HasValue ? ToUnixSeconds(endDateTime.Value) : (int?)null
                }
            };
            var response = await _graphClient.SendQueryAsync<TimeSeriesResponse>(request);
            return response.Data.getTimeSeriesData;
        }


        /// <summary>
        /// Historical: For multi-day, multi-year ranges
        /// Suitable for long-term historical data (daily, weekly, monthly, etc.)
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="freq"> "day", "week", "month"</param>
        /// <param name="startDate">Uses date strings("YYYY-MM-DD") for start and end</param>
        /// <param name="endDate">Uses date strings("YYYY-MM-DD") for start and end</param>
        /// <returns></returns>
        public async Task<List<TimeSeriesPointItem>> getTimeSeriesData(string symbol, string freq, string startDate, string endDate)
        {
            var request = new GraphQLRequest
            {
                OperationName = "getTimeSeriesData",
                Query = @"
                query getTimeSeriesData(
                    $symbol: String!,
                    $freq: String,
                    $start: String,
                    $end: String
                ) {
                  getTimeSeriesData(
                    symbol: $symbol
                    freq: $freq
                    start: $start
                    end: $end
                  ) {
                    dateTime
                    open
                    high
                    low
                    close
                    volume
                  }
                }",
                Variables = new
                {
                    symbol = $"{symbol}",   //BCE:US",
                    freq = $"{freq}",       //"week",
                    start = $"{startDate}", //"2015-10-25",
                    end = $"{endDate}",     //"2020-10-25"
                }
            };
            var response = await _graphClient.SendQueryAsync<TimeSeriesResponse>(request);
            return response.Data.getTimeSeriesData;
        }




        //TMX getTimeSeriesData Compatibility Table
        //| **freq**   | **interval(Int)**             | **startDateTime / endDateTime(Unix seconds)**  | **start / end(String dates)**   | **Typical range / purpose**                    | **Supported by TMX?** | **Notes**                                                      |
        //| ---------- | ----------------------------- | ----------------------------------------------  | ------------------------------ | ---------------------------------------------- | --------------------- | -------------------------------------------------------------- |
        //| `"minute"` | ✅ Required(1, 5, 15, 30, 60) | ✅ Yes                                          | ❌ No                           | Intraday(same trading day or recent sessions) | ✅                     | Use for intraday charts.API returns fine-grained candles.     |
        //| `"hour"`   | ⚠️ Optional                   | ✅ Yes                                          | ❌ No                           | Multi-hour intraday view                       | ⚠️ Partial            | Works inconsistently — some symbols may return empty results.  |
        //| `"day"`    | ❌ Ignored                    | ❌ No                                           | ✅ Yes                          | Historical daily OHLC data                     | ✅                     | Use `start` and `end` in `"YYYY-MM-DD"` format.Most stable.   |
        //| `"week"`   | ❌ Ignored                    | ❌ No                                           | ✅ Yes                          | Weekly OHLC data                               | ✅                     | Great for mid-term (3–5 years) data.                           |
        //| `"month"`  | ❌ Ignored                    | ❌ No                                           | ✅ Yes                          | Monthly OHLC data                              | ✅                     | Long-term historical data(up to 10+ years).                   |
        //| `"year"`   | ❌ Ignored                    | ❌ No                                           | ✅ Yes                          | Yearly summary points                          | ⚠️ Sometimes          | Works for well-established tickers only; limited availability. |


        /// <summary>
        /// 
        /// </summary>
        /// <param name="symbol">"BCE:US" for BCE’s NYSE listing, or "BCE" for TSX.</param>
        /// <param name="interval">The time interval in minutes. interval = 1, 5, 15, 30, 60 → intraday candles.</param>
        /// 
        /// <returns></returns>


        /// <summary>
        /// How interval interacts with freq

        ///        When freq = "minute", you can use interval to specify the candle spacing:

        ///interval = 1 → 1-minute

        ///interval = 5 → 5-minute

        ///interval = 15 → 15-minute

        ///interval = 30 → 30-minute

        ///interval = 60 → hourly equivalent

        ///When freq is "day", "week", or "month", the interval parameter is ignored — those frequencies are fixed.
        /// </summary>
        /// <param name="symbol">"BCE:US" for BCE’s NYSE listing, or "BCE" for TSX.</param>
        /// <param name="freq">"minute"	1-minute intervals	Intraday charts	                    Often used with interval to specify 1, 5, 15, or 30 minutes.
        ///                    "hour"	Hourly candles      Short-term intraday                 Rarely used publicly, but supported on backend.
        ///                    "day"	Daily OHLC data     Standard daily historical charts    Most stable and reliable option for long spans.
        ///                    "week"	Weekly OHLC data    Medium-term trends                  Combines each week’s data into one candle.
        ///                    "month"	Monthly OHLC data   Long-term trend analysis            Great for 5-year+ views.
        ///                    "year"	Yearly aggregates   Long-term summary                   May not always return if symbol lacks history that long.</param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="interval"></param>
        /// <param name="startDateTime"></param>
        /// <param name="endDateTime"></param>
        /// <returns></returns>
        /// 

        private async Task<List<TimeSeriesPointItem>> GetTimeSeriesData(string symbol, string freq, string start = null, string end = null, int? interval = (int?)null, DateTime? startDateTime = null, DateTime? endDateTime = null)
        {

            // --- Build GraphQL request ---
            //var request = new GraphQLRequest
            //{
            //    OperationName = "getTimeSeriesData",
            //    Query = @"
            //    query getTimeSeriesData(
            //        $symbol: String!,
            //        $freq: String,
            //        $interval: Int,
            //        $start: String,
            //        $end: String,
            //        $startDateTime: Int,
            //        $endDateTime: Int
            //    ) {
            //      getTimeSeriesData(
            //        symbol: $symbol
            //        freq: $freq
            //        interval: $interval
            //        start: $start
            //        end: $end
            //        startDateTime: $startDateTime
            //        endDateTime: $endDateTime
            //      ) {
            //        dateTime
            //        open
            //        high
            //        low
            //        close
            //        volume
            //      }
            //    }",
            //    Variables = new
            //    {
            //        symbol = $"{symbol}",       // or "BCE" for TSX version
            //        interval = interval,            // e.g., 5-minute candles
            //        startDateTime = new DateTimeOffset(dateAndTime.ToUniversalTime()).ToUnixTimeSeconds(),//1761007560, // Unix timestamp (seconds)
            //        endDateTime = (int?)null
            //    }
            //};

            // --- GraphQL request (weekly frequency, string dates) ---
            var request = new GraphQLRequest
            {
                OperationName = "getTimeSeriesData",
                Query = @"
                query getTimeSeriesData(
                    $symbol: String!,
                    $freq: String,
                    $interval: Int,
                    $start: String,
                    $end: String,
                    $startDateTime: Int,
                    $endDateTime: Int
                ) {
                  getTimeSeriesData(
                    symbol: $symbol
                    freq: $freq
                    interval: $interval
                    start: $start
                    end: $end
                    startDateTime: $startDateTime
                    endDateTime: $endDateTime
                  ) {
                    dateTime
                    open
                    high
                    low
                    close
                    volume
                  }
                }",
                Variables = new
                {
                    symbol = $"{symbol}",       // or "BCE" for TSX version
                    //freq = "week",
                    //start = "2015-10-25",
                    //end = "2020-10-25",
                    interval = interval,            // e.g., 5-minute candles
                    //interval = (int?)null,
                    //startDateTime = (int?)null,
                    //endDateTime = (int?)null,
                            
                    
                    //startDateTime = new DateTimeOffset(dateAndTime.ToUniversalTime()).ToUnixTimeSeconds(),//1761007560, // Unix timestamp (seconds)
                   //endDateTime = (int?)null
                }
            };

            var response = await _graphClient.SendQueryAsync<TimeSeriesResponse>(request);
            return response.Data.getTimeSeriesData;

        }

        //public async Task<TimeSeriesResponse?> FetchAsync(string symbol, DateTime start, DateTime? end = null)
        //{
        //    // Normalize to UTC (TMX comparisons are safest in UTC)
        //    var nowUtc = DateTime.UtcNow;
        //    var startUtc = start.Kind == DateTimeKind.Utc ? start : start.ToUniversalTime();
        //    var endUtc = end.HasValue ? (end.Value.Kind == DateTimeKind.Utc ? end.Value : end.Value.ToUniversalTime()) : (DateTime?)null;

        //    // Decide mode
        //    var useIntraday = (nowUtc - startUtc) <= TimeSpan.FromDays(5);

        //    // Heuristic for historical frequency
        //    string freq = "day";
        //    if (!useIntraday)
        //    {
        //        var effectiveEnd = endUtc ?? nowUtc;
        //        var span = effectiveEnd - startUtc;
        //        if (span > TimeSpan.FromDays(8 * 365)) freq = "month";
        //        else if (span > TimeSpan.FromDays(2 * 365)) freq = "week";
        //        else freq = "day";
        //    }

        //    // Build request
        //    var request = new GraphQLRequest
        //    {
        //        OperationName = "getTimeSeriesData",
        //        Query = @"
        //        query getTimeSeriesData(
        //            $symbol: String!,
        //            $freq: String,
        //            $interval: Int,
        //            $start: String,
        //            $end: String,
        //            $startDateTime: Int,
        //            $endDateTime: Int
        //        ) {
        //          getTimeSeriesData(
        //            symbol: $symbol
        //            freq: $freq
        //            interval: $interval
        //            start: $start
        //            end: $end
        //            startDateTime: $startDateTime
        //            endDateTime: $endDateTime
        //          ) {
        //            dateTime
        //            open
        //            high
        //            low
        //            close
        //            volume
        //          }
        //        }",
        //        Variables = useIntraday
        //            ? new
        //            {
        //                symbol,
        //                freq = "minute",
        //                interval = 5,
        //                start = (string?)null,
        //                end = (string?)null,
        //                startDateTime = ToUnixSeconds(startUtc),
        //                endDateTime = endUtc.HasValue ? ToUnixSeconds(endUtc.Value) : (int?)null
        //            }
        //            : new
        //            {
        //                symbol,
        //                freq,
        //                interval = (int?)null,
        //                start = startUtc.ToString("yyyy-MM-dd"),
        //                end = (endUtc ?? nowUtc).ToString("yyyy-MM-dd"),
        //                startDateTime = (int?)null,
        //                endDateTime = (int?)null
        //            }
        //    };
        //}

        private static int ToUnixSeconds(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (int)(utc - epoch).TotalSeconds;
        }

        public static DateTime UnixSecondsToLocalDateTime(long unixTime)
        {
            //long unixTime = 1761007560; // Example: seconds since 1970-01-01 UTC

            DateTimeOffset dto = DateTimeOffset.FromUnixTimeSeconds(unixTime);
            DateTime utcDateTime = dto.UtcDateTime;         // UTC time
            DateTime localDateTime = dto.LocalDateTime;     // Converted to your local time zone
            return localDateTime;
        }

        //public async Task<List<Models.Market.TMXMarket>> GetMarketQuote()
        //{
        //    //{ "operationName":"getQuoteForSymbols","variables":{ "symbols":["^TSX","^JX:CA","^COMPX:US","^NYA:US"]},"query":"query getQuoteForSymbols($symbols: [String]) {getQuoteForSymbols(symbols: $symbols) {   symbol    longname   price    volume    openPrice    priceChange    percentChange    dayHigh    dayLow    prevClose    __typename  }}"}
        //    try
        //    {
        //        var stockTickerRequest = new GraphQLRequest
        //        {
        //            Query = @"query getQuoteForSymbols($symbols: [String]) {getQuoteForSymbols(symbols: $symbols) {   symbol    longname   price    volume    openPrice    priceChange    percentChange    dayHigh    dayLow    prevClose    __typename  }}",
        //            OperationName = "getQuoteForSymbols",
        //            Variables = new
        //            {
        //                symbols = new[] { "^TSX", "^JX:CA", "^COMPX:US", "^NYA:US" }
        //            }
        //        };

        //        // To use NewtonsoftJsonSerializer, add a reference to NuGet package GraphQL.Client.Serializer.Newtonsoft
        //        var client = new GraphQLHttpClient("https://app-money.tmx.com/graphql", new NewtonsoftJsonSerializer());
        //        var response = await client.SendQueryAsync<Core.TMX.Models.Market.Data>(stockTickerRequest);

        //        return response.Data.getQuoteForSymbols;
        //    }
        //    catch (Exception ex)
        //    {
        //        var msg = ex.Message;
        //    }

        //    return default;
        //}


    }
}
