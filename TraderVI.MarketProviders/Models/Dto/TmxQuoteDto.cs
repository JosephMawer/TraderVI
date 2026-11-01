
/// <summary>
/// TMX GraphQL DTO for getQuoteForSymbols response.
/// Property names match TMX API exactly (camelCase).
/// </summary>
public class TmxQuoteDto
    {
        public string symbol { get; set; } = "";
        public string currency { get; set; } = "";
        public string exchange { get; set; } = "";
        public string longname { get; set; } = "";
        public string shortName { get; set; } = "";
        public decimal? price { get; set; }
        public decimal? priceChange { get; set; }
        public decimal? percentChange { get; set; }
        public decimal? dayHigh { get; set; }
        public decimal? dayLow { get; set; }
        public decimal? prevClose { get; set; }
        public decimal? openPrice { get; set; }
        public decimal? bid { get; set; }
        public decimal? ask { get; set; }
        public decimal? weeks52high { get; set; }
        public decimal? weeks52low { get; set; }
        public long? volume { get; set; }

        public override string ToString() =>
            $"{symbol,-8} {price,10:C} ({percentChange,6:N2}%)";
    }
