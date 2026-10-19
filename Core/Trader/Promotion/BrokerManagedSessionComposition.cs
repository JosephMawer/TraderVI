#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

public sealed record BrokerManagedSessionComponents(BrokerManagedSessionWorkflow Workflow, IBrokerManagedAccountSource AccountSource);

/// <summary>Composition only. Creates no timer, performs no reads/writes and grants no execution authority.</summary>
public static class BrokerManagedSessionComposition
{
    public static string ImplementationIdentity => TradingRecommendationStorage.ImplementationIdentity;
    public static WealthsimpleTfsaAccount MatchAccount(BrokerManagedSession session, IEnumerable<WealthsimpleTfsaAccount> accounts)
    {
        var matches = accounts.Where(a => a.Id == session.Account.Import.Plan.Observation.AccountId).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new InvalidOperationException("The connected account does not uniquely match the approved assignment.");
    }
    public static BrokerManagedSessionWorkflow Create(IBrokerReconciliationSource accountReads,
        IManualBuyBroker buyBroker, IManualSellBroker sellBroker, IManualSellJournal orders,
        IBrokerReconciliationJournal observations, SemaphoreSlim sharedOrderGate,
        WealthsimpleTfsaAccount selectedAccount, ITsxSessionCalendar calendar, IBrokerRiskEvidenceSource riskEvidence,
        TimeProvider? time = null) => CreateComponents(accountReads, buyBroker, sellBroker, orders, observations,
            sharedOrderGate, selectedAccount, calendar, riskEvidence, time).Workflow;

    public static BrokerManagedSessionComponents CreateComponents(IBrokerReconciliationSource accountReads,
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
        return new(new BrokerManagedSessionWorkflow(new BrokerManagedSessionRepository(), source, transport, time), source);
    }
}
