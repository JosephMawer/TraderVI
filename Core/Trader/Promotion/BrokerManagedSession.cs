#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

public enum BrokerManagedPhase { Transition, Active, Paused, NeedsAttention }
public sealed record BrokerQualification(string Check, Guid BindingId, string StrategyFingerprint,
    DateTimeOffset VerifiedAt, string EvidenceHash, string EvidenceReference);
public sealed record BrokerActivationReview(Guid RequestId, Guid BindingId, BrokerManagedStrategy Strategy,
    Guid BaselineId, string AccountFingerprint, DateOnly Session, DateTimeOffset PreparedAt,
    decimal PurchaseCeiling, ImmutableArray<Guid> RequiredExits, ImmutableArray<Guid> RetainedPositions,
    ImmutableArray<BrokerPositionProtection> CurrentProtection, ImmutableArray<BrokerQualification> Qualifications)
{
    public ImmutableArray<BrokerPositionCycle> ReviewedPositions { get; init; } = [];
    public static ImmutableArray<string> RequiredChecks { get; } =
        ["cash-events", "opening-values", "native-order-lifecycle", "strategy-evidence", "restart-recovery", "installed-storage"];
    [JsonIgnore] public string Fingerprint => BrokerAccountImport.Hash(this);
    public void Validate(BrokerAccountProjection account, DateTimeOffset now)
    {
        Strategy.Validate();
        if (RequestId == Guid.Empty || BindingId != account.Risk.BindingId || BaselineId != account.Risk.BaselineId ||
            AccountFingerprint != account.Fingerprint || PreparedAt > now || now - PreparedAt > TimeSpan.FromMinutes(2) ||
            Session != account.Account.TradingDate || PurchaseCeiling < 0 || !account.NewRiskBlocks.IsEmpty ||
            account.Import.Plan.Observation.SnapshotAt > now || now - account.Import.Plan.Observation.SnapshotAt > TimeSpan.FromSeconds(30) ||
            RequiredExits.Intersect(RetainedPositions).Any() ||
            RequiredExits.Concat(RetainedPositions).Distinct().Count() != account.Positions.Length ||
            !RequiredExits.Concat(RetainedPositions).Order().SequenceEqual(account.Positions.Select(p => p.PositionId).Order()) ||
            !ReviewedPositions.OrderBy(p => p.PositionId).SequenceEqual(account.Positions.OrderBy(p => p.PositionId)) ||
            CurrentProtection.Any(p => !account.Positions.Any(h => h.PositionId == p.PositionId)) ||
            CurrentProtection.Select(p => p.PositionId).Distinct().Count() != CurrentProtection.Length ||
            account.Positions.Any(p => p.LegacyPositionId.HasValue && !CurrentProtection.Any(s => s.PositionId == p.PositionId)))
            throw new InvalidOperationException("Activation review is stale, incomplete or differs from the real account. Review again.");
        foreach (string check in RequiredChecks)
        {
            var matching = Qualifications.Where(q => q.Check == check).ToArray();
            if (matching.Length != 1 || matching[0].BindingId != BindingId || matching[0].StrategyFingerprint != Strategy.Fingerprint ||
                matching[0].VerifiedAt > now || !CompleteTradingStrategy.IsHash(matching[0].EvidenceHash) ||
                string.IsNullOrWhiteSpace(matching[0].EvidenceReference))
                throw new InvalidOperationException($"Required launch evidence is missing or incompatible: {check}.");
        }
    }
}
public sealed record BrokerExecutionApproval(BrokerActivationReview Review, string Reviewer, string Reason)
{
    [JsonIgnore] public string Fingerprint => BrokerAccountImport.Hash(this);
}
public sealed record BrokerStrategyOrder(Guid RequestId, Guid DecisionId, Guid? PositionId, string SecurityId,
    string Symbol, string Side, int Quantity, decimal ReferencePrice, string Kind, decimal? PositionTarget,
    DateTimeOffset CreatedAt, WealthsimpleSubmissionStatus? Submission = null, BrokerOrderFact? Latest = null,
    bool Reconciled = false)
{
    [JsonIgnore] public string ExternalId => "order-" + RequestId.ToString("D");
}
public sealed record BrokerManagedSession(Guid SessionId, Guid BindingId, long Revision, Guid RevisionId,
    Guid? PreviousRevisionId, string Event, DateTimeOffset SavedAt, BrokerExecutionApproval Approval,
    BrokerManagedPhase Phase, DateTimeOffset? ActivatedAt, BrokerAccountProjection Account,
    ImmutableArray<BrokerPositionProtection> Protection, TradingRecommendationMemory Memory,
    ImmutableArray<BrokerStrategyOrder> Orders, ImmutableArray<string> Blocks)
{
    public bool ReviewInvalidated { get; init; }
    [JsonIgnore] public BrokerManagedStrategy Strategy => Approval.Review.Strategy;
    [JsonIgnore] public decimal CommittedPurchaseCost => Orders.Where(o => o.Side == "BUY")
        .Sum(o => o.Reconciled ? o.Latest?.NetValue ?? 0 : o.Quantity * o.ReferencePrice);
}

/// <summary>The lease serializes review, durable intent and transmission across hosts. Each append commits before returning.</summary>
public interface IBrokerManagedSessionLease : IAsyncDisposable
{
    BrokerManagedSession? Current { get; }
    Task AppendAsync(BrokerManagedSession next, CancellationToken ct);
}
public interface IBrokerManagedSessionStore
{
    Task<IBrokerManagedSessionLease> AcquireAsync(Guid bindingId, CancellationToken ct);
}
public interface IBrokerManagedAccountSource
{
    // Recover original order statuses before reconciliation/import. Failed reads must throw, never return an empty account.
    Task<BrokerAccountProjection> SynchronizeAsync(ImmutableArray<BrokerStrategyOrder> orders, CancellationToken ct);
    Task<ImmutableArray<BrokerProtectionMark>> ReadProtectionMarksAsync(BrokerAccountProjection account, CancellationToken ct);
    Task<TradingRecommendationInput> ReadStrategyInputAsync(BrokerManagedSession session, CancellationToken ct);
    Task PreserveRiskHoldsAsync(BrokerAccountProjection account, TradingRecommendationMemory memory, CancellationToken ct);
    Task<string> ResolveSecurityAsync(string symbol, CancellationToken ct);
}
public interface IBrokerManagedOrderTransport
{
    // Shared manual-order checks, journal/connection leases, and broker preflight must remain held through submission.
    Task<IBrokerManagedOrderPreflight> PrepareAsync(BrokerStrategyOrder order, BrokerAccountProjection account, CancellationToken ct);
}
public interface IBrokerManagedOrderPreflight : IAsyncDisposable
{
    Task<WealthsimpleSubmissionStatus> SubmitOnceAsync(CancellationToken ct);
}
