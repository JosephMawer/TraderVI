using System;

namespace Core
{
    public interface IStockInfo
    {
        DateTime TimeOfRequest { get; set; }
        string Name { get; set; }

        string Ticker { get; set; }
        decimal High { get; set; }
        decimal Low { get; set; }
        
        decimal Price { get; set; }
        decimal Open { get; set; }
        decimal Close { get; set; }
        
        
        long Volume { get; set; }
    }
}
