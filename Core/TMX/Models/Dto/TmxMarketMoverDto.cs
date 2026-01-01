namespace Core.TMX.Models.Dto
{
    /// <summary>
    /// TMX GraphQL DTO for getMarketMovers response.
    /// Property names match TMX API exactly (camelCase).
    /// </summary>
    public class TmxMarketMoverDto
    {
        public string symbol { get; set; } = "";
        public string name { get; set; } = "";
        public string exchangeCode { get; set; } = "";
        public decimal price { get; set; }
        public decimal priceChange { get; set; }
        public decimal percentChange { get; set; }
        public long volume { get; set; }
        public long tradeVolume { get; set; }
        public decimal open { get; set; }
        public decimal high { get; set; }
        public decimal low { get; set; }
        public decimal weeks52low { get; set; }
        public decimal weeks52high { get; set; }

        public override string ToString() =>
            $"{symbol,-8} {price,8:C} ({percentChange,6:N2}%) Vol: {volume:N0}";
    }
}