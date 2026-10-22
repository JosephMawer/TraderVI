#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

public sealed record EngineEnableExecution(Guid BindingId, Guid SessionId, string StrategyFingerprint, string Reviewer);

/// <summary>One process owns auth, manual/automatic order workflows, their journal and coordinated gate.</summary>
public sealed class EngineBrokerService
{
    private readonly IWealthsimpleConnection connection;
    private readonly IBrokerOrderJournal journal;
    private readonly IWealthsimpleSessionProtector protector;
    private readonly SemaphoreSlim admission = new(1, 1), orderGate = new(1, 1);
    private readonly ManualMarketBuyWorkflow buys;
    private readonly ManualMarketSellWorkflow sells;
    private readonly BrokerReconciliationWorkflow reconciliation;
    private readonly BrokerExecutionHost host;
    private readonly bool verificationOnly;
    private readonly Dictionary<Guid, PreparedManualBuy> reviews = [];
    private readonly EngineAccountCatalogue accountCatalogue = new();
    private volatile bool closing;

    public EngineBrokerService(IWealthsimpleConnection connection, IBrokerOrderJournal journal,
        IWealthsimpleSessionProtector protector, IBrokerExecutionHostGrantStore grants,
        ITsxSessionCalendar calendar, Func<BrokerManagedSession, CancellationToken, Task> verifyInputs,
        Func<Guid, CancellationToken, Task<bool>>? mayReuseBrokerState = null, bool verificationOnly = false)
    {
        this.connection = connection; this.journal = journal; this.protector = protector;
        this.verificationOnly = verificationOnly;
        buys = new(connection, journal, new ManualBuySafetyRepository(requireIndependentIntegrity: true), orderGate: orderGate);
        sells = new(connection, journal, orderGate: orderGate);
        reconciliation = new(connection, journal, journal, orderGate);
        var sessions = new BrokerManagedSessionRepository(calendar, sharedStorage: true, mayReuse: mayReuseBrokerState);
        host = new(grants, new BrokerManagedSessionControls(sessions),
            async (saved, ct) =>
            {
                await verifyInputs(saved, ct).ConfigureAwait(false);
                long revision = connection.SessionRevision;
                var accounts = await connection.ReadTfsaAccountsAsync(ct).ConfigureAwait(false);
                if (!connection.IsConnected || revision != connection.SessionRevision ||
                    accounts.Status != WealthsimpleReadStatus.Success || accounts.Data is null)
                    throw new InvalidOperationException("The connected account could not be verified.");
                var account = BrokerManagedSessionComposition.MatchAccount(saved, accounts.Data);
                IBrokerRiskEvidenceSource risk = saved.EffectiveReview.NoTransferScope is { } scope
                    ? new BrokerNoTransferRiskEvidenceSource(scope, calendar) : new UnqualifiedBrokerRiskEvidenceSource();
                var workflow = BrokerManagedSessionComposition.Create(connection, connection, connection, journal,
                    journal, orderGate, account, calendar, risk, independentInputs: true, sessionStore: sessions);
                return await workflow.TickAsync(saved.BindingId, ct).ConfigureAwait(false);
            }, calendar, BrokerManagedSessionComposition.ImplementationIdentity);
    }

    public Task StartAsync() => verificationOnly ? host.DisableAsync() : connection.StartAsync();
    public Task PollAuthenticationAsync() => verificationOnly ? Task.CompletedTask : AdmittedAsync(() => connection.CheckAsync());
    public Task PollExecutionAsync() => verificationOnly ? Task.CompletedTask : AdmittedAsync(() => host.PollAsync(connection.IsConnected));
    public void BeginStop() => closing = true;
    private async Task AdmittedAsync(Func<Task> action)
    {
        if (!await admission.WaitAsync(0).ConfigureAwait(false)) return;
        try { if (!closing) await action().ConfigureAwait(false); }
        finally { admission.Release(); }
    }
    public async Task CloseAsync()
    {
        await admission.WaitAsync().ConfigureAwait(false);
        try
        {
            closing = true;
            await host.DisableAsync().ConfigureAwait(false);
            await connection.StopAsync().ConfigureAwait(false);
            journal.Dispose(); reviews.Clear();
        }
        finally { admission.Release(); }
    }
    public async Task<object> DispatchAsync(EngineRequest request)
    {
        if (verificationOnly) EngineVerification.RequireLocalBrokerRead(request.Operation);
        // Status stays readable during a slow admitted order. It does not confer financial authority.
        if (request.Operation == EngineOperation.Connection) return EngineConnectionStatus.Read(connection);
        if (request.Operation == EngineOperation.ExecutionStatus) return verificationOnly
            ? new BrokerExecutionHostStatus("Off", "Native verification: real trading and broker access are disabled.", DateTimeOffset.UtcNow) : host.Status;
        if (request.Operation == EngineOperation.ExecutionDiagnostics)
        {
            if (closing) throw new InvalidOperationException("Engine shutdown is in progress.");
            return await new BrokerManagedSessionRepository().ReadDiagnosticsAsync(request.Payload.Deserialize<Guid>()).ConfigureAwait(false);
        }
        await admission.WaitAsync().ConfigureAwait(false);
        try
        {
            if (closing) throw new InvalidOperationException("Engine shutdown is in progress.");
            var json = EngineRpcJson.Options(resolve: reference => accountCatalogue.Resolve(reference,
                connection.SessionRevision, connection.IsConnected));
            T Read<T>() => request.Payload.Deserialize<T>(json) ?? throw new InvalidOperationException("Missing command input.");
            switch (request.Operation)
            {
                case EngineOperation.SignIn:
                    var login = Read<EngineSignIn>();
                    await connection.SignInAsync(login.Email, login.Password, login.Remember); return true;
                case EngineOperation.SubmitOtp: await connection.SubmitOtpAsync(Read<string>()); return true;
                case EngineOperation.CancelSignIn: await connection.CancelSignInAsync(); return true;
                case EngineOperation.ConnectProtectedSession:
                    var transfer = Read<EngineProtectedSession>();
                    try { await connection.ConnectAsync(WealthsimpleAuthClient.FromProtectedSession(transfer.ProtectedSession, protector), transfer.Remember); }
                    finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(transfer.ProtectedSession); }
                    return true;
                case EngineOperation.CheckConnection: await connection.CheckAsync(true); return true;
                case EngineOperation.RememberSession: await connection.SetRememberAsync(Read<bool>()); return true;
                case EngineOperation.Disconnect: await connection.DisconnectAsync(); reviews.Clear(); return true;
                case EngineOperation.Accounts:
                    long accountRevision = connection.SessionRevision;
                    var catalogue = await connection.ReadTfsaAccountsAsync();
                    if (catalogue.Status == WealthsimpleReadStatus.Success && catalogue.Data is not null &&
                        connection.IsConnected && connection.SessionRevision == accountRevision)
                        accountCatalogue.Publish(catalogue.Data, accountRevision);
                    else accountCatalogue.Publish([], connection.SessionRevision);
                    return catalogue;
                case EngineOperation.AccountSnapshot: return await connection.ReadTfsaSnapshotAsync(Read<WealthsimpleTfsaAccount>());
                case EngineOperation.PendingOrders: return await connection.ReadTfsaPendingOrdersAsync(Read<WealthsimpleTfsaAccount>());
                case EngineOperation.DailyPerformance: return await connection.ReadTfsaDailyPerformanceAsync(Read<WealthsimpleTfsaAccount>(), default);
                case EngineOperation.Securities: return await connection.FindCanadianSecuritiesAsync(Read<string>());
                case EngineOperation.BuyContext:
                    var buyContext = Read<EngineInstrumentRequest>(); return await connection.ReadManualBuyContextAsync(buyContext.Account, buyContext.Symbol, default);
                case EngineOperation.SellContext:
                    var sellContext = Read<EngineInstrumentRequest>(); return await connection.ReadManualSellContextAsync(sellContext.Account, sellContext.Symbol, default);
                case EngineOperation.BuyOrder: return await connection.ReadManualBuyOrderAsync(Read<ManualBuyIntent>(), default);
                case EngineOperation.SellOrder: return await connection.ReadManualSellOrderAsync(Read<ManualSellIntent>(), default);
                case EngineOperation.SellInventory: return await connection.ReadManualSellInventoryAsync(Read<ManualSellIntent>(), default);
                case EngineOperation.OrderHistory: return await connection.ReadTfsaOrderHistoryAsync(Read<WealthsimpleTfsaAccount>(), default);
                case EngineOperation.ActivityCheckpoint: return await connection.ReadTfsaActivityCheckpointAsync(Read<WealthsimpleTfsaAccount>(), default);
                case EngineOperation.Orders: return new EngineOrders(journal.ReadAll().ToArray(), journal.ReadSells().ToArray());
                case EngineOperation.PrepareBuy:
                    var prepare = Read<EnginePrepareBuy>();
                    foreach (var expired in reviews.Where(p => DateTimeOffset.UtcNow - p.Value.PreparedAt > ManualBuyChecks.ReviewLifetime).Select(p => p.Key).ToArray()) reviews.Remove(expired);
                    if (reviews.Count >= 64) throw new InvalidOperationException("Too many outstanding reviews.");
                    var prepared = await buys.PrepareAsync(prepare.Account, prepare.Origin);
                    if (prepared.Prepared is { } verified) reviews[verified.ReviewId] = verified;
                    return prepared;
                case EngineOperation.SubmitBuy:
                    var submit = Read<EngineSubmitBuy>();
                    if (!reviews.Remove(submit.ReviewId, out var reviewed)) throw new InvalidOperationException("Refresh the engine-issued review before submitting.");
                    return await buys.SubmitAsync(reviewed, submit.Quantity);
                case EngineOperation.CheckBuy:
                    var checkedBuy = await buys.CheckOrderAsync(Read<Guid>());
                    return new EngineCheckBuy(checkedBuy.Order, checkedBuy.Message);
                case EngineOperation.SellAll:
                    var sell = Read<EngineSell>(); return await sells.SellReviewedAsync(sell.Selection, sell.RequestId);
                case EngineOperation.CheckSell: return await sells.CheckOrderAsync(Read<Guid>());
                case EngineOperation.Reconcile:
                    var reconcile = Read<EngineReconcile>(); return await reconciliation.ReadAsync(reconcile.Account, reconcile.Revision);
                case EngineOperation.EnableExecution:
                    var enable = Read<EngineEnableExecution>();
                    await host.EnableAsync(enable.BindingId, enable.SessionId, enable.StrategyFingerprint, enable.Reviewer); return true;
                case EngineOperation.DisableExecution: await host.DisableAsync(); return true;
                default: throw new InvalidOperationException("Unsupported broker command.");
            }
        }
        finally { admission.Release(); }
    }
}
