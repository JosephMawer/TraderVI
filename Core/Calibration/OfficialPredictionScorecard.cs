#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Core.Calibration;

public sealed record OfficialPredictionScorecardDefinition(
    Guid OutcomeDefinitionId,
    string DefinitionName,
    int DefinitionVersion);

public sealed record OfficialPredictionRunEvidence(
    Guid RunId,
    DateTime MarketDataAsOf,
    string RunPurpose,
    string AuditState,
    string RunContextJson);

public sealed record OfficialPredictionCandidateEvidence(
    Guid RunId,
    DateTime MarketDataAsOf,
    Guid CandidateId,
    string Symbol,
    DateTime ObservationDate,
    float ObservationOpen,
    float ObservationHigh,
    float ObservationLow,
    float ObservationClose,
    long ObservationVolume,
    double? UpProbability,
    double? DownProbability,
    double? BreakoutProbability,
    double? VolExpansionProbability,
    double? RsCompositeScore,
    double? RsCompositeScoreZ,
    string? ObvState,
    string SnapshotJson,
    string? MaturityState,
    string? OutcomeAuditState,
    string? OutcomeJson);

public sealed record OfficialPredictionLensEvidence(
    Guid CandidateId,
    string Lens,
    bool IsEligible,
    bool IsPublished,
    int Rank,
    string? FirstFailedGate);

public sealed record OfficialPredictionEvidenceSet(
    OfficialPredictionScorecardDefinition Definition,
    IReadOnlyList<OfficialPredictionRunEvidence> Runs,
    IReadOnlyList<OfficialPredictionCandidateEvidence> Candidates,
    IReadOnlyList<OfficialPredictionLensEvidence> Lenses);

public sealed record ProbabilityReliabilityBucket(
    int Bucket,
    double LowerBound,
    double UpperBound,
    int Observations,
    int ContributingCohorts,
    double CohortWeight,
    double MeanProbability,
    double ObservedEventRate);

public sealed record ProbabilityDecileReport(
    int Decile,
    int Observations,
    int ContributingCohorts,
    double MeanProbability,
    double ObservedEventRate,
    double EventRateLiftVersusRunBaseline);

public sealed record ProbabilityCalibrationReport(
    string TaskType,
    int ExpectedCandidates,
    int UsablePredictions,
    int MissingOrInvalidPredictions,
    int ContributingCohorts,
    double PredictionCoverage,
    bool MetricsAvailable,
    double? BrierScore,
    double? AreaUnderRocCurve,
    double? ExpectedCalibrationError,
    double? TopDecileEventLift,
    IReadOnlyList<ProbabilityReliabilityBucket> Reliability,
    IReadOnlyList<ProbabilityDecileReport> ProbabilityDeciles);

public sealed record LensRankSelectionReport(
    string Selection,
    int SelectedObservations,
    int ContributingCohorts,
    double? MeanReturn10,
    double? MeanExcessReturn10,
    double? ReturnLiftVersusEligibleBaseline);

public sealed record LensRankPerformanceReport(
    string Lens,
    int EligibleObservations,
    int ContributingCohorts,
    bool MetricsAvailable,
    double? SpearmanRankInformationCoefficient,
    IReadOnlyList<LensRankSelectionReport> Selections);

public sealed record PredictionSliceReport(
    string Dimension,
    string Value,
    int Observations,
    int ContributingCohorts,
    double MeanReturn10,
    double MeanExcessReturn10,
    double UpEventRate,
    double DownEventRate,
    double BreakoutEventRate,
    double VolExpansionEventRate);

public sealed record OfficialPredictionScorecard(
    OfficialPredictionScorecardDefinition Definition,
    CalibrationCoverageScorecard Coverage,
    IReadOnlyList<ProbabilityCalibrationReport> Models,
    IReadOnlyList<LensRankPerformanceReport> Lenses,
    IReadOnlyList<PredictionSliceReport> Slices);

/// <summary>
/// Builds version-1 official prediction scorecards. Candidate observations are
/// averaged within runs, official reruns are averaged within MarketDataAsOf
/// cohorts, and cohorts receive equal weight.
/// </summary>
public static class OfficialPredictionScorecardCalculator
{
    public static readonly Guid PredictionLabels10DefinitionId =
        new("A72C01CB-9C83-45A6-9A72-CC49E67B9F5A");
    public const int SchemaVersion = 1;
    public const int ReliabilityBucketCount = 10;
    public const string ContinuationLens = "Continuation";
    public const string BreakoutLens = "Breakout";

    private const string OfficialPurpose = nameof(CalibrationRunPurpose.OfficialPaper);
    private static readonly string[] LensNames = [ContinuationLens, BreakoutLens];
    private static readonly ModelSpecification[] ModelSpecifications =
    [
        new("BinaryUp10", candidate => candidate.UpProbability),
        new("BinaryDown10", candidate => candidate.DownProbability),
        new("VolExpansionRelative10", candidate => candidate.VolExpansionProbability),
        new("BreakoutEnhanced", candidate => candidate.BreakoutProbability)
    ];

    private enum OutcomeState
    {
        Pending,
        Valid,
        Degraded,
        Invalid
    }

    private sealed record ModelSpecification(
        string TaskType,
        Func<OfficialPredictionCandidateEvidence, double?> Probability);

    private sealed record ClassifiedCandidate(
        OfficialPredictionCandidateEvidence Source,
        OutcomeState State,
        PredictionOutcomeV1? Outcome,
        IReadOnlyDictionary<string, bool> Events);

    private sealed record ModelObservation(
        Guid RunId,
        DateTime Cohort,
        Guid CandidateId,
        double Probability,
        double Event,
        double Weight = 0);

    private sealed record DecileObservation(
        Guid RunId,
        DateTime Cohort,
        int Decile,
        double Probability,
        double Event,
        double Lift);

    private sealed record LensObservation(
        Guid RunId,
        DateTime Cohort,
        int Rank,
        double Return10,
        double ExcessReturn10);

    private sealed record RunSelectionObservation(
        Guid RunId,
        DateTime Cohort,
        int Selected,
        double MeanReturn,
        double MeanExcessReturn,
        double Lift);

    private sealed record SliceObservation(
        Guid RunId,
        DateTime Cohort,
        string Dimension,
        string Value,
        double Return10,
        double ExcessReturn10,
        double Up,
        double Down,
        double Breakout,
        double VolExpansion);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static OfficialPredictionScorecard Build(OfficialPredictionEvidenceSet evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ValidateEvidenceSet(evidence);

        IReadOnlyDictionary<Guid, OfficialPredictionRunEvidence> runs =
            evidence.Runs.ToDictionary(run => run.RunId);
        IReadOnlyDictionary<Guid, IReadOnlyList<OfficialPredictionLensEvidence>> lenses =
            evidence.Lenses
                .GroupBy(lens => lens.CandidateId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<OfficialPredictionLensEvidence>)group.ToList());
        List<ClassifiedCandidate> classified = evidence.Candidates.Select(Classify).ToList();
        CalibrationCoverageScorecard coverage = BuildCoverage(evidence, classified);

        IReadOnlyList<ProbabilityCalibrationReport> models = ModelSpecifications
            .Select(model => BuildModelReport(model, classified, coverage.PrimaryScoreAvailable))
            .ToList();
        IReadOnlyList<LensRankPerformanceReport> lensReports = LensNames
            .Select(lens => BuildLensReport(lens, classified, lenses, coverage.PrimaryScoreAvailable))
            .ToList();
        IReadOnlyList<PredictionSliceReport> slices = coverage.PrimaryScoreAvailable
            ? BuildSlices(classified, runs, lenses)
            : [];

        return new OfficialPredictionScorecard(
            evidence.Definition,
            coverage,
            models,
            lensReports,
            slices);
    }

    private static void ValidateEvidenceSet(OfficialPredictionEvidenceSet evidence)
    {
        if (evidence.Definition is null || evidence.Runs is null ||
            evidence.Candidates is null || evidence.Lenses is null)
            throw new ArgumentException("Definition, run, candidate, and lens evidence are required.", nameof(evidence));
        if (evidence.Definition.OutcomeDefinitionId != PredictionLabels10DefinitionId ||
            evidence.Definition.DefinitionName != "PredictionLabels10" ||
            evidence.Definition.DefinitionVersion != 1)
            throw new ArgumentException("The scorecard requires PredictionLabels10 version 1.", nameof(evidence));

        var duplicateRun = evidence.Runs.GroupBy(run => run.RunId).FirstOrDefault(group => group.Count() > 1);
        if (duplicateRun is not null)
            throw new ArgumentException("Duplicate official runs are not allowed.", nameof(evidence));
        if (evidence.Runs.Any(run =>
                run.RunId == Guid.Empty ||
                run.MarketDataAsOf == default ||
                run.RunPurpose != OfficialPurpose ||
                run.AuditState is not (nameof(CalibrationAuditState.Valid) or nameof(CalibrationAuditState.Degraded))))
            throw new ArgumentException("Only identified, non-invalid OfficialPaper runs are allowed.", nameof(evidence));

        var runDates = evidence.Runs.ToDictionary(run => run.RunId, run => run.MarketDataAsOf.Date);
        var duplicateCandidate = evidence.Candidates
            .GroupBy(candidate => candidate.CandidateId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateCandidate is not null)
            throw new ArgumentException("Duplicate candidate evidence is not allowed.", nameof(evidence));
        if (evidence.Candidates.Any(candidate =>
                candidate.CandidateId == Guid.Empty ||
                !runDates.TryGetValue(candidate.RunId, out DateTime cohort) ||
                cohort != candidate.MarketDataAsOf.Date ||
                cohort != candidate.ObservationDate.Date))
            throw new ArgumentException("Every candidate must match one official run and cohort.", nameof(evidence));

        var candidateIds = evidence.Candidates.Select(candidate => candidate.CandidateId).ToHashSet();
        if (evidence.Lenses.Any(lens => !candidateIds.Contains(lens.CandidateId)))
            throw new ArgumentException("Every lens row must reference an included candidate.", nameof(evidence));
        if (evidence.Lenses.Any(lens => !LensNames.Contains(lens.Lens, StringComparer.Ordinal)))
            throw new ArgumentException("Only Continuation and Breakout lens rows are allowed.", nameof(evidence));
        var duplicateLens = evidence.Lenses
            .GroupBy(lens => (lens.CandidateId, lens.Lens))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateLens is not null)
            throw new ArgumentException("Duplicate candidate/lens evidence is not allowed.", nameof(evidence));
        if (evidence.Lenses.Any(lens => lens.Rank <= 0))
            throw new ArgumentException("Lens ranks must be positive.", nameof(evidence));
        if (evidence.Candidates.Any(candidate =>
                !evidence.Lenses
                    .Where(lens => lens.CandidateId == candidate.CandidateId)
                    .Select(lens => lens.Lens)
                    .OrderBy(lens => lens, StringComparer.Ordinal)
                    .SequenceEqual(LensNames.OrderBy(lens => lens, StringComparer.Ordinal))))
            throw new ArgumentException("Every candidate requires exactly one Continuation and one Breakout row.", nameof(evidence));
        var candidateRuns = evidence.Candidates.ToDictionary(candidate => candidate.CandidateId, candidate => candidate.RunId);
        var duplicateRank = evidence.Lenses
            .GroupBy(lens => (candidateRuns[lens.CandidateId], lens.Lens, lens.Rank))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRank is not null)
            throw new ArgumentException("Lens ranks must be unique within each run.", nameof(evidence));
    }

    private static CalibrationCoverageScorecard BuildCoverage(
        OfficialPredictionEvidenceSet evidence,
        IReadOnlyList<ClassifiedCandidate> classified)
    {
        int valid = classified.Count(candidate => candidate.State == OutcomeState.Valid);
        int degraded = classified.Count(candidate => candidate.State == OutcomeState.Degraded);
        int invalid = classified.Count(candidate => candidate.State == OutcomeState.Invalid);
        int pending = classified.Count(candidate => candidate.State == OutcomeState.Pending);
        var cohorts = evidence.Runs.Select(run => run.MarketDataAsOf.Date).Distinct().ToList();
        int maturedCohorts = cohorts.Count(cohort => classified
            .Where(candidate => candidate.Source.MarketDataAsOf.Date == cohort)
            .All(candidate => candidate.State != OutcomeState.Pending));
        int maturedRecommendationCohorts = cohorts.Count(cohort =>
        {
            var rows = classified
                .Where(candidate => candidate.Source.MarketDataAsOf.Date == cohort)
                .ToList();
            return rows.Count > 0 && rows.All(candidate => candidate.State != OutcomeState.Pending);
        });

        var counts = new CalibrationCoverageCounts(
            evidence.Definition.OutcomeDefinitionId,
            evidence.Definition.DefinitionName,
            evidence.Definition.DefinitionVersion,
            "Prediction",
            evidence.Runs.Count,
            cohorts.Count,
            maturedCohorts,
            classified.Count,
            valid,
            degraded,
            invalid,
            pending);
        CalibrationCoverageScorecard coverage = CalibrationCoverageCalculator.Build(counts);
        return coverage.PrimaryScoreAvailable && maturedRecommendationCohorts == 0
            ? coverage with
            {
                PrimaryScoreAvailable = false,
                State = CalibrationCoverageState.Blocked
            }
            : coverage;
    }

    private static ClassifiedCandidate Classify(OfficialPredictionCandidateEvidence source)
    {
        if (string.IsNullOrWhiteSpace(source.Symbol) ||
            source.ObservationClose <= 0 || source.ObservationVolume < 0)
            return Invalid(source);
        if (source.MaturityState is null or nameof(CalibrationOutcomeMaturityState.Pending))
            return new ClassifiedCandidate(source, OutcomeState.Pending, null, EmptyEvents());
        if (source.MaturityState != nameof(CalibrationOutcomeMaturityState.Matured) ||
            source.OutcomeAuditState is not (nameof(CalibrationAuditState.Valid) or nameof(CalibrationAuditState.Degraded)) ||
            string.IsNullOrWhiteSpace(source.OutcomeJson))
            return Invalid(source);

        try
        {
            PredictionOutcomeV1? outcome = JsonSerializer.Deserialize<PredictionOutcomeV1>(source.OutcomeJson, JsonOptions);
            if (!ValidOutcome(source, outcome))
                return Invalid(source);
            IReadOnlyDictionary<string, bool> events = outcome!.Events.ToDictionary(item => item.TaskType, item => item.EventOccurred);
            return new ClassifiedCandidate(
                source,
                source.OutcomeAuditState == nameof(CalibrationAuditState.Degraded)
                    ? OutcomeState.Degraded
                    : OutcomeState.Valid,
                outcome,
                events);
        }
        catch (JsonException)
        {
            return Invalid(source);
        }
        catch (ArgumentException)
        {
            return Invalid(source);
        }
    }

    private static bool ValidOutcome(
        OfficialPredictionCandidateEvidence source,
        PredictionOutcomeV1? outcome)
    {
        if (outcome is null ||
            outcome.SchemaVersion != PredictionOutcomeCalculator.SchemaVersion ||
            outcome.ObservationDate.Date != source.MarketDataAsOf.Date ||
            outcome.MaturedSessions < PredictionOutcomeCalculator.LabelHorizon ||
            outcome.Return10 is not double return10 ||
            outcome.XiuReturn10 is not double xiuReturn10 ||
            outcome.ExcessReturn10 is not double excessReturn10 ||
            !Finite(return10) || !Finite(xiuReturn10) || !Finite(excessReturn10) ||
            System.Math.Abs((return10 - xiuReturn10) - excessReturn10) > 0.0000001 ||
            outcome.Events is null)
            return false;

        string[] taskTypes = outcome.Events.Select(item => item.TaskType).ToArray();
        return taskTypes.Length == ModelSpecifications.Length &&
               taskTypes.Distinct(StringComparer.Ordinal).Count() == taskTypes.Length &&
               ModelSpecifications.All(model => taskTypes.Contains(model.TaskType, StringComparer.Ordinal));
    }

    private static ProbabilityCalibrationReport BuildModelReport(
        ModelSpecification model,
        IReadOnlyList<ClassifiedCandidate> classified,
        bool primaryScoreAvailable)
    {
        List<ModelObservation> observations = classified
            .Where(candidate => candidate.State is OutcomeState.Valid or OutcomeState.Degraded)
            .Select(candidate =>
            {
                double? probability = model.Probability(candidate.Source);
                return probability is double value && Finite(value) && value is >= 0 and <= 1 &&
                       candidate.Events.TryGetValue(model.TaskType, out bool occurred)
                    ? new ModelObservation(
                        candidate.Source.RunId,
                        candidate.Source.MarketDataAsOf.Date,
                        candidate.Source.CandidateId,
                        value,
                        occurred ? 1 : 0)
                    : null;
            })
            .Where(observation => observation is not null)
            .Cast<ModelObservation>()
            .ToList();

        int expected = classified.Count;
        double predictionCoverage = expected == 0 ? 0 : (double)observations.Count / expected;
        bool metricsAvailable = primaryScoreAvailable &&
            predictionCoverage >= CalibrationCoverageCalculator.PrimaryCoverageFloor &&
            observations.Count > 0;
        if (!metricsAvailable)
        {
            return new ProbabilityCalibrationReport(
                model.TaskType,
                expected,
                observations.Count,
                expected - observations.Count,
                observations.Select(item => item.Cohort).Distinct().Count(),
                predictionCoverage,
                false,
                null,
                null,
                null,
                null,
                [],
                []);
        }

        List<ModelObservation> weighted = ApplyNestedWeights(observations);
        double brier = weighted.Sum(item => item.Weight * System.Math.Pow(item.Probability - item.Event, 2));
        IReadOnlyList<ProbabilityReliabilityBucket> reliability = BuildReliability(weighted);
        double calibrationError = reliability.Sum(bucket =>
            bucket.CohortWeight * System.Math.Abs(bucket.MeanProbability - bucket.ObservedEventRate));
        IReadOnlyList<ProbabilityDecileReport> deciles = BuildProbabilityDeciles(observations);

        return new ProbabilityCalibrationReport(
            model.TaskType,
            expected,
            observations.Count,
            expected - observations.Count,
            observations.Select(item => item.Cohort).Distinct().Count(),
            predictionCoverage,
            true,
            brier,
            CohortWeightedSupportedMetric(observations, AreaUnderRocCurve),
            calibrationError,
            deciles.SingleOrDefault(decile => decile.Decile == 1)?.EventRateLiftVersusRunBaseline,
            reliability,
            deciles);
    }

    private static List<ModelObservation> ApplyNestedWeights(IReadOnlyList<ModelObservation> observations)
    {
        int cohortCount = observations.Select(item => item.Cohort).Distinct().Count();
        var weighted = new List<ModelObservation>(observations.Count);
        foreach (var cohort in observations.GroupBy(item => item.Cohort))
        {
            int runCount = cohort.Select(item => item.RunId).Distinct().Count();
            foreach (var run in cohort.GroupBy(item => item.RunId))
            {
                double weight = 1.0 / cohortCount / runCount / run.Count();
                weighted.AddRange(run.Select(item => item with { Weight = weight }));
            }
        }

        return weighted;
    }

    private static IReadOnlyList<ProbabilityReliabilityBucket> BuildReliability(
        IReadOnlyList<ModelObservation> weighted)
    {
        return weighted
            .GroupBy(item => ProbabilityBucket(item.Probability))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                double weight = group.Sum(item => item.Weight);
                return new ProbabilityReliabilityBucket(
                    group.Key,
                    (group.Key - 1.0) / ReliabilityBucketCount,
                    group.Key / (double)ReliabilityBucketCount,
                    group.Count(),
                    group.Select(item => item.Cohort).Distinct().Count(),
                    weight,
                    group.Sum(item => item.Weight * item.Probability) / weight,
                    group.Sum(item => item.Weight * item.Event) / weight);
            })
            .ToList();
    }

    private static int ProbabilityBucket(double probability) =>
        System.Math.Min(ReliabilityBucketCount, (int)(probability * ReliabilityBucketCount) + 1);

    private static IReadOnlyList<ProbabilityDecileReport> BuildProbabilityDeciles(
        IReadOnlyList<ModelObservation> observations)
    {
        var rows = new List<DecileObservation>(observations.Count);
        foreach (var run in observations.GroupBy(item => (item.RunId, item.Cohort)))
        {
            List<ModelObservation> ordered = run
                .OrderByDescending(item => item.Probability)
                .ThenBy(item => item.CandidateId)
                .ToList();
            double baseline = ordered.Average(item => item.Event);
            for (int index = 0; index < ordered.Count; index++)
            {
                ModelObservation item = ordered[index];
                int decile = System.Math.Min(10, (int)System.Math.Floor(index * 10.0 / ordered.Count) + 1);
                rows.Add(new DecileObservation(
                    item.RunId,
                    item.Cohort,
                    decile,
                    item.Probability,
                    item.Event,
                    item.Event - baseline));
            }
        }

        return rows.GroupBy(row => row.Decile)
            .OrderBy(group => group.Key)
            .Select(group => new ProbabilityDecileReport(
                group.Key,
                group.Count(),
                group.Select(item => item.Cohort).Distinct().Count(),
                CohortWeightedAverage(group.ToList(), item => item.Probability)!.Value,
                CohortWeightedAverage(group.ToList(), item => item.Event)!.Value,
                CohortWeightedAverage(group.ToList(), item => item.Lift)!.Value))
            .ToList();
    }

    private static LensRankPerformanceReport BuildLensReport(
        string lens,
        IReadOnlyList<ClassifiedCandidate> classified,
        IReadOnlyDictionary<Guid, IReadOnlyList<OfficialPredictionLensEvidence>> lenses,
        bool primaryScoreAvailable)
    {
        List<LensObservation> observations = classified
            .Where(candidate => candidate.State is OutcomeState.Valid or OutcomeState.Degraded)
            .Select(candidate =>
            {
                OfficialPredictionLensEvidence? lensEvidence = lenses
                    .GetValueOrDefault(candidate.Source.CandidateId)?
                    .SingleOrDefault(item => item.Lens == lens);
                return lensEvidence is { IsEligible: true } &&
                       candidate.Outcome?.Return10 is double return10 &&
                       candidate.Outcome.ExcessReturn10 is double excessReturn10
                    ? new LensObservation(
                        candidate.Source.RunId,
                        candidate.Source.MarketDataAsOf.Date,
                        lensEvidence.Rank,
                        return10,
                        excessReturn10)
                    : null;
            })
            .Where(observation => observation is not null)
            .Cast<LensObservation>()
            .ToList();

        if (!primaryScoreAvailable || observations.Count == 0)
        {
            return new LensRankPerformanceReport(
                lens,
                observations.Count,
                observations.Select(item => item.Cohort).Distinct().Count(),
                false,
                null,
                []);
        }

        var selections = new List<LensRankSelectionReport>
        {
            BuildSelection("Top1", observations, rows => rows.Take(1).ToList()),
            BuildSelection("Top3", observations, rows => rows.Take(3).ToList()),
            BuildSelection("Top5", observations, rows => rows.Take(5).ToList()),
            BuildSelection(
                "TopDecile",
                observations,
                rows => rows.Take(System.Math.Max(1, (int)System.Math.Ceiling(rows.Count * 0.10))).ToList())
        };
        return new LensRankPerformanceReport(
            lens,
            observations.Count,
            observations.Select(item => item.Cohort).Distinct().Count(),
            true,
            CohortWeightedSupportedMetric(
                observations,
                rows => Spearman(
                    rows.Select(item => -(double)item.Rank).ToList(),
                    rows.Select(item => item.Return10).ToList())),
            selections);
    }

    private static LensRankSelectionReport BuildSelection(
        string name,
        IReadOnlyList<LensObservation> observations,
        Func<IReadOnlyList<LensObservation>, IReadOnlyList<LensObservation>> selector)
    {
        var runValues = new List<RunSelectionObservation>();
        foreach (var run in observations.GroupBy(item => (item.RunId, item.Cohort)))
        {
            List<LensObservation> ordered = run.OrderBy(item => item.Rank).ToList();
            IReadOnlyList<LensObservation> selected = selector(ordered);
            if (selected.Count == 0) continue;
            double baseline = ordered.Average(item => item.Return10);
            double meanReturn = selected.Average(item => item.Return10);
            runValues.Add(new RunSelectionObservation(
                run.Key.RunId,
                run.Key.Cohort,
                selected.Count,
                meanReturn,
                selected.Average(item => item.ExcessReturn10),
                meanReturn - baseline));
        }

        return new LensRankSelectionReport(
            name,
            runValues.Sum(item => item.Selected),
            runValues.Select(item => item.Cohort).Distinct().Count(),
            CohortWeightedAverage(runValues, item => item.MeanReturn),
            CohortWeightedAverage(runValues, item => item.MeanExcessReturn),
            CohortWeightedAverage(runValues, item => item.Lift));
    }

    private static IReadOnlyList<PredictionSliceReport> BuildSlices(
        IReadOnlyList<ClassifiedCandidate> classified,
        IReadOnlyDictionary<Guid, OfficialPredictionRunEvidence> runs,
        IReadOnlyDictionary<Guid, IReadOnlyList<OfficialPredictionLensEvidence>> lenses)
    {
        var observations = new List<SliceObservation>();
        foreach (ClassifiedCandidate candidate in classified.Where(item =>
                     item.State is OutcomeState.Valid or OutcomeState.Degraded))
        {
            OfficialPredictionCandidateEvidence source = candidate.Source;
            PredictionOutcomeV1 outcome = candidate.Outcome!;
            double return10 = outcome.Return10!.Value;
            double excessReturn10 = outcome.ExcessReturn10!.Value;
            double up = candidate.Events["BinaryUp10"] ? 1 : 0;
            double down = candidate.Events["BinaryDown10"] ? 1 : 0;
            double breakout = candidate.Events["BreakoutEnhanced"] ? 1 : 0;
            double volExpansion = candidate.Events["VolExpansionRelative10"] ? 1 : 0;

            void Add(string dimension, string value) => observations.Add(new SliceObservation(
                source.RunId,
                source.MarketDataAsOf.Date,
                dimension,
                string.IsNullOrWhiteSpace(value) ? "Unavailable" : value,
                return10,
                excessReturn10,
                up,
                down,
                breakout,
                volExpansion));

            Add("OBV", source.ObvState ?? "Unavailable");
            Add("Regime", ReadRegime(runs[source.RunId].RunContextJson));
            Add("Sector", ReadSector(source.SnapshotJson));
            Add("ObservationDollarVolume", DollarVolumeBucket(source));
            Add("ObservationRange", ObservationRangeBucket(source));

            foreach (OfficialPredictionLensEvidence lens in lenses.GetValueOrDefault(source.CandidateId) ?? [])
            {
                Add(
                    $"{lens.Lens}Gate",
                    lens.IsEligible
                        ? "Pass"
                        : $"Fail:{lens.FirstFailedGate ?? "Unspecified"}");
                if (lens.IsPublished)
                    Add("PublishedLens", lens.Lens);
            }
        }

        return observations
            .GroupBy(item => (item.Dimension, item.Value))
            .OrderBy(group => group.Key.Dimension, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Value, StringComparer.Ordinal)
            .Select(group => new PredictionSliceReport(
                group.Key.Dimension,
                group.Key.Value,
                group.Count(),
                group.Select(item => item.Cohort).Distinct().Count(),
                CohortWeightedAverage(group.ToList(), item => item.Return10)!.Value,
                CohortWeightedAverage(group.ToList(), item => item.ExcessReturn10)!.Value,
                CohortWeightedAverage(group.ToList(), item => item.Up)!.Value,
                CohortWeightedAverage(group.ToList(), item => item.Down)!.Value,
                CohortWeightedAverage(group.ToList(), item => item.Breakout)!.Value,
                CohortWeightedAverage(group.ToList(), item => item.VolExpansion)!.Value))
            .ToList();
    }

    private static string DollarVolumeBucket(OfficialPredictionCandidateEvidence candidate)
    {
        double dollarVolume = candidate.ObservationClose * candidate.ObservationVolume;
        return dollarVolume < 1_000_000 ? "Low:<$1m"
            : dollarVolume < 5_000_000 ? "Medium:$1m-$5m"
            : "High:>=$5m";
    }

    private static string ObservationRangeBucket(OfficialPredictionCandidateEvidence candidate)
    {
        if (candidate.ObservationClose <= 0 || candidate.ObservationHigh < candidate.ObservationLow)
            return "Unavailable";
        double range = (candidate.ObservationHigh - candidate.ObservationLow) / candidate.ObservationClose;
        return range < 0.02 ? "Low:<2%"
            : range < 0.05 ? "Medium:2%-5%"
            : "High:>=5%";
    }

    private static string ReadRegime(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!TryGetProperty(document.RootElement, "regime", out JsonElement regime) ||
                regime.ValueKind != JsonValueKind.Object)
                return "Unavailable";
            bool hasBearish = TryGetBoolean(regime, "isBothBearish", out bool bearish);
            bool hasAnyUptrend = TryGetBoolean(regime, "isAnyBenchmarkUptrend", out bool anyUptrend);
            bool hasXiuUptrend = TryGetBoolean(regime, "isBenchmarkUptrend", out bool xiuUptrend);
            bool hasSpyUptrend = TryGetBoolean(regime, "isSpyUptrend", out bool spyUptrend);
            bool hasXiuPositive = TryGetBoolean(regime, "isBenchmark20dPositive", out bool xiuPositive);
            bool hasSpyPositive = TryGetBoolean(regime, "isSpy20dPositive", out bool spyPositive);
            if ((hasBearish && bearish) ||
                (!hasBearish && hasXiuUptrend && hasSpyUptrend && hasXiuPositive && hasSpyPositive &&
                 !xiuUptrend && !spyUptrend && !xiuPositive && !spyPositive))
                return "Bearish";
            if ((hasAnyUptrend && anyUptrend) ||
                (!hasAnyUptrend && ((hasXiuUptrend && xiuUptrend) || (hasSpyUptrend && spyUptrend))))
                return "Bullish";
            return "Mixed";
        }
        catch (JsonException)
        {
            return "Unavailable";
        }
    }

    private static string ReadSector(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return TryGetProperty(document.RootElement, "relativeStrength", out JsonElement relativeStrength) &&
                   relativeStrength.ValueKind == JsonValueKind.Object &&
                   TryGetProperty(relativeStrength, "sectorIndexSymbol", out JsonElement sector) &&
                   sector.ValueKind == JsonValueKind.String
                ? sector.GetString() ?? "Unavailable"
                : "Unavailable";
        }
        catch (JsonException)
        {
            return "Unavailable";
        }
    }

    private static bool TryGetBoolean(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!TryGetProperty(element, name, out JsonElement property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static double? CohortWeightedSupportedMetric<T>(
        IReadOnlyList<T> rows,
        Func<IReadOnlyList<T>, double?> perRun)
        where T : notnull
    {
        var runValues = rows
            .GroupBy(RunKey)
            .Select(group => new
            {
                group.Key.Cohort,
                Value = perRun(group.ToList())
            })
            .Where(item => item.Value.HasValue)
            .ToList();
        if (runValues.Count == 0) return null;
        return runValues
            .GroupBy(item => item.Cohort)
            .Select(group => group.Average(item => item.Value!.Value))
            .Average();
    }

    private static double? CohortWeightedAverage<T>(
        IReadOnlyList<T> rows,
        Func<T, double> selector)
        where T : notnull
    {
        if (rows.Count == 0) return null;
        return rows
            .GroupBy(RunKey)
            .Select(group => new
            {
                group.Key.Cohort,
                Value = group.Average(selector)
            })
            .GroupBy(item => item.Cohort)
            .Select(group => group.Average(item => item.Value))
            .Average();
    }

    private static (Guid RunId, DateTime Cohort) RunKey<T>(T row) => row switch
    {
        ModelObservation item => (item.RunId, item.Cohort),
        DecileObservation item => (item.RunId, item.Cohort),
        LensObservation item => (item.RunId, item.Cohort),
        RunSelectionObservation item => (item.RunId, item.Cohort),
        SliceObservation item => (item.RunId, item.Cohort),
        _ => throw new ArgumentException($"Unsupported cohort row type {typeof(T).Name}.", nameof(row))
    };

    private static double? AreaUnderRocCurve(IReadOnlyList<ModelObservation> rows)
    {
        int positives = rows.Count(item => item.Event == 1);
        int negatives = rows.Count - positives;
        if (positives == 0 || negatives == 0) return null;
        IReadOnlyList<double> ranks = AverageRanks(rows.Select(item => item.Probability).ToList());
        double positiveRankSum = rows
            .Select((item, index) => new { item.Event, Rank = ranks[index] })
            .Where(item => item.Event == 1)
            .Sum(item => item.Rank);
        return (positiveRankSum - positives * (positives + 1) / 2.0) / (positives * negatives);
    }

    private static double? Spearman(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        if (first.Count != second.Count || first.Count < 2) return null;
        IReadOnlyList<double> firstRanks = AverageRanks(first);
        IReadOnlyList<double> secondRanks = AverageRanks(second);
        double firstMean = firstRanks.Average();
        double secondMean = secondRanks.Average();
        double covariance = 0, firstVariance = 0, secondVariance = 0;
        for (int index = 0; index < firstRanks.Count; index++)
        {
            double firstDelta = firstRanks[index] - firstMean;
            double secondDelta = secondRanks[index] - secondMean;
            covariance += firstDelta * secondDelta;
            firstVariance += firstDelta * firstDelta;
            secondVariance += secondDelta * secondDelta;
        }

        double denominator = System.Math.Sqrt(firstVariance * secondVariance);
        return denominator == 0 ? null : covariance / denominator;
    }

    private static IReadOnlyList<double> AverageRanks(IReadOnlyList<double> values)
    {
        var indexed = values
            .Select((value, index) => new { value, index })
            .OrderBy(item => item.value)
            .ToList();
        var ranks = new double[values.Count];
        int start = 0;
        while (start < indexed.Count)
        {
            int end = start;
            while (end + 1 < indexed.Count && indexed[end + 1].value == indexed[start].value)
                end++;
            double rank = (start + 1 + end + 1) / 2.0;
            for (int index = start; index <= end; index++)
                ranks[indexed[index].index] = rank;
            start = end + 1;
        }

        return ranks;
    }

    private static ClassifiedCandidate Invalid(OfficialPredictionCandidateEvidence source) =>
        new(source, OutcomeState.Invalid, null, EmptyEvents());

    private static IReadOnlyDictionary<string, bool> EmptyEvents() =>
        new Dictionary<string, bool>(StringComparer.Ordinal);

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
