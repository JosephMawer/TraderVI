#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>Derived market references only; original broker books, orders and protection identities remain unchanged.</summary>
internal static class BrokerMarketSymbols
{
    internal static ImmutableDictionary<string, string> From(BrokerAccountProjection account, ImmutableArray<BrokerStrategyOrder> orders)
    {
        var map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders.Where(o => o.Reconciled && o.Latest is not null))
        {
            var fact = order.Latest!;
            if (fact.ExternalId != order.ExternalId || fact.SecurityId != order.SecurityId ||
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
