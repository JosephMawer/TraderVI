using System;

namespace Core.Indicators.Models
{
    public struct DifferenceTable
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Symbol { get; set; }
        public decimal Close { get; set; }
        public decimal Max { get; set; }
        public string Difference { get; set; }
    }
}
