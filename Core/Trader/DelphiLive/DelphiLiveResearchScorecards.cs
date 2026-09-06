#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveDiagnosticSlot(int Slot, string? Symbol, Guid? EvaluationId);
public sealed record DelphiLiveRankingCheckpoint(
    Guid CheckpointId, Guid SessionId, DateOnly TradingDate, DateTime BarEndUtc,
    Guid ChampionPolicyVersionId, DelphiLiveSourceLens Lens,
    ImmutableArray<DelphiLiveDiagnosticSlot> DailyTop5,
    ImmutableArray<DelphiLiveDiagnosticSlot> ConfirmedLiveTop5);
public sealed record DelphiLiveRankingEvidence(
    Guid? EvaluationId, DelphiLiveRankCandidate Candidate, bool ConfirmedLiveEligible);
public sealed record DelphiLiveBasketMetric(
    DelphiLiveOutcomeMetricState State, decimal? EqualWeightReturn,
    DelphiLiveMetricCoverage Coverage, ImmutableArray<DelphiLiveOutcomeMetric> Slots);
public sealed record DelphiLiveCheckpointComparison(
    DateOnly TradingDate, DateTime BarEndUtc, DelphiLiveSourceLens Lens,
    DelphiLiveBasketMetric Daily, DelphiLiveBasketMetric Live);
public sealed record DelphiLiveSessionRankingComparison(
    DateOnly TradingDate, DelphiLiveSourceLens Lens,
    decimal? DailyMeanReturn, decimal? LiveMeanReturn,
    DelphiLiveMetricCoverage PairedCheckpointCoverage);
public sealed record DelphiLiveRankingScorecard(
    DelphiLiveSourceLens Lens, decimal? DailyEqualCohortReturn,
    decimal? LiveEqualCohortReturn, decimal? IncrementalReturn,
    int ExpectedCohorts, DelphiLiveMetricCoverage CohortCoverage,
    ImmutableArray<DelphiLiveSessionRankingComparison> Sessions);

// A scheduled benchmark slot is coverage only. A missing stock anchor remains
// present as a stock outcome slot, with invalid metrics instead of disappearance.
public sealed record DelphiLiveExpectedResearchSlot(
    Guid SlotId, Guid SessionId, DateOnly TradingDate, DateTime BarEndUtc,
    string Symbol, bool IsBenchmark, Guid? AnchorObservationId,
    string OperationalDisposition, bool OperationalUsable);
public sealed record DelphiLiveResearchOutcomeRevision(
    Guid RevisionId, Guid SlotId, DateTime CalculatedUtc,
    DelphiLiveObservationOutcome? Outcome, string MissingAnchorReason);

public static class DelphiLiveResearchScorecards
{
    public static DelphiLiveRankingCheckpoint Snapshot(Guid checkpointId, Guid sessionId,
        DateOnly date, DateTime barEndUtc, Guid championPolicyVersionId, DelphiLiveSourceLens lens,
        IReadOnlyCollection<DelphiLiveRankingEvidence> evaluations)
    {
        if (checkpointId == Guid.Empty || sessionId == Guid.Empty || championPolicyVersionId == Guid.Empty ||
            barEndUtc.Kind != DateTimeKind.Utc || !Enum.IsDefined(lens))
            throw new ArgumentException("A ranking checkpoint requires frozen identities and exact UTC market time.");
        TimeOnly local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(barEndUtc,
            TimeZoneInfo.FindSystemTimeZoneById("America/Toronto")));
        if (local < new TimeOnly(9, 50) || local >= new TimeOnly(15, 45) || local.Minute % 5 != 0 || local.Second != 0)
            throw new ArgumentException("Diagnostic baskets exist at entry-window endpoints from 09:50 through 15:40.");
        if (evaluations.Select(e => e.Candidate.Symbol).Distinct(StringComparer.Ordinal).Count() != evaluations.Count ||
            evaluations.Any(e => e.EvaluationId == Guid.Empty || e.ConfirmedLiveEligible && e.EvaluationId is null))
            throw new ArgumentException("Each symbol contributes one canonical champion evaluation.");
        DelphiLiveRankingEvidence[] published = evaluations.Where(e => e.Candidate.DailySetup?.RankFor(lens) is not null).ToArray();
        var daily = published.OrderBy(e => e.Candidate.DailySetup!.RankFor(lens))
            .ThenBy(e => e.Candidate.Symbol, StringComparer.Ordinal).Take(5).ToArray();
        var bySymbol = published.ToDictionary(e => e.Candidate.Symbol, StringComparer.Ordinal);
        var live = DelphiLiveRanking.OrderForLens(published.Where(e => e.ConfirmedLiveEligible).Select(e => e.Candidate), lens)
            .Take(5).Select(candidate => bySymbol[candidate.Symbol]).ToArray();
        return new(checkpointId, sessionId, date, barEndUtc, championPolicyVersionId, lens, Slots(daily), Slots(live));
    }

    public static DelphiLiveBasketMetric CalculateBasket(ImmutableArray<DelphiLiveDiagnosticSlot> slots,
        IReadOnlyDictionary<string, DelphiLiveOutcomeMetric> exactAnchorMetrics, DelphiLivePolicyDefinition policy,
        bool horizonApplicable = true)
    {
        if (slots.Length != 5 || !slots.Select(s => s.Slot).SequenceEqual(new[] { 1, 2, 3, 4, 5 }) ||
            slots.Where(s => s.Symbol is not null).Select(s => s.Symbol).Distinct().Count() != slots.Count(s => s.Symbol is not null))
            throw new ArgumentException("A diagnostic basket has five distinct equal-weight slots, including cash.");
        var metrics = slots.Select(slot => !horizonApplicable ? DelphiLiveOutcomeMetric.NotApplicable() : slot.Symbol is null
                ? DelphiLiveOutcomeMetric.Valid(0m)
                : exactAnchorMetrics.TryGetValue(slot.Symbol, out var metric)
                    ? metric : DelphiLiveOutcomeMetric.Invalid("MissingExpectedAnchor"))
            .ToImmutableArray();
        DelphiLiveMetricCoverage coverage = DelphiLiveCoverageCalculator.Calculate(metrics.Select(m => m.State), policy);
        bool allUsable = metrics.All(m => m.State is DelphiLiveOutcomeMetricState.Valid or DelphiLiveOutcomeMetricState.Degraded);
        DelphiLiveOutcomeMetricState state = allUsable
            ? metrics.Any(m => m.State == DelphiLiveOutcomeMetricState.Degraded)
                ? DelphiLiveOutcomeMetricState.Degraded : DelphiLiveOutcomeMetricState.Valid
            : metrics.Any(m => m.State == DelphiLiveOutcomeMetricState.Pending)
                ? DelphiLiveOutcomeMetricState.Pending
                : metrics.All(m => m.State == DelphiLiveOutcomeMetricState.NotApplicable)
                    ? DelphiLiveOutcomeMetricState.NotApplicable : DelphiLiveOutcomeMetricState.Invalid;
        return new(state, allUsable ? metrics.Sum(m => m.RequireValue()) / 5m : null, coverage, metrics);
    }

    public static DelphiLiveRankingScorecard Aggregate(DelphiLiveSourceLens lens,
        IReadOnlyCollection<DelphiLiveCheckpointComparison> checkpoints, DelphiLivePolicyDefinition policy,
        IReadOnlyCollection<(DateOnly TradingDate, DateTime BarEndUtc)> expectedCheckpoints)
    {
        if (checkpoints.Any(c => c.Lens != lens) ||
            checkpoints.Select(c => (c.TradingDate, c.BarEndUtc)).Distinct().Count() != checkpoints.Count ||
            expectedCheckpoints.Distinct().Count() != expectedCheckpoints.Count ||
            checkpoints.Any(c => !expectedCheckpoints.Contains((c.TradingDate, c.BarEndUtc))))
            throw new ArgumentException("A lens scorecard requires distinct canonical checkpoints.");
        var byEndpoint = checkpoints.ToDictionary(c => (c.TradingDate, c.BarEndUtc));
        var sessions = expectedCheckpoints.GroupBy(c => c.TradingDate).OrderBy(g => g.Key).Select(group =>
        {
            var present = group.Where(byEndpoint.ContainsKey).Select(c => byEndpoint[c]).ToArray();
            var paired = present.Where(c => c.Daily.EqualWeightReturn.HasValue && c.Live.EqualWeightReturn.HasValue).ToArray();
            var coverage = DelphiLiveCoverageCalculator.Calculate(group.Select(c => byEndpoint.TryGetValue(c, out var row)
                ? PairState(row.Daily.State, row.Live.State) : DelphiLiveOutcomeMetricState.Invalid), policy);
            bool permitted = coverage.Readiness is DelphiLiveCoverageReadiness.Ready or DelphiLiveCoverageReadiness.Degraded;
            return new DelphiLiveSessionRankingComparison(group.Key, lens,
                permitted && paired.Length > 0 ? paired.Average(c => c.Daily.EqualWeightReturn!.Value) : null,
                permitted && paired.Length > 0 ? paired.Average(c => c.Live.EqualWeightReturn!.Value) : null, coverage);
        }).ToImmutableArray();
        var cohortCoverage = DelphiLiveCoverageCalculator.Calculate(sessions.Select(s => s.PairedCheckpointCoverage.Readiness switch
        {
            DelphiLiveCoverageReadiness.Ready => DelphiLiveOutcomeMetricState.Valid,
            DelphiLiveCoverageReadiness.Degraded => DelphiLiveOutcomeMetricState.Degraded,
            DelphiLiveCoverageReadiness.NotMature => DelphiLiveOutcomeMetricState.Pending,
            DelphiLiveCoverageReadiness.NotApplicable => DelphiLiveOutcomeMetricState.NotApplicable,
            _ => DelphiLiveOutcomeMetricState.Invalid
        }), policy);
        bool report = cohortCoverage.Readiness is DelphiLiveCoverageReadiness.Ready or DelphiLiveCoverageReadiness.Degraded;
        var usable = sessions.Where(s => s.DailyMeanReturn.HasValue && s.LiveMeanReturn.HasValue).ToArray();
        decimal? daily = report && usable.Length > 0 ? usable.Average(s => s.DailyMeanReturn!.Value) : null;
        decimal? live = report && usable.Length > 0 ? usable.Average(s => s.LiveMeanReturn!.Value) : null;
        return new(lens, daily, live, live - daily, sessions.Length, cohortCoverage, sessions);
    }

    public static DelphiLiveResearchOutcomeRevision CalculateExpectedOutcome(
        DelphiLiveExpectedResearchSlot slot, DelphiLiveOutcomeCalculationInput? input,
        DateTime calculatedUtc, DelphiLivePolicyDefinition policy)
    {
        if (slot.SlotId == Guid.Empty || slot.SessionId == Guid.Empty || slot.IsBenchmark || slot.Symbol == "XIU")
            throw new ArgumentException("Only an expected stock slot receives a stock outcome; XIU retains benchmark coverage.");
        if (calculatedUtc.Kind != DateTimeKind.Utc || calculatedUtc <= slot.BarEndUtc)
            throw new ArgumentException("Research calculation time must follow its checkpoint.");
        if (input is null)
            return new(Guid.NewGuid(), slot.SlotId, calculatedUtc, null, "MissingExpectedAnchor");
        if (input.Anchor.Symbol != slot.Symbol || input.Anchor.EndUtc != slot.BarEndUtc ||
            input.Anchor.SessionDate != slot.TradingDate || input.AsOfUtc != calculatedUtc ||
            (slot.AnchorObservationId is Guid anchor && input.Anchor.ObservationId != anchor))
            throw new ArgumentException("Research input must retain its expected symbol/checkpoint anchor identity.");
        return new(Guid.NewGuid(), slot.SlotId, calculatedUtc,
            DelphiLiveObservationOutcomeCalculator.Calculate(input, policy), "");
    }

    public static decimal MaximumCheckpointDrawdown(decimal startingCapital, IEnumerable<decimal> chronologicalCompleteNavs)
    {
        if (startingCapital <= 0m) throw new ArgumentOutOfRangeException(nameof(startingCapital));
        decimal high = startingCapital, worst = 0m;
        foreach (decimal nav in chronologicalCompleteNavs)
        {
            if (nav < 0m) throw new ArgumentOutOfRangeException(nameof(chronologicalCompleteNavs));
            worst = System.Math.Max(worst, 1m - nav / high);
            high = System.Math.Max(high, nav);
        }
        return worst;
    }

    private static ImmutableArray<DelphiLiveDiagnosticSlot> Slots(DelphiLiveRankingEvidence[] selected) =>
        Enumerable.Range(0, 5).Select(index => index < selected.Length
            ? new DelphiLiveDiagnosticSlot(index + 1, selected[index].Candidate.Symbol, selected[index].EvaluationId)
            : new DelphiLiveDiagnosticSlot(index + 1, null, null)).ToImmutableArray();

    private static DelphiLiveOutcomeMetricState PairState(DelphiLiveOutcomeMetricState first, DelphiLiveOutcomeMetricState second) =>
        first == DelphiLiveOutcomeMetricState.Pending || second == DelphiLiveOutcomeMetricState.Pending
            ? DelphiLiveOutcomeMetricState.Pending
            : first == DelphiLiveOutcomeMetricState.NotApplicable && second == DelphiLiveOutcomeMetricState.NotApplicable
                ? DelphiLiveOutcomeMetricState.NotApplicable
                : first is DelphiLiveOutcomeMetricState.Valid or DelphiLiveOutcomeMetricState.Degraded &&
                  second is DelphiLiveOutcomeMetricState.Valid or DelphiLiveOutcomeMetricState.Degraded
                    ? first == DelphiLiveOutcomeMetricState.Degraded || second == DelphiLiveOutcomeMetricState.Degraded
                        ? DelphiLiveOutcomeMetricState.Degraded : DelphiLiveOutcomeMetricState.Valid
                    : DelphiLiveOutcomeMetricState.Invalid;
}
