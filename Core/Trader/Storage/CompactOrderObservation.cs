#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Linq;

namespace Core.Trader.Storage;

internal static class CompactOrderObservation
{
    internal static (string Status, TradingFillApplication Applied) Reconcile(TradingOrder order,
        BrokerOrderFact? fact, DateTimeOffset observedAt)
    {
        string externalId = "order-" + order.RequestId.ToString("D");
        if (fact is null || fact.ExternalId != externalId || fact.SecurityId != order.SecurityId ||
            fact.Symbol != order.Symbol || fact.Side != order.Side || fact.SubmittedQuantity != order.Quantity ||
            !WealthsimpleReconciliation.Valid(fact, observedAt))
            throw new InvalidOperationException("Missing or conflicting broker facts for the original request.");
        // The common broker validator above checks whole quantities, fill time and net cost.
        bool hasFillValue = order.Side == "SELL" ? fact.NetValue >= 0 : fact.NetValue > 0;
        bool partial = fact.FilledQuantity > 0 && fact.FilledQuantity < order.Quantity && hasFillValue;
        bool unfilled = fact.FilledQuantity == 0;
        string status = fact.Status switch
        {
            "PENDING" when unfilled => "Submitted",
            "REJECTED" when unfilled => "Rejected",
            "EXPIRED" when unfilled || partial => "Expired",
            "PARTIALLY_FILLED" when partial => "Submitted",
            "CANCEL_PENDING" when partial => "Submitted",
            "CANCELLED" or "CANCELED" when unfilled || partial => "Cancelled",
            "FILLED" or "POSTED" when fact.FilledQuantity == order.Quantity && hasFillValue => "Filled",
            _ when unfilled && fact.Status != "PARTIALLY_FILLED" && WealthsimplePendingOrder.FeedStatuses.Contains(fact.Status) => "Submitted",
            _ => throw new InvalidOperationException("Unsupported order status or inconsistent cumulative fill.")
        };
        decimal netCost = fact.NetValue ?? 0m;
        if (TradingStorageValidation.IsTerminal(order.Status) &&
            (order.Status != status || order.LatestFilledQuantity != fact.FilledQuantity || order.LatestNetValue != netCost))
            throw new InvalidOperationException("The broker outcome differs from the saved terminal receipt.");

        return (status, TradingFillApplication.Prepare(order, (int)fact.FilledQuantity, netCost));
    }
}
