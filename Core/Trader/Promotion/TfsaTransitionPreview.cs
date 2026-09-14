#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.Promotion;

public enum WholeShareOrderSide { Buy, Sell }

/// <summary>Validated intent only; construction does not submit an order or grant trading authority.</summary>
public sealed class WholeShareLimitOrder
{
    public WholeShareOrderSide Side { get; }
    public int Quantity { get; }
    public decimal LimitPrice { get; }
    public decimal LimitNotional { get; }
    public WholeShareLimitOrder(WholeShareOrderSide side, decimal quantity, decimal limitPrice)
    {
        if (!Enum.IsDefined(side) || quantity <= 0 || quantity > int.MaxValue || decimal.Truncate(quantity) != quantity || limitPrice <= 0)
            throw new ArgumentException("A limit order requires a valid side, positive whole shares and a positive limit price.");
        Side = side; Quantity = (int)quantity; LimitPrice = limitPrice;
        LimitNotional = checked(Quantity * LimitPrice);
    }
    public override string ToString() => "Whole-share limit order intent (financial data omitted)";
}

public static class WholeShareLimitSizing
{
    // cashLimit already excludes fees/reserves. Never use expected proceeds from pending exits.
    public static int BuyQuantity(int riskShareLimit, decimal cashLimit, decimal limitPrice)
    {
        if (riskShareLimit < 0 || cashLimit < 0 || limitPrice <= 0) throw new ArgumentOutOfRangeException(nameof(cashLimit));
        decimal affordable = decimal.Floor(cashLimit / limitPrice);
        int capped = (int)System.Math.Min(riskShareLimit, affordable);
        return capped >= 10 ? capped / 10 * 10 : capped;
    }
}

public sealed record TfsaIncomingHolding(string Symbol, string? BrokerSecurityId)
{
    public static TfsaIncomingHolding FromSecurity(WealthsimpleCanadianSecurity security) => new(security.Symbol, security.Id);
}
public sealed record TfsaTransitionRow(string Symbol, string Action, decimal? ExistingQuantity, string Explanation);
public sealed record TfsaTransitionPreview(IReadOnlyList<TfsaTransitionRow> Rows, decimal? AvailablePurchaseCash,
    IReadOnlyList<string> Blocks)
{
    // Preview deliberately does not claim completed-trade reconciliation, executable prices or authority.
    public override string ToString() => "TFSA transition preview (financial data omitted)";
}

public static class TfsaTransitionPlanning
{
    public static TfsaTransitionPreview Preview(WealthsimpleTfsaSnapshot account, WealthsimplePendingOrders? orders,
        IReadOnlyList<TfsaIncomingHolding> incoming, decimal purchaseBudget, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account); ArgumentNullException.ThrowIfNull(incoming);
        if (purchaseBudget < 0) throw new ArgumentOutOfRangeException(nameof(purchaseBudget));
        var blocks = new List<string>(); var rows = new List<TfsaTransitionRow>();
        if (!Fresh(account.RetrievedAt, now)) blocks.Add("Refresh broker balances and holdings before reviewing orders.");
        if (orders == null || !ReferenceEquals(orders.Account, account.Account) || !Fresh(orders.RetrievedAt, now))
            blocks.Add("Pending orders for this TFSA have not been verified recently.");
        else if (orders.Orders.Count != 0) blocks.Add("Resolve outstanding orders and reconcile their actual fills before starting a transition.");
        decimal? available = account.Cash is { Currency: "CAD", Amount: >= 0 } cash &&
            account.BuyingPower is { Currency: "CAD", Amount: >= 0 } buying
            ? System.Math.Min(purchaseBudget, System.Math.Min(cash.Amount, buying.Amount)) : null;
        if (available == null) blocks.Add("Current CAD cash and buying power are both required.");
        if (purchaseBudget == 0) blocks.Add("No purchase budget has been authorized in this review.");
        if (incoming.Any(i => string.IsNullOrWhiteSpace(i.Symbol) || (i.BrokerSecurityId != null && string.IsNullOrWhiteSpace(i.BrokerSecurityId))) ||
            incoming.Select(i => i.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != incoming.Count ||
            incoming.Where(i => i.BrokerSecurityId != null).Select(i => i.BrokerSecurityId).Distinct().Count() != incoming.Count(i => i.BrokerSecurityId != null))
            throw new ArgumentException("Incoming holdings must have unambiguous symbols and broker identities.");
        if (incoming.Any(i => i.BrokerSecurityId == null)) blocks.Add("Resolve incoming symbols to unique Canadian broker securities before determining overlaps.");
        var nonzero = account.Holdings.Where(h => h.Quantity != 0).ToArray();
        if (nonzero.Any(h => string.IsNullOrWhiteSpace(h.SecurityId)) || nonzero.Select(h => h.SecurityId).Distinct().Count() != nonzero.Length)
            blocks.Add("Broker holding identities are missing or duplicated.");
        foreach (var h in nonzero)
        {
            if (h.SecurityType != "EQUITY" || h.Value?.Currency != "CAD" || h.BookValue?.Currency != "CAD")
            {
                blocks.Add($"{h.Symbol}: confirm Canadian equity identity and CAD valuation before transfer.");
                rows.Add(new(h.Symbol, "Verify instrument", h.Quantity, "This holding is not verified for the CAD equity transition."));
                continue;
            }
            if (h.Quantity < 0 || decimal.Truncate(h.Quantity) != h.Quantity || h.Quantity > int.MaxValue)
            {
                blocks.Add($"{h.Symbol}: the actual quantity cannot be managed by whole-share orders.");
                rows.Add(new(h.Symbol, "Resolve quantity", h.Quantity, "Preserve the broker quantity exactly; no rounding or fractional order."));
                continue;
            }
            var overlap = incoming.FirstOrDefault(i => i.BrokerSecurityId != null && i.BrokerSecurityId == h.SecurityId);
            if (overlap != null)
                rows.Add(new(h.Symbol, "Retain", h.Quantity, "Keep all actual shares and cost basis. The incoming strategy takes over at activation; no promotion sell/rebuy."));
            else if (string.IsNullOrWhiteSpace(h.SecurityId) || incoming.Any(i => i.BrokerSecurityId == null))
                rows.Add(new(h.Symbol, "Verify overlap", h.Quantity, "Instrument matching is incomplete; no exit is inferred."));
            else
                rows.Add(new(h.Symbol, "Exit at transition", h.Quantity, "Requires a reviewed limit sell and confirmed fills. Pending proceeds do not fund purchases."));
        }
        foreach (var target in incoming.Where(i => i.BrokerSecurityId == null || !nonzero.Any(h => h.SecurityId == i.BrokerSecurityId)))
            rows.Add(new(target.Symbol, "Await entry evaluation", null, "Use fresh strategy evidence, real-account risk sizing and a reviewed limit. Paper quantities and simulated fills are not copied."));
        return new(rows.AsReadOnly(), available, blocks.AsReadOnly());
    }
    private static bool Fresh(DateTimeOffset at, DateTimeOffset now) => at <= now && now - at <= TimeSpan.FromMinutes(2);
}
