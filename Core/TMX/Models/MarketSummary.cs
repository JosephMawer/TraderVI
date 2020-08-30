using System;

namespace Core.TMX.Models
{
    public struct MarketSummary
    {
        /// <summary>
        /// The date at which the market summary request was sent to and parsed from
        /// TMX @ https://web.tmxmoney.com/marketsca.php
        /// </summary>
        public DateTime Date { get; set; }
        /// <summary>
        /// The name of the indice
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Total volume at time of request
        /// </summary>
        public long Volume { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public long Value { get; set; }
        public int IssuesTraded { get; set; }
        /// <summary>
        /// The number of stocks that have advanced at time of request
        /// </summary>
        public int Advancers { get; set; }
        public int Unchanged { get; set; }
        public int Decliners { get; set; }
    }
}
