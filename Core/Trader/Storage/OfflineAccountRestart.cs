#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Storage;

internal sealed record RestartNotice(string Action, string Symbol, Guid OriginalId, string Reason);
internal sealed record AccountRestartResult(TradingCurrentState Current, ImmutableArray<RestartNotice> Notices);

/// <summary>Disconnected account rehearsal. Only test composition supplies the simulated sell transport.</summary>
internal sealed class OfflineAccountRestart(TradingCurrentStore sourceStore,
    Func<TradingOrder, CancellationToken, Task<BrokerOrderFact?>> lookup,
    Func<Guid, CancellationToken, Task<(Guid Binding, WealthsimpleTfsaSnapshot Snapshot)>> readBrokerAccount,
    Func<TradingOrder, CancellationToken, Task> simulateSell, Func<CancellationToken, Task<IRestartSellLease>> acquireSellLease,
    Func<TradingApprovedSettings, TradingOrder, BuyOpeningTerms> openingTerms, TimeProvider clock,
    RestartOrderJournal? orderJournal = null, Func<TradingOrder, bool>? belongsToManagedSale = null)
{
    internal OfflineAccountRestart(TradingCurrentStore store, Func<Guid, string, CancellationToken, Task<BrokerOrderFact?>> lookup,
        Func<Guid, CancellationToken, Task<(Guid Binding, WealthsimpleTfsaSnapshot Snapshot)>> snapshot,
        Func<TradingOrder, CancellationToken, Task> sell, Func<CancellationToken, Task<IRestartSellLease>> lease,
        Func<TradingApprovedSettings, TradingOrder, BuyOpeningTerms> opening, TimeProvider time, RestartOrderJournal? journal = null)
        : this(store, (order, ct) => lookup(order.BindingId, "order-" + order.RequestId.ToString("D"), ct),
            snapshot, sell, lease, opening, time, journal) { }
    internal async Task<AccountRestartResult> RunAsync(Guid bindingId, CancellationToken ct = default)
    {
        var notices = ImmutableArray.CreateBuilder<RestartNotice>();
        void Align(TradingCurrentState state)
        {
            try { orderJournal!.AlignHoldings(state); }
            catch (Exception error) when (error is InvalidOperationException or System.IO.IOException or
                System.IO.InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or
                System.Text.Json.JsonException)
            {
                // Verified fills still belong in SQL. An uncovered position fails its own original-sale check.
                if (!notices.Any(n => n.Action == "Journal coverage"))
                    notices.Add(new("Journal coverage", "", bindingId, "Check the prepared sale lookup. Retry coverage from committed holdings."));
            }
        }
        var store = orderJournal == null ? sourceStore : sourceStore.WithHoldingObserver(Align);
        async Task<TradingCurrentState> Read() => await store.ReadAsync(bindingId, ct)
            ?? throw new InvalidOperationException("Missing saved account.");
        var current = await Read();
        var buys = new InterruptedBuyRecovery(store, lookup, openingTerms);
        // Snapshot work identities only. A saved trigger takes priority over unrelated status calls.
        var exits = current.Positions.Where(p => p.ExitDecisionId.HasValue).Select(p => p.PositionId).ToArray();
        foreach (var id in exits)
        {
            ct.ThrowIfCancellationRequested();
            var position = current.Positions.SingleOrDefault(p => p.PositionId == id);
            if (position?.ExitDecisionId is not Guid decision) continue;
            Guid request = BrokerManagedStrategy.StableId(decision.ToString("D"));
            try
            {
                var old = await store.ReadOrderAsync(bindingId, request, ct);
                if (old is not null)
                {
                    if (old.Side != "SELL" || old.DecisionId != decision || old.PositionId != id || old.SecurityId != position.SecurityId)
                        throw new InvalidOperationException("The original exit identity belongs to different saved instructions.");
                    if (!old.NeedsWork)
                        notices.Add(new("Sell", position.Symbol, request, $"Original exit ended {old.Status}; review remaining shares."));
                    continue;
                }
                var reason = SaleBlock(current, position, clock.GetUtcNow());
                if (reason is not null)
                {
                    notices.Add(new("Sell", position.Symbol, request, reason));
                    continue;
                }
                using var admission = await acquireSellLease(ct);
                admission.Verify(current, position, request, clock.GetUtcNow(), final: false);
                var sale = new TradingOrder { BindingId = bindingId, RequestId = request, DecisionId = decision,
                    PermissionId = current.Account.PermissionId, PositionId = id, SecurityId = position.SecurityId,
                    Symbol = position.Symbol, Side = "SELL", Quantity = position.Quantity, ReservedQuantity = position.Quantity,
                    Status = "Unknown", CreatedUtc = clock.GetUtcNow().UtcDateTime };
                var saved = await store.SaveAsync(new(Guid.NewGuid(), "ReserveProtectiveExit", sale.CreatedUtc,
                    current.Account.Version, current.Account) { Orders = [sale],
                    Positions = [position with { ReservedSellQuantity = sale.Quantity }] }, ct);
                current = await Read();
                // Only the creator can dispatch. A later pass can only query the original ID,
                // even when the first pass died between commit and send or lost a reply.
                if (saved.AlreadyCommitted || saved.WrittenRows == 0 || current.Account.Version != saved.Version ||
                    !CanSell(current, clock.GetUtcNow()))
                    notices.Add(new("Sell", sale.Symbol, request, "Reservation changed or authority expired before send. Check the original request."));
                else
                {
                    admission.Verify(current, position, request, clock.GetUtcNow(), final: true);
                    await simulateSell(sale, ct);
                }
            }
            catch (Exception e) when (Recoverable(e, ct))
            {
                notices.Add(new("Sell", position.Symbol, request, Message(e)));
                current = await Read();
            }
        }

        // Include newly reserved sells. Existing orders are looked up even when new submissions are disabled.
        foreach (var work in current.Orders.OrderBy(o => o.Side == "SELL" ? 0 : 1).ToArray())
        {
            ct.ThrowIfCancellationRequested();
            var order = current.Orders.SingleOrDefault(o => o.RequestId == work.RequestId);
            if (order is null) continue;
            try
            {
                if (order.Side == "BUY") orderJournal?.FindOriginal(order);
                var saved = order.Side == "SELL"
                    ? await RecoverSale(store, current, order, ct)
                    : await buys.RecoverAsync(current, order, clock.GetUtcNow(), ct);
                if (saved.WrittenRows > 0) current = await Read();
                // An authoritative read resolves any earlier uncertainty about dispatch.
                for (int i = notices.Count - 1; i >= 0; i--)
                    if (notices[i].OriginalId == order.RequestId) notices.RemoveAt(i);
                if (current.Orders.Any(o => o.RequestId == order.RequestId))
                    notices.Add(new(order.Side, order.Symbol, order.RequestId, "Still open. Keep the reservation and check this request again."));
                else if (order.Side == "SELL" && current.Positions.Any(p => p.PositionId == order.PositionId))
                {
                    var closed = await store.ReadOrderAsync(bindingId, order.RequestId, ct);
                    notices.Add(new("Sell", order.Symbol, order.RequestId, $"Original exit ended {closed?.Status}; review remaining shares."));
                }
            }
            catch (Exception e) when (Recoverable(e, ct))
            {
                notices.Add(new(order.Side, order.Symbol, order.RequestId, Message(e)));
                current = await Read();
            }
        }
        // One account observation per pass, after exit handling. A discrepancy is evidence
        // to reconcile. Holdings alone cannot attribute a fill to an original request.
        try
        {
            var observation = await readBrokerAccount(bindingId, ct);
            current = await Read();
            var write = RestartAccountObservation.Prepare(current, observation.Binding, observation.Snapshot, clock.GetUtcNow(), notices);
            var saved = await store.SaveAsync(write, ct);
            if (saved.WrittenRows > 0) current = await Read();
        }
        catch (Exception e) when (Recoverable(e, ct))
        {
            notices.Add(new("Account observation", "", bindingId, Message(e)));
            current = await Read();
        }
        return new(current, notices.ToImmutable());
    }

    private static bool Recoverable(Exception error, CancellationToken ct) => !ct.IsCancellationRequested &&
        error is InvalidOperationException or TimeoutException or System.Net.Http.HttpRequestException;

    private static string Message(Exception error) => error is InvalidOperationException ? error.Message :
        "Broker or save reply unavailable. Keep the original request and retry its lookup.";

    private static bool CanSell(TradingCurrentState s, DateTimeOffset now) => s.Account.ExecutionEnabled &&
        s.Account.Phase == "Active" && s.Settings.SellAllowed && s.Settings.ApprovedUtc <= now.UtcDateTime &&
        (!s.Settings.ExpiresUtc.HasValue || s.Settings.ExpiresUtc > now.UtcDateTime);

    private static string? SaleBlock(TradingCurrentState state, TradingPositionCurrent p, DateTimeOffset now)
    {
        if (!CanSell(state, now)) return "Current execution controls or sell permission prevent a new sale.";
        if (state.Positions.Count(other => other.SecurityId == p.SecurityId) != 1)
            return "Resolve the overlapping current position cycles before selling these shares.";
        if (!state.Settings.OwnsProtection(p) || p.ProtectionFloor is <= 0 ||
            p.ExitTriggeredUtc < p.FirstEntryUtc || p.ExitTriggeredUtc > now.UtcDateTime)
            return "Check the saved protection owner and trigger time for this holding.";
        if (p.ReservedSellQuantity != 0 || state.Orders.Any(o => o.Side == "SELL" &&
            (o.PositionId == p.PositionId || o.SecurityId == p.SecurityId || o.Symbol == p.Symbol)))
            return "Resolve the existing sell reservation for this holding.";
        foreach (var restriction in state.Restrictions)
        {
            if (restriction.ActionCode == "Buy") continue;
            if (AffectsPosition(restriction.ScopeKey, p))
                return $"{restriction.ReasonCode}: {restriction.Explanation}";
        }
        return null;
    }

    // A known different position/security/symbol is independent. Unknown scope stays restrictive.
    private static bool AffectsPosition(string key, TradingPositionCurrent p)
    {
        if (key.StartsWith("Position:", StringComparison.Ordinal) && Guid.TryParse(key[9..], out var id))
            return id == p.PositionId;
        if (key.StartsWith("Security:", StringComparison.Ordinal) && key.Length > 9) return key[9..] == p.SecurityId;
        if (key.StartsWith("Symbol:", StringComparison.Ordinal) && key.Length > 7) return key[7..] == p.Symbol;
        return true;
    }

    private async Task<TradingSaveResult> RecoverSale(TradingCurrentStore store, TradingCurrentState state, TradingOrder order, CancellationToken ct)
    {
        var position = ReservedSalePosition(state, order, belongsToManagedSale);
        var fact = await lookup(order, ct);
        var write = PrepareSale(state, position, order, fact, clock.GetUtcNow(), orderJournal);
        return await store.SaveAsync(write, ct);
    }

    internal static TradingPositionCurrent ReservedSalePosition(TradingCurrentState state, TradingOrder order,
        Func<TradingOrder, bool>? belongsToManagedSale = null)
    {
        var p = state.Positions.SingleOrDefault(p => p.PositionId == order.PositionId);
        if (p is null || p.SecurityId != order.SecurityId || p.Symbol != order.Symbol ||
            (p.ExitDecisionId != order.DecisionId && belongsToManagedSale?.Invoke(order) != true) ||
            order.ReservedQuantity != order.Quantity - order.AppliedFilledQuantity ||
            p.ReservedSellQuantity != order.ReservedQuantity || p.Quantity < p.ReservedSellQuantity)
            throw new InvalidOperationException("Protective sell and saved position reservations do not agree.");
        return p;
    }

    internal static TradingStateWrite PrepareSale(TradingCurrentState state, TradingPositionCurrent p, TradingOrder order,
        BrokerOrderFact? fact, DateTimeOffset now, RestartOrderJournal? orderJournal)
    {
        var (status, fill) = CompactOrderObservation.Reconcile(order, fact, now);
        CompactOrderObservation.RequireSellFillTime(order, p, fact!, () => orderJournal?.FindOriginalSell(order));
        bool open = !TradingStorageValidation.IsTerminal(status);
        int remaining = p.Quantity - fill.QuantityDelta;
        int reserved = open ? order.Quantity - fill.Order.AppliedFilledQuantity : 0;
        var receipt = fill.Order with { Status = status, NeedsWork = open, ReservedQuantity = reserved };
        if (!TradingStorageValidation.Same(order, receipt)) receipt = receipt with { LastCheckedUtc = now.UtcDateTime };
        var position = p with { IsOpen = remaining > 0, Quantity = remaining, ReservedSellQuantity = reserved,
            CostBasis = remaining == 0 ? 0 : decimal.Round(p.CostBasis * remaining / p.Quantity, 6) };
        var write = new TradingStateWrite(Guid.NewGuid(), "ReconcileProtectiveExit", now.UtcDateTime, state.Account.Version,
            state.Account) { Orders = [receipt], Positions = [position] };
        var symbol = state.Symbols.SingleOrDefault(s => s.Symbol == p.Symbol && s.StrategyId == state.Account.StrategyId);
        if (remaining == 0 && symbol is not null)
            write = write with { Symbols = [symbol with { LastExitUtc = fact!.LastFilledAt!.Value.UtcDateTime, LastExitReason = p.ExitReason }] };
        return write;
    }
}
