#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    public async Task<WealthsimpleReadResult<ManualSellContext>> ReadManualSellContextAsync(
        WealthsimpleTfsaAccount account, string symbol, CancellationToken ct = default)
    {
        var securities = await FindCanadianSecuritiesAsync(symbol, ct);
        if (securities.Status != WealthsimpleReadStatus.Success || securities.Data?.Count != 1)
            return new(securities.Status == WealthsimpleReadStatus.Success ? WealthsimpleReadStatus.Unavailable : securities.Status, Stage: "unique Canadian stock");
        var security = securities.Data[0];
        var quote = await ReadManualSellQuoteAsync(security, ct);
        if (quote.Status != WealthsimpleReadStatus.Success || quote.Data == null) return new(quote.Status, Stage: "sell quote and regular session");
        var snapshot = await ReadTfsaSnapshotAsync(account, ct);
        if (snapshot.Status != WealthsimpleReadStatus.Success || snapshot.Data == null) return new(snapshot.Status, Stage: "holdings");
        var pending = await ReadTfsaPendingOrdersAsync(account, ct);
        if (pending.Status != WealthsimpleReadStatus.Success || pending.Data == null) return new(pending.Status, Stage: "pending orders");
        return new(WealthsimpleReadStatus.Success, new(account, security, snapshot.Data, pending.Data, quote.Data));
    }

    internal Task<WealthsimpleReadResult<ManualBuyQuote>> ReadManualSellQuoteAsync(WealthsimpleCanadianSecurity security, CancellationToken ct = default) =>
        ReadManualOrderQuoteAsync(security, isSell: true, ct);

    public async Task<WealthsimpleReadResult<WealthsimpleMarketOrderState>> ReadManualSellOrderAsync(ManualSellIntent intent, CancellationToken ct = default)
    {
        intent.Validate();
        var accounts = await ReadTfsaAccountsAsync(ct);
        if (accounts.Status != WealthsimpleReadStatus.Success || accounts.Data == null) return new(accounts.Status);
        var account = accounts.Data.SingleOrDefault(a => a.Id == intent.AccountId);
        if (account == null) return new(WealthsimpleReadStatus.AccountUnavailable);
        return await ReadMarketOrderAsync(WealthsimpleMarketOrder.From(new WealthsimpleMarketSell(account,
            new(intent.SecurityId, intent.Symbol, intent.Exchange), intent.Quantity, intent.RequestId)), intent.IdentityId, ct);
    }

    public async Task<WealthsimpleReadResult<ManualSellInventory>> ReadManualSellInventoryAsync(ManualSellIntent intent, CancellationToken ct = default)
    {
        intent.Validate();
        var accounts = await ReadTfsaAccountsAsync(ct);
        if (accounts.Status != WealthsimpleReadStatus.Success || accounts.Data == null) return new(accounts.Status);
        var account = accounts.Data.SingleOrDefault(a => a.Id == intent.AccountId);
        if (account == null) return new(WealthsimpleReadStatus.AccountUnavailable);
        var snapshot = await ReadTfsaSnapshotAsync(account, ct);
        if (snapshot.Status != WealthsimpleReadStatus.Success || snapshot.Data == null) return new(snapshot.Status);
        var pending = await ReadTfsaPendingOrdersAsync(account, ct);
        if (pending.Status != WealthsimpleReadStatus.Success || pending.Data == null) return new(pending.Status);
        // Recheck persisted ownership under the client lock after the complete read. A replacement
        // login cannot resolve a saved sell against another identity's positions.
        return await ReadLockedAsync(() =>
        {
            if (intent.IdentityId != session.IdentityId || !OwnsAccount(account))
                throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "sell identity");
            var quantity = snapshot.Data.Holdings.SingleOrDefault(h => h.SecurityId == intent.SecurityId)?.Quantity ?? 0;
            if (quantity < 0) throw Invalid();
            return Task.FromResult(new ManualSellInventory(quantity, pending.Data.Orders.Count != 0, snapshot.Data.RetrievedAt));
        }, ct);
    }
}
