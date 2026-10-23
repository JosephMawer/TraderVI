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
public enum BrokerExecutionDuration { SingleSession, Ongoing }
public sealed record BrokerQualification(string Check, Guid BindingId, string StrategyFingerprint,
    DateTimeOffset VerifiedAt, string EvidenceHash, string EvidenceReference)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FirstSessionTrialOnly { get; init; }
}
public sealed record BrokerActivationReview(Guid RequestId, Guid BindingId, BrokerManagedStrategy Strategy,
    Guid BaselineId, string AccountFingerprint, DateOnly Session, DateTimeOffset PreparedAt,
    decimal PurchaseCeiling, ImmutableArray<Guid> RequiredExits, ImmutableArray<Guid> RetainedPositions,
    ImmutableArray<BrokerPositionProtection> CurrentProtection, ImmutableArray<BrokerQualification> Qualifications)
{
    public ImmutableArray<BrokerPositionCycle> ReviewedPositions { get; init; } = [];
    // Older approvals remain limited to their reviewed session. Duration is part of the immutable fingerprint.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public BrokerExecutionDuration Duration { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrokerNoTransferSessionScope? NoTransferScope { get; init; }
    public static ImmutableArray<string> RequiredChecks { get; } =
        ["cash-events", "opening-values", "native-order-lifecycle", "strategy-evidence", "restart-recovery", "installed-storage"];
    public static ImmutableArray<string> AdditionalScopedChecks { get; } =
        ["supervised-no-transfer-cash", "scoped-initial-reference", "opening-native-sale-fills", "operator-supervised-trial", "scoped-durable-recovery"];
    public static ImmutableArray<string> LimitedTrialNativeChecks { get; } =
        RequiredChecks.Where(c => c is not ("cash-events" or "opening-values")).ToImmutableArray();
    // Only the separately approved first-session trial substitutes scoped cash/reference evidence.
    // Merely adding the older no-transfer scope retains every original production check.
    public static ImmutableArray<string> ChecksFor(BrokerNoTransferSessionScope? scope) => scope is null ? RequiredChecks :
        (scope.LimitedFirstSessionTrial ? LimitedTrialNativeChecks
            : RequiredChecks).AddRange(AdditionalScopedChecks);
    // Completed first-day verification stays historical. The explicitly reviewed October 21
    // continuation validates current readiness, not another dated qualification certificate.
    [JsonIgnore] public ImmutableArray<string> RequiredQualificationChecks =>
        NoTransferScope?.Continuation is not null ? [] : ChecksFor(NoTransferScope);
    [JsonIgnore] public string Fingerprint => BrokerAccountImport.Hash(this);
    public void Validate(BrokerAccountProjection account, DateTimeOffset now)
    {
        Strategy.Validate();
        NoTransferScope?.ValidateAnchor();
        if (NoTransferScope?.Continuation is not null)
            BrokerContinuationReviewPolicy.RequireCurrentAccount(account, now);
        if (account.Risk.TrialCarry?.Fingerprint != NoTransferScope?.TrialCarry?.Fingerprint)
            throw new InvalidOperationException("Trial-carried risk references require the exact next-session scope; production and older approvals cannot inherit them.");
        if (account.Risk.Continuation?.Fingerprint != NoTransferScope?.Continuation?.Fingerprint)
            throw new InvalidOperationException("The saved continuation risk association must match the reviewed dated permission.");
        if (account.Risk.October22?.Fingerprint != NoTransferScope?.October22?.Fingerprint)
            throw new InvalidOperationException("October 22 risk and reviewed permission must agree.");
        if (account.Risk.SessionContinuation?.Fingerprint != NoTransferScope?.SessionContinuation?.Fingerprint)
            throw new InvalidOperationException("Selected-session risk and reviewed permission must agree.");
        if (NoTransferScope is { } scope && (scope.BindingId != BindingId || scope.BaselineId != BaselineId ||
            scope.Session != Session || scope.ApprovedAt > now || Strategy.Strategy.Family != TradingStrategyFamily.DelphiLive ||
            PurchaseCeiling > scope.CashOnlyAnchor.Cash || PurchaseCeiling > scope.CashOnlyAnchor.BuyingPower ||
            scope.CashOnlyAnchor.AccountId != account.Import.Plan.Observation.AccountId ||
            scope.CashOnlyAnchor.IdentityId != account.Import.Plan.Observation.IdentityId || !scope.OpeningSalesVerified ||
            scope.LimitedFirstSessionTrial && (Strategy.GateProfile != (scope.October22 is null && scope.SessionContinuation is null ? PaperGateProfile.NormalV1 : PaperGateProfile.PortfolioRulesOnlyV1) || Strategy.Comparison is null ||
                scope.PurchaseExpiresAt <= now) ||
            !RequiredExits.IsEmpty || !RetainedPositions.IsEmpty || !account.Positions.IsEmpty))
            throw new InvalidOperationException("The supervised no-transfer launch must match the actual cash-only account after required exits and its available-capital ceiling.");
        if (!Enum.IsDefined(Duration) || RequestId == Guid.Empty || BindingId != account.Risk.BindingId || BaselineId != account.Risk.BaselineId ||
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
        foreach (string check in RequiredQualificationChecks)
        {
            var matching = Qualifications.Where(q => q.Check == check).ToArray();
            if (matching.Length != 1 || matching[0].BindingId != BindingId || matching[0].StrategyFingerprint != Strategy.Fingerprint ||
                matching[0].VerifiedAt > now || !CompleteTradingStrategy.IsHash(matching[0].EvidenceHash) ||
                string.IsNullOrWhiteSpace(matching[0].EvidenceReference) ||
                matching[0].FirstSessionTrialOnly && NoTransferScope is not { LimitedFirstSessionTrial: true, FullFillOnlyNativeTrial: true })
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
    // Absent in earlier snapshots: those sessions retain their existing buying behavior.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NewBuysStopped { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TrialOrderReviewRequired { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrokerManagedContinuation? Continuation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrokerManagedContinuation? October22 { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrokerManagedContinuation? SessionContinuation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrokerLiveSourceSwitch? SourceSwitch { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrokerImplementationReview? ImplementationReview { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrokerDecisionTrace? LatestDecision { get; init; }
    [JsonIgnore] public BrokerActivationReview EffectiveReview
    {
        get
        {
            var review = Continuation?.Approval.Review ?? Approval.Review;
            if (SourceSwitch is not null) review = review with { Strategy = SourceSwitch.SourceReview.Strategy };
            if (October22 is not null) review = October22.Approval.Review;
            if (SessionContinuation is not null) review = SessionContinuation.Approval.Review;
            // Later dated permissions ignore legacy/earlier build approvals. A new build approval
            // must bind this exact existing permission; it does not replace or renew that permission.
            bool applies = ImplementationReview is not null &&
                (SessionContinuation is null && October22 is null
                    ? ImplementationReview.PermissionRequestId is null
                    : ImplementationReview.PermissionRequestId == review.RequestId);
            return !applies ? review : review with { Strategy = review.Strategy with
                { Strategy = review.Strategy.Strategy with { ImplementationIdentity = ImplementationReview!.ImplementationIdentity } } };
        }
    }
    [JsonIgnore] public DateTime? StrategyEffectiveUtc => SessionContinuation?.ApprovedAt?.UtcDateTime ?? October22?.ApprovedAt?.UtcDateTime ?? SourceSwitch?.ApprovedAt.UtcDateTime ?? ActivatedAt?.UtcDateTime;
    [JsonIgnore] public BrokerManagedStrategy Strategy => EffectiveReview.Strategy;
    [JsonIgnore] public bool IsOngoing => Approval.Review.Duration == BrokerExecutionDuration.Ongoing;
    // An ongoing assignment does not refill its explicitly approved spending ceiling at midnight.
    [JsonIgnore] public decimal CommittedPurchaseCost => Orders.Where(o => o.Side == "BUY")
        .Sum(o => o.Reconciled ? o.Latest?.NetValue ?? 0 : o.Quantity * o.ReferencePrice);
    [JsonIgnore] public DateTime? ProtectionReplayFromUtc => Protection
        .Where(p => Account.Positions.Any(h => h.PositionId == p.PositionId))
        .Select(p => (DateTime?)(p.ThroughUtc ?? p.EstablishedAt.UtcDateTime)).Min();
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
    string ImplementationIdentity { get; }
    // Local journal inspection after status reads; no extra broker call or submission.
    bool RequiresTrialOrderReview(ImmutableArray<BrokerStrategyOrder> orders) => false;
    // Recover original order statuses before reconciliation/import. Failed reads must throw, never return an empty account.
    Task<BrokerAccountProjection> SynchronizeAsync(ImmutableArray<BrokerStrategyOrder> orders, CancellationToken ct);
    Task<ImmutableArray<BrokerProtectionMark>> ReadProtectionMarksAsync(BrokerAccountProjection account, CancellationToken ct);
    Task<TradingRecommendationInput> ReadStrategyInputAsync(BrokerManagedSession session, CancellationToken ct);
    Task<ImmutableArray<TradingQuote>> ReadQuotesAsync(TradingRecommendationInput input, CancellationToken ct) =>
        Task.FromResult(input.Quotes);
    async Task<TradingQuoteReadResult> ReadQuoteObservationAsync(TradingRecommendationInput input, CancellationToken ct) =>
        new(await ReadQuotesAsync(input, ct), [], ["SourceDoesNotProvideAttemptDiagnostics"]);
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
