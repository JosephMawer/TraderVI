#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    public async Task<WealthsimpleReadResult<ManualBuyContext>> ReadManualBuyContextAsync(
        WealthsimpleTfsaAccount account, string symbol, CancellationToken ct = default)
    {
        var securities = await FindCanadianSecuritiesAsync(symbol, ct);
        if (securities.Status != WealthsimpleReadStatus.Success || securities.Data?.Count != 1)
            return new(securities.Status == WealthsimpleReadStatus.Success ? WealthsimpleReadStatus.Unavailable : securities.Status, Stage: "unique Canadian stock");
        var security = securities.Data[0];
        var quote = await ReadManualBuyQuoteAsync(security, ct);
        if (quote.Status != WealthsimpleReadStatus.Success || quote.Data == null) return new(quote.Status, Stage: "quote and regular session");
        var snapshot = await ReadTfsaSnapshotAsync(account, ct);
        if (snapshot.Status != WealthsimpleReadStatus.Success || snapshot.Data == null) return new(snapshot.Status, Stage: "account balances");
        var pendingOrders = await ReadTfsaPendingOrdersAsync(account, ct);
        if (pendingOrders.Status != WealthsimpleReadStatus.Success || pendingOrders.Data == null) return new(pendingOrders.Status, Stage: "pending orders");
        return new(WealthsimpleReadStatus.Success, new(account, security, snapshot.Data, pendingOrders.Data, quote.Data));
    }

    internal Task<WealthsimpleReadResult<ManualBuyQuote>> ReadManualBuyQuoteAsync(WealthsimpleCanadianSecurity security, CancellationToken ct = default) =>
        ReadManualOrderQuoteAsync(security, isSell: false, ct);

    private Task<WealthsimpleReadResult<ManualBuyQuote>> ReadManualOrderQuoteAsync(WealthsimpleCanadianSecurity security, bool isSell, CancellationToken ct) =>
        ReadLockedAsync(async () =>
        {
            if (!ReferenceEquals(security.SourceClient, this) || security.SourceIdentity != session.IdentityId)
                throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "security identity");
            using var json = await QueryAsync(isSell ? "TraderVIManualSellQuote" : "TraderVIManualBuyQuote", ManualOrderQuoteQuery(isSell), new { id = security.Id }, ct);
            var data = Required(json.RootElement, "data", "security"); RequireId(data, security.Id);
            if (RequiredText(data, "currency") != "CAD" || RequiredText(data, "securityType") != "EQUITY" ||
                !Required(data, "active").GetBoolean() || !Required(data, isSell ? "sellable" : "buyable").GetBoolean() || !Required(data, "wsTradeEligible").GetBoolean() ||
                RequiredText(Required(data, "stock"), "primaryMic") != security.Exchange ||
                !RequiredArray(data, "allowedOrderSubtypes").Any(x => x.GetString() == "MARKET"))
                throw new ReadFailure(WealthsimpleReadStatus.Unavailable, isSell ? "market-sell eligibility" : "market-buy eligibility");
            var quote = Required(data, "quoteV2");
            if (RequiredText(quote, "__typename") != "EquityQuote" || RequiredText(quote, "securityId") != security.Id || RequiredText(quote, "currency") != "CAD") throw Invalid();
            var regular = RequiredArray(Required(data, "marketMetadata", "currentTradeDay"), "regular").ToArray();
            // The captured Canadian day has one regular session. Unknown/split shapes fail closed.
            if (regular.Length != 1) throw Invalid();
            return new ManualBuyQuote(Number(Required(quote, "ask")), Number(Required(quote, "bid")),
                Timestamp(quote, "quotedAsOf") ?? throw Invalid(), RequiredText(quote, "marketStatus"),
                Timestamp(regular[0], "start") ?? throw Invalid(), Timestamp(regular[0], "end") ?? throw Invalid(), clock.GetUtcNow());
        }, ct);

    // Recovery is a read, scoped to the persisted identity/account/security; no ticker re-resolution.
    public async Task<WealthsimpleReadResult<WealthsimpleMarketBuyState>> ReadManualBuyOrderAsync(ManualBuyIntent intent, CancellationToken ct = default)
    {
        intent.Validate();
        var accounts = await ReadTfsaAccountsAsync(ct);
        if (accounts.Status != WealthsimpleReadStatus.Success || accounts.Data == null) return new(accounts.Status);
        var account = accounts.Data.SingleOrDefault(a => a.Id == intent.AccountId);
        if (account == null) return new(WealthsimpleReadStatus.AccountUnavailable);
        // The order read verifies the current identity as well. Keep the persisted identity check
        // inside the same lock as its query, so replacing the session cannot re-scope recovery.
        return await ReadMarketBuyOrderAsync(new(account, new(intent.SecurityId, intent.Symbol, intent.Exchange), intent.Quantity, intent.RequestId), intent.IdentityId, ct);
    }

    private static string ManualOrderQuoteQuery(bool isSell) => $$"""
        query {{(isSell ? "TraderVIManualSellQuote" : "TraderVIManualBuyQuote")}}($id: ID!) {
          security(id: $id) {
            id currency securityType active {{(isSell ? "sellable" : "buyable")}} wsTradeEligible allowedOrderSubtypes stock { primaryMic }
            marketMetadata { currentTradeDay { regular { start end } } }
            quoteV2(currency: null) { __typename securityId ask bid currency quotedAsOf ... on EquityQuote { marketStatus } }
          }
        }
        """;
}
