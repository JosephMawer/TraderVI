#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveSessionContext(
    DelphiLiveFrozenSession Session, DelphiLiveSessionBounds Bounds,
    IReadOnlyList<DelphiLivePolicyAssignment> Assignments,
    IReadOnlyDictionary<Guid, DelphiLivePolicyDefinition> Policies,
    IReadOnlyDictionary<string, DelphiLiveFrozenCandidate> Candidates,
    IReadOnlyDictionary<string, DelphiLiveFrozenBaseline> Baselines)
{
    public IReadOnlyDictionary<string, DelphiLiveObservationMembership> ObservationMembership { get; init; } =
        new Dictionary<string, DelphiLiveObservationMembership>(StringComparer.Ordinal);
}

public sealed record DelphiLiveObservationMembership(
    string Symbol, bool IsFrozenDailyCandidate, bool IsXiuBenchmark, bool IsTrackedHolding,
    bool IsDelphiLiveHolding, bool HasPendingProtectiveSell, bool IsSessionCarryCandidate,
    DateTime RequiredFromBarEndUtc, DateTime RequiredThroughBarEndUtc);

public sealed record DelphiLiveFrozenBaseline(
    decimal? PreviousClose, IReadOnlyList<DelphiLiveDailyBar> Bars,
    IReadOnlyList<DateOnly> CanonicalDates, DelphiLiveVolatilityRulerMeasurements Rulers);

public sealed record DelphiLiveStoredEvaluation(
    DelphiLiveEvaluationInput Input, DelphiLiveEvaluationResult Result, int ContinuityEpoch);

public interface IDelphiLiveSessionContextStore : IDelphiLiveSessionStore, IDelphiLivePolicyAssignmentSource
{
    Task<DelphiLiveSessionContext?> ReadContextAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<DelphiLivePolicyDefinition> GetPolicyAsync(Guid policyId, CancellationToken cancellationToken = default);
    Task<DelphiLiveSessionContext> SynchronizeObservationSetAsync(Guid sessionId, DateTime nextBarEndUtc,
        DelphiLiveLease lease, IReadOnlyList<DelphiLivePortfolioSnapshot> portfolios,
        CancellationToken cancellationToken = default);
}

public interface IDelphiLiveEvaluationStore
{
    Task<DelphiLiveStoredEvaluation?> GetLatestAsync(Guid sessionId, Guid policyId, string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DelphiLiveStoredEvaluation>> GetLatestSnapshotAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task PersistAsync(DelphiLiveEvaluationInput input, DelphiLiveEvaluationResult result, int continuityEpoch,
        DelphiLiveLease lease, CancellationToken cancellationToken = default);
}

public sealed record DelphiLiveRuntimeSnapshot(
    string Status, DateOnly TradingDate, Guid? SessionId, DateTime? LastCheckpointUtc,
    IReadOnlyList<DelphiLiveStoredEvaluation> Evaluations,
    IReadOnlyList<DelphiLivePortfolioSnapshot> Portfolios,
    IReadOnlyList<string> Warnings)
{
    public DelphiLiveExperimentState? Experiment { get; init; }
    public DelphiLivePromotionScore? PromotionScore { get; init; }
    public DelphiLivePolicyDefinition? ChampionPolicy { get; init; }
    public DelphiLiveResearchPresentation? Research { get; init; }
    public DateOnly? ResearchFrom { get; init; }
    public DateOnly? ResearchThrough { get; init; }
    public DateTime? ResearchReadUtc { get; init; }
}
