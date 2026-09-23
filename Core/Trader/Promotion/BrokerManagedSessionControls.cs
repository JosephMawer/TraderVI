#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

public interface IBrokerManagedSessionControls
{
    Task<BrokerManagedSession?> ReadAsync(Guid bindingId, CancellationToken ct);
    Task<BrokerManagedSession> SetBuyingStoppedAsync(Guid bindingId, Guid expectedSessionId, bool stopped, CancellationToken ct);
}

/// <summary>Saved assignment controls only. No approval, broker client, order transport or execution tick.</summary>
public sealed class BrokerManagedSessionControls(IBrokerManagedSessionStore store, TimeProvider? time = null)
    : IBrokerManagedSessionControls
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public async Task<BrokerManagedSession?> ReadAsync(Guid bindingId, CancellationToken ct = default)
    {
        await using var lease = await store.AcquireAsync(bindingId, ct);
        if (lease.Current is { } saved && saved.BindingId != bindingId)
            throw new InvalidOperationException("Saved assignment belongs to another account.");
        return lease.Current;
    }
    public async Task<BrokerManagedSession> SetBuyingStoppedAsync(Guid bindingId, Guid expectedSessionId,
        bool stopped, CancellationToken ct = default)
    {
        if (expectedSessionId == Guid.Empty) throw new ArgumentException("The displayed strategy assignment is required.");
        await using var lease = await store.AcquireAsync(bindingId, ct);
        if (lease.Current?.BindingId != bindingId || lease.Current.SessionId != expectedSessionId)
            throw new InvalidOperationException("The strategy assignment changed. Refresh its saved controls.");
        return await ChangeAsync(lease, stopped, clock.GetUtcNow(), ct);
    }
    internal static async Task<BrokerManagedSession> ChangeAsync(IBrokerManagedSessionLease lease, bool stopped,
        DateTimeOffset at, CancellationToken ct)
    {
        var current = lease.Current ?? throw new InvalidOperationException("No managed strategy assignment exists.");
        if (current.NewBuysStopped == stopped) return current;
        var next = current with { NewBuysStopped = stopped, Revision = current.Revision + 1,
            PreviousRevisionId = current.RevisionId, RevisionId = Guid.NewGuid(), SavedAt = at,
            Event = stopped ? "OperatorStoppedNewBuys" : "OperatorResumedBuying" };
        await lease.AppendAsync(next, ct);
        return next;
    }
}
