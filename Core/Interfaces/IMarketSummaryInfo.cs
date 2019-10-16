using System;

namespace Core
{
    public interface IMarketSummaryInfo
    {
        DateTime Date { get; set; }
        string Name { get; set; }
        long Volume { get; set; }
        long Value { get; set; }
        int IssuesTraded { get; set; }
        int Advancers { get; set; }
        int Unchanged { get; set; }
        int Decliners { get; set; }
    }
}
