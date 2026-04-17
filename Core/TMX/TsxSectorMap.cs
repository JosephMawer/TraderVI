using System;
using System.Collections.Generic;

namespace Core.TMX;

/// <summary>
/// Maps TMX sector names (as returned by <c>getQuoteBySymbol.sector</c>) to 
/// TSX sector index symbols (<c>^TT*</c>).
///
/// TMX does not expose a "which index does this stock belong to" API.
/// Instead, we pull each stock's <c>sector</c> metadata and normalize it
/// to our own controlled mapping. This gives us a stable internal lookup
/// independent of TMX naming changes.
///
/// Mapping was validated against Hermes run on 2026-04-16 (388 equities).
/// </summary>
public static class TsxSectorMap
{
    /// <summary>
    /// Maps normalized TMX sector strings to TSX sector index symbols.
    /// Keys are lowercased for case-insensitive matching.
    /// 
    /// Extend this dictionary as new sector names are discovered from TMX data.
    /// Run <see cref="TryGetSectorIndex"/> to see which stocks return null — those
    /// are unmapped sectors that need to be added here.
    /// </summary>
    private static readonly Dictionary<string, string?> SectorToIndex = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Financials (^TTFS) ──
        // TMX returns both "Finance" and "Financials" depending on the stock.
        ["finance"]               = "^TTFS",
        ["financials"]            = "^TTFS",
        ["financial services"]    = "^TTFS",
        ["banks"]                 = "^TTFS",
        ["insurance"]             = "^TTFS",
        ["diversified financials"] = "^TTFS",

        // ── Energy ──
        // No confirmed ^TT* Energy sub-index symbol yet.
        // Mapped to null until we discover the real symbol.
        // TODO: Probe TMX GraphQL with ^TTEG, ^TTEN (in sector context), ^TTCE, etc.
        // Once confirmed, update this and re-add Energy to CyclicalBasket.
        ["energy"]                = null,
        ["oil & gas"]             = null,
        ["oil and gas"]           = null,
        ["pipelines"]             = null,

        // ── Industrials (^TTIN) ──
        ["industrials"]           = "^TTIN",
        ["industrial"]            = "^TTIN",
        ["capital goods"]         = "^TTIN",
        ["transportation"]        = "^TTIN",

        // ── Technology (^TTTK) ──
        ["technology"]            = "^TTTK",
        ["information technology"] = "^TTTK",

        // ── Utilities (^TTUT) ──
        ["utilities"]             = "^TTUT",

        // ── Materials (^TTMT) ──
        ["materials"]             = "^TTMT",
        ["metals & mining"]       = "^TTMT",
        ["metals and mining"]     = "^TTMT",
        ["gold"]                  = "^TTMT",
        ["mining"]                = "^TTMT",
        ["basic materials"]       = "^TTMT",

        // ── Health Care (^TTHC) ──
        // TMX returns "Healthcare" (one word) — was previously unmapped.
        ["health care"]           = "^TTHC",
        ["healthcare"]            = "^TTHC",

        // ── Consumer Discretionary (^TTCD — verify symbol resolves) ──
        ["consumer discretionary"] = "^TTCD",

        // ── Consumer Staples (^TTCS — verify symbol resolves) ──
        ["consumer staples"]      = "^TTCS",

        // ── Real Estate (^TTRE — verify symbol resolves) ──
        ["real estate"]           = "^TTRE",

        // ── Communication Services / Media (^TTTS — verify symbol resolves) ──
        // TMX returns "Media" for some telco/media stocks (e.g., BCE, T, CGX).
        ["communication services"] = "^TTTS",
        ["telecommunications"]    = "^TTTS",
        ["media"]                 = "^TTTS",
    };

    /// <summary>
    /// Attempts to map a TMX sector string to a TSX sector index symbol.
    /// Returns false if the sector is unknown or has no corresponding index.
    /// </summary>
    public static bool TryGetSectorIndex(string? tmxSector, out string? sectorIndexSymbol)
    {
        sectorIndexSymbol = null;
        if (string.IsNullOrWhiteSpace(tmxSector))
            return false;

        if (SectorToIndex.TryGetValue(tmxSector.Trim(), out var symbol) && symbol is not null)
        {
            sectorIndexSymbol = symbol;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns all known TMX sector strings (for diagnostic / coverage reporting).
    /// </summary>
    public static IReadOnlyCollection<string> KnownSectors => SectorToIndex.Keys;
}
