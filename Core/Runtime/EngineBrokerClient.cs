#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

public sealed record EngineConnectionStatus(DateTimeOffset CheckedAt, bool IsConnected, bool HasSession,
    long SessionRevision, bool RememberSession, bool IsBusy, bool AwaitingOtp, bool BrowserVerificationRequired,
    string Status, string Detail, string VerificationText)
{
    public static EngineConnectionStatus Read(IWealthsimpleConnection source) => new(DateTimeOffset.UtcNow,
        source.IsConnected, source.HasSession, source.SessionRevision, source.RememberSession, source.IsBusy,
        source.AwaitingOtp, source.BrowserVerificationRequired, source.Status, source.Detail, source.VerificationText);
}
public sealed record EngineSignIn(string Email, string Password, bool Remember)
{ public override string ToString() => "Engine sign-in (credentials omitted)"; }
public sealed record EngineProtectedSession(byte[] ProtectedSession, bool Remember)
{ public override string ToString() => "Protected engine sign-in (contents omitted)"; }
public sealed record EngineInstrumentRequest(WealthsimpleTfsaAccount Account, string Symbol);

/// <summary>Display client only. The server owns authentication, orders and process lifetime.</summary>
public sealed class EngineBrokerClient(IEngineRpc rpc, IWealthsimpleSessionProtector protector) : IWealthsimpleConnection
{
    private volatile EngineConnectionStatus? snapshot;
    private readonly SynchronizationContext? notifications = SynchronizationContext.Current;
    private readonly SemaphoreSlim refresh = new(1, 1);
    private bool detached;
    public IEngineRpc Rpc => rpc;
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Current => !detached && snapshot is { } s && s.CheckedAt <= DateTimeOffset.UtcNow &&
        DateTimeOffset.UtcNow - s.CheckedAt <= TimeSpan.FromSeconds(20);
    public bool IsConnected => Current && snapshot!.IsConnected;
    public bool HasSession => Current && snapshot!.HasSession;
    public long SessionRevision => snapshot?.SessionRevision ?? -1;
    public bool RememberSession => snapshot?.RememberSession == true;
    public bool IsBusy => Current && snapshot!.IsBusy;
    public bool AwaitingOtp => Current && snapshot!.AwaitingOtp;
    public bool BrowserVerificationRequired => Current && snapshot!.BrowserVerificationRequired;
    public string Status => Current ? snapshot!.Status : "Engine disconnected";
    public string Detail => Current ? snapshot!.Detail : "Connect to the running engine. Closing this dashboard does not stop it.";
    public string VerificationText => Current ? snapshot!.VerificationText : "Engine connection has not been verified.";
    public string AccessibleStatus => $"Wealthsimple: {Status}. {Detail}";
    public string IndicatorColor => IsConnected ? "#55D68B" : Current ? "#F5B94C" : "#FF747D";
    public Task StartAsync(CancellationToken ct = default) { detached = false; return RefreshAsync(ct); }
    public Task StopAsync() { detached = true; Notify(); return Task.CompletedTask; }
    public Task SignInAsync(string email, string password, bool remember, CancellationToken ct = default) =>
        CommandAsync(EngineOperation.SignIn, new EngineSignIn(email, password, remember), ct);
    public Task SubmitOtpAsync(string code, CancellationToken ct = default) => CommandAsync(EngineOperation.SubmitOtp, code, ct);
    public Task CancelSignInAsync() => CommandAsync(EngineOperation.CancelSignIn, null, default);
    public async Task ConnectAsync(WealthsimpleAuthClient candidate, bool remember, CancellationToken ct = default)
    {
        byte[]? encrypted = null;
        try
        {
            encrypted = await candidate.ProtectSessionAsync(protector, ct).ConfigureAwait(false);
            await CommandAsync(EngineOperation.ConnectProtectedSession, new EngineProtectedSession(encrypted, remember), ct).ConfigureAwait(false);
        }
        finally { candidate.Dispose(); if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted); }
    }
    public Task SetRememberAsync(bool remember, CancellationToken ct = default) => CommandAsync(EngineOperation.RememberSession, remember, ct);
    public Task DisconnectAsync(CancellationToken ct = default) => CommandAsync(EngineOperation.Disconnect, null, ct);
    public Task CheckAsync(bool force = false, CancellationToken ct = default) => force
        ? CommandAsync(EngineOperation.CheckConnection, true, ct) : RefreshAsync(ct);
    private async Task CommandAsync(EngineOperation operation, object? payload, CancellationToken ct)
    {
        try { await rpc.CallAsync<bool>(operation, payload, ct).ConfigureAwait(false); }
        finally { await RefreshAsync(CancellationToken.None).ConfigureAwait(false); }
    }
    private async Task RefreshAsync(CancellationToken ct)
    {
        if (!await refresh.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try { snapshot = await rpc.CallAsync<EngineConnectionStatus>(EngineOperation.Connection, ct: ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is System.IO.IOException or OperationCanceledException or InvalidOperationException)
        { snapshot = null; }
        finally { Notify(); refresh.Release(); }
    }
    private void Notify()
    {
        if (notifications is not null && SynchronizationContext.Current != notifications)
            notifications.Post(_ => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)), null);
        else PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadTfsaAccountsAsync(CancellationToken ct = default) =>
        rpc.CallAsync<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>>(EngineOperation.Accounts, ct: ct);
    public Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>> ReadTfsaSnapshotAsync(WealthsimpleTfsaAccount account, CancellationToken ct = default) =>
        rpc.CallAsync<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>>(EngineOperation.AccountSnapshot, account, ct);
    public Task<WealthsimpleReadResult<WealthsimpleDailyPerformance>> ReadTfsaDailyPerformanceAsync(WealthsimpleTfsaAccount account, CancellationToken ct = default) =>
        rpc.CallAsync<WealthsimpleReadResult<WealthsimpleDailyPerformance>>(EngineOperation.DailyPerformance, account, ct);
    public Task<WealthsimpleReadResult<WealthsimplePendingOrders>> ReadTfsaPendingOrdersAsync(WealthsimpleTfsaAccount account, CancellationToken ct = default) =>
        rpc.CallAsync<WealthsimpleReadResult<WealthsimplePendingOrders>>(EngineOperation.PendingOrders, account, ct);
    public Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleCanadianSecurity>>> FindCanadianSecuritiesAsync(string symbol, CancellationToken ct = default) =>
        rpc.CallAsync<WealthsimpleReadResult<IReadOnlyList<WealthsimpleCanadianSecurity>>>(EngineOperation.Securities, symbol, ct);
    public Task<WealthsimpleReadResult<ManualBuyContext>> ReadManualBuyContextAsync(WealthsimpleTfsaAccount account, string symbol, CancellationToken ct) =>
        rpc.CallAsync<WealthsimpleReadResult<ManualBuyContext>>(EngineOperation.BuyContext, new EngineInstrumentRequest(account, symbol), ct);
    public Task<WealthsimpleReadResult<ManualSellContext>> ReadManualSellContextAsync(WealthsimpleTfsaAccount account, string symbol, CancellationToken ct) =>
        rpc.CallAsync<WealthsimpleReadResult<ManualSellContext>>(EngineOperation.SellContext, new EngineInstrumentRequest(account, symbol), ct);
    public Task<WealthsimpleReadResult<WealthsimpleMarketBuyState>> ReadManualBuyOrderAsync(ManualBuyIntent intent, CancellationToken ct) =>
        rpc.CallAsync<WealthsimpleReadResult<WealthsimpleMarketBuyState>>(EngineOperation.BuyOrder, intent, ct);
    public Task<WealthsimpleReadResult<WealthsimpleMarketOrderState>> ReadManualSellOrderAsync(ManualSellIntent intent, CancellationToken ct) =>
        rpc.CallAsync<WealthsimpleReadResult<WealthsimpleMarketOrderState>>(EngineOperation.SellOrder, intent, ct);
    public Task<WealthsimpleReadResult<ManualSellInventory>> ReadManualSellInventoryAsync(ManualSellIntent intent, CancellationToken ct) =>
        rpc.CallAsync<WealthsimpleReadResult<ManualSellInventory>>(EngineOperation.SellInventory, intent, ct);
    public Task<WealthsimpleReadResult<BrokerAccountHistory>> ReadTfsaOrderHistoryAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
        rpc.CallAsync<WealthsimpleReadResult<BrokerAccountHistory>>(EngineOperation.OrderHistory, account, ct);
    public Task<WealthsimpleReadResult<BrokerActivityCheckpoint>> ReadTfsaActivityCheckpointAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
        rpc.CallAsync<WealthsimpleReadResult<BrokerActivityCheckpoint>>(EngineOperation.ActivityCheckpoint, account, ct);
    public Task<WealthsimpleMarketBuySubmission> SubmitManualBuyAsync(WealthsimpleMarketBuy order, CancellationToken ct) =>
        throw new InvalidOperationException("Submit through the engine's reviewed manual-order workflow.");
    public Task<WealthsimpleMarketOrderSubmission> SubmitManualSellAsync(WealthsimpleMarketSell order, CancellationToken ct) =>
        throw new InvalidOperationException("Submit through the engine's reviewed manual-order workflow.");
}
