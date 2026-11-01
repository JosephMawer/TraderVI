#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Storage;

// Supplied by the approved strategy for this original request. Recovery never chooses a stop.
public sealed record BuyOpeningTerms(Guid BindingId, Guid PermissionId, Guid RequestId,
    string ProtectionOwner, string ProtectionStage, decimal? ProtectionFloor, decimal? OriginalTarget);

/// <summary>Offline reconciliation of an original opening buy from its broker status.</summary>
public sealed class InterruptedBuyRecovery(TradingCurrentStore store,
    Func<Guid, string, CancellationToken, Task<BrokerOrderFact?>> readOriginalOrder,
    Func<TradingApprovedSettings, TradingOrder, BuyOpeningTerms> openingTerms)
{
    // A null result means this request has no active work. Both external lookups are read-only.
    public async Task<TradingSaveResult?> RecoverAsync(Guid bindingId, Guid requestId, DateTimeOffset observedAt,
        CancellationToken ct = default)
    {
        var state = await store.ReadAsync(bindingId, ct)
            ?? throw new InvalidOperationException("Current account is missing.");
        var order = state.Orders.SingleOrDefault(o => o.RequestId == requestId);
        if (order is null) return null;
        return await RecoverAsync(state, order, observedAt, ct);
    }

    internal async Task<TradingSaveResult> RecoverAsync(TradingCurrentState state, TradingOrder order,
        DateTimeOffset observedAt, CancellationToken ct)
    {
        var bindingId = state.Account.BindingId;
        var requestId = order.RequestId;
        if (order.Side != "BUY" || order.PermissionId != state.Settings.PermissionId)
            throw new InvalidOperationException("This recovery requires the original buy and its approved settings.");
        string externalId = "order-" + requestId.ToString("D");
        Guid cycle = BrokerManagedStrategy.StableId($"{bindingId:D}:{order.SecurityId}:{externalId}");
        if (order.PositionId.HasValue && order.PositionId != cycle)
            throw new InvalidOperationException("The original request belongs to a different position cycle.");
        var fact = await readOriginalOrder(bindingId, externalId, ct);
        var (status, applied) = CompactOrderObservation.Reconcile(order, fact, observedAt);
        var receipt = applied.Order with { Status = status, NeedsWork = !TradingStorageValidation.IsTerminal(status) };
        // Poll the broker every time the request is active. Only persist a changed observation.
        if (!TradingStorageValidation.Same(receipt, order)) receipt = receipt with { LastCheckedUtc = observedAt.UtcDateTime };
        var write = new TradingStateWrite(Guid.NewGuid(), "ReconcileOpeningBuy", observedAt.UtcDateTime,
            state.Account.Version, state.Account) { Orders = [receipt] };
        if (receipt.LatestFilledQuantity > 0)
            write = write with { Positions = [ApplyPositionFill(state, order, fact!, applied, cycle)] };
        return await store.SaveAsync(write, ct);
    }

    private TradingPositionCurrent ApplyPositionFill(TradingCurrentState state, TradingOrder order, BrokerOrderFact fact,
        TradingFillApplication applied, Guid cycle)
    {
        var bindingId = order.BindingId;
        DateTime fillUtc = fact.LastFilledAt!.Value.UtcDateTime;
        if (fillUtc < order.CreatedUtc)
            throw new InvalidOperationException("Reported fill predates the original intent.");
        if (PaperTradingMonitor.ToToronto(fact.CreatedAt.UtcDateTime).Date != PaperTradingMonitor.ToToronto(fillUtc).Date)
            throw new InvalidOperationException("Resolve individual entry timing for this multi-session buy.");
        var position = state.Positions.SingleOrDefault(p => p.SecurityId == order.SecurityId);
        if (position is null)
        {
            if (order.AppliedFilledQuantity != 0 || order.AppliedNetValue != 0)
                throw new InvalidOperationException("Previously applied buy has no current position.");
            var terms = openingTerms(state.Settings, order);
            if (terms.BindingId != bindingId || terms.PermissionId != order.PermissionId || terms.RequestId != order.RequestId)
                throw new InvalidOperationException("Opening protection does not belong to the original approved buy.");
            if (terms.ProtectionFloor is <= 0 || terms.OriginalTarget is <= 0)
                throw new InvalidOperationException("Invalid opening floor or target for the original buy.");
            position = new TradingPositionCurrent { BindingId = bindingId, PositionId = cycle, SecurityId = order.SecurityId,
                Symbol = order.Symbol, FirstEntryUtc = fillUtc, LastEntryUtc = fillUtc, EntryCount = 1,
                ProtectionOwner = terms.ProtectionOwner, ProtectionStage = terms.ProtectionStage,
                ProtectionFloor = terms.ProtectionFloor, OriginalTarget = terms.OriginalTarget };
        }
        else if (position.PositionId != cycle || position.Symbol != order.Symbol ||
            position.Quantity != order.AppliedFilledQuantity || position.CostBasis != order.AppliedNetValue ||
            fillUtc < position.LastEntryUtc || position.EntryCount != 1 || position.AddOnCount != 0)
            throw new InvalidOperationException("Saved position cannot be attributed to this opening buy alone.");

        return position with { IsOpen = true, Quantity = checked(position.Quantity + applied.QuantityDelta),
            CostBasis = position.CostBasis + applied.NetValueDelta, LastEntryUtc = fillUtc };
    }
}
