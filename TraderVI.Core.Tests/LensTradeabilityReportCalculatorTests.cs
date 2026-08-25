using Core.Calibration;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public class LensTradeabilityReportCalculatorTests
{
    [Fact]
    public void BuildsSeparateLensReportsAndExcludesNoEntryFromReturnAverages()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        var evidence = new[]
        {
            Entered(run, cohort, "Continuation", 1, .02),
            NoEntry(run, cohort, "Continuation", 2),
            Entered(run, cohort, "Breakout", 1, -.01)
        };

        var reports = LensTradeabilityReportCalculator.BuildReports(evidence);
        var continuation = reports.Single(x => x.Lens == "Continuation");
        var breakout = reports.Single(x => x.Lens == "Breakout");

        continuation.Coverage.State.ShouldBe(CalibrationCoverageState.Ready);
        continuation.Coverage.EnteredRecommendations.ShouldBe(1);
        continuation.Coverage.NoEntryRecommendations.ShouldBe(1);
        continuation.NoEntryRate!.Value.ShouldBe(.5, .0000001);
        continuation.Horizons[0].MeanNetReturn!.Value.ShouldBe(.02, .0000001);
        continuation.Horizons[0].MeanMfeReturn!.Value.ShouldBe(.05, .0000001);
        continuation.Horizons[0].MeanMaeReturn!.Value.ShouldBe(-.02, .0000001);

        breakout.Coverage.State.ShouldBe(CalibrationCoverageState.Ready);
        breakout.Horizons[0].MeanNetReturn!.Value.ShouldBe(-.01, .0000001);
        breakout.Horizons[0].ProfitableRate.ShouldBe(0);
    }

    [Fact]
    public void IncompleteCoverageBlocksPerformanceMetrics()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        var evidence = new[]
        {
            Entered(run, cohort, "Continuation", 1, .02),
            Pending(run, cohort, "Continuation", 2)
        };

        var report = LensTradeabilityReportCalculator.BuildReports(evidence)
            .Single(x => x.Lens == "Continuation");

        report.Coverage.State.ShouldBe(CalibrationCoverageState.Blocked);
        report.Coverage.UsableCoverage.ShouldBe(.5, .0000001);
        report.Coverage.MaturedCohorts.ShouldBe(0);
        report.NoEntryRate.ShouldBeNull();
        report.Horizons.ShouldBeEmpty();
    }

    [Fact]
    public void ReportingFloorAllowsDegradedReportWithVisibleInvalidEvidence()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        var evidence = Enumerable.Range(1, 19)
            .Select(rank => Entered(run, cohort, "Continuation", rank, .01))
            .Append(InvalidAudit(run, cohort, "Continuation", 20))
            .ToList();

        var report = LensTradeabilityReportCalculator.BuildReports(evidence)
            .Single(x => x.Lens == "Continuation");

        report.Coverage.UsableCoverage.ShouldBe(.95, .0000001);
        report.Coverage.CompletionCoverage.ShouldBe(1);
        report.Coverage.InvalidRecommendations.ShouldBe(1);
        report.Coverage.State.ShouldBe(CalibrationCoverageState.Degraded);
        report.Coverage.PrimaryScoreAvailable.ShouldBeTrue();
        report.Horizons.Count.ShouldBe(3);
    }

    [Fact]
    public void NestedAggregationPreventsRerunsFromAddingCohortWeight()
    {
        DateTime firstCohort = new(2026, 8, 21);
        DateTime secondCohort = new(2026, 8, 24);
        Guid firstRun = Guid.NewGuid();
        Guid rerun = Guid.NewGuid();
        Guid secondRun = Guid.NewGuid();
        var evidence = new[]
        {
            Entered(firstRun, firstCohort, "Continuation", 1, 0),
            Entered(rerun, firstCohort, "Continuation", 1, .10),
            Entered(rerun, firstCohort, "Continuation", 2, .30),
            Entered(secondRun, secondCohort, "Continuation", 1, .30)
        };

        var report = LensTradeabilityReportCalculator.BuildReports(evidence)
            .Single(x => x.Lens == "Continuation");

        report.Coverage.OfficialRuns.ShouldBe(3);
        report.Coverage.TotalCohorts.ShouldBe(2);
        report.Horizons[0].MeanNetReturn!.Value.ShouldBe(.20, .0000001);
    }

    [Fact]
    public void ConflictingTerminalOutcomeStatesAreInvalid()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        var entered = Entered(run, cohort, "Continuation", 1, .02);
        var evidence = new[]
        {
            entered with
            {
                MarkMaturityState = nameof(CalibrationOutcomeMaturityState.NoEntry)
            }
        };

        var report = LensTradeabilityReportCalculator.BuildReports(evidence)
            .Single(x => x.Lens == "Continuation");

        report.Coverage.InvalidRecommendations.ShouldBe(1);
        report.Coverage.CompletionCoverage.ShouldBe(1);
        report.Coverage.UsableCoverage.ShouldBe(0);
        report.Coverage.State.ShouldBe(CalibrationCoverageState.Blocked);
    }

    [Fact]
    public void DuplicateRunCandidateLensEvidenceIsRejected()
    {
        var row = Entered(Guid.NewGuid(), new DateTime(2026, 8, 21), "Continuation", 1, .02);

        Should.Throw<ArgumentException>(() =>
            LensTradeabilityReportCalculator.BuildReports(new[] { row, row }));
    }

    [Fact]
    public void OfficialAbstainingRunRemainsVisibleWithoutInventingRecommendations()
    {
        var evidence = new LensTradeabilityEvidenceSet(
            new[] { new LensTradeabilityRunEvidence(Guid.NewGuid(), new DateTime(2026, 8, 21)) },
            Array.Empty<LensTradeabilityEvidenceRow>());

        var reports = LensTradeabilityReportCalculator.BuildReports(evidence);

        reports.ShouldAllBe(x => x.Coverage.OfficialRuns == 1);
        reports.ShouldAllBe(x => x.Coverage.TotalCohorts == 1);
        reports.ShouldAllBe(x => x.Coverage.MaturedCohorts == 1);
        reports.ShouldAllBe(x => x.Coverage.ExpectedRecommendations == 0);
        reports.ShouldAllBe(x => x.Coverage.State == CalibrationCoverageState.NoEvidence);
    }

    [Fact]
    public void EmptyMaturedCohortCannotUnlockAPartialRecommendationCohort()
    {
        DateTime emptyCohort = new(2026, 8, 21);
        DateTime partialCohort = new(2026, 8, 24);
        Guid emptyRun = Guid.NewGuid();
        Guid partialRun = Guid.NewGuid();
        var recommendations = Enumerable.Range(1, 19)
            .Select(rank => Entered(partialRun, partialCohort, "Continuation", rank, .01))
            .Append(Pending(partialRun, partialCohort, "Continuation", 20))
            .ToList();
        var evidence = new LensTradeabilityEvidenceSet(
            new[]
            {
                new LensTradeabilityRunEvidence(emptyRun, emptyCohort),
                new LensTradeabilityRunEvidence(partialRun, partialCohort)
            },
            recommendations);

        var report = LensTradeabilityReportCalculator.BuildReports(evidence)
            .Single(x => x.Lens == "Continuation");

        report.Coverage.MaturedCohorts.ShouldBe(1);
        report.Coverage.UsableCoverage.ShouldBe(.95, .0000001);
        report.Coverage.PrimaryScoreAvailable.ShouldBeFalse();
        report.Coverage.State.ShouldBe(CalibrationCoverageState.Blocked);
    }

    private static LensTradeabilityEvidenceRow Entered(
        Guid runId,
        DateTime cohort,
        string lens,
        int rank,
        double netReturn)
    {
        DateTime entry = cohort.Date.AddDays(1);
        DateTime runStarted = DateTime.SpecifyKind(cohort.Date.AddHours(12), DateTimeKind.Utc);
        var mark = new SwingMarkToMarketOutcomeV1(
            1,
            cohort.Date,
            runStarted,
            entry,
            entry,
            0,
            100,
            100.25,
            200,
            .001,
            .0015,
            .001,
            .0015,
            Enumerable.Range(1, 3).Select(sessions => new SwingHorizonMark(
                sessions,
                entry.AddDays(sessions - 1),
                100 * (1 + netReturn),
                100 * (1 + netReturn) * .9975,
                netReturn + .005,
                netReturn,
                200,
                .005,
                netReturn - .005)).ToList());
        var excursion = new SwingExcursionOutcomeV1(
            1,
            cohort.Date,
            runStarted,
            entry,
            entry,
            0,
            100,
            Enumerable.Range(1, 3).Select(sessions => new SwingExcursionHorizonV1(
                sessions,
                entry.AddDays(sessions - 1),
                .05,
                entry,
                1,
                -.02,
                entry,
                1,
                SwingMarkToMarketOutcomeCalculator.SameSessionUnknown)).ToList());

        return Row(
            runId,
            cohort,
            lens,
            rank,
            nameof(CalibrationOutcomeMaturityState.Matured),
            nameof(CalibrationAuditState.Valid),
            JsonSerializer.Serialize(mark),
            nameof(CalibrationOutcomeMaturityState.Matured),
            nameof(CalibrationAuditState.Valid),
            JsonSerializer.Serialize(excursion));
    }

    private static LensTradeabilityEvidenceRow NoEntry(
        Guid runId,
        DateTime cohort,
        string lens,
        int rank)
    {
        var outcome = new NoEntrySwingOutcomeV1(
            1,
            cohort.Date,
            DateTime.SpecifyKind(cohort.Date.AddHours(12), DateTimeKind.Utc),
            cohort.Date.AddDays(1),
            3,
            "NoSymbolBarWithinEntryAllowance");
        string json = JsonSerializer.Serialize(outcome);
        return Row(
            runId,
            cohort,
            lens,
            rank,
            nameof(CalibrationOutcomeMaturityState.NoEntry),
            nameof(CalibrationAuditState.Valid),
            json,
            nameof(CalibrationOutcomeMaturityState.NoEntry),
            nameof(CalibrationAuditState.Valid),
            json);
    }

    private static LensTradeabilityEvidenceRow Pending(
        Guid runId,
        DateTime cohort,
        string lens,
        int rank) => Row(
            runId,
            cohort,
            lens,
            rank,
            null,
            null,
            null,
            null,
            null,
            null);

    private static LensTradeabilityEvidenceRow InvalidAudit(
        Guid runId,
        DateTime cohort,
        string lens,
        int rank) => Row(
            runId,
            cohort,
            lens,
            rank,
            nameof(CalibrationOutcomeMaturityState.Matured),
            nameof(CalibrationAuditState.Invalid),
            "{}",
            nameof(CalibrationOutcomeMaturityState.Matured),
            nameof(CalibrationAuditState.Invalid),
            "{}");

    private static LensTradeabilityEvidenceRow Row(
        Guid runId,
        DateTime cohort,
        string lens,
        int rank,
        string? markMaturity,
        string? markAudit,
        string? markJson,
        string? excursionMaturity,
        string? excursionAudit,
        string? excursionJson) => new(
            runId,
            cohort.Date,
            lens,
            rank,
            Guid.NewGuid(),
            $"TEST{rank}",
            markMaturity,
            markAudit,
            markJson,
            excursionMaturity,
            excursionAudit,
            excursionJson);
}
