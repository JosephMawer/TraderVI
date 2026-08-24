using System;

namespace Core.Calibration;

public sealed record CalibrationCoverageCounts(
    Guid OutcomeDefinitionId,
    string DefinitionName,
    int DefinitionVersion,
    int OfficialRuns,
    int TotalCohorts,
    int MaturedCohorts,
    int ExpectedCandidates,
    int ValidOutcomes,
    int DegradedOutcomes,
    int InvalidOutcomes,
    int PendingOutcomes);

public enum CalibrationCoverageState
{
    NoEvidence,
    Blocked,
    Degraded,
    Ready
}

public sealed record CalibrationCoverageScorecard(
    CalibrationCoverageCounts Counts,
    double CompletionCoverage,
    double UsableCoverage,
    bool PrimaryScoreAvailable,
    CalibrationCoverageState State);

public static class CalibrationCoverageCalculator
{
    public const double PrimaryCoverageFloor = 0.95;

    public static CalibrationCoverageScorecard Build(CalibrationCoverageCounts counts)
    {
        if (counts.OfficialRuns < 0 || counts.TotalCohorts < 0 || counts.MaturedCohorts < 0 ||
            counts.ExpectedCandidates < 0 || counts.ValidOutcomes < 0 || counts.DegradedOutcomes < 0 ||
            counts.InvalidOutcomes < 0 || counts.PendingOutcomes < 0)
            throw new ArgumentOutOfRangeException(nameof(counts), "Coverage counts cannot be negative.");

        int classified = counts.ValidOutcomes + counts.DegradedOutcomes + counts.InvalidOutcomes + counts.PendingOutcomes;
        if (classified != counts.ExpectedCandidates)
            throw new ArgumentException("Outcome classifications must equal the expected candidate count.", nameof(counts));

        if (counts.MaturedCohorts > counts.TotalCohorts)
            throw new ArgumentException("Matured cohorts cannot exceed total cohorts.", nameof(counts));

        if (counts.ExpectedCandidates == 0)
            return new CalibrationCoverageScorecard(counts, 0, 0, false, CalibrationCoverageState.NoEvidence);

        int completed = counts.ExpectedCandidates - counts.PendingOutcomes;
        int usable = counts.ValidOutcomes + counts.DegradedOutcomes;
        double completionCoverage = (double)completed / counts.ExpectedCandidates;
        double usableCoverage = (double)usable / counts.ExpectedCandidates;
        bool primaryAvailable = counts.MaturedCohorts > 0 && usableCoverage >= PrimaryCoverageFloor;

        CalibrationCoverageState state = !primaryAvailable
            ? CalibrationCoverageState.Blocked
            : counts.DegradedOutcomes > 0 || counts.InvalidOutcomes > 0 || counts.PendingOutcomes > 0
                ? CalibrationCoverageState.Degraded
                : CalibrationCoverageState.Ready;

        return new CalibrationCoverageScorecard(
            counts,
            completionCoverage,
            usableCoverage,
            primaryAvailable,
            state);
    }
}
