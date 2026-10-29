#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.DelphiLive;

internal sealed record HoldingSymbolAlias(string Source, string Alias, string Symbol);
internal sealed record TrackedHoldingMarketSymbol(Guid PositionId, string BrokerSymbol, string MarketSymbol);

/// <summary>Translates observation references without editing the owning account's records.</summary>
internal static class DelphiLiveHoldingSymbols
{
    internal static IReadOnlyList<DelphiLiveObservedHolding> Resolve(
        IReadOnlyList<DelphiLiveObservedHolding> holdings, IEnumerable<string> symbols,
        IReadOnlyList<HoldingSymbolAlias> aliases,
        IReadOnlyList<TrackedHoldingMarketSymbol>? managed = null)
    {
        var canonical = symbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return holdings.Select(holding =>
        {
            string input = holding.Symbol.Trim().ToUpperInvariant();
            // Paper records already use app symbols. Broker aliases apply only to Real observations.
            var matches = holding.Owner is "Real" or "BrokerManaged" or "BrokerPending"
                ? aliases.Where(a => a.Source == "Broker" &&
                    string.Equals(a.Alias, input, StringComparison.OrdinalIgnoreCase)).ToArray()
                : [];
            if (matches.Length > 1 || (matches.Length == 1 && canonical.Contains(input) &&
                !string.Equals(matches[0].Symbol, input, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Live monitoring has conflicting symbol mappings for '{input}'. Review SymbolAliases.");
            string resolved = matches.Length == 1 ? matches[0].Symbol.Trim().ToUpperInvariant() : input;
            var owned = holding.Owner == "Real"
                ? (managed ?? []).Where(m => m.PositionId == holding.OwnerRecordId).ToArray() : [];
            if (owned.Length > 1 || owned.Length == 1 &&
                (!string.Equals(owned[0].BrokerSymbol, input, StringComparison.OrdinalIgnoreCase) ||
                 (matches.Length == 1 || canonical.Contains(input)) &&
                 !string.Equals(resolved, owned[0].MarketSymbol, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Tracked holding differs from its verified managed purchase correspondence.");
            if (owned.Length == 1) resolved = owned[0].MarketSymbol.Trim().ToUpperInvariant();
            if (!canonical.Contains(resolved))
                throw new InvalidOperationException($"Live monitoring cannot identify '{input}' in Symbols. Add a verified source alias before retrying; account history is unchanged.");
            return holding with { Symbol = resolved };
        }).ToArray();
    }
}
