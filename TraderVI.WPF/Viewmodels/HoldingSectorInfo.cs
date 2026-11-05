#nullable enable
using Core.TMX;
using Core.Runtime;
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderVI.WPF.Viewmodels;

public sealed record HoldingSectorReference(string BrokerSymbol, string MarketSymbol, string Sector,
    string? Industry, string? SectorIndexSymbol, DateTime UpdatedUtc);

public sealed record HoldingSectorInfo(string Sector, string Details)
{
    public static HoldingSectorInfo Unknown { get; } = new("Unknown", "No unambiguous saved sector classification matches this symbol.");

    public static HoldingSectorInfo Resolve(string symbol, ManualSellSelection? holding,
        BrokerManagedSession? verifiedSession, IReadOnlyDictionary<string, HoldingSectorInfo> references)
    {
        var direct = references.GetValueOrDefault(symbol.Trim());
        if (holding == null || verifiedSession == null) return direct ?? Unknown;
        try
        {
            var positions = verifiedSession.Account.Positions.Where(p => p.SecurityId == holding.SecurityId &&
                string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase) && p.Quantity == holding.Quantity).ToArray();
            if (positions.Length != 1) return direct ?? Unknown;
            // This public projection calls the existing BrokerMarketSymbols verifier: original purchase,
            // security/listing, position cycle and fill boundary. No suffix inference or alias writes.
            var mapped = EngineRealObservation.From(verifiedSession).Holdings.Where(h =>
                h.Owner == "BrokerManaged" && h.OwnerRecordId == positions[0].PositionId).ToArray();
            if (mapped.Length != 1) return Unknown;
            var resolved = references.GetValueOrDefault(mapped[0].Symbol) ?? Unknown;
            if (direct != null && direct != resolved) return Unknown;
            return resolved;
        }
        catch (InvalidOperationException) { return Unknown; }
        catch (ArgumentException) { return Unknown; }
        catch (System.IO.InvalidDataException) { return Unknown; }
    }

    // Exact stored names or verified Broker aliases only. Do not guess from ticker suffixes.
    public static IReadOnlyDictionary<string, HoldingSectorInfo> From(IEnumerable<HoldingSectorReference> references) =>
        references.GroupBy(r => r.BrokerSymbol.Trim(), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g =>
        {
            var rows = g.Distinct().ToArray();
            if (rows.Length != 1 || string.IsNullOrWhiteSpace(rows[0].Sector) ||
                rows[0].Sector.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return Unknown;
            var row = rows[0];
            string sector = row.SectorIndexSymbol is { } index && TsxSectorSymbols.All.TryGetValue(index, out var name)
                ? name : row.Sector;
            return new HoldingSectorInfo(sector,
                $"Saved TMX classification for {row.MarketSymbol}. Industry: {row.Industry ?? "Unavailable"}. " +
                $"Updated {TradingAccountOverview.Local(DateTime.SpecifyKind(row.UpdatedUtc, DateTimeKind.Utc))}. " +
                $"Source sector: {row.Sector}. This is classification metadata, not a market quote.");
        }, StringComparer.OrdinalIgnoreCase);
}
