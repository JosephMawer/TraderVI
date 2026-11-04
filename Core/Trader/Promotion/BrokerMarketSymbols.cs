#nullable enable
using Core.WSTrade.Accounts;
using Core.Trader.DelphiLive;
using Core.Trader.Storage;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>Derived market references only; original broker books, orders and protection identities remain unchanged.</summary>
internal static class BrokerMarketSymbols
{
    internal static IReadOnlyList<TrackedHoldingMarketSymbol> ForTrackedHoldings(BrokerManagedSession state)
    {
        // Reuse the original managed-order/listing proof. Never infer a mapping from a suffix alone.
        _ = From(state.Account, state.Orders);
        var result = new List<TrackedHoldingMarketSymbol>();
        foreach (var holding in state.Account.Positions.Where(p => p.LegacyPositionId is null))
        {
            var purchases = state.Orders.Where(o => o.Side == "BUY" && o.SecurityId == holding.SecurityId &&
                o.ExternalId == holding.FirstBuyOrderId && o.Reconciled && o.Latest is { FilledQuantity: > 0, Side: "BUY" }).ToArray();
            if (purchases.Length > 1) throw new InvalidOperationException("Ambiguous managed purchase for a tracked holding.");
            if (purchases.Length == 0) continue; // Unmanaged/legacy records still require reviewed aliases.
            var purchase = purchases[0];
            string broker = BrokerTradeProjector.ListingSymbol(purchase.Latest!, state.Account.Import.Plan.Observation);
            result.Add(new(BrokerTradeProjector.TrackedPositionId(state.Account.Import.Plan.BrokerKey, purchase.ExternalId),
                broker, purchase.Symbol));
        }
        if (result.Select(r => r.PositionId).Distinct().Count() != result.Count)
            throw new InvalidOperationException("Multiple managed holdings share a tracked position identity.");
        return result;
    }

    internal static ImmutableDictionary<string, string> From(BrokerAccountProjection account, ImmutableArray<BrokerStrategyOrder> orders)
    {
        var map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders.Where(o => o.Reconciled && o.Latest is not null))
        {
            var fact = order.Latest!;
            if (fact.ExternalId != order.ExternalId || fact.SecurityId != order.SecurityId || fact.Side != order.Side ||
                fact.SubmittedQuantity != order.Quantity ||
                !Matches(account.Import.Plan.Observation, order.SecurityId, order.Symbol, fact.Symbol))
                throw new InvalidOperationException("An owned order's listing correspondence changed.");
            if (fact.Symbol == order.Symbol) continue;
            if (map.TryGetValue(fact.Symbol, out var existing) && existing != order.Symbol)
                throw new InvalidOperationException("Conflicting market names for an owned broker symbol.");
            map[fact.Symbol] = order.Symbol;
        }
        foreach (var holding in account.Positions)
        {
            var owned = orders.Where(o => o.SecurityId == holding.SecurityId && o.Side == "BUY" &&
                o.ExternalId == holding.FirstBuyOrderId && o.Reconciled && o.Latest is { FilledQuantity: > 0 }).ToArray();
            if (owned.Length > 1) throw new InvalidOperationException("Ambiguous original purchase identity.");
            if (owned.Length == 1 && holding.LegacyPositionId is null && (holding.PositionId !=
                BrokerManagedStrategy.StableId($"{account.Import.BindingId:D}:{holding.SecurityId}:{holding.FirstBuyOrderId}") ||
                owned[0].Latest!.LastFilledAt != holding.EntryCompletedAt))
                throw new InvalidOperationException("The managed purchase belongs to a different position cycle.");
            string market = owned.SingleOrDefault()?.Symbol ?? holding.Symbol;
            if (!Matches(account.Import.Plan.Observation, holding.SecurityId, market, holding.Symbol))
                throw new InvalidOperationException("The original purchase and verified broker listing do not identify the same market symbol.");
            // The original strategy intent supplies the market name; never strip an unknown suffix.
            if (map.TryGetValue(holding.Symbol, out var prior) && prior != market)
                throw new InvalidOperationException("The held symbol conflicts with its original purchase.");
            map[holding.Symbol] = market;
        }
        if (account.Positions.Select(p => map[p.Symbol]).Distinct(StringComparer.OrdinalIgnoreCase).Count() != account.Positions.Length)
            throw new InvalidOperationException("Two broker positions cannot share a market symbol.");
        return map.ToImmutable();
    }

    // A compact position keeps the spelling with which its original fill was committed. The
    // managed broker projection may use a listing spelling, but cannot rename that stored cycle.
    internal static void RequirePosition(BrokerManagedSession state, TradingPositionCurrent position)
    {
        var held = state.Account.Positions.Single(p => p.PositionId == position.PositionId);
        if (position.BindingId != state.BindingId || position.SecurityId != held.SecurityId ||
            position.FirstEntryUtc != held.EntryCompletedAt.UtcDateTime)
            throw new InvalidOperationException("The saved position differs from the managed purchase identity.");
        if (position.Symbol == held.Symbol) return;
        var map = From(state.Account, state.Orders);
        var purchase = state.Orders.SingleOrDefault(o => o.Side == "BUY" && o.Reconciled &&
            o.SecurityId == held.SecurityId && o.ExternalId == held.FirstBuyOrderId && o.Latest is { FilledQuantity: > 0 });
        if (held.LegacyPositionId is not null || purchase is null || position.Symbol != purchase.Symbol ||
            map.GetValueOrDefault(held.Symbol) != position.Symbol)
            throw new InvalidOperationException("The saved position's symbol lacks its original purchase and verified listing.");
    }

    internal static ImmutableDictionary<string, string> ForCompactProgress(BrokerManagedSession state, TradingCurrentState book)
    {
        var market = From(state.Account, state.Orders);
        string Canonical(string name) => market.GetValueOrDefault(name) ?? name;
        var result = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in market.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase))
        {
            var positions = book.Positions.Where(p => Canonical(p.Symbol) == group.Key).ToArray();
            if (positions.Length > 1) throw new InvalidOperationException("Ambiguous compact position symbol.");
            if (positions.Length == 1 && state.Account.Positions.Any(p => p.PositionId == positions[0].PositionId))
                RequirePosition(state, positions[0]);
            var progress = book.Symbols.Where(s => Canonical(s.Symbol) == group.Key).ToArray();
            if (positions.Length == 0 && progress.Length > 1)
                throw new InvalidOperationException("Conflicting compact symbol progress requires review.");
            string target = positions.SingleOrDefault()?.Symbol ?? progress.SingleOrDefault()?.Symbol ?? group.Key;
            result[group.Key] = target;
            foreach (var pair in group) result[pair.Key] = target;
        }
        return result.ToImmutable();
    }

    internal static bool Matches(BrokerAccountReconciliation observation, string security, string market, string broker)
    {
        if (string.Equals(market, broker, StringComparison.OrdinalIgnoreCase)) return true;
        var listings = (observation.Instruments ?? []).Where(i => i.SecurityId == security).ToArray();
        return listings.Length == 1 && listings[0] is { Currency: "CAD", SecurityType: "EQUITY", PrimaryMic: "XTSE" } listing &&
            (string.Equals(listing.Symbol, market, StringComparison.OrdinalIgnoreCase) || string.Equals(listing.Symbol, broker, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(broker, market + ".TO", StringComparison.OrdinalIgnoreCase);
    }

    internal static string Market(TradingRecommendationInput input, string symbol) =>
        input.BrokerMarketSymbols?.GetValueOrDefault(symbol) ?? symbol;

    internal static TradingRecommendationResult Evaluate(TradingRecommendationInput input,
        Func<TradingRecommendationInput, TradingRecommendationResult> evaluate)
    {
        var map = input.BrokerMarketSymbols;
        if (map is null || map.All(p => p.Key == p.Value)) return evaluate(input);
        var reverse = map.Where(p => input.CurrentHoldings.Any(h => string.Equals(h.Symbol, p.Key, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);
        string Market(string s) => map.GetValueOrDefault(s) ?? s;
        string Broker(string s) => reverse.GetValueOrDefault(s) ?? s;
        var memory = input.PreviousMemory.Symbols.ToBuilder();
        foreach (var pair in map.Where(p => p.Key != p.Value))
        {
            if (!memory.TryGetValue(pair.Key, out var owned)) continue;
            if (memory.TryGetValue(pair.Value, out var existing) && existing.PositionId is not null)
                throw new InvalidOperationException("Conflicting owned protection under broker and market symbols requires review.");
            memory.Remove(pair.Key);
            memory[pair.Value] = owned;
        }
        var normalized = input with {
            CurrentHoldings = input.CurrentHoldings.Select(h => h with { Symbol = Market(h.Symbol) }).ToImmutableArray(),
            ActualFills = input.ActualFills.Select(f => f with { Symbol = Market(f.Symbol) }).ToImmutableArray(),
            Quotes = input.Quotes.Select(q => q with { Symbol = Market(q.Symbol) }).ToImmutableArray(),
            PreviousMemory = input.PreviousMemory with { Symbols = memory.ToImmutable() }, BrokerMarketSymbols = null
        };
        var result = evaluate(normalized);
        return result with {
            Recommendations = result.Recommendations.Select(r => r with { Symbol = Broker(r.Symbol) }).ToImmutableArray(),
            EntryDecisions = result.EntryDecisions?.Select(e => e with { Symbol = Broker(e.Symbol) }).ToImmutableArray(),
            Memory = result.Memory with { Symbols = result.Memory.Symbols.ToImmutableDictionary(p => Broker(p.Key), p => p.Value, StringComparer.OrdinalIgnoreCase) }
        };
    }
}
