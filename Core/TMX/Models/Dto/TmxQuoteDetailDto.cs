namespace Core.TMX.Models.Dto
{
    /// <summary>
    /// TMX GraphQL DTO for getQuoteBySymbol response (full company profile).
    /// Property names match TMX API exactly (camelCase).
    /// </summary>
    public class TmxQuoteDetailDto
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
        public decimal? totalDebtToEquity { get; set; }

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
        public string exDividendDate { get; set; } = "";
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

        public override string ToString() =>
            $"{symbol} ({name}) — {price:C} ({percentChange:N2}%), Volume: {volume:N0}";
    }
}



