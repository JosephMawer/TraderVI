#nullable enable
using Core.WSTrade.Accounts;
using System;

namespace Core.WSTrade.Orders;

// Only the typed buy/sell intents can select a side at the shared transport boundary.
internal sealed record WealthsimpleMarketOrder(WealthsimpleTfsaAccount Account, WealthsimpleCanadianSecurity Security,
    int Quantity, Guid RequestId, bool IsSell)
{
    internal string ExternalId => "order-" + RequestId.ToString("D");
    internal string Side => IsSell ? "SELL" : "BUY";
    internal string OrderType => IsSell ? "SELL_QUANTITY" : "BUY_QUANTITY";
    internal string OperationPrefix => IsSell ? "TraderVIMarketSell" : "TraderVIMarketBuy";
    internal static WealthsimpleMarketOrder From(WealthsimpleMarketBuy order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(order.Account, order.Security, order.Quantity, order.RequestId, false);
    }
    internal static WealthsimpleMarketOrder From(WealthsimpleMarketSell order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(order.Account, order.Security, order.Quantity, order.RequestId, true);
    }
    public override string ToString() => "Wealthsimple market-order intent (financial data omitted)";
}

public sealed record WealthsimpleMarketOrderSubmission(WealthsimpleSubmissionStatus Status, DateTimeOffset? CreatedAt = null)
{
    // Acknowledgement only; this is neither the external correlation ID nor proof of a fill.
    internal string? BrokerOrderId { get; init; }
    public override string ToString() => $"Wealthsimple market-order submission: {Status}";
}

/// <summary>Cumulative broker observation. Repeated reads must not be applied as additional fills.</summary>
public sealed record WealthsimpleMarketOrderState(string Status, int SubmittedQuantity, decimal? FilledQuantity,
    WealthsimpleMoney? AverageFillPrice, WealthsimpleMoney? FilledValue, WealthsimpleMoney? FilledNetValue,
    WealthsimpleMoney? Fees, WealthsimpleMoney? TaxWithholding, DateTimeOffset CreatedAt,
    DateTimeOffset? LastFilledAt, DateTimeOffset? ExpiresAt, string TradingSession, DateTimeOffset RetrievedAt)
{
    public bool IsFilled => Status == "FILLED" && FilledQuantity == SubmittedQuantity;
    public override string ToString() => "Wealthsimple market-order state (financial data omitted)";
}
