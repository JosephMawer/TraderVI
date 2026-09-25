#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Accounts;

public sealed record BrokerAccountSynchronization(BrokerAccountRead Read, BrokerImportReceipt? Saved,
    IReadOnlyList<string> Blocks)
{
    public string? TradeSyncStatus { get; init; }
    public override string ToString() => "Broker account synchronization (financial data omitted)";
}

/// <summary>Refreshes a previously reviewed account link. Never links an account or submits an order.</summary>
public sealed class BrokerAccountSynchronizationWorkflow(BrokerImportWorkflow imports)
{
    public async Task<BrokerAccountSynchronization> RefreshAsync(
        Func<CancellationToken, Task<BrokerAccountRead>> read, CancellationToken ct = default)
    {
        var current = await read(ct);
        var plan = await imports.PreviewAsync(current.Reconciliation, ct);
        ct.ThrowIfCancellationRequested();
        if (!plan.CanImport) return new(current, null, plan.Blocks);
        if (plan.PreviousRequestId is null)
            return new(current, null, ["Review Import account history to link this TFSA before automatic history updates."]);

        // Reconciliation already checks holdings/orders and the final activity checkpoint.
        // Use the linked-account save: a second human-review read wrongly rejects moving prices.
        // SQL still revalidates the binding, history and ledger and projects trades atomically.
        var saved = await imports.SynchronizeAsync(plan, Guid.NewGuid(), "Account synchronization", ct);
        ct.ThrowIfCancellationRequested();
        return new(current, saved, []);
    }
}
