#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    /// <summary>
    /// Explicit broker mutation. The caller supplies reviewed authority, fresh cash/risk checks,
    /// and a durably retained intent/request ID. No price cap or automatic mutation retry is implied.
    /// </summary>
    public Task<WealthsimpleMarketBuySubmission> SubmitMarketBuyAsync(WealthsimpleMarketBuy order, CancellationToken ct = default) =>
        SubmitMarketBuyAsync(order, new WealthsimpleOrderAttemptJournal(), ct);

    internal async Task<WealthsimpleMarketBuySubmission> SubmitMarketBuyAsync(WealthsimpleMarketBuy order,
        WealthsimpleOrderAttemptJournal journal, CancellationToken ct = default)
    {
        var result = await SubmitMarketOrderAsync(WealthsimpleMarketOrder.From(order), journal, ct);
        return new(result.Status, result.CreatedAt) { BrokerOrderId = result.BrokerOrderId };
    }

    public Task<WealthsimpleReadResult<WealthsimpleMarketBuyState>> ReadMarketBuyOrderAsync(WealthsimpleMarketBuy order, CancellationToken ct = default) =>
        ReadMarketBuyOrderAsync(order, null, ct);

    // Preserve the existing manual-buy journal's API and serialized observation contract.
    private async Task<WealthsimpleReadResult<WealthsimpleMarketBuyState>> ReadMarketBuyOrderAsync(
        WealthsimpleMarketBuy order, string? expectedIdentity, CancellationToken ct)
    {
        var result = await ReadMarketOrderAsync(WealthsimpleMarketOrder.From(order), expectedIdentity, ct);
        var state = result.Data;
        return new(result.Status, state == null ? null : new(state.Status, state.SubmittedQuantity, state.FilledQuantity,
            state.AverageFillPrice, state.FilledValue, state.FilledNetValue, state.Fees, state.TaxWithholding,
            state.CreatedAt, state.LastFilledAt, state.ExpiresAt, state.TradingSession, state.RetrievedAt), result.Stage);
    }

    internal static Dictionary<string, object> MarketBuyBody(WealthsimpleMarketBuy order) => MarketOrderBody(WealthsimpleMarketOrder.From(order));
    internal const string MarketBuyMutation = MarketOrderMutation;
}
