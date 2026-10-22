#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Accounts;

public interface IWealthsimpleAccountSource : INotifyPropertyChanged
{
    bool IsConnected { get; }
    bool HasSession { get; }
    long SessionRevision { get; }
    Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadTfsaAccountsAsync(CancellationToken ct);
    Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>> ReadTfsaSnapshotAsync(WealthsimpleTfsaAccount account, CancellationToken ct);
    Task<WealthsimpleReadResult<WealthsimplePendingOrders>> ReadTfsaPendingOrdersAsync(WealthsimpleTfsaAccount account, CancellationToken ct);
    Task<WealthsimpleReadResult<WealthsimpleDailyPerformance>> ReadTfsaDailyPerformanceAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
        Task.FromResult(new WealthsimpleReadResult<WealthsimpleDailyPerformance>(WealthsimpleReadStatus.Unavailable));
}
