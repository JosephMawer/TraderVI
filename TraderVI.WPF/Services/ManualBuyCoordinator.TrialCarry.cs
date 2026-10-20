#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Services;

public sealed partial class ManualBuyCoordinator
{
    private async Task ReadOriginalTrialOrdersAsync(WealthsimpleTfsaAccount account, DateOnly date,
        CancellationToken ct, Action check, BrokerNativeTrialObservation? recover = null)
    {
        bool OnDate(DateTimeOffset at) => DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(at.UtcDateTime)) == date;
        var buys = (recover?.Buys ?? journal.ReadAll().ToImmutableArray())
            .Where(b => BrokerNativeTrialQualification.MatchesAccount(account, b.Intent.AccountId) && OnDate(b.Intent.CreatedAt)).ToArray();
        var sales = (recover?.Sales ?? journal.ReadSells().ToImmutableArray())
            .Where(s => BrokerNativeTrialQualification.MatchesAccount(account, s.Intent.AccountId) && OnDate(s.Intent.CreatedAt)).ToArray();
        if (buys.Length != 1 || sales.Length != 3 ||
            buys.Select(b => b.Intent.RequestId).Concat(sales.Select(s => s.Intent.RequestId)).Distinct().Count() != 4)
            throw new InvalidOperationException("Recover the same one verification buy and three manual sales from the original verification date. No replacement trade or inferred ID is permitted.");
        foreach (var buy in buys) { await workflow.CheckOrderAsync(buy.Intent.RequestId, ct); check(); }
        var sells = new ManualMarketSellWorkflow(connection, journal, orderGate: orderGate);
        foreach (var sale in sales) { await sells.CheckOrderAsync(sale.Intent.RequestId, ct); check(); }
    }

    private async Task<(BrokerAccountRiskBaseline Baseline, BrokerImportReceipt Original,
        BrokerAccountRiskState Risk, BrokerAccountReconciliation Account)> ReadTrialCarryInputsAsync(
        WealthsimpleTfsaAccount account, CancellationToken ct, Action check, Action<string> stage)
    {
        check(); _ = GetExecutionHost();
        var now = DateTimeOffset.UtcNow;
        var bounds = executionCalendar!.GetSessionBounds(BrokerTrialCarryForward.ExecutionDate);
        if (now < bounds.OpenUtc || now >= bounds.CloseUtc)
            throw new InvalidOperationException("The accepted carry is reviewed and saved only during the October 20 regular session. Keep the original trades and initial references; do not repeat them. Automatic trading remains off.");
        if (HostGrants().Read()?.Enabled == true)
            throw new InvalidOperationException("Stop the execution host before next-session preparation.");
        stage("recovering the original saved baseline and immutable import");
        var read = await new BrokerAccountRiskBaselineRepository().ReadAsync(ct); check();
        var baseline = read.RiskHistory.ExistingBaseline
            ?? throw new InvalidOperationException("Recover the original saved initial risk references. Next-session preparation cannot create a new baseline.");
        if (read.Import.Binding?.BindingId != baseline.BindingId ||
            await executionSessions.ReadAsync(baseline.BindingId, ct) is not null)
            throw new InvalidOperationException("The next-session carry requires the original linked account and no existing managed assignment.");
        var original = await new BrokerAccountImportRepository().FindAsync(baseline.ImportRequestId, ct)
            ?? throw new InvalidOperationException("Recover the exact immutable import used by the original baseline."); check();
        stage("reading back the four original verification order IDs");
        await ReadOriginalTrialOrdersAsync(account, baseline.TradingDate, ct, check);
        if (journal.HasUnresolvedOrders())
            throw new InvalidOperationException("Resolve the original orders before reviewing the carry. No replacement order is permitted.");
        stage("reconciling the current cash-only account for the next-session carry");
        var synchronized = await new BrokerAccountSynchronizationWorkflow(new BrokerImportWorkflow(new BrokerAccountImportRepository()))
            .RefreshAsync(token => reconciliation.ReadAsync(account, connection.SessionRevision, token), ct); check();
        var saved = synchronized.Saved ?? throw new InvalidOperationException("Resolve current account/order reconciliation before reviewing a carry.");
        if (saved.BindingId != baseline.BindingId)
            throw new InvalidOperationException("The carry cannot change the permanent account binding.");
        var risk = await new BrokerAccountRiskContinuationRepository(executionCalendar).ReadAsync(baseline.BaselineId, ct); check();
        BrokerTrialCarryForward.RequireUnchangedBook(original.Plan.Observation, saved.Plan.Observation);
        return (baseline, original, risk, saved.Plan.Observation);
    }
}
