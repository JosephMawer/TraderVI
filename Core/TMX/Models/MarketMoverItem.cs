using System;

namespace Core.TMX.Models
{
    // Data models for deserialization
    public class MarketMoversData
    {
        public MarketMoverItem[] getMarketMovers { get; set; } = Array.Empty<MarketMoverItem>();
    }

    public class MarketMoverItem
    {
        public string symbol { get; set; } = "";
        public string name { get; set; } = "";
        //public string exchangeName { get; set; } = "";
        public string exchangeCode { get; set; } = "";
        public decimal price { get; set; }
        public decimal priceChange { get; set; }
        public decimal percentChange { get; set; }
        public long volume { get; set; }
        public long tradeVolume { get; set; }
        public decimal open { get; set; }
        public decimal high { get; set; }
        public decimal low { get; set; }
        public decimal weeks52low { get; set; }
        public decimal weeks52high { get; set; }

        public override string ToString() =>
            $"{symbol,-8} {price,8:C} ({percentChange,6:N2}%) Vol: {volume:N0}";
    }

    ///// <summary>
    ///// The 'Market Movers' on from https://money.tmx.com/canadian-markets
    ///// </summary>
    //public struct MarketMover
    //{
    //    /// <summary>
    //    /// The date at which the market summary request was sent to and parsed from
    //    /// TMX @ https://web.tmxmoney.com/marketsca.php
    //    /// </summary>
    //    public DateTime Date { get; set; }
    //    public string Symbol { get; set; }
    //    public string Company { get; set; }
    //    public long Price { get; set; }
    //    public long Change { get; set; }
    //    public long ChangeInPercent { get; set; }
    //    public long Open {  get; set; }
    //    public long High { get; set; }
    //    public long Low { get; set; }
    //    public long Volume { get; set; }
    //    public string Range52Week { get; set; }
    //}
}
