#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Core.Trader.Storage;

internal static class CompactManagedPublication
{
    // The existing strategy, dashboard and risk record still consume the managed session during
    // the swap. Publish changes in its transaction. Never reapply an older snapshot over a fill.
    internal static TradingStateWrite Prepare(BrokerManagedSession prior, BrokerManagedSession next, TradingCurrentState book,
        TradingOrder? settledSale = null)
    {
        if (next.PreviousRevisionId != prior.RevisionId || next.BindingId != book.Account.BindingId ||
            next.SessionId != book.Account.AssignmentId || next.EffectiveReview.RequestId != book.Settings.FinancialPermissionId ||
            next.Strategy.VersionId != book.Account.StrategyId || next.Strategy.Strategy.ImplementationIdentity != book.Settings.ImplementationIdentity)
            throw new InvalidDataException("The strategy revision does not own this compact assignment.");
        var before = RestartAccountRehearsal.Positions(prior, true).ToDictionary(p => p.PositionId);
        var after = RestartAccountRehearsal.Positions(next, true).ToDictionary(p => p.PositionId);
        var current = book.Positions.ToDictionary(p => p.PositionId);
        var positions = ImmutableArray.CreateBuilder<TradingPositionCurrent>();
        var orders = ImmutableArray.CreateBuilder<TradingOrder>();
        var previousOrders = prior.Orders.ToDictionary(o => o.RequestId);
        foreach (var order in next.Orders.Where(o => !previousOrders.TryGetValue(o.RequestId, out var old) || old != o))
        {
            var existing = settledSale?.RequestId == order.RequestId ? settledSale : book.Orders.SingleOrDefault(o => o.RequestId == order.RequestId);
            if (existing == null && previousOrders.TryGetValue(order.RequestId, out var old) && old.Reconciled)
                continue; // Already settled before this transfer, including FILLED to POSTED.
            var receipt = existing ?? new TradingOrder { BindingId = next.BindingId, RequestId = order.RequestId,
                DecisionId = order.DecisionId, PermissionId = book.Settings.PermissionId, PositionId = order.PositionId,
                SecurityId = order.SecurityId, Symbol = order.Symbol, Side = order.Side, Quantity = order.Quantity,
                ReferencePrice = order.ReferencePrice, CreatedUtc = order.CreatedAt.UtcDateTime, Status = "Unknown" };
            if (receipt.Quantity != order.Quantity || receipt.DecisionId != order.DecisionId || receipt.Side != order.Side ||
                receipt.SecurityId != order.SecurityId || receipt.Symbol != order.Symbol || receipt.PositionId != order.PositionId)
                throw new InvalidDataException("The original order and compact receipt disagree.");
            if (order.Latest is { } fact)
            {
                if (fact.Symbol != order.Symbol)
                {
                    var instrument = next.Account.Import.Plan.Observation.Instruments?.SingleOrDefault(i => i.SecurityId == order.SecurityId);
                    if (instrument == null || instrument.Symbol != fact.Symbol || instrument.Currency != fact.Currency ||
                        instrument.SecurityType != fact.SecurityType || !BrokerAccountImport.CanadianTickerPair(order.Symbol, fact.Symbol, instrument.PrimaryMic))
                        throw new InvalidDataException("The broker listing differs from the saved order.");
                    fact = fact with { Symbol = order.Symbol };
                }
                var (status, applied) = CompactOrderObservation.Reconcile(receipt, fact, next.SavedAt);
                receipt = applied.Order with { Status = status, NeedsWork = !TradingStorageValidation.IsTerminal(status) };
                if (receipt.NeedsWork == order.Reconciled) throw new InvalidDataException("Order completion differs between the two records.");
            }
            orders.Add(receipt with { ReservedQuantity = receipt.Side == "SELL" && receipt.NeedsWork ? receipt.Quantity - receipt.AppliedFilledQuantity : 0 });
        }
        foreach (var id in before.Keys.Union(after.Keys))
        {
            before.TryGetValue(id, out var old);
            after.TryGetValue(id, out var updated);
            current.TryGetValue(id, out var actual);
            var reservation = orders.SingleOrDefault(o => o.Side == "SELL" && o.PositionId == id);
            if (old == updated && reservation == null) continue;
            if (actual != null && old != null &&
                (actual.Quantity != old.Quantity || actual.CostBasis != old.CostBasis) &&
                (actual.Quantity != (updated?.Quantity ?? 0) || actual.CostBasis != (updated?.CostBasis ?? 0)))
                throw new InvalidDataException("A newer broker fill must be reconciled before changing these shares.");
            if (updated != null) positions.Add(updated with { ReservedSellQuantity = reservation?.ReservedQuantity ?? actual?.ReservedSellQuantity ?? 0 });
            else if (actual != null) positions.Add(actual with { Quantity = 0, CostBasis = 0, IsOpen = false, ReservedSellQuantity = 0 });
        }
        if (settledSale != null)
        {
            // Recovery already committed the order, position, allowance and original symbol exit memory.
            // This revision can advance their common checkpoint only if publication agrees exactly.
            if (positions.Count != 0 || orders.Count != 1 || !TradingStorageValidation.Same(settledSale, orders[0]))
                throw new InvalidDataException("The managed sale would change an already applied compact fill.");
            return new(next.RevisionId, "PublishManagedRevision", next.SavedAt.UtcDateTime, book.Account.Version, book.Account)
                { AdvanceCheckpoint = true };
        }
        var previousSymbols = CompactDelphiAssignment.CurrentSymbols(prior).ToDictionary(s => s.Symbol);
        var symbols = CompactDelphiAssignment.CurrentSymbols(next).ToDictionary(s => s.Symbol);
        var changed = symbols.Values.Where(s => !previousSymbols.TryGetValue(s.Symbol, out var old) || old != s)
            .Concat(book.Symbols.Where(s => previousSymbols.ContainsKey(s.Symbol) && !symbols.ContainsKey(s.Symbol)).Select(s => s with { IsRelevant = false }));
        var account = RestartAccountRehearsal.Account(next, book.Account.ExecutionEnabled) with { PermissionId = book.Settings.PermissionId,
            CommittedPurchaseCost = book.Account.CommittedPurchaseCost, ReservedPurchaseCost = book.Account.ReservedPurchaseCost,
            BrokerCursor = book.Account.BrokerCursor, LastAppliedBrokerFactId = book.Account.LastAppliedBrokerFactId };
        if (account.BrokerObservedUtc < book.Account.BrokerObservedUtc)
            account = account with { ObservedCash = book.Account.ObservedCash, ObservedBuyingPower = book.Account.ObservedBuyingPower,
                BrokerObservedUtc = book.Account.BrokerObservedUtc };
        return new(next.RevisionId, "PublishManagedRevision", next.SavedAt.UtcDateTime, book.Account.Version, account) {
            AdvanceCheckpoint = true, Positions = positions.ToImmutable(), Orders = orders.ToImmutable(), Symbols = changed.ToImmutableArray() };
    }
}
