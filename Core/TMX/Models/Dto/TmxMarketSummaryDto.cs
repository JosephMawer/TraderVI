namespace Core.TMX.Models.Dto
{
    /// <summary>
    /// TMX GraphQL DTO for getMarketSummary response.
    /// Property names match TMX API exactly (camelCase).
    /// </summary>
    public class TmxMarketSummaryDto
    {
        public string exchange { get; set; } = "";
        public long totalVolume { get; set; }
        public int advancers { get; set; }
        public int decliners { get; set; }
        public int unchanged { get; set; }
    }
}


