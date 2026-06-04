using System;

namespace Core.Db
{
    public class SymbolInfo
    {
        public string? ShortName { get; set; }
        public string Symbol { get; set; }
        public string SecurityType { get; set; } = "Stock";

        /// <summary>
        /// True when the underlying instrument is a leveraged or inverse ETP
        /// (e.g. BetaPro 2x/-2x, MegaLong/MegaShort 3x, SavvyLong/SavvyShort,
        /// LFG Daily 2x). These rows are still imported and quoted, but the
        /// Delphi ranking universe excludes them — their daily-reset path
        /// dependency violates the ML training distribution and they are
        /// structurally risky vs single-name common stocks (see ADR-0009).
        /// </summary>
        public bool IsLeveragedOrInverseEtp { get; set; }

        public bool IsETF => SecurityType.Equals("ETF", StringComparison.OrdinalIgnoreCase);
    }
}
