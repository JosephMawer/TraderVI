using System.Collections.Generic;

namespace Core.Indicators.Granville;

/// <summary>
/// Interface implemented by each Granville indicator category (Plurality, Disparity, etc.).
/// Each group evaluates its subset of the 56 indicators and returns results.
/// </summary>
public interface IGranvilleIndicatorGroup
{
    /// <summary>The category this group covers.</summary>
    IndicatorCategory Category { get; }

    /// <summary>Human-readable category name.</summary>
    string Name { get; }

    /// <summary>
    /// Evaluate all indicators in this group using the provided market data.
    /// </summary>
    /// <param name="context">Shared market data for the evaluation day.</param>
    /// <returns>One result per indicator in the group.</returns>
    IReadOnlyList<GranvilleResult> Evaluate(GranvilleMarketContext context);
}
