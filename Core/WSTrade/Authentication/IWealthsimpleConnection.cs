#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public interface IWealthsimpleConnection : IWealthsimpleAccountSource, IManualBuyBroker, IManualSellBroker, IBrokerReconciliationSource
{
    new bool IsConnected { get; }
    new long SessionRevision { get; }
    bool RememberSession { get; }
    bool IsBusy { get; }
    bool AwaitingOtp { get; }
    bool BrowserVerificationRequired { get; }
    string Status { get; }
    string IndicatorColor { get; }
    string Detail { get; }
    string VerificationText { get; }
    string AccessibleStatus { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    Task SignInAsync(string email, string password, bool remember, CancellationToken ct = default);
    Task SubmitOtpAsync(string code, CancellationToken ct = default);
    Task CancelSignInAsync();
    Task ConnectAsync(WealthsimpleAuthClient candidate, bool remember, CancellationToken ct = default);
    Task CheckAsync(bool force = false, CancellationToken ct = default);
    Task SetRememberAsync(bool remember, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    new Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadTfsaAccountsAsync(CancellationToken ct = default);
    new Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>> ReadTfsaSnapshotAsync(WealthsimpleTfsaAccount account, CancellationToken ct = default);
    new Task<WealthsimpleReadResult<WealthsimplePendingOrders>> ReadTfsaPendingOrdersAsync(WealthsimpleTfsaAccount account, CancellationToken ct = default);
    Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleCanadianSecurity>>> FindCanadianSecuritiesAsync(string symbol, CancellationToken ct = default);
}
