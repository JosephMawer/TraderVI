namespace Core.Indicators.Models
{
    public struct DifferenceTable
    {
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public string Symbol { get; set; }
        public decimal Close { get; set; }
        public decimal Max { get; set; }
        public string Difference { get; set; }
    }
}
