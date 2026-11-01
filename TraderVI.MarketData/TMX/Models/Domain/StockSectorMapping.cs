using System;

namespace Core.TMX.Models.Domain;

/// <summary>
/// Maps a single TSX stock to its sector and corresponding sector index.
/// Built from TMX <c>getQuoteBySymbol</c> metadata, not from TMX index membership.
/// </summary>
public sealed record StockSectorMapping(
    string Symbol,
    string Sector,
    string? Industry,
    string? SectorIndexSymbol,
    DateTime LastUpdated
)
{
    public override string ToString() =>
        $"{Symbol,-8} {Sector,-25} → {SectorIndexSymbol ?? "(unmapped)"}";
}