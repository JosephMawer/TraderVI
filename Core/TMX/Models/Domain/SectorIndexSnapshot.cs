using System;

namespace Core.TMX.Models.Domain;

/// <summary>
/// A point-in-time snapshot of a TSX sector sub-index, fetched via
/// <c>getQuoteForSymbols</c> using the <c>^TT*</c> symbol convention.
/// </summary>
public sealed record SectorIndexSnapshot(
    string Symbol,
    string SectorName,
    decimal Price,
    decimal PriceChange,
    decimal PercentChange,
    DateTime Date
)
{
    public override string ToString() =>
        $"{SectorName,-14} ({Symbol}) {Price,10:F2} {PercentChange,7:+0.00;-0.00}%";
}