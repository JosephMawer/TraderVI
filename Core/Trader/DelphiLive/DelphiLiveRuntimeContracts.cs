#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
    public DelphiLiveDailySourceRevision? SourceRevision { get; init; }
    public IReadOnlyDictionary<Guid, PaperComparisonAccount> ComparisonAccounts { get; init; } = new Dictionary<Guid, PaperComparisonAccount>();
    public IReadOnlyDictionary<Guid, PaperComparisonSource> ComparisonSources { get; init; } = new Dictionary<Guid, PaperComparisonSource>();
    public IReadOnlyDictionary<Guid, string[]> RealObservedSymbols { get; init; } = new Dictionary<Guid, string[]>();

    public DelphiLiveSessionContext ForPolicy(Guid policy)
    {
        if (!ComparisonAccounts.TryGetValue(policy, out var account)) return this;
        if (!ComparisonSources.TryGetValue(account.BatchId, out var source) || source.DailyStrategyVersionId != account.DailyStrategyVersionId)
            return this with { Session = Session with { CalibrationRunId = null, DailyStrategyVersionId = account.DailyStrategyVersionId },
                Candidates = new Dictionary<string, DelphiLiveFrozenCandidate>(), SourceRevision = null };
        var candidates = PaperGateComparison.SelectLive(source, account.Profile);
        return this with { Session = Session with { CalibrationRunId = source.RunId, DailyStrategyVersionId = source.DailyStrategyVersionId },
            Candidates = candidates, Baselines = Baselines.Concat(source.Baselines).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Last().Value),
            SourceRevision = new(source.RevisionId, source.RunId, source.DailyStrategyVersionId, source.PublishedUtc,
                source.AdoptedUtc, source.EffectiveUtc, candidates.Values.ToArray(), source.Baselines) };
    }

    public PaperGateAdmission? AdmissionFor(Guid policy)
    {
        if (!ComparisonAccounts.TryGetValue(policy, out var account) || !ComparisonSources.TryGetValue(account.BatchId, out var source)) return null;
        return PaperGateComparison.Admission(account, source, policy);
    }
    public IReadOnlyDictionary<string, DelphiLiveObservationMembership> ObservationMembership { get; init; } =
        new Dictionary<string, DelphiLiveObservationMembership>(StringComparer.Ordinal);
}

public sealed record DelphiLiveDailySourceRevision(Guid RevisionId, Guid RunId, Guid StrategyVersionId,
    DateTime PublishedUtc, DateTime AdoptedUtc, DateTime EffectiveUtc,
    IReadOnlyList<DelphiLiveFrozenCandidate> Candidates,
    IReadOnlyDictionary<string, DelphiLiveFrozenBaseline> Baselines);

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
    Task RefreshDailySourceAsync(DateOnly date, DelphiLiveLease lease, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task<DelphiLiveSessionContext?> ReadContextAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<DelphiLiveSessionContext?> ReadOperationalContextAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        ReadContextAsync(date, cancellationToken);
    Task<DelphiLivePolicyDefinition> GetPolicyAsync(Guid policyId, CancellationToken cancellationToken = default);
    Task<DelphiLiveSessionContext> SynchronizeObservationSetAsync(Guid sessionId, DateTime nextBarEndUtc,
        DelphiLiveLease lease, IReadOnlyList<DelphiLivePortfolioSnapshot> portfolios,
        CancellationToken cancellationToken = default);
    Task<DelphiLiveSessionContext> SynchronizeCurrentObservationsAsync(Guid sessionId, DateTime nextBarEndUtc,
        DelphiLiveLease lease, IReadOnlyList<DelphiLiveObservationPortfolio> portfolios,
        CancellationToken cancellationToken = default) => throw new NotSupportedException("Current observation projections are required.");
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
