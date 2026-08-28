#nullable enable

using Core.Calibration;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class OfficialPredictionScorecardCalculatorTests
{
    [Fact]
    public void BuildsCalibrationRankingAndDiagnosticReports()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        OfficialPredictionEvidenceSet evidence = Evidence(
            [Run(run, cohort)],
            [
                Candidate(run, cohort, 1, probability: .9, occurred: true, return10: .10, obv: "Rising"),
                Candidate(run, cohort, 2, probability: .1, occurred: false, return10: -.10, obv: "Falling")
            ]);

        OfficialPredictionScorecard report = OfficialPredictionScorecardCalculator.Build(evidence);

        report.Coverage.State.ShouldBe(CalibrationCoverageState.Ready);
        ProbabilityCalibrationReport up = report.Models.Single(model => model.TaskType == "BinaryUp10");
        up.MetricsAvailable.ShouldBeTrue();
        up.BrierScore!.Value.ShouldBe(.01, .0000001);
        up.AreaUnderRocCurve!.Value.ShouldBe(1, .0000001);
        up.ExpectedCalibrationError!.Value.ShouldBe(.1, .0000001);
        up.TopDecileEventLift!.Value.ShouldBe(.5, .0000001);

        LensRankPerformanceReport continuation = report.Lenses.Single(lens => lens.Lens == "Continuation");
        continuation.SpearmanRankInformationCoefficient!.Value.ShouldBe(1, .0000001);
        LensRankSelectionReport top = continuation.Selections.Single(selection => selection.Selection == "Top1");
        top.MeanReturn10!.Value.ShouldBe(.10, .0000001);
        top.ReturnLiftVersusEligibleBaseline!.Value.ShouldBe(.10, .0000001);

        PredictionSliceReport rising = report.Slices.Single(slice =>
            slice.Dimension == "OBV" && slice.Value == "Rising");
        rising.MeanReturn10.ShouldBe(.10, .0000001);
        rising.UpEventRate.ShouldBe(1, .0000001);
        report.Slices.ShouldContain(slice =>
            slice.Dimension == "PublishedLens" && slice.Value == "Continuation");
    }

    [Fact]
    public void NestedWeightingPreventsRerunsFromInflatingModelMetrics()
    {
        DateTime firstCohort = new(2026, 8, 21);
        DateTime secondCohort = new(2026, 8, 24);
        Guid firstRun = Guid.NewGuid();
        Guid rerun = Guid.NewGuid();
        Guid secondRun = Guid.NewGuid();
        OfficialPredictionEvidenceSet evidence = Evidence(
            [Run(firstRun, firstCohort), Run(rerun, firstCohort), Run(secondRun, secondCohort)],
            [
                Candidate(firstRun, firstCohort, 1, probability: 1, occurred: true, return10: .01),
                Candidate(rerun, firstCohort, 2, probability: .5, occurred: true, return10: .01),
                Candidate(secondRun, secondCohort, 3, probability: 0, occurred: true, return10: .01)
            ]);

        ProbabilityCalibrationReport up = OfficialPredictionScorecardCalculator.Build(evidence)
            .Models.Single(model => model.TaskType == "BinaryUp10");

        // Cohort one: mean run Brier = (0 + .25) / 2 = .125.
        // Cohort two: Brier = 1. Equal cohort weight gives .5625.
        up.BrierScore!.Value.ShouldBe(.5625, .0000001);
    }

    [Fact]
    public void ProbabilityTiesUseStableCandidateIdentityNotFutureOutcome()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        List<OfficialPredictionCandidateEvidence> candidates = Enumerable.Range(1, 10)
            .Select(index => Candidate(
                run,
                cohort,
                index,
                probability: .5,
                occurred: index != 1,
                return10: .01))
            .ToList();

        ProbabilityCalibrationReport up = OfficialPredictionScorecardCalculator.Build(
                Evidence([Run(run, cohort)], candidates))
            .Models.Single(model => model.TaskType == "BinaryUp10");

        up.AreaUnderRocCurve!.Value.ShouldBe(.5, .0000001);
        up.TopDecileEventLift!.Value.ShouldBe(-.9, .0000001);
    }

    [Fact]
    public void IncompleteOutcomeCoverageBlocksEveryPerformanceSurface()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        List<OfficialPredictionCandidateEvidence> candidates = Enumerable.Range(1, 20)
            .Select(index => Candidate(run, cohort, index, .7, true, .01))
            .ToList();
        candidates[^1] = candidates[^1] with
        {
            MaturityState = null,
            OutcomeAuditState = null,
            OutcomeJson = null
        };
        candidates[^2] = candidates[^2] with
        {
            MaturityState = null,
            OutcomeAuditState = null,
            OutcomeJson = null
        };

        OfficialPredictionScorecard report = OfficialPredictionScorecardCalculator.Build(
            Evidence([Run(run, cohort)], candidates));

        report.Coverage.UsableCoverage.ShouldBe(.9, .0000001);
        report.Coverage.State.ShouldBe(CalibrationCoverageState.Blocked);
        report.Models.ShouldAllBe(model => !model.MetricsAvailable && model.BrierScore == null);
        report.Lenses.ShouldAllBe(lens => !lens.MetricsAvailable && lens.Selections.Count == 0);
        report.Slices.ShouldBeEmpty();
    }

    [Fact]
    public void WrongSessionOutcomeIsVisibleAsInvalidEvidence()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        OfficialPredictionCandidateEvidence candidate = Candidate(run, cohort, 1, .8, true, .02);
        PredictionOutcomeV1 outcome = Outcome(cohort.AddDays(1), true, .02);
        candidate = candidate with { OutcomeJson = JsonSerializer.Serialize(outcome) };

        OfficialPredictionScorecard report = OfficialPredictionScorecardCalculator.Build(
            Evidence([Run(run, cohort)], [candidate]));

        report.Coverage.Counts.InvalidOutcomes.ShouldBe(1);
        report.Coverage.State.ShouldBe(CalibrationCoverageState.Blocked);
    }

    [Fact]
    public void DuplicateCandidatesMixedPurposesAndDefinitionChangesAreRejected()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        OfficialPredictionCandidateEvidence candidate = Candidate(run, cohort, 1, .8, true, .02);
        OfficialPredictionEvidenceSet valid = Evidence([Run(run, cohort)], [candidate]);

        Should.Throw<ArgumentException>(() => OfficialPredictionScorecardCalculator.Build(
            valid with { Candidates = [candidate, candidate] }));
        Should.Throw<ArgumentException>(() => OfficialPredictionScorecardCalculator.Build(
            valid with
            {
                Runs = [Run(run, cohort) with { RunPurpose = nameof(CalibrationRunPurpose.ExploratoryReplay) }]
            }));
        Should.Throw<ArgumentException>(() => OfficialPredictionScorecardCalculator.Build(
            valid with
            {
                Definition = valid.Definition with { DefinitionVersion = 2 }
            }));
        Should.Throw<ArgumentException>(() => OfficialPredictionScorecardCalculator.Build(
            valid with
            {
                Definition = valid.Definition with { OutcomeDefinitionId = Guid.NewGuid() }
            }));
        Should.Throw<ArgumentException>(() => OfficialPredictionScorecardCalculator.Build(
            valid with
            {
                Lenses = valid.Lenses
                    .Select((lens, index) => index == 0 ? lens with { Lens = "Combined" } : lens)
                    .ToList()
            }));
    }

    [Fact]
    public void DuplicateLensRanksAreRejected()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        OfficialPredictionEvidenceSet evidence = Evidence(
            [Run(run, cohort)],
            [
                Candidate(run, cohort, 1, .8, true, .02),
                Candidate(run, cohort, 2, .7, false, -.01)
            ]);
        List<OfficialPredictionLensEvidence> lenses = evidence.Lenses.ToList();
        lenses[2] = lenses[2] with { Rank = 1 };

        Should.Throw<ArgumentException>(() => OfficialPredictionScorecardCalculator.Build(
            evidence with { Lenses = lenses }));
    }

    [Fact]
    public void CsvExportIsVersionedAndContainsEveryScorecardSurface()
    {
        DateTime cohort = new(2026, 8, 21);
        Guid run = Guid.NewGuid();
        OfficialPredictionScorecard report = OfficialPredictionScorecardCalculator.Build(
            Evidence(
                [Run(run, cohort)],
                [Candidate(run, cohort, 1, .8, true, .02)]));

        IReadOnlyList<OfficialPredictionScorecardCsvArtifact> artifacts =
            OfficialPredictionScorecardCsv.Build(report);

        artifacts.Count.ShouldBe(5);
        artifacts.ShouldAllBe(artifact => artifact.FileName.StartsWith("official-prediction-v1-"));
        artifacts.Select(artifact => artifact.FileName).Distinct().Count().ShouldBe(5);
        artifacts.Single(artifact => artifact.FileName.EndsWith("models.csv"))
            .Content.ShouldContain("brier_score");
        artifacts.Single(artifact => artifact.FileName.EndsWith("slices.csv"))
            .Content.ShouldContain("PublishedLens");
    }

    private static OfficialPredictionEvidenceSet Evidence(
        IReadOnlyList<OfficialPredictionRunEvidence> runs,
        IReadOnlyList<OfficialPredictionCandidateEvidence> candidates)
    {
        var lenses = new List<OfficialPredictionLensEvidence>();
        foreach (IGrouping<Guid, OfficialPredictionCandidateEvidence> run in candidates.GroupBy(candidate => candidate.RunId))
        {
            int rank = 1;
            foreach (OfficialPredictionCandidateEvidence candidate in run.OrderBy(candidate => candidate.Symbol))
            {
                lenses.Add(new OfficialPredictionLensEvidence(
                    candidate.CandidateId,
                    "Continuation",
                    true,
                    rank <= 25,
                    rank,
                    null));
                lenses.Add(new OfficialPredictionLensEvidence(
                    candidate.CandidateId,
                    "Breakout",
                    true,
                    rank <= 25,
                    rank,
                    null));
                rank++;
            }
        }

        return new OfficialPredictionEvidenceSet(
            new OfficialPredictionScorecardDefinition(
                new Guid("A72C01CB-9C83-45A6-9A72-CC49E67B9F5A"),
                "PredictionLabels10",
                1),
            runs,
            candidates,
            lenses);
    }

    private static OfficialPredictionRunEvidence Run(Guid id, DateTime cohort) => new(
        id,
        cohort.Date,
        nameof(CalibrationRunPurpose.OfficialPaper),
        nameof(CalibrationAuditState.Valid),
        "{\"regime\":{\"isBothBearish\":false,\"isAnyBenchmarkUptrend\":true}}");

    private static OfficialPredictionCandidateEvidence Candidate(
        Guid runId,
        DateTime cohort,
        int id,
        double probability,
        bool occurred,
        double return10,
        string obv = "Rising")
    {
        Guid candidateId = new(id, 0, 0, new byte[8]);
        return new OfficialPredictionCandidateEvidence(
            runId,
            cohort.Date,
            candidateId,
            $"TEST{id}",
            cohort.Date,
            10,
            10.5f,
            9.5f,
            10,
            1_000_000,
            probability,
            probability,
            probability,
            probability,
            .01,
            .1,
            obv,
            "{\"relativeStrength\":{\"sectorIndexSymbol\":\"^TTEN\"}}",
            nameof(CalibrationOutcomeMaturityState.Matured),
            nameof(CalibrationAuditState.Valid),
            JsonSerializer.Serialize(Outcome(cohort, occurred, return10)));
    }

    private static PredictionOutcomeV1 Outcome(DateTime cohort, bool occurred, double return10) => new(
        PredictionOutcomeCalculator.SchemaVersion,
        cohort.Date,
        PredictionOutcomeCalculator.LabelHorizon,
        .001,
        .005,
        return10,
        null,
        .01,
        return10 - .01,
        [
            new PredictionEventOutcome("BinaryUp10", "BinaryUp", occurred),
            new PredictionEventOutcome("BinaryDown10", "BinaryDown", occurred),
            new PredictionEventOutcome("VolExpansionRelative10", "VolExpansion", occurred),
            new PredictionEventOutcome("BreakoutEnhanced", "Breakout", occurred)
        ]);
}
