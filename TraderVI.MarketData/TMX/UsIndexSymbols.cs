using System.Collections.Generic;

namespace Core.TMX;

/// <summary>
/// Canonical US-listed index symbols used by Granville's Genuity indicators (#17–#20).
/// These are vendor-canonical (Yahoo / FMP / most providers); the TMX flavor would
/// be <c>"^GSPC:US"</c>, but TMX does not return data for them — see ADR-0004.
/// </summary>
public static class UsIndexSymbols
{
    /// <summary>S&amp;P 500 Composite Index — primary confirming index for Granville Genuity.</summary>
    public const string SP500 = "^GSPC";

    /// <summary>NYSE Composite Index — broad-market confirming index (every NYSE common stock).</summary>
    public const string NyseComposite = "^NYA";

    /// <summary>
    /// All confirming indices Genuity consumes. Keep this list small — adding a symbol here
    /// expands the daily Hermes ingestion and the Genuity diagnostic surface.
    /// </summary>
    public static readonly IReadOnlyList<string> AllSymbols = [SP500, NyseComposite];
}
