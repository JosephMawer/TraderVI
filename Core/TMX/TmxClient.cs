using Core.Db;
using Core.TMX.Models.Domain;
using Core.TMX.Models.Dto;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Core.TMX
{
    /// <summary>
    /// TMX Money GraphQL API client.
    /// Returns canonical domain models (not TMX DTOs).
    /// Production tips:
    /// - Respect rate limits (e.g., once per minute)
    /// - Cache responses locally
    /// - Review TMX ToS and robots.txt
    /// </summary>
    public sealed class TmxClient : IDisposable
    {
        private readonly GraphQLHttpClient _graphClient;
        private readonly HttpClient _httpClient;
        private readonly HttpClientHandler _handler;
        private static readonly Uri GraphQLEndpoint = new("https://app-money.tmx.com/graphql");

        public TmxClient()
        {
            var cookieContainer = new CookieContainer();
            _handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(_handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("Origin", "https://money.tmx.com");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://money.tmx.com/");
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var options = new GraphQLHttpClientOptions { EndPoint = GraphQLEndpoint };
            _graphClient = new GraphQLHttpClient(options, new NewtonsoftJsonSerializer(), _httpClient);
        }

        // ═══════════════════════════════════════════════════════════════════
        // TIME SERIES (OHLCV Bars)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets intraday time-series data (minute/hour intervals).
        /// Returns canonical OhlcvBar with UTC timestamps.
        /// </summary>
        /// <param name="symbol">e.g., "BCE" or "BCE:US"</param>
        /// <param name="freq">"minute" or "hour"</param>
        /// <param name="interval">1, 5, 15, 30, 60</param>
        /// <param name="startDateTime">UTC start time</param>
        /// <param name="endDateTime">UTC end time (optional)</param>
        public async Task<List<OhlcvBar>> GetIntradayTimeSeriesAsync(
            string symbol,
            string freq,
            int interval,
            DateTime startDateTime,
            DateTime? endDateTime = null,
            CancellationToken ct = default)
        {
            if (startDateTime.Kind != DateTimeKind.Utc)
                startDateTime = startDateTime.ToUniversalTime();
            if (endDateTime.HasValue && endDateTime.Value.Kind != DateTimeKind.Utc)
                endDateTime = endDateTime.Value.ToUniversalTime();

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
                    symbol,
                    freq,
                    interval,
                    startDateTime = ToUnixSeconds(startDateTime),
                    endDateTime = endDateTime.HasValue ? ToUnixSeconds(endDateTime.Value) : (int?)null
                }
            };

            var response = await _graphClient.SendQueryAsync<TmxTimeSeriesResponse>(request, ct);
            if (response.Errors is { Length: > 0 })
                throw new InvalidOperationException(
                    $"TMX GraphQL errors: {string.Join(" | ", response.Errors.Select(e => e.Message))}");

            // Map DTO → Domain — skip bars with null OHLCV (halted/suspended days)
            return response.Data?.getTimeSeriesData
                .Where(p => p.IsComplete)
                .Select(TmxMapper.ToOhlcvBar)
                .ToList()
                ?? new List<OhlcvBar>();
        }

        /// <summary>
        /// Gets historical time-series data (daily/weekly/monthly).
        /// Returns canonical OhlcvBar with UTC timestamps.
        /// </summary>
        /// <param name="symbol">e.g., "BCE"</param>
        /// <param name="freq">"day", "week", or "month"</param>
        /// <param name="startDate">"YYYY-MM-DD"</param>
        /// <param name="endDate">"YYYY-MM-DD"</param>
        public async Task<List<OhlcvBar>> GetHistoricalTimeSeriesAsync(
            string symbol,
            string freq,
            string startDate,
            string endDate,
            CancellationToken ct = default)
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
                Variables = new { symbol, freq, start = startDate, end = endDate }
            };

            var response = await _graphClient.SendQueryAsync<TmxTimeSeriesResponse>(request, ct);
            if (response.Errors is { Length: > 0 })
                throw new InvalidOperationException(
                    $"TMX GraphQL errors: {string.Join(" | ", response.Errors.Select(e => e.Message))}");

            // Map DTO → Domain — skip bars with null OHLCV (halted/suspended days)
            return response.Data?.getTimeSeriesData
                .Where(p => p.IsComplete)
                .Select(TmxMapper.ToOhlcvBar)
                .ToList()
                ?? new List<OhlcvBar>();
        }

        // ═══════════════════════════════════════════════════════════════════
        // QUOTES (Real-time snapshots)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets real-time quotes for multiple symbols.
        /// Returns canonical QuoteSnapshot models.
        /// </summary>
        public async Task<List<QuoteSnapshot>> GetQuotesBySymbolsAsync(
            string[] symbols,
            CancellationToken ct = default)
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

            // Basic retry on transient failures
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var resp = await _graphClient.SendQueryAsync<TmxQuoteResponse>(request, ct);
                    if (resp.Errors is { Length: > 0 })
                        throw new InvalidOperationException(
                            $"TMX GraphQL errors: {string.Join(" | ", resp.Errors.Select(e => e.Message))}");

                    return resp.Data?.marketActivity
                        .Select(TmxMapper.ToQuoteSnapshot)
                        .ToList()
                        ?? new List<QuoteSnapshot>();
                }
                catch (HttpRequestException) when (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                }
            }

            // Last attempt without catching to surface error
            var final = await _graphClient.SendQueryAsync<TmxQuoteResponse>(request, ct);
            if (final.Errors is { Length: > 0 })
                throw new InvalidOperationException(
                    $"TMX GraphQL errors: {string.Join(" | ", final.Errors.Select(e => e.Message))}");

            return final.Data?.marketActivity
                .Select(TmxMapper.ToQuoteSnapshot)
                .ToList()
                ?? new List<QuoteSnapshot>();
        }

        /// <summary>
        /// Gets detailed quote information for a single symbol (includes fundamentals).
        /// Returns raw TMX DTO (too many fields to map cleanly to domain model).
        /// </summary>
        public async Task<TmxQuoteDetailDto> GetQuoteDetailAsync(
            string symbol,
            CancellationToken ct = default)
        {
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
                Variables = new { symbol, locale = "en" }
            };

            var response = await _graphClient.SendQueryAsync<TmxQuoteDetailResponse>(request, ct);

            if (response.Errors?.Length > 0)
                throw new InvalidOperationException(
                    $"TMX GraphQL errors: {string.Join(", ", response.Errors.Select(e => e.Message))}");

            return response.Data.getQuoteBySymbol;
        }

        // ═══════════════════════════════════════════════════════════════════
        // MARKET DATA
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets market movers (most active, gainers, losers).
        /// Returns raw TMX DTOs (caller can map if needed).
        /// </summary>
        public async Task<TmxMarketMoverDto[]> GetMarketMoversAsync(
            string sortOrder = "dollarvolume",
            string statExchange = "tsx",
            int marketId = 11,
            int limit = 50,
            CancellationToken ct = default)
        {
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
                Variables = new { sortOrder, statExchange, marketId, limit }
            };

            var response = await _graphClient.SendQueryAsync<TmxMarketMoversResponse>(request, ct);

            if (response.Errors?.Length > 0)
                throw new InvalidOperationException(
                    $"TMX GraphQL errors: {string.Join(", ", response.Errors.Select(e => e.Message))}");

            return response.Data.getMarketMovers;
        }

        /// <summary>
        /// Gets market summary (advancers/decliners).
        /// Returns canonical MarketSummary models.
        /// </summary>
        public async Task<List<Models.Domain.MarketSummary>> GetMarketSummaryAsync(
            string market = "caMarket",
            CancellationToken ct = default)
        {
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
                Variables = new { market }
            };

            var response = await _graphClient.SendQueryAsync<TmxMarketSummaryResponse>(request, ct);

            if (response.Errors?.Length > 0)
                throw new InvalidOperationException(
                    $"TMX GraphQL errors: {string.Join(", ", response.Errors.Select(e => e.Message))}");

            return response.Data.getMarketSummary
                .Select(TmxMapper.ToMarketSummary)
                .ToList();
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECTOR INDICES
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets current-day snapshots for all TSX sector sub-indices.
        /// Reuses <c>getQuoteForSymbols</c> — TMX treats indices as regular symbols
        /// differentiated by the <c>^</c> prefix.
        /// </summary>
        public async Task<List<Models.Domain.SectorIndexSnapshot>> GetSectorIndicesAsync(
            CancellationToken ct = default)
        {
            return await GetSectorIndicesAsync(TsxSectorSymbols.AllSymbols, ct);
        }

        /// <summary>
        /// Gets current-day snapshots for the specified sector index symbols.
        /// </summary>
        public async Task<List<Models.Domain.SectorIndexSnapshot>> GetSectorIndicesAsync(
            string[] symbols,
            CancellationToken ct = default)
        {
            var request = new GraphQLRequest
            {
                OperationName = "getQuoteForSymbols",
                Query = @"
                query getQuoteForSymbols($activity: [String]) {
                  marketActivity: getQuoteForSymbols(symbols: $activity) {
                    symbol
                    price
                    priceChange
                    percentChange
                    __typename
                  }
                }",
                Variables = new { activity = symbols }
            };

            var response = await _graphClient.SendQueryAsync<TmxQuoteResponse>(request, ct);

            if (response.Errors is { Length: > 0 })
                throw new InvalidOperationException(
                    $"TMX GraphQL errors: {string.Join(" | ", response.Errors.Select(e => e.Message))}");

            var today = DateTime.Today;

            return response.Data?.marketActivity
                .Where(q => q.symbol != null)
                .Select(q => new Models.Domain.SectorIndexSnapshot(
                    Symbol: q.symbol,
                    SectorName: TsxSectorSymbols.GetName(q.symbol),
                    Price: q.price ?? 0m,
                    PriceChange: q.priceChange ?? 0m,
                    PercentChange: q.percentChange ?? 0m,
                    Date: today))
                .ToList()
                ?? [];
        }

        // ═══════════════════════════════════════════════════════════════════
        // UTILITIES
        // ═══════════════════════════════════════════════════════════════════

        private static int ToUnixSeconds(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (int)(utc - epoch).TotalSeconds;
        }

        public void Dispose()
        {
            _graphClient?.Dispose();
            _httpClient?.Dispose();
            _handler?.Dispose();
        }
    }
}