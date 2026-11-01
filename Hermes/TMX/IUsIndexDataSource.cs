using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.Indicators.Granville;

namespace Core.TMX;

/// <summary>
/// Abstraction over the upstream source of daily US-index OHLC bars.
/// Allows swapping Yahoo's <c>chart</c> endpoint for FMP, Stooq+key, etc.
/// without touching <c>GenuityIndicators</c> or Hermes ingestion code.
/// </summary>
public interface IUsIndexDataSource
{
    /// <summary>
    /// Fetches daily bars for <paramref name="canonicalSymbol"/> (e.g., "^GSPC").
    /// </summary>
    /// <param name="canonicalSymbol">Vendor-canonical symbol (caret-prefixed).</param>
    /// <param name="startDate">Inclusive start date (UTC).</param>
    /// <param name="endDate">Inclusive end date (UTC). Use <see cref="DateTime.Today"/> for "up to now".</param>
    Task<IReadOnlyList<UsIndexBar>> GetDailyBarsAsync(
        string canonicalSymbol,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default);
}
