namespace Core.Models
{
    /// <summary>
    /// Represents information about a stock, such as the symbol, current price, current volume,
    /// Daily high/low, etc.
    /// </summary>
    public struct StockQuote : IStockInfo
    {
        /// <summary>
        /// The time the stock info was requested
        /// </summary>
        public string TimeOfRequest { get; set; }

        /// <summary>
        /// The stock ticker, aka symbol
        /// </summary>
        public string Ticker { get; set; }

        /// <summary>
        /// The name of the stock
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The current price of the stock
        /// </summary>
        public decimal Price { get; set; }

        public decimal Open { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
    }

}
