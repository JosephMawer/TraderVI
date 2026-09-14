#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    /// <summary>
    /// Explicit broker mutation for reviewed whole shares. The caller must durably retain the intent
    /// and request ID, verify currently available holdings and pending orders, and supply trading
    /// authority. This protocol client does not reserve shares, journal fills or guarantee proceeds.
    /// Uncertain transmissions are fenced locally and must be recovered by reading, never replaying.
    /// </summary>
    public Task<WealthsimpleMarketOrderSubmission> SubmitMarketSellAsync(WealthsimpleMarketSell order, CancellationToken ct = default) =>
        SubmitMarketSellAsync(order, new WealthsimpleOrderAttemptJournal(), ct);

    internal Task<WealthsimpleMarketOrderSubmission> SubmitMarketSellAsync(WealthsimpleMarketSell order,
        WealthsimpleOrderAttemptJournal journal, CancellationToken ct = default) =>
        SubmitMarketOrderAsync(WealthsimpleMarketOrder.From(order), journal, ct);

    public Task<WealthsimpleReadResult<WealthsimpleMarketOrderState>> ReadMarketSellOrderAsync(WealthsimpleMarketSell order, CancellationToken ct = default) =>
        ReadMarketOrderAsync(WealthsimpleMarketOrder.From(order), null, ct);
}
