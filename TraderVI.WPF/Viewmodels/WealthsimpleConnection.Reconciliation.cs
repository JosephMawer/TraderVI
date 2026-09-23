#nullable enable
using Core.WSTrade.Accounts;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed partial class WealthsimpleConnection : IBrokerReconciliationSource
{
    public Task<WealthsimpleReadResult<BrokerAccountHistory>> ReadTfsaOrderHistoryAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadTfsaOrderHistoryAsync(account, ct), ct);
    public Task<WealthsimpleReadResult<BrokerActivityCheckpoint>> ReadTfsaActivityCheckpointAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadTfsaActivityCheckpointAsync(account, ct), ct);
}
