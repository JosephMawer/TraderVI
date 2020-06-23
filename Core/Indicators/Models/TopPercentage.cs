namespace Core.Indicators.Models
{
    public struct TopPercentage
    {
        public string Ticker { get; set; }
        public string Name { get; set; }
        public decimal Close { get; set; }
        public decimal PriceIncrease { get; set; }
    }
}
