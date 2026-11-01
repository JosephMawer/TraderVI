#nullable enable
using Core.WSTrade.Accounts;
using Core.Trader.Storage;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleConnection : IBrokerReconciliationSource
{
    internal Task<WealthsimpleReadResult<BrokerOrderFact>> ReadOriginalOrderAsync(WealthsimpleTfsaAccount account,
        string identity, TradingOrder order, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadOriginalOrderAsync(account, identity, order.RequestId, order.SecurityId,
            order.Symbol, order.Side, order.Quantity, order.LatestFilledQuantity > 0 || order.AppliedFilledQuantity > 0, ct), ct);

    public Task<WealthsimpleReadResult<BrokerAccountHistory>> ReadTfsaOrderHistoryAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadTfsaOrderHistoryAsync(account, ct), ct);
    public Task<WealthsimpleReadResult<BrokerActivityCheckpoint>> ReadTfsaActivityCheckpointAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadTfsaActivityCheckpointAsync(account, ct), ct);
}
