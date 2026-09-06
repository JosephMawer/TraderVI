#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace TraderVI.Core.Tests;

public sealed class DelphiLiveDiagnosticScorecardTests
{
    private static readonly DateOnly Date = new(2026, 9, 8);
    private static readonly DateTime Open = new(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DelphiLivePolicyDefinition Policy = DelphiLivePolicyDefinition.Version1;

    [Fact]
    public void ComponentAssociationsAverageCheckpointsThenSessionsAndHaveNoActionAuthority()
    {
        var a = Evidence(0, 20); var b = Evidence(0, 25); var c = Evidence(1, 20);
        var slots = new[] { Slot(a), Slot(b), Slot(c) };
        var rows = DelphiLiveDiagnosticScorecards.Calculate(slots, [a, b, c],
            [Outcome(slots[0], .10m), Outcome(slots[1], .30m), Outcome(slots[2], .40m)], Calendar(), Policy);
        var row = rows.Single(r => r.Category == "Component" && r.Variant == "Persistence" && r.Signal == "Supportive" && r.Horizon == DelphiLiveOutcomeHorizon.Session5);
        row.Authority.ShouldBe("ResearchCounterfactual");
        row.ExpectedSlots.ShouldBe(3); row.ObservedSignalCount.ShouldBe(3); row.SessionCount.ShouldBe(2);
        row.EqualCohortSignalFrequency.ShouldBe(1m);
        row.ForwardMetrics.Single(m => m.Metric == "RawReturn").EqualCohortMean.ShouldBe(.30m);
        row.ForwardMetrics.Single(m => m.Metric == "SubsequentWinnerRate").EqualCohortMean.ShouldBe(1m);
    }

    [Fact]
    public void PredeclaredThresholdVariantsExposeFrequencyTimingAgreementAndIndependentOpportunityRates()
    {
        var evaluation = Evidence(0, 20); var slot = Slot(evaluation);
        var rows = DelphiLiveDiagnosticScorecards.Calculate([slot], [evaluation], [Outcome(slot, .02m)], Calendar(), Policy);
        var rawLow = rows.Single(r => r.Category == "RawMoveThreshold" && r.Variant.StartsWith("0.15 units / 20m") && r.Signal == "Up" && r.Horizon == DelphiLiveOutcomeHorizon.Session1);
        var rawStandard = rows.Single(r => r.Category == "RawMoveThreshold" && r.Variant.StartsWith("0.25 units / 20m") && r.Signal == "Up" && r.Horizon == DelphiLiveOutcomeHorizon.Session1);
        rawLow.EqualCohortSignalFrequency.ShouldBe(1m); rawStandard.EqualCohortSignalFrequency.ShouldBe(0m);
        rawLow.EqualCohortFirstSignalMinutesAfterOpen.ShouldBe(20m);
        rawLow.AbsoluteRelativeAgreementCount.ShouldBe(1);
        rawLow.ForwardMetrics.Single(m => m.Metric == "OpportunityAtLeast2%").EqualCohortMean.ShouldBe(1m);
        rawLow.ForwardMetrics.Single(m => m.Metric == "OpportunityAtLeast10%").EqualCohortMean.ShouldBe(0m);
        rawLow.ForwardMetrics.Single(m => m.Metric == "OpportunityCaptureAtLeast2%").EqualCohortMean.ShouldBe(1m);
        rawStandard.ForwardMetrics.Single(m => m.Metric == "OpportunityCaptureAtLeast2%").EqualCohortMean.ShouldBe(0m);
        rows.Single(r => r.Category == "RelativeDeadband" && r.Variant.StartsWith("0.1 units / 20m") && r.Signal == "Up" && r.Horizon == DelphiLiveOutcomeHorizon.Session1)
            .EqualCohortSignalFrequency.ShouldBe(0m);
    }

    [Fact]
    public void EntireMissingEvaluationCohortBlocksAssociationsInsteadOfDisappearing()
    {
        var observed = Evidence(0, 20); var missing = Evidence(1, 20);
        var first = Slot(observed); var second = Slot(missing);
        var row = DelphiLiveDiagnosticScorecards.Calculate([first, second], [observed], [Outcome(first, .01m), Outcome(second, -.2m)], Calendar(), Policy)
            .Single(r => r.Category == "Component" && r.Variant == "Persistence" && r.Signal == "Supportive" && r.Horizon == DelphiLiveOutcomeHorizon.Session5);
        row.SignalCoverage.InvalidCount.ShouldBe(1);
        row.EqualCohortSignalFrequency.ShouldBeNull();
        row.ForwardMetrics.Single(m => m.Metric == "RawReturn").EqualCohortMean.ShouldBeNull();
        row.Sessions.Length.ShouldBe(2);
    }

    [Fact]
    public void DataConfidenceCanReportBlockedEntryWhilePreservingPriorMarketJudgment()
    {
        var evidence = Evidence(0, 20);
        evidence = evidence with { Result = evidence.Result with { ObservationIsValid = false, ConfirmedLiveEligible = false,
            NextState = evidence.Input.PreviousState with { Confidence = new(DelphiLiveDataConfidenceState.Degraded, 2) } } };
        var slot = Slot(evidence);
        var row = DelphiLiveDiagnosticScorecards.Calculate([slot], [evidence], [Outcome(slot, -.02m)], Calendar(), Policy)
            .Single(r => r.Category == "DataConfidence" && r.Signal == "Degraded" && r.Horizon == DelphiLiveOutcomeHorizon.Session1);
        row.ConfirmedEntryAbsentCount.ShouldBe(1);
        row.MissingObservationChangedMarketJudgmentCount.ShouldBe(0);
        row.SignalCoverage.Readiness.ShouldBe(DelphiLiveCoverageReadiness.Ready);
        row.ForwardMetrics.Single(m => m.Metric == "SubsequentLossRate").EqualCohortMean.ShouldBe(1m);
    }

    [Fact]
    public void UncalculatedLabelsArePendingAndLateDayHorizonsRemainNotApplicable()
    {
        var evidence = Evidence(0, 380); var slot = Slot(evidence);
        var rows = DelphiLiveDiagnosticScorecards.Calculate([slot], [evidence], [], Calendar(), Policy);
        var session = rows.Single(r => r.Category == "Component" && r.Variant == "Persistence" && r.Signal == "Supportive" && r.Horizon == DelphiLiveOutcomeHorizon.Session5);
        session.ForwardMetrics.Single(m => m.Metric == "RawReturn").Coverage.PendingCount.ShouldBe(1);
        var intraday = rows.Single(r => r.Category == "Component" && r.Variant == "Persistence" && r.Signal == "Supportive" && r.Horizon == DelphiLiveOutcomeHorizon.Minutes20);
        intraday.ForwardMetrics.Single(m => m.Metric == "RawReturn").Coverage.NotApplicableCount.ShouldBe(1);
    }

    [Fact]
    public void SafetyEntryVetoIsCountedWithoutInventingHeldPositionOrExit()
    {
        var evidence = Evidence(0, 20);
        evidence = evidence with { Result = evidence.Result with {
            SafetyInput = evidence.Result.SafetyInput with { CompletedBarOpen = 100m, CompletedBarClose = 89m },
            Safety = new(true, null, [], null, null) } };
        var slot = Slot(evidence);
        var row = DelphiLiveDiagnosticScorecards.Calculate([slot], [evidence], [Outcome(slot, -.02m)], Calendar(), Policy)
            .Single(r => r.Category == "Safety" && r.Variant == "FastDownside10Pct" && r.Horizon == DelphiLiveOutcomeHorizon.Session1);
        row.ObservedSignalCount.ShouldBe(1);
        row.ForwardMetrics.Single(m => m.Metric == "SubsequentLossRate").EqualCohortMean.ShouldBe(1m);
        evidence.Result.Safety.FiredExitRules.ShouldBeEmpty();
    }

    private static DelphiLiveStoredEvaluation Evidence(int day, int minutes)
    {
        DateOnly date = Date.AddDays(day); DateTime open = Open.AddDays(day), end = open.AddMinutes(minutes);
        DelphiLiveTrueRangeRulerMeasurement Ruler(int sessions) => new(sessions, date.AddDays(-1), DelphiLiveScalarMeasurement.Available(.04m));
        var input = new DelphiLiveEvaluationInput { EvaluationId = Guid.NewGuid(), SessionId = new Guid(day + 1, 0, 0, new byte[8]),
            BarEndUtc = end, EvaluatedUtc = end.AddMinutes(2), Stock = new("AAA", date, open, open, ImmutableArray<DelphiLiveFiveMinuteBar>.Empty),
            Xiu = new("XIU", date, open, open, ImmutableArray<DelphiLiveFiveMinuteBar>.Empty), VolatilityRulers = new(Ruler(5), Ruler(10), Ruler(14), Ruler(20)),
            PreviousState = DelphiLiveEvaluationState.Initial(false), Policy = Policy };
        var result = DelphiLiveEvaluationEngine.Evaluate(input);
        DelphiLiveWindowReturnMeasurement Window(int horizon) => new(TimeSpan.FromMinutes(horizon), DelphiLiveScalarMeasurement.Available(.008m),
            DelphiLiveScalarMeasurement.Available(.005m), DelphiLiveScalarMeasurement.Available(.003m));
        result = result with { ObservationIsValid = true, FamiliesMature = true,
            Persistence = new(new(DelphiLiveSignalFamily.Persistence, DelphiLiveFamilyState.Supportive, "Measured"), 4),
            PriceMovementMeasurements = new(end, Window(20), Window(60), Window(120), Window(180), DelphiLiveScalarMeasurement.Available(.008m)) };
        return new(input, result, 1);
    }
    private static DelphiLiveExpectedResearchSlot Slot(DelphiLiveStoredEvaluation evaluation) => new(Guid.NewGuid(), evaluation.Input.SessionId,
        evaluation.Input.Stock.SessionDate, evaluation.Input.BarEndUtc, "AAA", false, Guid.NewGuid(), "OperationalOnTime", true);
    private static DelphiLiveResearchOutcomeRevision Outcome(DelphiLiveExpectedResearchSlot slot, decimal raw) => new(Guid.NewGuid(), slot.SlotId,
        slot.BarEndUtc.AddDays(7), new(Guid.NewGuid(), "LiveObservationOutcomeV1", slot.AnchorObservationId!.Value, Guid.NewGuid(), "AAA", slot.TradingDate,
            slot.BarEndUtc, slot.BarEndUtc.AddMinutes(2), 100m, 100m, DelphiLiveOutcomeEvidenceBasket.ModelGrade,
            Enum.GetValues<DelphiLiveOutcomeHorizon>().Select(h => new DelphiLiveOutcomeHorizonResult(h, null, slot.TradingDate,
                DelphiLiveOutcomeMetric.Valid(raw), DelphiLiveOutcomeMetric.Valid(0m), DelphiLiveOutcomeMetric.Valid(raw),
                DelphiLiveOutcomeMetric.Valid(.03m), DelphiLiveOutcomeMetric.Valid(-.01m), DelphiLivePathOrdering.ExactIntradayOrder, [])).ToImmutableArray()), "");
    private static ReviewedTsxSessionCalendar Calendar() => new(new("Test", "Reviewed test calendar", Date.AddDays(-1), Date.AddDays(10),
        Enumerable.Range(-1, 12).Select(i => Date.AddDays(i)).ToArray()));
}
