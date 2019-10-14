using System;

namespace Core.TMX
{

    public partial class Market
    {
        public struct IndiceSummary : IIndexSummary
        {
            public DateTime Date { get; set; }
            public string Name { get; set; }
            public float Last { get; set; }
            public float Change { get; set; }
            public float PercentChange { get; set; }
        }
    }
}
