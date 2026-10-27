#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>Fresh broker bids for an already-owned position; never an entry quote or fill.</summary>
internal static class BrokerHoldingQuote
{
    internal const string Contract = "BrokerProtectionBidV1";
    internal static TradingQuote Validate(TradingRecommendationInput input, WealthsimpleTfsaAccount account,
        string symbol, DateTime requested, DateTime received, ManualSellContext context)
    {
        var holding = input.CurrentHoldings.SingleOrDefault(h => string.Equals(h.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        var quote = context.Quote;
        if (holding is null || input.BrokerHoldingSecurityIds is null ||
            !input.BrokerHoldingSecurityIds.TryGetValue(symbol, out var security) || context.Security.Id != security ||
            context.Security.SourceIdentity is null || context.Security.Exchange != "XTSE" ||
            !string.Equals(context.Security.Symbol, symbol, StringComparison.OrdinalIgnoreCase) ||
            context.Account != account || context.Snapshot.Account != account || context.PendingOrders.Account != account ||
            context.HeldQuantity != holding.Quantity || context.PendingOrders.Orders.Count != 0 ||
            context.Snapshot.Holdings.Single(h => h.SecurityId == security).Value?.Currency != "CAD" ||
            requested.Kind != DateTimeKind.Utc || received.Kind != DateTimeKind.Utc || requested < input.EvaluatedUtc || received < requested ||
            quote.RetrievedAt.UtcDateTime < requested || quote.RetrievedAt.UtcDateTime > received ||
            quote.QuotedAt > quote.RetrievedAt || received - quote.QuotedAt.UtcDateTime > TimeSpan.FromSeconds(60) ||
            context.Snapshot.RetrievedAt.UtcDateTime < requested || context.Snapshot.RetrievedAt.UtcDateTime > received ||
            context.PendingOrders.RetrievedAt.UtcDateTime < requested || context.PendingOrders.RetrievedAt.UtcDateTime > received ||
            quote.MarketStatus != "OPEN" || requested < quote.SessionOpen.UtcDateTime || received >= quote.SessionClose.UtcDateTime ||
            requested < input.SessionOpenUtc || received >= input.SessionCloseUtc || quote.Bid <= 0)
            throw new InvalidOperationException("Held-position protection needs a fresh matching broker bid, account, inventory and regular session.");
        return new(symbol, requested, received, null, quote.Bid, null, Contract);
    }
}
