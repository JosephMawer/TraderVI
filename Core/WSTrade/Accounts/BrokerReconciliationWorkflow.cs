#nullable enable
using Core.WSTrade.Orders;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Accounts;

public interface IBrokerReconciliationSource
{
    bool IsConnected { get; }
    long SessionRevision { get; }
    Task<WealthsimpleReadResult<BrokerAccountHistory>> ReadTfsaOrderHistoryAsync(WealthsimpleTfsaAccount account, CancellationToken ct);
    Task<WealthsimpleReadResult<BrokerActivityCheckpoint>> ReadTfsaActivityCheckpointAsync(WealthsimpleTfsaAccount account, CancellationToken ct);
    Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>> ReadTfsaSnapshotAsync(WealthsimpleTfsaAccount account, CancellationToken ct);
    Task<WealthsimpleReadResult<WealthsimplePendingOrders>> ReadTfsaPendingOrdersAsync(WealthsimpleTfsaAccount account, CancellationToken ct);
}

public interface IBrokerReconciliationJournal
{
    BrokerAccountReconciliation? ReadReconciliation(string identityId, string accountId);
    void SaveReconciliation(BrokerAccountReconciliation reconciliation);
}

/// <summary>Explicit read/reconcile action. Shares the manual-order gate; contains no order-submission or SQL dependency.</summary>
public sealed class BrokerReconciliationWorkflow(IBrokerReconciliationSource source, IManualBuyJournal orders,
    IBrokerReconciliationJournal journal, SemaphoreSlim orderGate, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public async Task<BrokerAccountReconciliation> RefreshAsync(WealthsimpleTfsaAccount account, long revision, CancellationToken ct = default)
    {
        if (!await orderGate.WaitAsync(0, ct)) throw new InvalidOperationException("An order or reconciliation is already running.");
        try
        {
            CheckSession();
            // Opening the journal first obtains the same cross-process lease used by manual orders.
            bool unresolved = orders.HasUnresolvedOrders();
            var history = Data(await source.ReadTfsaOrderHistoryAsync(account, ct), "reading order history"); CheckSession();
            var prior = journal.ReadReconciliation(history.IdentityId, history.AccountId);
            var snapshot = Data(await source.ReadTfsaSnapshotAsync(account, ct), "reading balances and holdings"); CheckSession();
            var pending = Data(await source.ReadTfsaPendingOrdersAsync(account, ct), "reading pending orders"); CheckSession();
            var checkpoint = Data(await source.ReadTfsaActivityCheckpointAsync(account, ct), "checking activity after the read"); CheckSession();
            if (history.ActivityFingerprint == null || checkpoint.IdentityId != history.IdentityId || checkpoint.AccountId != history.AccountId ||
                checkpoint.Fingerprint != history.ActivityFingerprint || checkpoint.RetrievedAt < snapshot.RetrievedAt ||
                checkpoint.RetrievedAt > clock.GetUtcNow() || clock.GetUtcNow() - checkpoint.RetrievedAt > TimeSpan.FromMinutes(2))
                throw new InvalidOperationException("Account activity changed during reconciliation. Refresh again; no partial result was saved.");
            var result = WealthsimpleReconciliation.Compare(history, snapshot, pending, clock.GetUtcNow(), unresolved, prior);
            // The first reconciliation must also cover fills already observed by our manual workflows.
            // Feed exhaustion alone cannot silently erase locally known completed round trips.
            bool localConflict = orders.ReadAll().Where(o => o.Intent.IdentityId == history.IdentityId && o.Intent.AccountId == history.AccountId)
                .Any(o => o.Latest is { } state && Conflicts(history, o.Intent.RequestId, o.Intent.SecurityId, "BUY", o.Intent.Quantity,
                    state.Status, state.FilledQuantity, state.FilledValue, state.FilledNetValue, state.Fees, state.TaxWithholding)) ||
                (orders is IManualSellJournal sells && sells.ReadSells().Where(o => o.Intent.IdentityId == history.IdentityId && o.Intent.AccountId == history.AccountId)
                .Any(o => o.Latest is { } state && Conflicts(history, o.Intent.RequestId, o.Intent.SecurityId, "SELL", o.Intent.Quantity,
                    state.Status, state.FilledQuantity, state.FilledValue, state.FilledNetValue, state.Fees, state.TaxWithholding)));
            if (localConflict) result = result with { HistoryConflicts = result.HistoryConflicts.Append(
                "Broker history is missing or contradicts an order already observed in the saved manual journal.").Distinct().ToArray() };
            ct.ThrowIfCancellationRequested();
            journal.SaveReconciliation(result);
            return result;
        }
        finally { orderGate.Release(); }
        void CheckSession()
        {
            if (!source.IsConnected || source.SessionRevision != revision)
                throw new InvalidOperationException("The Wealthsimple login changed. Refresh the selected account again.");
        }
    }
    private static T Data<T>(WealthsimpleReadResult<T> result, string step) => result.Status == WealthsimpleReadStatus.Success && result.Data != null
        ? result.Data : throw new InvalidOperationException($"Broker reconciliation stopped while {step}: {result.Status}." +
            (string.IsNullOrEmpty(result.Stage) ? "" : $"\nDiagnostic: {result.Stage}.") + "\nNo partial result was saved.");
    private static bool Conflicts(BrokerAccountHistory history, Guid request, string security, string side, int quantity,
        string status, decimal? filled, WealthsimpleMoney? gross, WealthsimpleMoney? net, WealthsimpleMoney? fees, WealthsimpleMoney? tax)
    {
        var order = history.Orders.SingleOrDefault(o => o.ExternalId == "order-" + request.ToString("D"));
        if (order == null || order.SecurityId != security || order.Side != side || order.SubmittedQuantity != quantity || order.FilledQuantity < filled)
            return true;
        bool terminal = status is "FILLED" or "POSTED" or "CANCELLED" or "CANCELED" or "EXPIRED" or "REJECTED";
        bool sameStatus = order.Status == status || (status == "FILLED" && order.Status == "POSTED");
        if (terminal && (!sameStatus || order.FilledQuantity != filled)) return true;
        return filled > 0 && filled == order.FilledQuantity &&
            ((gross != null && gross.Amount != order.GrossValue) || (net != null && net.Amount != order.NetValue) ||
             (fees != null && fees.Amount != order.Fees) || (tax != null && tax.Amount != order.Tax));
    }
}
