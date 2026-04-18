using System.Collections.Generic;

namespace Core.TMX;

/// <summary>
/// TMX benchmark and broad-market index symbols used across the system.
/// These are resolved via <c>getQuoteForSymbols</c> just like sector indices.
///
/// Naming convention: TMX Money uses a <c>^</c> prefix for index symbols.
/// ETF proxies (XIU, XIC) do not use the caret prefix.
/// </summary>
public static class TsxBenchmarkSymbols
{
    // ── Broad-market benchmarks ──

    /// <summary>S&P/TSX Composite Index (cap-weighted, broad market).</summary>
    public const string TsxComposite = "^GSPTSE";

    /// <summary>S&P/TSX 60 Index (large-cap leadership proxy).</summary>
    public const string Tsx60 = "^TX60";

    /// <summary>
    /// S&P/TSX Composite Equal Weight Index.
    /// Compares against cap-weighted Composite to measure leadership breadth:
    /// if cap-weighted outperforms equal-weight, leadership is narrow (concentrated);
    /// if equal-weight keeps pace, participation is broad.
    /// 
    /// NOTE: ^TXCE is the Composite equal-weight. Do not confuse with ^TXEW
    /// which is the TSX 60 Equal Weight Index (different universe).
    /// </summary>
    public const string TsxCompositeEqualWeight = "^TXCE";

    // ── ETF proxies (tradable) ──

    /// <summary>iShares S&P/TSX 60 ETF — tradable proxy for the TSX 60, already used as benchmark.</summary>
    public const string Xiu = "XIU";

    /// <summary>All benchmark index symbols (caret-prefixed) for batch fetching.</summary>
    public static readonly IReadOnlyList<string> AllIndexSymbols =
    [
        TsxComposite,
        Tsx60,
        TsxCompositeEqualWeight,
    ];
}