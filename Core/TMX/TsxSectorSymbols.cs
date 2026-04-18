using System;
using System.Collections.Generic;

namespace Core.TMX;

/// <summary>
/// Maps TMX sector index symbols (^TT*) to human-readable sector names.
/// TMX treats indices as regular symbols resolved via <c>getQuoteForSymbols</c>
///
/// NOTE: The ^TT* symbol set was discovered empirically. TMX does not publish
/// an official directory. If a symbol stops resolving, check TMX Money for updates.
/// </summary>
public static class TsxSectorSymbols
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["^TTEN"] = "Energy",              // S&P/TSX Capped Energy Index — confirmed via TMX Money
        ["^TTFS"] = "Financials",
        ["^TTHC"] = "Health Care",         // S&P/TSX Capped Health Care Index
        ["^TTIN"] = "Industrials",
        ["^TTTK"] = "Technology",
        ["^TTUT"] = "Utilities",
        ["^TTMT"] = "Materials",
        ["^TTCD"] = "Consumer Discretionary",  // Discovered from TMX; verify if resolves
        ["^TTCS"] = "Consumer Staples",        // Discovered from TMX; verify if resolves
        ["^TTRE"] = "Real Estate",             // Discovered from TMX; verify if resolves
        ["^TTTS"] = "Communication Services",  // Discovered from TMX; verify if resolves
    };

    /// <summary>
    /// The symbols that make up the "cyclical basket" for Granville Disparity analysis.
    /// Equal-weight average of these sectors' percent changes is compared against the
    /// benchmark (XIU) to detect real-economy divergence.
    ///
    /// NOTE: Currently equal-weighted. May move to market-cap weighting in the future
    /// if sector sizes diverge significantly enough to distort the signal.
    /// </summary>
    public static readonly IReadOnlyList<string> CyclicalBasket = ["^TTEN", "^TTIN", "^TTMT"];

    public static string[] AllSymbols => [.. All.Keys];

    public static string GetName(string symbol) =>
        All.TryGetValue(symbol, out var name) ? name : symbol;
}