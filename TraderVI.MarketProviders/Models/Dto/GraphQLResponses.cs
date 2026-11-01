using System;
using System.Collections.Generic;

namespace Core.TMX.Models.Dto
{
    /// <summary>
    /// All TMX GraphQL response wrapper types in one file.
    /// These mirror the GraphQL response shape exactly.
    /// </summary>

    // ─────── Time Series ───────
    public class TmxTimeSeriesResponse
    {
        public List<TmxTimeSeriesPointDto> getTimeSeriesData { get; set; } = new();
    }

    // ─────── Quotes ───────
    public class TmxQuoteResponse
    {
        public List<TmxQuoteDto> marketActivity { get; set; } = new();
        public List<TmxQuoteDto> futures { get; set; } = new();
        public List<TmxQuoteDto> commodities { get; set; } = new();
    }

    public class TmxQuoteDetailResponse
    {
        public TmxQuoteDetailDto getQuoteBySymbol { get; set; } = new();
    }

    // ─────── Market Data ───────
    public class TmxMarketMoversResponse
    {
        public TmxMarketMoverDto[] getMarketMovers { get; set; } = Array.Empty<TmxMarketMoverDto>();
    }

    public class TmxMarketSummaryResponse
    {
        public TmxMarketSummaryDto[] getMarketSummary { get; set; } = Array.Empty<TmxMarketSummaryDto>();
    }
}