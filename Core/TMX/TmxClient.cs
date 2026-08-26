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
        private static readonly TimeZoneInfo TmxEasternTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        private const int MaximumTransportAttempts = 3;
        private static readonly TimeSpan IntradayChunkWindow = TimeSpan.FromDays(5);
        private static readonly TimeSpan IntradayChunkPause = TimeSpan.FromMilliseconds(250);

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
        /// Gets intraday time-series data using TMX's current interval and
        /// Unix-time request contract. Intraday requests intentionally omit
        /// the historical <c>freq</c> argument; TMX uses that argument for
        /// daily and longer aggregations.
        /// Returns canonical OhlcvBar with UTC timestamps.
        /// </summary>
        /// <param name="symbol">e.g., "BCE" or "BCE:US"</param>
        /// <param name="interval">1, 5, 15, 30, 60</param>
        /// <param name="startDateTime">UTC start time</param>
        /// <param name="endDateTime">UTC end time (optional)</param>
        public async Task<List<OhlcvBar>> GetIntradayTimeSeriesAsync(
            string symbol,
            int interval,
            DateTime startDateTime,
            DateTime? endDateTime = null,
            CancellationToken ct = default)
        {
            TmxIntradayBatch batch = await GetIntradayTimeSeriesBatchAsync(
                symbol,
                interval,
                startDateTime,
                endDateTime,
                ct);
            return batch.Bars.ToList();
        }

        /// <summary>
        /// Gets one validated intraday response together with its request and
        /// receipt metadata. Use this method when the evidence will drive a
        /// delayed decision or be persisted for later replay.
        /// </summary>
        public async Task<TmxIntradayBatch> GetIntradayTimeSeriesBatchAsync(
            string symbol,
            int interval,
            DateTime startDateTime,
            DateTime? endDateTime = null,
            CancellationToken ct = default)
        {
            DateTime fetchStartedUtc = DateTime.UtcNow;
            GraphQLRequest request = BuildIntradayTimeSeriesRequest(
                symbol,
                interval,
                startDateTime,
                endDateTime);

            RetryResult<GraphQLResponse<TmxTimeSeriesResponse>> transport =
                await SendQueryWithRetryAsync<TmxTimeSeriesResponse>(request, ct);
            DateTime receivedUtc = DateTime.UtcNow;
            GraphQLResponse<TmxTimeSeriesResponse> response = transport.Value;
            if (response.Errors is { Length: > 0 })
                throw new InvalidOperationException(
                    $"TMX GraphQL errors: {string.Join(" | ", response.Errors.Select(e => e.Message))}");

            // Map DTO → Domain — skip bars with null OHLCV (halted/suspended days).
            List<OhlcvBar> bars = response.Data?.getTimeSeriesData
                .Where(p => p.IsComplete)
                .Select(TmxMapper.ToOhlcvBar)
                .ToList()
                ?? [];

            List<OhlcvBar> validated = ValidateIntradayResponse(
                bars,
                interval,
                startDateTime,
                endDateTime);

            DateTime requestedStartUtc = FloorToUtcMinute(startDateTime);
            DateTime requestedEndUtc = endDateTime.HasValue
                ? FloorToUtcMinute(endDateTime.Value)
                : FloorToUtcMinute(fetchStartedUtc);

            return new TmxIntradayBatch(
                symbol.Trim(),
                interval,
                requestedStartUtc,
                requestedEndUtc,
                fetchStartedUtc,
                receivedUtc,
                transport.AttemptCount,
                RequestCount: 1,
                validated.AsReadOnly());
        }

        /// <summary>
        /// Gets a wide intraday range through explicit five-calendar-day TMX
        /// requests, then validates and deduplicates the combined result.
        /// This avoids TMX's observed 754-bar single-response cap.
        /// </summary>
        public async Task<TmxIntradayBatch> GetIntradayTimeSeriesChunkedAsync(
            string symbol,
            int interval,
            DateTime startDateTime,
            DateTime endDateTime,
            CancellationToken ct = default)
        {
            IReadOnlyList<IntradayRequestWindow> windows = BuildIntradayRequestWindows(
                startDateTime,
                endDateTime);
            DateTime fetchStartedUtc = DateTime.UtcNow;
            DateTime receivedUtc = fetchStartedUtc;
            int attemptCount = 0;
            var bars = new List<OhlcvBar>();

            for (int index = 0; index < windows.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                IntradayRequestWindow window = windows[index];
                TmxIntradayBatch chunk = await GetIntradayTimeSeriesBatchAsync(
                    symbol,
                    interval,
                    window.StartUtc,
                    window.EndUtc,
                    ct);

                bars.AddRange(chunk.Bars);
                attemptCount += chunk.AttemptCount;
                receivedUtc = chunk.ReceivedUtc;

                if (index < windows.Count - 1)
                    await Task.Delay(IntradayChunkPause, ct);
            }

            List<OhlcvBar> merged = MergeChunkBars(bars);
            List<OhlcvBar> validated = ValidateIntradayResponse(
                merged,
                interval,
                windows[0].StartUtc,
                windows[^1].EndUtc);

            return new TmxIntradayBatch(
                symbol.Trim(),
                interval,
                windows[0].StartUtc,
                windows[^1].EndUtc,
                fetchStartedUtc,
                receivedUtc,
                attemptCount,
                windows.Count,
                validated.AsReadOnly());
        }

        internal static GraphQLRequest BuildIntradayTimeSeriesRequest(
            string symbol,
            int interval,
            DateTime startDateTime,
            DateTime? endDateTime)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

            ValidateIntradayInterval(interval);

            DateTime startUtc = FloorToUtcMinute(startDateTime);
            DateTime? endUtc = endDateTime.HasValue
                ? FloorToUtcMinute(endDateTime.Value)
                : null;

            if (endUtc.HasValue && endUtc.Value <= startUtc)
                throw new ArgumentException(
                    "TMX intraday end time must be later than the start time.",
                    nameof(endDateTime));

            return new GraphQLRequest
            {
                OperationName = "getTimeSeriesData",
                Query = @"
                query getTimeSeriesData(
                    $symbol: String!,
                    $interval: Int,
                    $startDateTime: Int,
                    $endDateTime: Int
                ) {
                  getTimeSeriesData(
                    symbol: $symbol
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
                    symbol = symbol.Trim(),
                    interval,
                    startDateTime = ToUnixSeconds(startUtc),
                    endDateTime = endUtc.HasValue ? ToUnixSeconds(endUtc.Value) : (int?)null
                }
            };
        }

        internal static IReadOnlyList<IntradayRequestWindow> BuildIntradayRequestWindows(
            DateTime startDateTime,
            DateTime endDateTime)
        {
            DateTime startUtc = FloorToUtcMinute(startDateTime);
            DateTime endUtc = FloorToUtcMinute(endDateTime);
            if (endUtc <= startUtc)
                throw new ArgumentException(
                    "TMX intraday end time must be later than the start time.",
                    nameof(endDateTime));

            var windows = new List<IntradayRequestWindow>();
            DateTime windowStartUtc = startUtc;
            while (windowStartUtc < endUtc)
            {
                DateTime windowEndUtc = windowStartUtc + IntradayChunkWindow;
                if (windowEndUtc > endUtc)
                    windowEndUtc = endUtc;
                windows.Add(new IntradayRequestWindow(windowStartUtc, windowEndUtc));
                windowStartUtc = windowEndUtc;
            }

            return windows.AsReadOnly();
        }

        internal static List<OhlcvBar> MergeChunkBars(
            IReadOnlyCollection<OhlcvBar> sourceBars)
        {
            var merged = new List<OhlcvBar>();
            foreach (IGrouping<DateTime, OhlcvBar> group in sourceBars
                .GroupBy(bar => bar.TimestampUtc)
                .OrderBy(group => group.Key))
            {
                OhlcvBar canonical = group.First();
                if (group.Skip(1).Any(bar => bar != canonical))
                    throw new InvalidOperationException(
                        $"TMX returned conflicting bars at chunk boundary {group.Key:O}.");
                merged.Add(canonical);
            }

            return merged;
        }

        internal static List<OhlcvBar> ValidateIntradayResponse(
            IReadOnlyCollection<OhlcvBar> sourceBars,
            int interval,
            DateTime startDateTime,
            DateTime? endDateTime)
        {
            ValidateIntradayInterval(interval);

            List<OhlcvBar> bars = sourceBars
                .OrderBy(bar => bar.TimestampUtc)
                .ToList();

            if (bars.Count == 0)
                return bars;

            if (bars.GroupBy(bar => bar.TimestampUtc).Any(group => group.Count() > 1))
                throw new InvalidOperationException(
                    "TMX returned duplicate timestamps for an intraday request.");

            OhlcvBar nonUtc = bars.FirstOrDefault(bar =>
                bar.TimestampUtc.Kind != DateTimeKind.Utc);
            if (nonUtc is not null)
                throw new InvalidOperationException(
                    $"TMX intraday timestamp is not UTC: {nonUtc.TimestampUtc:O}.");

            OhlcvBar invalidOhlc = bars.FirstOrDefault(bar =>
                bar.Open <= 0m ||
                bar.High <= 0m ||
                bar.Low <= 0m ||
                bar.Close <= 0m ||
                bar.Low > bar.High ||
                bar.Low > System.Math.Min(bar.Open, bar.Close) ||
                bar.High < System.Math.Max(bar.Open, bar.Close));
            if (invalidOhlc is not null)
                throw new InvalidOperationException(
                    $"TMX returned an invalid OHLC range at {invalidOhlc.TimestampUtc:O}.");

            OhlcvBar negativeVolume = bars.FirstOrDefault(bar => bar.Volume < 0);
            if (negativeVolume is not null)
                throw new InvalidOperationException(
                    $"TMX returned negative volume at {negativeVolume.TimestampUtc:O}.");

            OhlcvBar misaligned = bars.FirstOrDefault(bar =>
            {
                if (bar.TimestampUtc.Second != 0 ||
                    bar.TimestampUtc.Millisecond != 0 ||
                    bar.TimestampUtc.Ticks % TimeSpan.TicksPerMinute != 0)
                {
                    return true;
                }

                DateTime local = TimeZoneInfo.ConvertTimeFromUtc(
                    bar.TimestampUtc,
                    TmxEasternTimeZone);
                int minutesFromRegularOpen =
                    (int)local.TimeOfDay.TotalMinutes - (9 * 60 + 30);
                return minutesFromRegularOpen % interval != 0;
            });
            if (misaligned is not null)
                throw new InvalidOperationException(
                    $"TMX timestamp {misaligned.TimestampUtc:O} does not align to a " +
                    $"{interval}-minute interval anchored at the 9:30 a.m. TSX open.");

            DateTime startUtc = FloorToUtcMinute(startDateTime);
            DateTime? endUtc = endDateTime.HasValue
                ? FloorToUtcMinute(endDateTime.Value)
                : null;
            TimeSpan boundaryTolerance = TimeSpan.FromMinutes(interval);

            bool outsideWindow = bars.Any(bar =>
                bar.TimestampUtc < startUtc - boundaryTolerance ||
                endUtc.HasValue && bar.TimestampUtc > endUtc.Value + boundaryTolerance);
            if (outsideWindow)
                throw new InvalidOperationException(
                    "TMX returned timestamps outside the requested intraday window; " +
                    "the response may be a lower-frequency fallback.");

            var sessions = bars
                .GroupBy(bar => TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(bar.TimestampUtc, DateTimeKind.Utc),
                    TmxEasternTimeZone).Date)
                .ToList();
            bool looksLikeDailyFallback =
                sessions.Count >= 2 &&
                sessions.All(session => session.Count() == 1) &&
                bars.All(bar => TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(bar.TimestampUtc, DateTimeKind.Utc),
                    TmxEasternTimeZone).TimeOfDay == new TimeSpan(16, 0, 0));
            if (looksLikeDailyFallback)
                throw new InvalidOperationException(
                    "TMX returned one 4:00 p.m. bar per session for an intraday request; " +
                    "this is daily fallback data, not intraday evidence.");

            return bars;
        }

        private static void ValidateIntradayInterval(int interval)
        {
            if (interval is not (1 or 5 or 15 or 30 or 60))
                throw new ArgumentOutOfRangeException(
                    nameof(interval),
                    interval,
                    "TMX intraday interval must be 1, 5, 15, 30, or 60 minutes.");
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
        // QUOTES (Current snapshots; freshness depends on provider/source)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets current quote snapshots for multiple symbols.
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

            RetryResult<GraphQLResponse<TmxQuoteResponse>> transport =
                await SendQueryWithRetryAsync<TmxQuoteResponse>(request, ct);
            GraphQLResponse<TmxQuoteResponse> response = transport.Value;
            if (response.Errors is { Length: > 0 })
                throw new InvalidOperationException(
                    $"TMX GraphQL errors: {string.Join(" | ", response.Errors.Select(e => e.Message))}");

            return response.Data?.marketActivity
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
        /// </summary>
        public async Task<List<Models.Domain.SectorIndexSnapshot>> GetSectorIndicesAsync(
            DateTime? tradingDate = null,
            CancellationToken ct = default)
        {
            return await GetSectorIndicesAsync(TsxSectorSymbols.AllSymbols, tradingDate, ct);
        }

        /// <summary>
        /// Gets current-day snapshots for the specified sector index symbols.
        /// </summary>
        public async Task<List<Models.Domain.SectorIndexSnapshot>> GetSectorIndicesAsync(
            string[] symbols,
            DateTime? tradingDate = null,
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

            var date = tradingDate ?? DateTime.Today;

            return response.Data?.marketActivity
                .Where(q => q.symbol != null)
                .Select(q => new Models.Domain.SectorIndexSnapshot(
                    Symbol: q.symbol,
                    SectorName: TsxSectorSymbols.GetName(q.symbol),
                    Price: q.price ?? 0m,
                    PriceChange: q.priceChange ?? 0m,
                    PercentChange: q.percentChange ?? 0m,
                    Date: date))
                .ToList()
                ?? [];
        }


        // ═══════════════════════════════════════════════════════════════════
        // BENCHMARK INDICES
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets current-day snapshots for benchmark indices (TSX Composite, TSX 60, TXCE).
        /// Reuses the same <c>getQuoteForSymbols</c> query used for sector indices.
        /// </summary>
        public async Task<List<Models.Domain.SectorIndexSnapshot>> GetBenchmarkIndicesAsync(
            DateTime? tradingDate = null,
            CancellationToken ct = default)
        {
            return await GetSectorIndicesAsync(TsxBenchmarkSymbols.AllIndexSymbols.ToArray(), tradingDate, ct);
        }

        // ═══════════════════════════════════════════════════════════════════
        // UTILITIES
        // ═══════════════════════════════════════════════════════════════════

        private async Task<RetryResult<GraphQLResponse<TResponse>>> SendQueryWithRetryAsync<TResponse>(
            GraphQLRequest request,
            CancellationToken ct)
        {
            return await ExecuteWithRetryAsync(
                token => _graphClient.SendQueryAsync<TResponse>(request, token),
                ct);
        }

        internal static async Task<RetryResult<T>> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct,
            Func<TimeSpan, CancellationToken, Task> delayAsync = null)
        {
            ArgumentNullException.ThrowIfNull(operation);
            delayAsync ??= static (delay, token) => Task.Delay(delay, token);

            for (int attempt = 1; attempt <= MaximumTransportAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    T value = await operation(ct);
                    return new RetryResult<T>(value, attempt);
                }
                catch (Exception ex) when (
                    attempt < MaximumTransportAttempts &&
                    IsTransientTransportFailure(ex, ct))
                {
                    await delayAsync(TimeSpan.FromSeconds(attempt), ct);
                }
            }

            throw new InvalidOperationException("TMX retry loop completed without a result.");
        }

        internal static bool IsTransientTransportFailure(
            Exception exception,
            CancellationToken ct)
        {
            return exception switch
            {
                GraphQLHttpRequestException graphQlHttp =>
                    IsTransientStatusCode(graphQlHttp.StatusCode),
                HttpRequestException http =>
                    !http.StatusCode.HasValue ||
                    IsTransientStatusCode(http.StatusCode.Value),
                TaskCanceledException => !ct.IsCancellationRequested,
                _ => false
            };
        }

        private static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
            statusCode is HttpStatusCode.RequestTimeout or
                HttpStatusCode.TooManyRequests ||
            (int)statusCode >= 500;

        private static DateTime FloorToUtcMinute(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc
                ? value
                : value.ToUniversalTime();
            return new DateTime(
                utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute,
                DateTimeKind.Utc);
        }

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

    internal readonly record struct RetryResult<T>(T Value, int AttemptCount);

    internal readonly record struct IntradayRequestWindow(
        DateTime StartUtc,
        DateTime EndUtc);
}
