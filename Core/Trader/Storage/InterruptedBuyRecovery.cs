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

/// <summary>Offline recovery boundary for an original opening buy whose remainder was cancelled.</summary>
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
        if (order.Side != "BUY" || order.PermissionId != state.Settings.PermissionId)
            throw new InvalidOperationException("This recovery requires the original buy and its approved settings.");
        string externalId = "order-" + requestId.ToString("D");
        var fact = await readOriginalOrder(bindingId, externalId, ct);
        if (fact is null || fact.ExternalId != externalId || fact.SecurityId != order.SecurityId ||
            fact.Symbol != order.Symbol || fact.Side != order.Side || fact.SubmittedQuantity != order.Quantity ||
            !WealthsimpleReconciliation.Valid(fact, observedAt))
            throw new InvalidOperationException("Missing or conflicting broker facts for the original buy.");
        if (fact.Status is not ("CANCELLED" or "CANCELED") || fact.FilledQuantity <= 0 ||
            fact.FilledQuantity >= order.Quantity || fact.NetValue is not > 0 || fact.LastFilledAt is null)
            throw new InvalidOperationException("This recovery requires a partially filled buy with a confirmed cancellation.");
        if (TradingStorageValidation.IsTerminal(order.Status) &&
            (order.Status != "Cancelled" || order.LatestFilledQuantity != fact.FilledQuantity || order.LatestNetValue != fact.NetValue))
            throw new InvalidOperationException("The broker outcome differs from the saved terminal receipt.");

        DateTime fillUtc = fact.LastFilledAt.Value.UtcDateTime;
        if (fillUtc < order.CreatedUtc)
            throw new InvalidOperationException("Reported fill predates the original intent.");
        if (PaperTradingMonitor.ToToronto(fact.CreatedAt.UtcDateTime).Date != PaperTradingMonitor.ToToronto(fillUtc).Date)
            throw new InvalidOperationException("Resolve individual entry timing for this multi-session buy.");
        var applied = TradingFillApplication.Prepare(order, (int)fact.FilledQuantity, fact.NetValue.Value);
        Guid cycle = BrokerManagedStrategy.StableId($"{bindingId:D}:{order.SecurityId}:{externalId}");
        var position = state.Positions.SingleOrDefault(p => p.SecurityId == order.SecurityId);
        if (order.PositionId.HasValue && order.PositionId != cycle)
            throw new InvalidOperationException("The original request belongs to a different position cycle.");
        if (position is null)
        {
            if (order.AppliedFilledQuantity != 0 || order.AppliedNetValue != 0)
                throw new InvalidOperationException("Previously applied buy has no current position.");
            var terms = openingTerms(state.Settings, order);
            if (terms.BindingId != bindingId || terms.PermissionId != order.PermissionId || terms.RequestId != requestId)
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

        var updated = position with { IsOpen = true, Quantity = checked(position.Quantity + applied.QuantityDelta),
            CostBasis = position.CostBasis + applied.NetValueDelta, LastEntryUtc = fillUtc };
        var receipt = applied.Order with { Status = "Cancelled", NeedsWork = false, LastCheckedUtc = observedAt.UtcDateTime };
        return await store.SaveAsync(new(Guid.NewGuid(), "RecoverCancelledOpeningBuy", observedAt.UtcDateTime,
            state.Account.Version, state.Account) { Positions = [updated], Orders = [receipt] }, ct);
    }
}
