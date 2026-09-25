#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Accounts;

public interface IBrokerImportStore
{
    Task<BrokerImportState> ReadAsync(CancellationToken ct);
    Task<BrokerImportReceipt?> FindAsync(Guid requestId, CancellationToken ct);
    Task<BrokerImportReceipt> SaveAsync(BrokerImportRequest request, CancellationToken ct);
}

/// <summary>Explicit first import and repeatable refresh. Has no order submission or strategy activation dependency.</summary>
public sealed class BrokerImportWorkflow(IBrokerImportStore store, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public async Task<BrokerImportPlan> PreviewAsync(BrokerAccountReconciliation observation, CancellationToken ct = default) =>
        BrokerAccountImport.Preview(observation, await store.ReadAsync(ct), clock.GetUtcNow());

    /// <summary>Saves a freshly reconciled observation for an already reviewed binding; no human preview is being approved.</summary>
    public async Task<BrokerImportReceipt> SynchronizeAsync(BrokerImportPlan observed, Guid requestId, string recordedBy,
        CancellationToken ct = default)
    {
        Validate(requestId, recordedBy);
        if (observed.PreviousRequestId is null || !observed.CanImport)
            throw new InvalidOperationException("Link and reconcile the account before automatic trade synchronization.");
        var request = new BrokerImportRequest(requestId, observed.Fingerprint, recordedBy, observed);
        var saved = await store.FindAsync(requestId, ct);
        if (saved is not null)
        {
            if (saved.RequestHash != request.RequestHash) throw new InvalidOperationException("This import request already has different saved content.");
            return saved;
        }
        // Reconciliation already checks complete orders against holdings and a final activity checkpoint.
        // A second market-value read would invalidate sound trade facts whenever quoted prices move.
        // SaveAsync revalidates this exact plan against binding/history/ledger under its SQL transaction.
        ct.ThrowIfCancellationRequested();
        return await store.SaveAsync(request, ct);
    }

    public async Task<BrokerImportReceipt> ApplyAsync(BrokerImportPlan reviewed, Guid requestId, string reviewedBy,
        Func<CancellationToken, Task<BrokerAccountReconciliation>> refresh, CancellationToken ct = default)
    {
        Validate(requestId, reviewedBy);
        var request = new BrokerImportRequest(requestId, reviewed.Fingerprint, reviewedBy, reviewed);
        // A lost response is recovered by the durable request ID, even when broker reads are currently unavailable.
        var saved = await store.FindAsync(requestId, ct);
        if (saved is not null)
        {
            if (saved.RequestHash != request.RequestHash) throw new InvalidOperationException("This import request already has different saved content.");
            return saved;
        }
        if (!reviewed.CanImport) throw new InvalidOperationException("Resolve the import preview issues before saving account history.");
        var fresh = await PreviewAsync(await refresh(ct), ct);
        if (!fresh.CanImport || fresh.Fingerprint != reviewed.Fingerprint)
            throw new InvalidOperationException("Broker facts or saved account history changed. Refresh and review the import again.");
        ct.ThrowIfCancellationRequested();
        return await store.SaveAsync(request with { Plan = fresh }, ct);
    }
    internal static void Validate(Guid requestId, string reviewedBy)
    {
        if (requestId == Guid.Empty || string.IsNullOrWhiteSpace(reviewedBy) || reviewedBy.Length > 128)
            throw new ArgumentException("An import request identity and reviewer are required.");
    }
}
