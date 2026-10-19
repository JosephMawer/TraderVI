#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

/// <summary>Explicit operator scope for one regular session, anchored after confirmed liquidation.</summary>
public sealed record BrokerNoTransferSessionScope(Guid ScopeId, Guid BindingId, Guid BaselineId,
    DateOnly Session, DateTimeOffset ApprovedAt, string ApprovedBy, BrokerAccountReconciliation CashOnlyAnchor)
{
    public const string Contract = "SupervisedNoTransferCashReconciliationV1";
    public ImmutableArray<ManualSellRecord> OpeningSales { get; init; } = [];
    // Explicit October 19 operator decision. Absent in older scopes: those retain all production checks.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LimitedFirstSessionTrial { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? PurchaseExpiresAt { get; init; }
    public bool OpeningSalesVerified => !OpeningSales.IsDefault && OpeningSales.Length == 2 && OpeningSales.All(s => s.IsResolved &&
        s.Latest?.IsFilled == true && s.Latest.FilledQuantity == s.Intent.Quantity &&
        s.InventoryVerifiedAt <= CashOnlyAnchor.SnapshotAt &&
        s.Intent.AccountId == CashOnlyAnchor.AccountId && s.Intent.IdentityId == CashOnlyAnchor.IdentityId && CashOnlyAnchor.Orders.Any(o =>
            o.ExternalId == "order-" + s.Intent.RequestId.ToString("D") && o.Side == "SELL" &&
            o.SecurityId == s.Intent.SecurityId && o.Symbol == s.Intent.Symbol && o.Currency == "CAD" && o.IsTerminal &&
            o.FilledQuantity == s.Intent.Quantity && o.SubmittedQuantity == s.Intent.Quantity &&
            o.NetValue == s.Latest.FilledNetValue?.Amount && o.GrossValue == s.Latest.FilledValue?.Amount &&
            o.Fees == s.Latest.Fees?.Amount && o.Tax == s.Latest.TaxWithholding?.Amount)) &&
        OpeningSales.Select(s => s.Intent.SecurityId).Distinct().Count() == 2;
    public void Validate(ITsxSessionCalendar calendar)
    {
        ValidateAnchor();
        var bounds = calendar.GetSessionBounds(Session);
        if (CashOnlyAnchor.SnapshotAt < bounds.OpenUtc || CashOnlyAnchor.SnapshotAt >= bounds.CloseUtc)
            throw new InvalidOperationException("The scoped cash-only anchor must belong to its reviewed regular session.");
        if (LimitedFirstSessionTrial && PurchaseExpiresAt != bounds.CloseUtc)
            throw new InvalidOperationException("Limited-trial purchase permission must end at the reviewed regular close.");
    }
    public void ValidateAnchor()
    {
        if (ScopeId == Guid.Empty || BindingId == Guid.Empty || BaselineId == Guid.Empty ||
            ApprovedAt < CashOnlyAnchor.CheckedAt || string.IsNullOrWhiteSpace(ApprovedBy) ||
            string.IsNullOrWhiteSpace(CashOnlyAnchor.AccountId) || string.IsNullOrWhiteSpace(CashOnlyAnchor.IdentityId) ||
            CashOnlyAnchor.SnapshotAt == default ||
            LimitedFirstSessionTrial && DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(ApprovedAt.UtcDateTime)) != Session ||
            LimitedFirstSessionTrial && (PurchaseExpiresAt is null || PurchaseExpiresAt <= ApprovedAt) ||
            CashOnlyAnchor.CheckedAt < CashOnlyAnchor.SnapshotAt || CashOnlyAnchor.Cash is not > 0 ||
            CashOnlyAnchor.AccountValue != CashOnlyAnchor.Cash || CashOnlyAnchor.BuyingPower is null or < 0 ||
            !CashOnlyAnchor.HoldingsMatch || CashOnlyAnchor.Holdings.Any(h => h.BrokerQuantity != 0) ||
            CashOnlyAnchor.Orders.Any(o => !o.IsTerminal))
            throw new InvalidOperationException("The scoped session needs reviewed authorization and a reconciled cash-only book after the required exits.");
    }
    public override string ToString() => "Supervised no-transfer session scope (financial data omitted)";
}

/// <summary>
/// Operator-attested, one-session scope plus exact cash/order reconciliation. This is NOT native
/// proof of a complete capital-event feed; it never advances daily references or supports transfers.
/// </summary>
public sealed class BrokerNoTransferRiskEvidenceSource(BrokerNoTransferSessionScope scope,
    ITsxSessionCalendar calendar, TimeProvider? time = null) : IBrokerRiskEvidenceSource
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public Task<BrokerRiskEvidence> ReadAsync(BrokerImportReceipt import, BrokerAccountRiskState state, CancellationToken ct) =>
        ReadAsync(import, state, [], ct);

    public Task<BrokerRiskEvidence> ReadAsync(BrokerImportReceipt import, BrokerAccountRiskState state,
        ImmutableArray<BrokerStrategyOrder> ownedOrders, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        scope.Validate(calendar);
        var now = clock.GetUtcNow(); var bounds = calendar.GetSessionBounds(scope.Session);
        var current = import.Plan.Observation; var anchor = scope.CashOnlyAnchor;
        BrokerRiskEvidence Block(string reason) => new([], null, [reason]);
        if (scope.ApprovedAt > now || state.BaselineId != scope.BaselineId || state.BindingId != scope.BindingId ||
            import.BindingId != scope.BindingId || state.TradingDate != scope.Session ||
            now < bounds.OpenUtc || now >= bounds.CloseUtc || state.Through < anchor.SnapshotAt ||
            state.Through > current.SnapshotAt || current.SnapshotAt < anchor.SnapshotAt ||
            current.SnapshotAt > now || now - current.SnapshotAt > TimeSpan.FromSeconds(30) ||
            current.CheckedAt < current.SnapshotAt || current.CheckedAt > now || !current.HoldingsMatch ||
            current.IdentityId != anchor.IdentityId || current.AccountId != anchor.AccountId)
            return Task.FromResult(Block("The supervised no-transfer scope is stale, expired or does not match this reconciled account/session. New buys stay stopped."));
        if (anchor.Orders.Select(o => o.ExternalId).Distinct().Count() != anchor.Orders.Count ||
            ownedOrders.Select(o => o.ExternalId).Distinct().Count() != ownedOrders.Length)
            return Task.FromResult(Block("Duplicate order identities require review before new buys."));
        var original = anchor.Orders.ToDictionary(o => o.ExternalId, StringComparer.Ordinal);
        if (current.Orders.Select(o => o.ExternalId).Distinct().Count() != current.Orders.Count ||
            original.Values.Any(o => !current.Orders.Any(n => n.ExternalId == o.ExternalId && n.IsSameOrPostedSuccessor(o))))
            return Task.FromResult(Block("An original financial order changed or disappeared. Review the account before new buys."));
        var added = current.Orders.Where(o => !original.ContainsKey(o.ExternalId)).ToArray();
        foreach (var order in added)
        {
            var owned = ownedOrders.SingleOrDefault(o => o.ExternalId == order.ExternalId);
            if (owned is null || owned.SecurityId != order.SecurityId || owned.Symbol != order.Symbol || owned.Side != order.Side ||
                order.SubmittedQuantity != owned.Quantity || order.FilledQuantity < 0 || order.FilledQuantity > owned.Quantity ||
                order.Currency != "CAD" || order.FilledQuantity > 0 && order.NetValue is not > 0)
                return Task.FromResult(Block("An outside or conflicting order violates the supervised scope. Reconcile before new buys."));
        }
        decimal expected = anchor.Cash!.Value + added.Sum(o => o.FilledQuantity <= 0 ? 0m :
            o.Side == "SELL" ? o.NetValue!.Value : -o.NetValue!.Value);
        if (current.Cash is null || current.Cash != expected)
            return Task.FromResult(Block("Cash no longer equals the scoped starting cash plus confirmed owned trades. Stop new buys and review the unexplained movement."));
        var hash = BrokerAccountImport.Hash(new { Contract = BrokerNoTransferSessionScope.Contract, scope.ScopeId,
            Scope = scope, Current = BrokerAccountImport.FinancialFingerprint(current), ExpectedCash = expected,
            OwnedOrders = ownedOrders.Select(o => new { o.ExternalId, o.Side, o.SecurityId, o.Quantity }) });
        return Task.FromResult(new BrokerRiskEvidence([], new(scope.BindingId, state.Through, current.SnapshotAt,
            now, BrokerNoTransferSessionScope.Contract, hash), []));
    }
}
