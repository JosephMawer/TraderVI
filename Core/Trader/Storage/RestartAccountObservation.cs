#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Storage;

internal static class RestartAccountObservation
{
    private const string Mismatch = "BrokerHoldingMismatch";
    private static Guid RestrictionId(Guid binding, string security) =>
        BrokerManagedStrategy.StableId($"{binding:D}:{Mismatch}:{security}");

    internal static TradingStateWrite Prepare(TradingCurrentState state, Guid binding, WealthsimpleTfsaSnapshot snapshot,
        DateTimeOffset now, ImmutableArray<RestartNotice>.Builder notices)
    {
        var account = state.Account;
        var at = snapshot.RetrievedAt.UtcDateTime;
        if (binding != account.BindingId || at == default || snapshot.RetrievedAt > now || at < account.BrokerObservedUtc ||
            snapshot.Cash is not { Currency: "CAD" } cash || snapshot.BuyingPower is not { Currency: "CAD" } buyingPower)
            throw new InvalidOperationException("Account identity, observation time or CAD balances could not be verified.");
        if (account.BrokerObservedUtc == at && (account.ObservedCash != cash.Amount || account.ObservedBuyingPower != buyingPower.Amount))
            throw new InvalidOperationException("Conflicting balances at the same broker observation time.");
        var holdings = new Dictionary<string, WealthsimpleHolding>(StringComparer.Ordinal);
        foreach (var h in snapshot.Holdings)
            if (string.IsNullOrWhiteSpace(h.SecurityId) || h.Quantity < 0 || h.Quantity != decimal.Truncate(h.Quantity) ||
                !holdings.TryAdd(h.SecurityId, h))
                throw new InvalidOperationException("Ambiguous security or quantity in current broker holdings.");

        var mismatches = new HashSet<Guid>();
        var changed = ImmutableArray.CreateBuilder<TradingRestrictionCurrent>();
        void Flag(string security, string symbol)
        {
            Guid id = RestrictionId(binding, security);
            mismatches.Add(id);
            const string explanation = "Saved shares differ from the broker. Reconcile the original orders for this security.";
            notices.Add(new("Holdings", symbol, id, explanation));
            changed.Add(new() { BindingId = binding, RestrictionId = id, ScopeKey = "Security:" + security,
                ActionCode = "All", ReasonCode = Mismatch, Explanation = explanation, LastCheckedUtc = at });
        }
        foreach (var group in state.Positions.GroupBy(p => p.SecurityId))
        {
            holdings.Remove(group.Key, out var holding);
            if ((holding?.Quantity ?? 0m) != group.Sum(p => (decimal)p.Quantity))
                Flag(group.Key, group.First().Symbol);
        }
        foreach (var h in holdings.Values.Where(h => h.Quantity > 0)) Flag(h.SecurityId!, h.Symbol);
        // Clear only a restriction this comparison owns, after a newer matching observation.
        foreach (var prior in state.Restrictions.Where(r => r.ReasonCode == Mismatch && r.ScopeKey.StartsWith("Security:", StringComparison.Ordinal)))
            if (!mismatches.Contains(prior.RestrictionId) && prior.RestrictionId == RestrictionId(binding, prior.ScopeKey[9..]) &&
                at > prior.LastCheckedUtc)
                changed.Add(prior with { IsActive = false, LastCheckedUtc = at });
        return new TradingStateWrite(Guid.NewGuid(), "ObserveBrokerAccount", now.UtcDateTime, account.Version,
            account with { ObservedCash = cash.Amount, ObservedBuyingPower = buyingPower.Amount, BrokerObservedUtc = at })
            { Restrictions = changed.ToImmutable() };
    }
}
