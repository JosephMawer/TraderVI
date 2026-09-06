#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLivePortfolioHistoryItem(DelphiLivePortfolioSnapshot Portfolio, DateOnly? EndExclusiveTradingDate);
public sealed record DelphiLiveDiagnosticEvaluation(Guid EvaluationId, Guid SessionId, string Symbol, DateOnly TradingDate, DateTime BarEndUtc,
    DelphiLiveTrueRangeRulerMeasurement TenSessionRuler, bool ObservationIsValid, bool FamiliesMature, bool ConfirmedLiveEligible,
    DelphiLivePersistenceJudgment Persistence, DelphiLivePriceMovementJudgment PriceMovement,
    DelphiLiveVolumeSupportJudgment VolumeSupport, DelphiLivePriceStructureJudgment PriceStructure,
    DelphiLivePriceMovementMeasurements PriceMovementMeasurements, DelphiLiveDataConfidence Confidence,
    DelphiLiveMomentumJudgment PreviousMomentum, DelphiLiveMomentumJudgment CurrentMomentum, DelphiLiveSafetyEvaluation Safety,
    DelphiLiveSafetyInput SafetyInput)
{
    public static DelphiLiveDiagnosticEvaluation FromStored(DelphiLiveStoredEvaluation e) => new(e.Input.EvaluationId,
        e.Input.SessionId, e.Input.Stock.Symbol, e.Input.Stock.SessionDate, e.Input.BarEndUtc, e.Input.VolatilityRulers.TenSession,
        e.Result.ObservationIsValid, e.Result.FamiliesMature, e.Result.ConfirmedLiveEligible, e.Result.Persistence,
        e.Result.PriceMovement, e.Result.VolumeSupport, e.Result.PriceStructure, e.Result.PriceMovementMeasurements,
        e.Result.NextState.Confidence, e.Input.PreviousState.Momentum, e.Result.NextState.Momentum, e.Result.Safety, e.Result.SafetyInput);
}
public interface IDelphiLiveDiagnosticSource
{
    Task<IReadOnlyList<DelphiLiveDiagnosticEvaluation>> ReadChampionEvaluationsAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DelphiLivePortfolioHistoryItem>> ReadPortfolioHistoryAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default);
}

public sealed record DelphiLiveDiagnosticForwardMetric(string Metric, decimal? EqualCohortMean, DelphiLiveMetricCoverage Coverage);
public sealed record DelphiLiveDiagnosticSessionSummary(DateOnly TradingDate, int ExpectedSlots, int ObservedSignalCount,
    decimal? SignalFrequency, decimal? FirstSignalMinutesAfterOpen, int DirectionFlipCount,
    int AbsoluteRelativeAgreementCount, int ConfirmedEntryAbsentCount, int MissingObservationChangedMarketJudgmentCount,
    DelphiLiveMetricCoverage SignalCoverage, ImmutableArray<DelphiLiveDiagnosticForwardMetric> ForwardMetrics);
public sealed record DelphiLiveDiagnosticScorecard(string Category, string Variant, string Signal, string Authority,
    DelphiLiveOutcomeHorizon Horizon, int ExpectedSlots, int ObservedSignalCount,
    DelphiLiveMetricCoverage SignalCoverage, decimal? EqualCohortSignalFrequency,
    decimal? EqualCohortFirstSignalMinutesAfterOpen, int DirectionFlipCount, int AbsoluteRelativeAgreementCount,
    int ConfirmedEntryAbsentCount, int MissingObservationChangedMarketJudgmentCount,
    ImmutableArray<DelphiLiveDiagnosticForwardMetric> ForwardMetrics,
    int SessionCount, ImmutableArray<DelphiLiveDiagnosticSessionSummary> Sessions);

/// <summary>
/// Research associations only. A threshold trigger is a measured condition,
/// never an entry, fill, portfolio return, or independent promotion authority.
/// </summary>
public static class DelphiLiveDiagnosticScorecards
{
    private sealed record SignalFact(DelphiLiveOutcomeMetricState State, bool Fired, int Direction = 0,
        bool AbsoluteRelativeAgreement = false, bool ConfirmedEntryAbsent = false, bool MissingChangedMarket = false);
    private sealed record Probe(string Category, string Variant, string Signal,
        Func<DelphiLiveDiagnosticEvaluation, SignalFact> Evaluate);

    public static ImmutableArray<DelphiLiveDiagnosticScorecard> Calculate(
        IReadOnlyCollection<DelphiLiveExpectedResearchSlot> expectedSlots,
        IReadOnlyCollection<DelphiLiveStoredEvaluation> championEvaluations,
        IReadOnlyCollection<DelphiLiveResearchOutcomeRevision> latestOutcomes,
        ITsxSessionCalendar calendar, DelphiLivePolicyDefinition policy) =>
        Calculate(expectedSlots, championEvaluations.Select(DelphiLiveDiagnosticEvaluation.FromStored).ToArray(), latestOutcomes, calendar, policy);

    public static ImmutableArray<DelphiLiveDiagnosticScorecard> Calculate(
        IReadOnlyCollection<DelphiLiveExpectedResearchSlot> expectedSlots,
        IReadOnlyCollection<DelphiLiveDiagnosticEvaluation> championEvaluations,
        IReadOnlyCollection<DelphiLiveResearchOutcomeRevision> latestOutcomes,
        ITsxSessionCalendar calendar, DelphiLivePolicyDefinition policy)
    {
        policy.Validate();
        var slots = expectedSlots.Where(s => !s.IsBenchmark).OrderBy(s => s.TradingDate).ThenBy(s => s.BarEndUtc).ThenBy(s => s.Symbol).ToArray();
        if (slots.Select(s => (s.SessionId, s.Symbol, s.BarEndUtc)).Distinct().Count() != slots.Length ||
            championEvaluations.Select(e => (e.SessionId, e.Symbol, e.BarEndUtc)).Distinct().Count() != championEvaluations.Count ||
            latestOutcomes.Select(o => o.SlotId).Distinct().Count() != latestOutcomes.Count)
            throw new ArgumentException("Diagnostics require one expected slot, champion evaluation and latest outcome per canonical identity.");
        var evaluations = championEvaluations.ToDictionary(e => (e.SessionId, e.Symbol, e.BarEndUtc));
        var outcomes = latestOutcomes.ToDictionary(o => o.SlotId);
        string[] metricNames = Metrics(policy).ToArray();
        var rows = ImmutableArray.CreateBuilder<DelphiLiveDiagnosticScorecard>();
        foreach (var probe in Probes())
        {
            var facts = slots.ToDictionary(s => s.SlotId, s => evaluations.TryGetValue((s.SessionId, s.Symbol, s.BarEndUtc), out var evaluation)
                ? probe.Evaluate(evaluation) : new SignalFact(DelphiLiveOutcomeMetricState.Invalid, false));
            var allTriggered = slots.Where(s => facts[s.SlotId].Fired && IsUsable(facts[s.SlotId].State)).ToArray();
            foreach (var horizon in Enum.GetValues<DelphiLiveOutcomeHorizon>())
            {
                var cohortRows = slots.GroupBy(s => s.TradingDate).Select(group =>
                {
                    var covered = group.Where(s => IsUsable(facts[s.SlotId].State)).ToArray();
                    var triggered = covered.Where(s => facts[s.SlotId].Fired).ToArray();
                    var coverage = DelphiLiveCoverageCalculator.Calculate(group.Select(s => facts[s.SlotId].State), policy);
                    bool permitted = Usable(coverage);
                    // Equal symbols within each checkpoint, then equal checkpoints.
                    decimal? frequency = permitted && covered.Length > 0 ? covered.GroupBy(s => s.BarEndUtc)
                        .Average(checkpoint => checkpoint.Average(s => facts[s.SlotId].Fired ? 1m : 0m)) : null;
                    decimal? firstMinutes = permitted && triggered.Length > 0 ? triggered.GroupBy(s => s.Symbol)
                        .Average(symbol => (decimal)(symbol.Min(s => s.BarEndUtc) - calendar.GetSessionBounds(group.Key).OpenUtc).TotalMinutes) : null;
                    int flips = 0;
                    foreach (var symbol in group.GroupBy(s => s.Symbol))
                    {
                        DelphiLiveExpectedResearchSlot? prior = null;
                        foreach (var current in symbol.OrderBy(s => s.BarEndUtc))
                        {
                            var fact = facts[current.SlotId];
                            if (prior is not null && current.BarEndUtc - prior.BarEndUtc == policy.BarInterval &&
                                IsUsable(fact.State) && IsUsable(facts[prior.SlotId].State) && fact.Fired &&
                                fact.Direction * facts[prior.SlotId].Direction == -1) flips++;
                            prior = current;
                        }
                    }
                    var forward = metricNames.Select(metric =>
                    {
                        bool capture = metric.StartsWith("OpportunityCapture", StringComparison.Ordinal);
                        var values = (capture ? covered : triggered).Select(slot => (Slot: slot, Metric: Forward(slot, outcomes.GetValueOrDefault(slot.SlotId), horizon, metric, calendar))).ToArray();
                        var metricCoverage = DelphiLiveCoverageCalculator.Calculate(values.Select(v => v.Metric.State), policy);
                        var valid = values.Where(v => IsUsable(v.Metric.State)).ToArray();
                        decimal? mean = null;
                        if (permitted && Usable(metricCoverage) && valid.Length > 0)
                        {
                            if (capture)
                            {
                                var opportunities = valid.Where(v => v.Metric.RequireValue() == 1m).GroupBy(v => v.Slot.BarEndUtc).ToArray();
                                mean = opportunities.Length > 0 ? opportunities.Average(checkpoint =>
                                    checkpoint.Average(v => facts[v.Slot.SlotId].Fired ? 1m : 0m)) : null;
                            }
                            else mean = valid.GroupBy(v => v.Slot.BarEndUtc).Average(checkpoint => checkpoint.Average(v => v.Metric.RequireValue()));
                        }
                        return new DelphiLiveDiagnosticForwardMetric(metric, mean, metricCoverage);
                    }).ToImmutableArray();
                    return new DelphiLiveDiagnosticSessionSummary(group.Key, group.Count(), triggered.Length, frequency, firstMinutes, flips,
                        triggered.Count(s => facts[s.SlotId].AbsoluteRelativeAgreement), triggered.Count(s => facts[s.SlotId].ConfirmedEntryAbsent),
                        triggered.Count(s => facts[s.SlotId].MissingChangedMarket), coverage, forward);
                }).OrderBy(s => s.TradingDate).ToImmutableArray();
                var signalCoverage = DelphiLiveCoverageCalculator.Calculate(slots.Select(s => facts[s.SlotId].State), policy);
                // A wholly missing market day cannot disappear when the
                // remaining days are averaged into an apparently usable mean.
                var cohortCoverage = DelphiLiveCoverageCalculator.Calculate(cohortRows.Select(s => CoverageState(s.SignalCoverage)), policy);
                bool report = Usable(signalCoverage) && Usable(cohortCoverage);
                var aggregated = metricNames.Select(metric =>
                {
                    bool capture = metric.StartsWith("OpportunityCapture", StringComparison.Ordinal);
                    var selectedSlots = capture ? slots.Where(s => IsUsable(facts[s.SlotId].State)) : allTriggered;
                    var coverage = DelphiLiveCoverageCalculator.Calculate(selectedSlots.Select(s => Forward(s, outcomes.GetValueOrDefault(s.SlotId), horizon, metric, calendar).State), policy);
                    var usableCohorts = cohortRows.Where(s => s.ForwardMetrics.Single(m => m.Metric == metric).EqualCohortMean.HasValue).ToArray();
                    var metricCohortCoverage = DelphiLiveCoverageCalculator.Calculate(cohortRows.Select(s =>
                        CoverageState(s.ForwardMetrics.Single(m => m.Metric == metric).Coverage)), policy);
                    decimal? mean = report && Usable(coverage) && Usable(metricCohortCoverage) && usableCohorts.Length > 0
                        ? usableCohorts.Average(s => s.ForwardMetrics.Single(m => m.Metric == metric).EqualCohortMean!.Value) : null;
                    return new DelphiLiveDiagnosticForwardMetric(metric, mean, coverage);
                }).ToImmutableArray();
                var frequencyCohorts = cohortRows.Where(s => s.SignalFrequency.HasValue).ToArray();
                var timedCohorts = cohortRows.Where(s => s.FirstSignalMinutesAfterOpen.HasValue).ToArray();
                rows.Add(new(probe.Category, probe.Variant, probe.Signal, "ResearchCounterfactual", horizon,
                    slots.Length, cohortRows.Sum(s => s.ObservedSignalCount), signalCoverage,
                    report && frequencyCohorts.Length > 0 ? frequencyCohorts.Average(s => s.SignalFrequency!.Value) : null,
                    report && timedCohorts.Length > 0 ? timedCohorts.Average(s => s.FirstSignalMinutesAfterOpen!.Value) : null,
                    cohortRows.Sum(s => s.DirectionFlipCount), cohortRows.Sum(s => s.AbsoluteRelativeAgreementCount),
                    cohortRows.Sum(s => s.ConfirmedEntryAbsentCount), cohortRows.Sum(s => s.MissingObservationChangedMarketJudgmentCount),
                    aggregated, cohortRows.Length, cohortRows));
            }
        }
        return rows.ToImmutable();
    }

    private static IEnumerable<Probe> Probes()
    {
        foreach (var family in Enum.GetValues<DelphiLiveSignalFamily>())
        foreach (var state in new[] { DelphiLiveFamilyState.Supportive, DelphiLiveFamilyState.PositiveLeaning,
            DelphiLiveFamilyState.Neutral, DelphiLiveFamilyState.NeutralConflict, DelphiLiveFamilyState.NegativeLeaning, DelphiLiveFamilyState.Weakening })
            yield return new("Component", family.ToString(), state.ToString(), evaluation =>
            {
                var measured = family switch
                {
                    DelphiLiveSignalFamily.Persistence => evaluation.Persistence.Family,
                    DelphiLiveSignalFamily.PriceMovement => evaluation.PriceMovement.Family,
                    DelphiLiveSignalFamily.VolumeSupport => evaluation.VolumeSupport.Family,
                    _ => evaluation.PriceStructure.Family
                };
                return new(MeasurementState(evaluation, measured.State), measured.State == state);
            });
        foreach (int minutes in new[] { 20, 60 })
        foreach (bool relative in new[] { false, true })
        foreach (decimal threshold in relative ? new[] { .025m, .05m, .10m } : new[] { .15m, .25m, .35m })
        foreach (int direction in new[] { 1, -1 })
            yield return new(relative ? "RelativeDeadband" : "RawMoveThreshold",
                $"{threshold.ToString("0.###", CultureInfo.InvariantCulture)} units / {minutes}m / MedianTR10",
                direction == 1 ? "Up" : "Down", evaluation =>
                {
                    var window = minutes == 20 ? evaluation.PriceMovementMeasurements.TwentyMinute : evaluation.PriceMovementMeasurements.OneHour;
                    var ruler = evaluation.TenSessionRuler.MedianTrueRangePct;
                    var windowState = window.StockReturn.Availability == DelphiLiveMeasurementAvailability.NotMature
                        ? DelphiLiveFamilyState.NotMature : window.StockReturn.Availability != DelphiLiveMeasurementAvailability.Available ||
                          window.ExcessReturn.Availability != DelphiLiveMeasurementAvailability.Available || ruler.Value is not > 0m
                            ? DelphiLiveFamilyState.Unavailable : DelphiLiveFamilyState.Neutral;
                    var availability = MeasurementState(evaluation, windowState);
                    if (!IsUsable(availability)) return new(availability, false);
                    decimal raw = window.StockReturn.RequireValue() / ruler.RequireValue();
                    decimal excess = window.ExcessReturn.RequireValue() / ruler.RequireValue();
                    decimal units = relative ? excess : raw;
                    int sign = units >= threshold ? 1 : units <= -threshold ? -1 : 0;
                    // The absolute rule stays 0.25 while deadband variants are
                    // compared; the relative rule stays 0.05 for raw variants.
                    bool agrees = relative ? raw * sign >= .25m : excess * sign >= .05m;
                    return new(availability, sign == direction, sign, agrees);
                });
        foreach (var veto in new[] { DelphiLiveExitRule.FastDownside10Pct, DelphiLiveExitRule.ConfirmedSupportFailure })
            yield return new("Safety", veto.ToString(), "VetoActive", e => new(
                e.ObservationIsValid ? DelphiLiveOutcomeMetricState.Valid : DelphiLiveOutcomeMetricState.Invalid,
                EntryVeto(e.SafetyInput, veto), ConfirmedEntryAbsent: !e.ConfirmedLiveEligible));
        yield return new("Safety", "EntrySafetyVeto", "ClearControl", e => new(
            e.ObservationIsValid && e.FamiliesMature ? DelphiLiveOutcomeMetricState.Valid :
                e.ObservationIsValid ? DelphiLiveOutcomeMetricState.NotApplicable : DelphiLiveOutcomeMetricState.Invalid,
            !e.Safety.EntrySafetyVetoActive));
        foreach (var confidence in Enum.GetValues<DelphiLiveDataConfidenceState>())
            yield return new("DataConfidence", "PersistedMonitoringState", confidence.ToString(), e => new(
                DelphiLiveOutcomeMetricState.Valid, e.Confidence.State == confidence,
                ConfirmedEntryAbsent: !e.ConfirmedLiveEligible,
                MissingChangedMarket: !e.ObservationIsValid && e.CurrentMomentum != e.PreviousMomentum));
    }

    private static DelphiLiveOutcomeMetricState MeasurementState(DelphiLiveDiagnosticEvaluation evaluation, DelphiLiveFamilyState state) =>
        !evaluation.ObservationIsValid ? DelphiLiveOutcomeMetricState.Invalid : state switch
        { DelphiLiveFamilyState.NotMature => DelphiLiveOutcomeMetricState.NotApplicable,
          DelphiLiveFamilyState.Unavailable => DelphiLiveOutcomeMetricState.Invalid, _ => DelphiLiveOutcomeMetricState.Valid };

    private static bool EntryVeto(DelphiLiveSafetyInput input, DelphiLiveExitRule veto) => veto switch
    {
        DelphiLiveExitRule.FastDownside10Pct => input.CompletedBarOpen is > 0m && input.CompletedBarClose is > 0m &&
            input.CompletedBarClose / input.CompletedBarOpen - 1m <= DelphiLivePolicyDefinition.Version1.FastDownsideReturnFloor,
        DelphiLiveExitRule.ConfirmedSupportFailure => !input.IsWarmingUp && input.SessionVwapReferenceAvailable &&
            input.CloseBelowBufferedSessionVwap && input.PriorRangeReferenceAvailable && input.CloseBelowBufferedPriorTwentyMinuteLow &&
            input.VolumeSupport.State == DelphiLiveFamilyState.Weakening,
        _ => false
    };

    private static IEnumerable<string> Metrics(DelphiLivePolicyDefinition policy) => new[]
        { "RawReturn", "ExcessReturn", "MaximumFavourableMovement", "MaximumAdverseMovement", "SubsequentWinnerRate", "SubsequentLossRate" }
        .Concat(policy.OpportunityThresholds.Select(t => "OpportunityAtLeast" + t.ToString("0.##%", CultureInfo.InvariantCulture)))
        .Concat(policy.OpportunityThresholds.Select(t => "OpportunityCaptureAtLeast" + t.ToString("0.##%", CultureInfo.InvariantCulture)));

    private static DelphiLiveOutcomeMetric Forward(DelphiLiveExpectedResearchSlot slot, DelphiLiveResearchOutcomeRevision? revision,
        DelphiLiveOutcomeHorizon horizon, string metric, ITsxSessionCalendar calendar)
    {
        int? minutes = horizon switch { DelphiLiveOutcomeHorizon.Minutes20 => 20, DelphiLiveOutcomeHorizon.Minutes60 => 60,
            DelphiLiveOutcomeHorizon.Minutes120 => 120, DelphiLiveOutcomeHorizon.Minutes180 => 180, _ => null };
        if (minutes.HasValue && slot.BarEndUtc.AddMinutes(minutes.Value) > calendar.GetSessionBounds(slot.TradingDate).CloseUtc)
            return DelphiLiveOutcomeMetric.NotApplicable();
        if (revision is null) return new(DelphiLiveOutcomeMetricState.Pending, null, "AwaitingOutcomeCalculation");
        var outcome = revision?.Outcome?.Horizons.SingleOrDefault(h => h.Horizon == horizon);
        if (outcome is null) return DelphiLiveOutcomeMetric.Invalid(string.IsNullOrWhiteSpace(revision?.MissingAnchorReason) ? "MissingExpectedAnchor" : revision.MissingAnchorReason);
        return metric switch
        {
            "RawReturn" => outcome.RawReturn, "ExcessReturn" => outcome.ExcessReturn,
            "MaximumFavourableMovement" => outcome.MaximumFavourableMovement, "MaximumAdverseMovement" => outcome.MaximumAdverseMovement,
            "SubsequentWinnerRate" => Binary(outcome.RawReturn, value => value > 0m),
            "SubsequentLossRate" => Binary(outcome.RawReturn, value => value < 0m),
            _ => Binary(outcome.MaximumFavourableMovement, value => value >= decimal.Parse(
                metric[(metric.StartsWith("OpportunityCapture", StringComparison.Ordinal) ? "OpportunityCaptureAtLeast".Length : "OpportunityAtLeast".Length)..].TrimEnd('%'), CultureInfo.InvariantCulture) / 100m)
        };
    }
    private static DelphiLiveOutcomeMetric Binary(DelphiLiveOutcomeMetric input, Func<decimal, bool> predicate) => IsUsable(input.State)
        ? input with { Value = predicate(input.RequireValue()) ? 1m : 0m } : input;
    private static bool IsUsable(DelphiLiveOutcomeMetricState state) => state is DelphiLiveOutcomeMetricState.Valid or DelphiLiveOutcomeMetricState.Degraded;
    private static bool Usable(DelphiLiveMetricCoverage coverage) => coverage.Readiness is DelphiLiveCoverageReadiness.Ready or DelphiLiveCoverageReadiness.Degraded;
    private static DelphiLiveOutcomeMetricState CoverageState(DelphiLiveMetricCoverage coverage) => coverage.Readiness switch
    { DelphiLiveCoverageReadiness.Ready => DelphiLiveOutcomeMetricState.Valid, DelphiLiveCoverageReadiness.Degraded => DelphiLiveOutcomeMetricState.Degraded,
      DelphiLiveCoverageReadiness.NotMature => DelphiLiveOutcomeMetricState.Pending, DelphiLiveCoverageReadiness.NotApplicable => DelphiLiveOutcomeMetricState.NotApplicable,
      _ => DelphiLiveOutcomeMetricState.Invalid };
}
