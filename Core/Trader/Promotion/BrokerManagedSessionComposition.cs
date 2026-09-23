#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

/// <summary>Composition only. Creates no timer, performs no reads/writes and grants no execution authority.</summary>
public static class BrokerManagedSessionComposition
{
    public static BrokerManagedSessionWorkflow Create(IBrokerReconciliationSource accountReads,
        IManualBuyBroker buyBroker, IManualSellBroker sellBroker, IManualSellJournal orders,
        IBrokerReconciliationJournal observations, SemaphoreSlim sharedOrderGate,
        WealthsimpleTfsaAccount selectedAccount, ITsxSessionCalendar calendar, IBrokerRiskEvidenceSource riskEvidence,
        TimeProvider? time = null)
    {
        // Callers must supply their existing clients, journal and gate, not create another submission owner.
        ArgumentNullException.ThrowIfNull(sharedOrderGate);
        ArgumentNullException.ThrowIfNull(riskEvidence);
        var risk = new BrokerAccountRiskContinuationRepository(calendar);
        var safety = new ManualBuySafetyRepository();
        var buys = new ManualMarketBuyWorkflow(buyBroker, orders, safety, time, sharedOrderGate);
        var sells = new ManualMarketSellWorkflow(sellBroker, orders, time, sharedOrderGate);
        var reconcile = new BrokerReconciliationWorkflow(accountReads, orders, observations, sharedOrderGate, time);
        async Task<string> Resolve(string symbol, CancellationToken ct)
        {
            var read = await buyBroker.ReadManualBuyContextAsync(selectedAccount, symbol, ct);
            // The existing native reader verifies one exact Canadian listing, including exchange-qualified aliases.
            if (read.Status != WealthsimpleReadStatus.Success || read.Data is not { } context)
                throw new InvalidOperationException("The exact broker security could not be verified.");
            return context.Security.Id;
        }
        var source = new BrokerManagedAccountSource(reconcile, () => accountReads.SessionRevision, selectedAccount,
            new BrokerAccountRiskBaselineRepository(), risk, calendar, riskEvidence,
            new BrokerImportWorkflow(new BrokerAccountImportRepository()), buys, sells, sellBroker, Resolve, time);
        var transport = new BrokerManagedOrderTransport(buyBroker, sellBroker, orders, safety, risk,
            selectedAccount, sharedOrderGate, time);
        return new(new BrokerManagedSessionRepository(), source, transport, time);
    }
}
