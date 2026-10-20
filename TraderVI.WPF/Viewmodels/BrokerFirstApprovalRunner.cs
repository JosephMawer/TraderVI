#nullable enable
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

/// <summary>Runs the existing approval workflow, retaining its actual validation input for safe diagnostics.</summary>
public sealed class BrokerFirstApprovalRunner(IBrokerManagedSessionStore store, IBrokerManagedAccountSource source,
    TimeProvider? time = null)
{
    public async Task<BrokerManagedSession> ApproveAsync(BrokerExecutionApproval approval, CancellationToken ct)
    {
        var clock = time ?? TimeProvider.System;
        BrokerAccountProjection? observed = null;
        var workflow = new BrokerManagedSessionWorkflow(store,
            new ApprovalSource(source, value => observed = value), new NoOrders(), clock);
        try { return await workflow.ApproveAsync(approval, ct); }
        catch (InvalidOperationException error) when (observed is not null && error.Message ==
            "Activation review is stale, incomplete or differs from the real account. Review again.")
        {
            var review = approval.Review; var now = clock.GetUtcNow();
            var reasons = ImmutableArray.CreateBuilder<string>();
            if (review.PreparedAt > now || now - review.PreparedAt > TimeSpan.FromMinutes(2)) reasons.Add("ReviewExpiredOrFuture");
            if (review.AccountFingerprint != observed.Fingerprint) reasons.Add("AccountOrRiskFingerprintChanged");
            if (review.BindingId != observed.Risk.BindingId || review.BaselineId != observed.Risk.BaselineId) reasons.Add("AccountBindingOrBaselineChanged");
            if (review.Session != observed.Account.TradingDate) reasons.Add("SessionChanged");
            if (observed.Import.Plan.Observation.SnapshotAt > now || now - observed.Import.Plan.Observation.SnapshotAt > TimeSpan.FromSeconds(30)) reasons.Add("AccountSnapshotStaleOrFuture");
            if (!observed.NewRiskBlocks.IsEmpty) reasons.Add("AccountRiskBlocked");
            if (!review.ReviewedPositions.OrderBy(p => p.PositionId).SequenceEqual(observed.Positions.OrderBy(p => p.PositionId))) reasons.Add("ReviewedPositionsChanged");
            if (reasons.Count == 0) reasons.Add("PositionPlanOrReviewStructureMismatch");
            throw new InvalidOperationException("Assignment was not saved: " + string.Join(", ", reasons) +
                $". Review age: {(now - review.PreparedAt).TotalSeconds:F1}s; actual account snapshot age: {(now - observed.Import.Plan.Observation.SnapshotAt).TotalSeconds:F1}s. " +
                "Preserve the original references and qualification packet; report this diagnostic before retrying.", error);
        }
    }

    // Approval is deliberately incapable of sending an order, even if workflow responsibilities change.
    private sealed class NoOrders : IBrokerManagedOrderTransport
    {
        public Task<IBrokerManagedOrderPreflight> PrepareAsync(BrokerStrategyOrder order, BrokerAccountProjection account, CancellationToken ct) =>
            throw new InvalidOperationException("The assignment approval path cannot submit orders.");
    }

    private sealed class ApprovalSource(IBrokerManagedAccountSource source, Action<BrokerAccountProjection> observed) : IBrokerManagedAccountSource
    {
        public string ImplementationIdentity => source.ImplementationIdentity;
        public async Task<BrokerAccountProjection> SynchronizeAsync(ImmutableArray<BrokerStrategyOrder> orders, CancellationToken ct)
        { var value = await source.SynchronizeAsync(orders, ct); observed(value); return value; }
        public Task<ImmutableArray<BrokerProtectionMark>> ReadProtectionMarksAsync(BrokerAccountProjection account, CancellationToken ct) => throw new NotSupportedException();
        public Task<TradingRecommendationInput> ReadStrategyInputAsync(BrokerManagedSession session, CancellationToken ct) => throw new NotSupportedException();
        public Task PreserveRiskHoldsAsync(BrokerAccountProjection account, TradingRecommendationMemory memory, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> ResolveSecurityAsync(string symbol, CancellationToken ct) => throw new NotSupportedException();
    }
}
