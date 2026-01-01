using System;

namespace Core.TMX.Models.Domain
{
    /// <summary>
    /// Canonical OHLCV bar used across the application (backtesting, ML, storage).
    /// All timestamps in UTC.
    /// </summary>
    public record OhlcvBar(
        DateTime TimestampUtc,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume
    )
    {
        public override string ToString() =>
            $"{TimestampUtc:yyyy-MM-dd HH:mm} O:{Open} H:{High} L:{Low} C:{Close} V:{Volume:N0}";
    }


    /// <summary>
    /// Canonical real-time quote snapshot (domain model).
    /// Normalized to PascalCase C# conventions.
    /// </summary>
    public record QuoteSnapshot(
        string Symbol,
        string LongName,
        decimal? Price,
        decimal? PriceChange,
        decimal? PercentChange,
        decimal? OpenPrice,
        decimal? DayHigh,
        decimal? DayLow,
        decimal? PrevClose,
        decimal? Bid,
        decimal? Ask,
        decimal? Week52High,
        decimal? Week52Low,
        long? Volume,
        string Currency = "",
        string Exchange = ""
    )
    {
        public override string ToString() =>
            $"{Symbol,-8} {Price,10:C} ({PercentChange,6:N2}%)";
    }

    /// <summary>
    /// Canonical market summary (domain model).
    /// </summary>
    public record MarketSummary(
        string Exchange,
        long TotalVolume,
        int Advancers,
        int Decliners,
        int Unchanged
    )
    {
        public override string ToString() =>
            $"{Exchange}: Vol={TotalVolume:N0}, Adv={Advancers}, Dec={Decliners}, Unch={Unchanged}";
    }
}





