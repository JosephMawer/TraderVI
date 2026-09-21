#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

public interface IBrokerAccountRiskContinuationStore
{
    Task<BrokerRiskContinuationReceipt?> FindAsync(Guid requestId, CancellationToken ct);
    Task<BrokerAccountRiskState> ReadAsync(Guid baselineId, CancellationToken ct);
    // Implementations must recover the same request or recheck Apply under an exclusive account fence.
    Task<BrokerRiskContinuationReceipt> AppendAsync(BrokerRiskContinuationRequest request, CancellationToken ct);
}

/// <summary>Restart-safe shared workflow. No broker calls, scheduled work or execution authority.</summary>
public sealed class BrokerAccountRiskContinuationWorkflow(IBrokerAccountRiskContinuationStore store,
    ITsxSessionCalendar calendar, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public async Task<BrokerRiskContinuationReceipt> ApplyAsync(BrokerRiskContinuationRequest request,
        CancellationToken ct = default)
    {
        BrokerAccountRiskContinuation.Validate(request);
        ct.ThrowIfCancellationRequested();
        var saved = await store.FindAsync(request.RequestId, ct);
        if (saved is not null)
        {
            RequireSameRequest(request, saved);
            return saved;
        }
        var state = await store.ReadAsync(request.BaselineId, ct);
        BrokerAccountRiskContinuation.Apply(state, request, calendar, clock.GetUtcNow());
        ct.ThrowIfCancellationRequested();
        return await store.AppendAsync(request, ct);
    }

    internal static void RequireSameRequest(BrokerRiskContinuationRequest request, BrokerRiskContinuationReceipt saved)
    {
        if (request.RequestId != saved.Request.RequestId || request.RequestHash != saved.Request.RequestHash)
            throw new InvalidOperationException("This risk request already has different saved evidence. Preserve it and reconcile the conflict.");
    }
}
