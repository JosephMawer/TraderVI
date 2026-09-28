#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;

namespace Core.Oracle;

public sealed record AiLiveSignalHistory(string Symbol, Guid? DailyCandidateId,
    DelphiLivePortfolioCandidateState? AccountState, ImmutableArray<DelphiLiveStoredEvaluation> Checkpoints,
    bool HasSourceConflict);

/// <summary>Candidate-paper context only. Contains no cash, holdings, order transport or managed-account permission.</summary>
public sealed record AiLiveSavedContext(Guid PortfolioId, Guid GenerationId, long Revision, DateOnly TradingDate,
    DateTime UpdatedUtc, Guid SessionId, DelphiLivePolicyDefinition Policy, PaperComparisonAccount? Comparison,
    Guid? SourceRevisionId, Guid? AccountSourceRevisionId, Guid RunId, Guid DailyStrategyId,
    StrategyConfig DailyConfiguration, ImmutableArray<TradingModelIdentity> Models,
    DateOnly MarketDataAsOf, DateTime PublishedUtc, DateTime EffectiveUtc, int CurrentEpoch,
    DateTime EpochStartedUtc, bool EpochActive, ImmutableArray<AiLiveSignalHistory> Signals,
    ImmutableArray<PortfolioDailyObservation> History);

public interface IAiLiveDecisionSource
{
    Task<AiLiveSavedContext?> ReadAsync(Guid portfolioId, DateOnly date, CancellationToken ct = default);
}

public sealed class AiLiveDecisionContextReader(IAiLiveDecisionSource source,
    ReviewedTsxSessionCalendar calendar, TimeSpan maximumAge, TimeProvider? clock = null)
{
    public async Task<AiStrategyDecisionContext> ReadAsync(AiPortfolioCatalogEntry choice, DateOnly date, CancellationToken ct = default)
    {
        var saved = await source.ReadAsync(choice.PaperAccountId, date, ct);
        ct.ThrowIfCancellationRequested();
        return AiLiveDecisionContext.Build(choice, saved, calendar, date, maximumAge, (clock ?? TimeProvider.System).GetUtcNow());
    }
}

public static class AiLiveDecisionContext
{
    public static AiStrategyDecisionContext Build(AiPortfolioCatalogEntry choice, AiLiveSavedContext? saved,
        ReviewedTsxSessionCalendar calendar, DateOnly date, TimeSpan maximumAge, DateTimeOffset now)
    {
        choice.Validate();
        if (maximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumAge));
        if (choice.Strategy is not { Family: TradingStrategyFamily.DelphiLive } strategy)
            throw new ArgumentException("A defined Delphi Live choice is required.");
        var bounds = calendar.GetSessionBounds(date);
        if (saved is null) return new(choice.Id, choice.Version, now, now, false, [], [new("live-unavailable", now, "No saved Live context")]);
        if (saved.PortfolioId != choice.PaperAccountId || saved.GenerationId == Guid.Empty || saved.Revision < 0 ||
            saved.TradingDate != date || saved.SessionId == Guid.Empty || saved.RunId == Guid.Empty ||
            !SamePolicy(saved.Policy, strategy.ReadLivePolicy()) || saved.Comparison != choice.Comparison ||
            saved.DailyStrategyId != strategy.Daily.StrategyVersionId ||
            !strategy.Daily.Matches(saved.DailyConfiguration, saved.Models) ||
            saved.MarketDataAsOf != calendar.GetImmediatelyPrecedingSession(date) ||
            !Utc(saved.PublishedUtc) || !Utc(saved.EffectiveUtc) || !Utc(saved.UpdatedUtc) || !Utc(saved.EpochStartedUtc) ||
            saved.PublishedUtc > saved.EffectiveUtc || saved.EffectiveUtc > now || saved.UpdatedUtc > now ||
            saved.EpochStartedUtc > now || saved.CurrentEpoch < 1 || saved.Signals.IsDefault || saved.History.IsDefault ||
            saved.Signals.Any(s => s is null || string.IsNullOrWhiteSpace(s.Symbol) || s.Symbol == "XIU" || s.Checkpoints.IsDefault) ||
            saved.Signals.Select(s => s.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Signals.Length)
            throw new InvalidDataException("Live source identity, policy or dated evidence differs from the catalogue.");

        bool usable = saved.EpochActive && saved.AccountSourceRevisionId == saved.SourceRevisionId && !saved.Signals.IsEmpty;
        var expiry = new DateTimeOffset(bounds.CloseUtc);
        var evidence = ImmutableArray.CreateBuilder<AiMarketEvidence>();
        var rules = ImmutableArray.Create(
            new AiPortfolioRule("source-validity", true, "Preserve exact source/policy/gate identity, current continuity epoch and valid on-time stock/XIU evidence. Missing or conflicting evidence cannot be waived."),
            new AiPortfolioRule("stock-protection", true, JsonSerializer.Serialize(new { saved.Policy.HardLossFraction,
                saved.Policy.FastDownsideReturnFloor, saved.Policy.ProfitFloorActivationGainFraction,
                saved.Policy.TrailingActivationGainFraction, saved.Policy.TrailingDistanceFraction,
                Requirement = "Existing stops and protective exits continue; established stops may only tighten." })),
            new AiPortfolioRule("account-limits", true, $"Maximum {saved.Policy.MaximumHoldings} strategy holdings. Recheck actual managed cash, whole shares, orders and shared risk allowance."),
            new AiPortfolioRule("live-entry-guidance", false, $"Engine entry guidance uses {saved.Policy.EntryConfirmationCount} consecutive qualifying observations and existing live ranking. Saved paper confirmation is not managed-account entry or post-exit re-entry authority."),
            new AiPortfolioRule("live-market-guidance", false, "Persistence, price movement, volume support, price structure and daily admission are trading guidance. Preserve original measurements, missingness, gates and fixed bypass attribution."));
        evidence.Add(new("live-source", saved.UpdatedUtc, JsonSerializer.Serialize(new { saved.PortfolioId, saved.GenerationId,
            saved.Revision, saved.SessionId, saved.RunId, saved.SourceRevisionId, saved.AccountSourceRevisionId,
            saved.PublishedUtc, saved.EffectiveUtc, saved.MarketDataAsOf, saved.CurrentEpoch, saved.EpochStartedUtc,
            saved.EpochActive, saved.Comparison, Scope = "Candidate paper context, not managed funds or permissions" })));
        foreach (var signal in saved.Signals.OrderBy(s => s.Symbol, StringComparer.Ordinal))
        {
            var checkpoints = signal.Checkpoints.OrderBy(e => e.Input.BarEndUtc).ToArray();
            if (checkpoints.Select(e => e.Input.BarEndUtc).Distinct().Count() != checkpoints.Length)
                throw new InvalidDataException("Duplicate Live checkpoints.");
            foreach (var checkpoint in checkpoints)
            {
                var input = checkpoint.Input; var result = checkpoint.Result;
                if (input.EvaluationId == Guid.Empty || input.EvaluationId != result.EvaluationId || input.SessionId != saved.SessionId ||
                    input.Stock.Symbol != signal.Symbol || input.Xiu.Symbol != "XIU" || !SamePolicy(input.Policy, saved.Policy) ||
                    !Utc(input.BarEndUtc) || !Utc(input.EvaluatedUtc) || input.EvaluatedUtc <= input.BarEndUtc ||
                    input.EvaluatedUtc > now || input.BarEndUtc <= bounds.OpenUtc || input.BarEndUtc > bounds.CloseUtc ||
                    checkpoint.ContinuityEpoch < 1 || checkpoint.ContinuityEpoch > saved.CurrentEpoch ||
                    result.ConfirmedLiveEligible && !result.ObservationIsValid)
                    throw new InvalidDataException("Invalid Live checkpoint envelope.");
            }
            var latest = checkpoints.LastOrDefault();
            var state = signal.AccountState;
            bool current = latest is not null && state is not null && !signal.HasSourceConflict &&
                latest.ContinuityEpoch == saved.CurrentEpoch && state.ContinuityEpoch == saved.CurrentEpoch &&
                state.EvaluationId == latest.Input.EvaluationId && Utc(state.EvaluatedUtc) &&
                state.EvaluatedUtc >= latest.Input.EvaluatedUtc && state.EvaluatedUtc <= saved.UpdatedUtc &&
                state.Lifecycle.LastScheduledBarEndUtc == latest.Input.BarEndUtc && Enum.IsDefined(state.Lifecycle.State) &&
                state.Lifecycle.ConsecutiveStrongObservations >= 0 && state.Lifecycle.ConsecutiveStrongWeakeningObservations >= 0 &&
                latest.Input.BarEndUtc >= saved.EffectiveUtc && latest.Input.BarEndUtc >= saved.EpochStartedUtc &&
                latest.Result.ObservationIsValid && latest.Input.ExactPairPersistedOnTime &&
                latest.Result.CurrentStockObservationId is { } observationId && observationId != Guid.Empty && latest.Result.NextState.Confidence.AllowsNewRisk;
            if (latest is not null)
            {
                // Age the market endpoint, not read time or the later account save.
                var limit = new DateTimeOffset(latest.Input.BarEndUtc).Add(maximumAge);
                if (limit < expiry) expiry = limit;
                var setup = latest.Input.DailySetup;
                current &= signal.DailyCandidateId is { } id
                    ? setup is not null && setup.CandidateId == id && setup.DelphiRunId == saved.RunId && setup.DailyStrategyVersionId == saved.DailyStrategyId
                    : setup is null; // Carry-only symbols never inherit an old daily rank.
                if (setup is not null)
                {
                    if (saved.Comparison is { } comparison)
                    {
                        var admission = setup.ComparisonAdmission;
                        current &= admission is not null && admission.AccountId == comparison.AccountId &&
                            admission.BatchId == comparison.BatchId && admission.PairId == comparison.PairId &&
                            admission.Profile == comparison.Profile && admission.SourceRevisionId == saved.SourceRevisionId &&
                            admission.RunId == saved.RunId && admission.DailyStrategyVersionId == saved.DailyStrategyId &&
                            admission.PolicyVersionId == saved.Policy.PolicyVersionId && Utc(admission.EffectiveUtc) &&
                            admission.EffectiveUtc <= latest.Input.BarEndUtc;
                    }
                    else current &= setup.ComparisonAdmission is null;
                }
            }
            usable &= current;
            evidence.Add(new("live-signal:" + signal.Symbol, latest?.Input.EvaluatedUtc ?? saved.UpdatedUtc, JsonSerializer.Serialize(new {
                signal.Symbol, signal.DailyCandidateId, signal.HasSourceConflict, Current = current,
                PaperAccountConfirmation = state, Scope = "Account-specific paper lifecycle; never copied into managed confirmation. Checkpoint gaps and different epochs are not a continuous confirmation sequence.",
                Checkpoints = checkpoints.Select(e => new { e.Input.EvaluationId, e.Input.BarEndUtc, e.Input.EvaluatedUtc,
                    e.ContinuityEpoch, e.Input.DailySetup, e.Result.ObservationIsValid, e.Result.FamiliesMature,
                    e.Result.CurrentStockObservationId, e.Result.NextState.Confidence, e.Result.NextState.Momentum,
                    e.Result.NextState.FamilyJudgments, e.Result.Persistence, e.Result.PriceMovement,
                    e.Result.VolumeSupport, e.Result.PriceStructure, e.Result.RawValues, e.Result.DerivedFacts }) })));
        }
        if (saved.History.Any(h => h.Date > date || h.AsOfUtc is { } at && (!Utc(at) || at > now)))
            throw new InvalidDataException("Future performance evidence.");
        evidence.Add(new("live-performance", saved.History.Where(h => h.AsOfUtc.HasValue).Select(h => new DateTimeOffset(h.AsOfUtc!.Value))
            .DefaultIfEmpty(saved.UpdatedUtc).Max(), JsonSerializer.Serialize(new {
                Scope = "Descriptive candidate paper daily history; missing/incomplete values stay null. Not available funds or proof of superiority.",
                Days = saved.History.Select(h => h.Date).Distinct().Order().Select(d => PortfolioDailyPerformance.Calculate(saved.History, d)) })));
        return new(choice.Id, choice.Version, evidence.Max(e => e.ObservedUtc), expiry,
            usable && expiry > now && now >= bounds.OpenUtc && now < bounds.CloseUtc, rules, evidence.ToImmutable());
    }
    private static bool Utc(DateTime time) => time.Kind == DateTimeKind.Utc;
    private static bool SamePolicy(DelphiLivePolicyDefinition left, DelphiLivePolicyDefinition right) =>
        DelphiLiveLedgerJson.Serialize(left) == DelphiLiveLedgerJson.Serialize(right);
}
