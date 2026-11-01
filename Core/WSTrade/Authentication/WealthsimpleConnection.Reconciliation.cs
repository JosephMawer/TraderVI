#nullable enable
using Core.WSTrade.Accounts;
using Core.Trader.Storage;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleConnection : IBrokerReconciliationSource
{
    internal IBrokerReconciliationSource RegularTradingReads => new RegularTradingSource(this);

    private sealed class RegularTradingSource(WealthsimpleConnection connection) : IBrokerReconciliationSource
    {
        public bool IsConnected => connection.IsConnected;
        public long SessionRevision => connection.SessionRevision;
        public Task<WealthsimpleReadResult<BrokerAccountHistory>> ReadTfsaOrderHistoryAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
            connection.ReadAccountDataAsync(client => client.ReadRegularTradingHistoryAsync(account, ct), ct);
        public Task<WealthsimpleReadResult<BrokerActivityCheckpoint>> ReadTfsaActivityCheckpointAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
            connection.ReadTfsaActivityCheckpointAsync(account, ct);
        public Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>> ReadTfsaSnapshotAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
            connection.ReadTfsaSnapshotAsync(account, ct);
        public Task<WealthsimpleReadResult<WealthsimplePendingOrders>> ReadTfsaPendingOrdersAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
            connection.ReadTfsaPendingOrdersAsync(account, ct);
    }

    internal Task<WealthsimpleReadResult<BrokerOrderFact>> ReadOriginalOrderAsync(WealthsimpleTfsaAccount account,
        string identity, TradingOrder order, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadOriginalOrderAsync(account, identity, order.RequestId, order.SecurityId,
            order.Symbol, order.Side, order.Quantity, order.LatestFilledQuantity > 0 || order.AppliedFilledQuantity > 0, ct), ct);

    public Task<WealthsimpleReadResult<BrokerAccountHistory>> ReadTfsaOrderHistoryAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadTfsaOrderHistoryAsync(account, ct), ct);
    public Task<WealthsimpleReadResult<BrokerActivityCheckpoint>> ReadTfsaActivityCheckpointAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadTfsaActivityCheckpointAsync(account, ct), ct);
}
