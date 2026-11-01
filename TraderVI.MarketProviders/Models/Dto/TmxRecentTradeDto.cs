namespace Core.TMX.Models.Dto
{
    /// <summary>Unvalidated recent-trade response used for source diagnosis, not order/fill evidence.</summary>
    public sealed class TmxRecentTradeDto
    {
        public string datetime { get; set; } = "";
        public decimal? price { get; set; }
        public long? volume { get; set; }
        public string exchangeCode { get; set; } = "";
    }
}
