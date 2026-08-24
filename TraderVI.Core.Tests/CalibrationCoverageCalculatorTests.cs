using Core.Calibration;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public class CalibrationCoverageCalculatorTests
{
    [Fact]
    public void CompleteValidEvidenceIsReady()
    {
        var result = CalibrationCoverageCalculator.Build(Counts(
            totalCohorts: 10, maturedCohorts: 10, expected: 100, valid: 100));

        result.State.ShouldBe(CalibrationCoverageState.Ready);
        result.PrimaryScoreAvailable.ShouldBeTrue();
        result.UsableCoverage.ShouldBe(1);
        result.CompletionCoverage.ShouldBe(1);
    }

    [Fact]
    public void PrimaryScoreIsBlockedBelowCoverageFloor()
    {
        var result = CalibrationCoverageCalculator.Build(Counts(
            totalCohorts: 10, maturedCohorts: 9, expected: 100, valid: 94, pending: 6));

        result.State.ShouldBe(CalibrationCoverageState.Blocked);
        result.PrimaryScoreAvailable.ShouldBeFalse();
        result.UsableCoverage.ShouldBe(.94, tolerance: .0001);
    }

    [Fact]
    public void ReportIsDegradedButAvailableAtCoverageFloor()
    {
        var result = CalibrationCoverageCalculator.Build(Counts(
            totalCohorts: 10, maturedCohorts: 9, expected: 100, valid: 94, degraded: 1, invalid: 2, pending: 3));

        result.State.ShouldBe(CalibrationCoverageState.Degraded);
        result.PrimaryScoreAvailable.ShouldBeTrue();
        result.UsableCoverage.ShouldBe(.95, tolerance: .0001);
        result.CompletionCoverage.ShouldBe(.97, tolerance: .0001);
    }

    [Fact]
    public void EmptyDefinitionReportsNoEvidence()
    {
        var result = CalibrationCoverageCalculator.Build(Counts(
            totalCohorts: 0, maturedCohorts: 0, expected: 0, valid: 0));

        result.State.ShouldBe(CalibrationCoverageState.NoEvidence);
        result.PrimaryScoreAvailable.ShouldBeFalse();
    }

    [Fact]
    public void CompleteRowsRemainBlockedWithoutAMaturedCohort()
    {
        var result = CalibrationCoverageCalculator.Build(Counts(
            totalCohorts: 1, maturedCohorts: 0, expected: 100, valid: 100));

        result.State.ShouldBe(CalibrationCoverageState.Blocked);
        result.PrimaryScoreAvailable.ShouldBeFalse();
    }

    private static CalibrationCoverageCounts Counts(
        int totalCohorts,
        int maturedCohorts,
        int expected,
        int valid,
        int degraded = 0,
        int invalid = 0,
        int pending = 0) =>
        new(Guid.NewGuid(), "PredictionPath20", 1, "Prediction", totalCohorts, totalCohorts,
            maturedCohorts, expected, valid, degraded, invalid, pending);
}
