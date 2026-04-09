using System;

namespace Core.Db
{
    public class SymbolInfo
    {
        public string? ShortName { get; set; }
        public string Symbol { get; set; }
        public string SecurityType { get; set; } = "Stock";

        public bool IsETF => SecurityType.Equals("ETF", StringComparison.OrdinalIgnoreCase);
    }
}
