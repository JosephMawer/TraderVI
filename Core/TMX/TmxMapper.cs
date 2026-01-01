using Core.Db;
using Core.TMX.Models.Domain;
using Core.TMX.Models.Dto;
using System;
using System.Globalization;
using System.Linq;

namespace Core.TMX
{
    /// <summary>
    /// Maps TMX DTOs to canonical domain models.
    /// </summary>
    internal static class TmxMapper
    {
        private static readonly TimeZoneInfo EasternTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        /// <summary>
        /// Converts TMX time-series point (DTO) → canonical OhlcvBar (UTC timestamp).
        /// </summary>
        public static OhlcvBar ToOhlcvBar(TmxTimeSeriesPointDto dto)
        {
            // Parse TMX's local time string (assumes Eastern Time)
            if (!DateTime.TryParse(dto.dateTime, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var localDt))
                localDt = DateTime.Parse(dto.dateTime);

            var unspecified = DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, EasternTimeZone);

            return new OhlcvBar(utc, dto.open, dto.high, dto.low, dto.close, dto.volume);
        }

        /// <summary>
        /// Converts TMX quote DTO → canonical QuoteSnapshot.
        /// </summary>
        public static QuoteSnapshot ToQuoteSnapshot(TmxQuoteDto dto)
        {
            return new QuoteSnapshot(
                Symbol: dto.symbol,
                LongName: dto.longname ?? dto.shortName ?? "",
                Price: dto.price,
                PriceChange: dto.priceChange,
                PercentChange: dto.percentChange,
                OpenPrice: dto.openPrice,
                DayHigh: dto.dayHigh,
                DayLow: dto.dayLow,
                PrevClose: dto.prevClose,
                Bid: dto.bid,
                Ask: dto.ask,
                Week52High: dto.weeks52high,
                Week52Low: dto.weeks52low,
                Volume: dto.volume,
                Currency: dto.currency,
                Exchange: dto.exchange
            );
        }

        /// <summary>
        /// Converts TMX market summary DTO → canonical MarketSummary.
        /// </summary>
        public static Models.Domain.MarketSummary ToMarketSummary(TmxMarketSummaryDto dto)
        {
            return new Models.Domain.MarketSummary(
                Exchange: dto.exchange,
                TotalVolume: dto.totalVolume,
                Advancers: dto.advancers,
                Decliners: dto.decliners,
                Unchanged: dto.unchanged
            );
        }
    }
}