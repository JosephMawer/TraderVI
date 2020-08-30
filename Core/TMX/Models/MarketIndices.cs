using System;

namespace Core.TMX.Models
{
    public struct MarketIndices
    {
        public DateTime Date { get; set; }
        public string Name { get; set; }
        public float Last { get; set; }
        public float Change { get; set; }
        public float PercentChange { get; set; }
    }
}
