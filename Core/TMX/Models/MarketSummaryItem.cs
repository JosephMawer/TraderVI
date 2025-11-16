using System;

namespace Core.TMX.Models
{
    // Model for the response data
    public class MarketSummaryData
    {
        public MarketSummaryItem[] getMarketSummary { get; set; }
    }

    public class MarketSummaryItem
    {
        public string exchange { get; set; } = "";
        public long totalVolume { get; set; }
        public int advancers { get; set; }
        public int decliners { get; set; }
        public int unchanged { get; set; }
    }


    // delete me/...
    //public struct MarketSummary
    //{
    //    /// <summary>
    //    /// The date at which the market summary request was sent to and parsed from
    //    /// TMX @ https://web.tmxmoney.com/marketsca.php
    //    /// </summary>
    //    public DateTime Date { get; set; }

    //    /// <summary>
    //    /// The name of the indice
    //    /// </summary>
    //    public string Name { get; set; }
    //    /// <summary>
    //    /// Total volume at time of request
    //    /// </summary>
    //    public long Volume { get; set; }
    //    /// <summary>
    //    /// The current monetary value of the indice.
    //    /// </summary>
    //    public long Value { get; set; }
    //    /// <summary>
    //    /// The total number of issues traded for the given indice.
    //    /// </summary>
    //    public int IssuesTraded { get; set; }
    //    /// <summary>
    //    /// The number of stocks that have increased price action.
    //    /// </summary>
    //    public int Advancers { get; set; }
    //    /// <summary>
    //    /// The number of stocks that have no price action.
    //    /// </summary>
    //    public int Unchanged { get; set; }
    //    /// <summary>
    //    /// The number of stocks that have declined price action.
    //    /// </summary>
    //    public int Decliners { get; set; }
    //}
}
