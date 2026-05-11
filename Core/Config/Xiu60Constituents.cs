using System;
using System.Collections.Generic;

namespace Core.Config;

/// <summary>
/// Static seed list of S&amp;P/TSX 60 constituents (the basket tracked by iShares XIU).
///
/// Used by the Weighting calibration script (Tools/Backtest.Weighting) and
/// future Granville Weighting indicator implementation. Refresh by editing
/// <see cref="Symbols"/> and bumping <see cref="LastReviewedUtc"/>.
///
/// NOTE: TSX dual-class symbols use a dot suffix (e.g., "BBD.B", "RCI.B",
/// "TECK.B") and must be preserved through the data pipeline.
/// </summary>
public static class Xiu60Constituents
{
    public static readonly DateTime LastReviewedUtc = new(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>S&amp;P/TSX 60 constituents, grouped by sector for readability.</summary>
    public static readonly IReadOnlyList<string> Symbols = new[]
    {
        // Financials
        "RY", "TD", "BNS", "BMO", "CM", "NA",
        "MFC", "SLF", "GWO", "IFC", "POW", "FFH", "BAM", "BN", "X",

        // Energy
        "ENB", "TRP", "PPL", "CNQ", "SU", "CVE", "IMO", "TOU", "ARX",

        // Materials
        "NTR", "AEM", "ABX", "WPM", "FNV", "FM", "TECK.B", "K", "CCO",

        // Industrials
        "CNR", "CP", "WCN", "GFL", "WSP", "STN", "TFII", "BBD.B",

        // Consumer Discretionary / Staples
        "L", "ATD", "MRU", "DOL", "QSR", "GIL", "MG",

        // Communication Services
        "BCE", "T", "RCI.B",

        // Utilities
        "FTS", "EMA", "AQN", "H",

        // Information Technology
        "SHOP", "CSU", "OTEX", "GIB.A",

        // Real Estate
        "REI.UN",
    };
}
