#nullable enable
using Core.Trader.Promotion;
using Core.Trader;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Orders;

/// <summary>Strategy intent uses the existing manual preflight, encrypted journal and one-attempt broker client.</summary>
public sealed class BrokerManagedOrderTransport(IManualBuyBroker buys, IManualSellBroker sells,
    IManualSellJournal journal, IManualBuySafetySource safety, IBrokerAccountRiskContinuationStore riskStore, WealthsimpleTfsaAccount account,
    SemaphoreSlim orderGate, TimeProvider? time = null) : IBrokerManagedOrderTransport
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public async Task<IBrokerManagedOrderPreflight> PrepareAsync(BrokerStrategyOrder order, BrokerAccountProjection projected, CancellationToken ct)
    {
        if (!await orderGate.WaitAsync(0, ct)) throw BrokerOrderFailureEvidence.Tag("Another broker operation owns the account.", BrokerOrderCheck.AccountOperationBusy);
        IManualBuySafetyLease? safetyLease = null;
        try
        {
            if (!buys.IsConnected || !sells.IsConnected || order.Quantity < 1 || order.Side is not ("BUY" or "SELL") ||
                journal.HasUnresolvedOrders() || journal.ReadAll().Any(o => o.Intent.RequestId == order.RequestId) ||
                journal.ReadSells().Any(o => o.Intent.RequestId == order.RequestId))
                throw BrokerOrderFailureEvidence.Tag("Recover saved orders before a new strategy transmission.", BrokerOrderCheck.OriginalOrderRecoveryRequired);
            long revision = buys.SessionRevision;
            if (order.Side == "BUY")
            {
                safetyLease = await safety.AcquireAsync(ct);
                if (safetyLease.BlockReason is not null) throw BrokerOrderFailureEvidence.Tag(safetyLease.BlockReason, BrokerOrderCheck.SharedSafetyBlocked);
                // The existing shared TFSA read fence also excludes continuation writers until transmission finishes.
                var currentRisk = await riskStore.ReadAsync(projected.Risk.BaselineId, ct);
                if (BrokerAccountImport.Hash(currentRisk) != BrokerAccountImport.Hash(projected.Risk))
                    throw BrokerOrderFailureEvidence.Tag("Account risk advanced after evaluation. Synchronize before buying.", BrokerOrderCheck.RiskChanged);
                var read = await buys.ReadManualBuyContextAsync(account, order.Symbol, ct);
                var context = read.Status == WealthsimpleReadStatus.Success ? read.Data : null;
                if (context is null) throw BrokerOrderFailureEvidence.Tag("Fresh broker buy checks are unavailable.", BrokerOrderCheck.BrokerBuyContextUnavailable, "Broker buy read: " + read.Status);
                string? error = ManualBuyChecks.Validate(context, clock.GetUtcNow(), order.Quantity);
                if (error is not null) throw BrokerOrderFailureEvidence.Tag(error, BrokerOrderCheck.ManualBuyValidation, error);
                RequireSameAccount(projected, context.Snapshot, context.Security.Id, order.SecurityId);
                if (context.Quote.Ask > order.ReferencePrice || order.Quantity * context.Quote.Ask > projected.Account.AvailableCash ||
                    projected.Risk.DailyLossHold || projected.Risk.CapitalReviewRequired || !projected.NewRiskBlocks.IsEmpty)
                    throw BrokerOrderFailureEvidence.Tag("The current ask or account risk no longer fits the strategy decision.",
                        context.Quote.Ask > order.ReferencePrice ? BrokerOrderCheck.AskAboveDecisionReference :
                        order.Quantity * context.Quote.Ask > projected.Account.AvailableCash ? BrokerOrderCheck.AvailableCashExceeded : BrokerOrderCheck.AccountRiskBlocked);
                var intent = new ManualBuyIntent(order.RequestId, context.Security.SourceIdentity!, account.Id, order.SecurityId,
                    account.DisplayName, order.Symbol, context.Security.Exchange, order.Quantity,
                    new(order.Symbol, "Reviewed real strategy · " + order.DecisionId.ToString("D"), "Buy")
                        { ExpectedBrokerSecurityId = order.SecurityId },
                    order.ReferencePrice, context.Quote, context.AvailableCash!.Value, clock.GetUtcNow());
                return new Preflight(orderGate, safetyLease, async token =>
                {
                    CheckRevision(revision);
                    RequirePurchaseWindow(projected.Risk);
                    var recheck = ManualBuyChecks.Validate(context, clock.GetUtcNow(), order.Quantity);
                    if (recheck is not null) throw BrokerOrderFailureEvidence.Tag(recheck, BrokerOrderCheck.ManualBuyValidation, recheck);
                    var saved = new ManualBuyRecord(intent, null, []);
                    journal.Save(saved);
                    // The durable journal write may itself cross the cutoff. Never transmit then;
                    // the original reserved intent/journal remains available for operator recovery.
                    RequirePurchaseWindow(projected.Risk);
                    WealthsimpleSubmissionStatus status;
                    try { status = (await buys.SubmitManualBuyAsync(new(account, context.Security, order.Quantity, order.RequestId), token)).Status; }
                    catch { status = WealthsimpleSubmissionStatus.OutcomeUnknown; }
                    journal.Save(saved with { Submission = status }); return status;
                });
            }
            else
            {
                var read = await sells.ReadManualSellContextAsync(account, order.Symbol, ct);
                var context = read.Status == WealthsimpleReadStatus.Success ? read.Data : null;
                if (context is null) throw BrokerOrderFailureEvidence.Tag("Fresh broker sell checks are unavailable.", BrokerOrderCheck.BrokerSellContextUnavailable, "Broker sell read: " + read.Status);
                var selection = new ManualSellSelection(account, order.SecurityId, order.Symbol, order.Quantity,
                    projected.Import.Plan.Observation.SnapshotAt, revision);
                string? error = ManualSellChecks.Validate(selection, context, clock.GetUtcNow());
                if (error is not null) throw BrokerOrderFailureEvidence.Tag(error, BrokerOrderCheck.ManualSellValidation, error);
                RequireSameAccount(projected, context.Snapshot, context.Security.Id, order.SecurityId);
                var intent = new ManualSellIntent(order.RequestId, context.Security.SourceIdentity!, account.Id, order.SecurityId,
                    account.DisplayName, order.Symbol, context.Security.Exchange, order.Quantity, selection.SnapshotAt, context.Quote, clock.GetUtcNow());
                return new Preflight(orderGate, null, async token =>
                {
                    CheckRevision(revision);
                    var recheck = ManualSellChecks.Validate(selection, context, clock.GetUtcNow());
                    if (recheck is not null) throw BrokerOrderFailureEvidence.Tag(recheck, BrokerOrderCheck.ManualSellValidation, recheck);
                    var saved = new ManualSellRecord(intent, null, []); journal.SaveSell(saved);
                    WealthsimpleSubmissionStatus status;
                    try { status = (await sells.SubmitManualSellAsync(new(account, context.Security, order.Quantity, order.RequestId), token)).Status; }
                    catch { status = WealthsimpleSubmissionStatus.OutcomeUnknown; }
                    journal.SaveSell(saved with { Submission = status }); return status;
                });
            }
        }
        catch { if (safetyLease is not null) await safetyLease.DisposeAsync(); orderGate.Release(); throw; }
    }
    private void CheckRevision(long revision)
    {
        if (!buys.IsConnected || !sells.IsConnected || buys.SessionRevision != revision || sells.SessionRevision != revision)
            throw BrokerOrderFailureEvidence.Tag("Broker login changed. Recover the saved intent before further execution.", BrokerOrderCheck.LoginChanged);
    }
    private void RequirePurchaseWindow(BrokerAccountRiskState risk)
    {
        if (risk.Continuation is null && risk.October22 is null) return;
        var now = clock.GetUtcNow();
        var date = risk.October22 is null ? BrokerOctober21Continuation.ExecutionDate : BrokerOctober22Continuation.ExecutionDate;
        var expiry = risk.October22?.PurchaseExpiresAt ?? risk.Continuation!.PurchaseExpiresAt;
        if (date != DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)) ||
            expiry == default || now >= expiry)
            throw BrokerOrderFailureEvidence.Tag("The dated trial purchase permission expired before broker transmission.", BrokerOrderCheck.PurchasePermissionExpired);
    }
    private static void RequireSameAccount(BrokerAccountProjection projected, WealthsimpleTfsaSnapshot snapshot, string actualSecurity, string expectedSecurity)
    {
        var observed = projected.Import.Plan.Observation;
        if (snapshot.Account.Id != observed.AccountId || actualSecurity != expectedSecurity ||
            snapshot.Cash?.Currency != "CAD" || snapshot.BuyingPower?.Currency != "CAD" ||
            snapshot.Cash?.Amount != observed.Cash || snapshot.BuyingPower?.Amount != observed.BuyingPower ||
            snapshot.Holdings.Count(h => h.Quantity > 0) != projected.Positions.Length ||
            projected.Positions.Any(p => !snapshot.Holdings.Any(h => h.SecurityId == p.SecurityId && h.Quantity == p.Quantity &&
                h.BookValue?.Currency == "CAD" && h.BookValue.Amount == p.CostBasis)))
            throw BrokerOrderFailureEvidence.Tag("Broker funds, holdings or instrument changed after strategy evaluation. Reconcile again.",
                snapshot.Account.Id != observed.AccountId ? BrokerOrderCheck.AccountIdentityChanged :
                actualSecurity != expectedSecurity ? BrokerOrderCheck.InstrumentChanged :
                snapshot.Cash?.Currency != "CAD" || snapshot.BuyingPower?.Currency != "CAD" ? BrokerOrderCheck.CurrencyChanged :
                snapshot.Cash?.Amount != observed.Cash ? BrokerOrderCheck.CashChanged :
                snapshot.BuyingPower?.Amount != observed.BuyingPower ? BrokerOrderCheck.BuyingPowerChanged : BrokerOrderCheck.HoldingsChanged);
    }
    private sealed class Preflight(SemaphoreSlim gate, IManualBuySafetyLease? safety, Func<CancellationToken, Task<WealthsimpleSubmissionStatus>> submit)
        : IBrokerManagedOrderPreflight
    {
        private int attempted, disposed;
        public Task<WealthsimpleSubmissionStatus> SubmitOnceAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref disposed) != 0 || Interlocked.Exchange(ref attempted, 1) != 0)
                throw new InvalidOperationException("This prepared strategy order cannot be sent again.");
            return submit(ct);
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { if (safety is not null) await safety.DisposeAsync(); } finally { gate.Release(); }
        }
    }
}
