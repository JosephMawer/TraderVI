using System.Globalization;

namespace Core.TMX.Models
{
    // ---------- Models ----------
    public class QuoteBySymbolResponse
    {
        public QuoteDetailItem getQuoteBySymbol { get; set; } = new();
    }

    public class QuoteDetailItem
    {

        // Identity / basics
        public string symbol { get; set; } = "";
        public string name { get; set; } = "";
        public string exchangeName { get; set; } = "";
        public string exShortName { get; set; } = "";
        public string exchangeCode { get; set; } = "";
        public string marketPlace { get; set; } = "";
        public string sector { get; set; } = "";
        public string industry { get; set; } = "";

        // Prices / changes
        public decimal? price { get; set; }
        public decimal? priceChange { get; set; }
        public decimal? percentChange { get; set; }
        public decimal? openPrice { get; set; }
        public decimal? dayHigh { get; set; }
        public decimal? dayLow { get; set; }
        public decimal? prevClose { get; set; }
        public decimal? close { get; set; }
        public decimal? vwap { get; set; }

        // Volume / shares
        public long? volume { get; set; }
        public long? shareOutStanding { get; set; }
        public long? totalSharesOutStanding { get; set; }
        public long? sharesESCROW { get; set; }

        // Valuation / fundamentals
        public decimal? MarketCap { get; set; }
        public decimal? MarketCapAllClasses { get; set; }
        public decimal? peRatio { get; set; }
        public decimal? priceToBook { get; set; }
        public decimal? priceToCashFlow { get; set; }
        public decimal? returnOnEquity { get; set; }
        public decimal? returnOnAssets { get; set; }
        public decimal? beta { get; set; }
        public decimal? eps { get; set; }
        public decimal? alpha { get; set; }

        // Averages / MAs / volumes
        public decimal? averageVolume10D { get; set; }
        public decimal? averageVolume20D { get; set; }
        public decimal? averageVolume30D { get; set; }
        public decimal? averageVolume50D { get; set; }
        public decimal? day21MovingAvg { get; set; }
        public decimal? day50MovingAvg { get; set; }
        public decimal? day200MovingAvg { get; set; }

        // Dividends
        public string dividendFrequency { get; set; } = "";
        public decimal? dividendYield { get; set; }
        public decimal? dividendAmount { get; set; }
        public string dividendCurrency { get; set; } = "";
        public string exDividendDate { get; set; } = "";   // TMX often returns formatted strings
        public string dividendPayDate { get; set; } = "";
        public decimal? dividend3Years { get; set; }
        public decimal? dividend5Years { get; set; }

        // 52-week stats
        public decimal? weeks52high { get; set; }
        public decimal? weeks52low { get; set; }

        // Company info
        public string longDescription { get; set; } = "";
        public string fulldescription { get; set; } = "";
        public string website { get; set; } = "";
        public string email { get; set; } = "";
        public string phoneNumber { get; set; } = "";
        public string fullAddress { get; set; } = "";
        public int? employees { get; set; }

        // Security metadata
        public string datatype { get; set; } = "";
        public string issueType { get; set; } = "";
        public string secType { get; set; } = "";
        public string qmdescription { get; set; } = "";

        //public string symbol { get; set; } = "";
        //public string name { get; set; } = "";
        //public string exchangeName { get; set; } = "";
        //public string exchangeCode { get; set; } = "";
        //public string marketPlace { get; set; } = "";
        //public string sector { get; set; } = "";
        //public string industry { get; set; } = "";
        //public string qmdescription { get; set; } = "";
        //public string website { get; set; } = "";
        //public decimal? price { get; set; }
        //public decimal? priceChange { get; set; }
        //public decimal? percentChange { get; set; }
        //public decimal? openPrice { get; set; }
        //public decimal? dayHigh { get; set; }
        //public decimal? dayLow { get; set; }
        //public decimal? prevClose { get; set; }
        //public decimal? peRatio { get; set; }
        //public decimal? dividendYield { get; set; }
        //public decimal? dividendAmount { get; set; }
        //public decimal? weeks52high { get; set; }
        //public decimal? weeks52low { get; set; }
        //public long? volume { get; set; }
        //public long? shareOutStanding { get; set; }
        //public long? totalSharesOutStanding { get; set; }
        //public decimal? MarketCap { get; set; }
        //public decimal? returnOnEquity { get; set; }

        public override string ToString()
            => $"{symbol} ({name}) — {price:C} ({percentChange:N2}%), Volume: {volume:N0}";
    }
    public class QuoteUiDto
    {
        // Core identity
        public string Symbol { get; init; } = "";
        public string Name { get; init; } = "";
        public string Exchange { get; init; } = "";

        // Raw numeric values (handy for sorting, charts, etc.)
        public decimal? Price { get; init; }
        public decimal? PriceChange { get; init; }
        public decimal? PercentChange { get; init; }       // e.g., 1.23 = +1.23%
        public decimal? DayHigh { get; init; }
        public decimal? DayLow { get; init; }
        public decimal? PrevClose { get; init; }
        public long? Volume { get; init; }
        public decimal? PE { get; init; }
        public decimal? DividendYield { get; init; }       // e.g., 8.12 = 8.12%
        public decimal? DividendAmount { get; init; }      // per distribution
        public decimal? Week52High { get; init; }
        public decimal? Week52Low { get; init; }
        public decimal? MarketCap { get; init; }

        public string Sector { get; init; } = "";
        public string Industry { get; init; } = "";
        public string Website { get; init; } = "";

        // Pre-formatted strings for UI
        public string PriceDisplay { get; init; } = "";
        public string ChangeDisplay { get; init; } = "";   // e.g., +0.12 (+0.45%)
        public string DayRangeDisplay { get; init; } = ""; // e.g., 4.90 – 5.15
        public string Range52WDisplay { get; init; } = ""; // e.g., 3.12 – 6.40
        public string VolumeDisplay { get; init; } = "";   // e.g., 1.23M
        public string MarketCapDisplay { get; init; } = ""; // e.g., $2.45B
        public string DividendDisplay { get; init; } = "";  // e.g., 8.12% ($0.08)
        public string PEDisplay { get; init; } = "";        // e.g., 14.7
    }
    public static class QuoteMappers
    {
        /// <summary>
        /// Maps QuoteDetail -> QuoteUiDto with sensible formatting for UI.
        /// </summary>
        public static QuoteUiDto ToUiDto(this QuoteDetailItem q, CultureInfo? culture = null, string currencySymbol = "$")
        {
            culture ??= new CultureInfo("en-CA");

            string Money(decimal? v) =>
                v.HasValue ? string.Format(culture, "{0}{1:N2}", currencySymbol, v.Value) : "—";

            string Number0(decimal? v) =>
                v.HasValue ? v.Value.ToString("N0", culture) : "—";

            string Percent(decimal? v) =>
                v.HasValue ? v.Value.ToString("0.##", culture) + "%" : "—";

            string Signed(decimal? v, string fmt = "N2") =>
                v.HasValue ? (v.Value >= 0 ? "+" : "") + v.Value.ToString(fmt, culture) : "—";

            string CompactLong(long? n)
            {
                if (!n.HasValue) return "—";
                var v = n.Value;
                if (v >= 1_000_000_000) return (v / 1_000_000_000D).ToString("0.##", culture) + "B";
                if (v >= 1_000_000) return (v / 1_000_000D).ToString("0.##", culture) + "M";
                if (v >= 1_000) return (v / 1_000D).ToString("0.##", culture) + "K";
                return v.ToString("N0", culture);
            }

            string CompactMoney(decimal? v)
            {
                if (!v.HasValue) return "—";
                var x = v.Value;
                var sign = x < 0 ? "-" : "";
                x = System.Math.Abs(x);
                if (x >= 1_000_000_000) return $"{sign}{currencySymbol}{(x / 1_000_000_000M):0.##}B";
                if (x >= 1_000_000) return $"{sign}{currencySymbol}{(x / 1_000_000M):0.##}M";
                if (x >= 1_000) return $"{sign}{currencySymbol}{(x / 1_000M):0.##}K";
                return $"{sign}{currencySymbol}{x:0.##}";
            }

            var change = q.priceChange;
            var pct = q.percentChange; // usually already 2-decimal-ish number, not 0.01 ratio

            var changeDisplay = (change.HasValue || pct.HasValue)
                ? $"{Signed(change)} ({Percent(pct)})"
                : "—";

            var dayRange = (q.dayLow.HasValue || q.dayHigh.HasValue)
                ? $"{Money(q.dayLow)} – {Money(q.dayHigh)}"
                : "—";

            var w52Range = (q.weeks52low.HasValue || q.weeks52high.HasValue)
                ? $"{Money(q.weeks52low)} – {Money(q.weeks52high)}"
                : "—";

            var dividendDisplay =
                (q.dividendYield.HasValue || q.dividendAmount.HasValue)
                    ? $"{Percent(q.dividendYield)}{(q.dividendAmount.HasValue ? $" ({Money(q.dividendAmount)})" : "")}"
                    : "—";

            return new QuoteUiDto
            {
                // identity
                Symbol = q.symbol,
                Name = q.name,
                Exchange = $"{q.exchangeName} ({q.exchangeCode})".Trim(),

                // raw
                Price = q.price,
                PriceChange = q.priceChange,
                PercentChange = q.percentChange,
                DayHigh = q.dayHigh,
                DayLow = q.dayLow,
                PrevClose = q.prevClose,
                Volume = q.volume,
                PE = q.peRatio,
                DividendYield = q.dividendYield,
                DividendAmount = q.dividendAmount,
                Week52High = q.weeks52high,
                Week52Low = q.weeks52low,
                MarketCap = q.MarketCap,
                Sector = q.sector,
                Industry = q.industry,
                Website = q.website,

                // formatted
                PriceDisplay = Money(q.price),
                ChangeDisplay = changeDisplay,
                DayRangeDisplay = dayRange,
                Range52WDisplay = w52Range,
                VolumeDisplay = CompactLong(q.volume),
                MarketCapDisplay = CompactMoney(q.MarketCap),
                DividendDisplay = dividendDisplay,
                PEDisplay = q.peRatio.HasValue ? q.peRatio.Value.ToString("0.##", culture) : "—"
            };
        }

    }
}