#nullable enable

using Core.Calibration;
using Core.Db;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TraderVI.WPF.Viewmodels;

public sealed class ScorecardsViewModel : INotifyPropertyChanged
{
    private bool isLoading;
    private string status = "Load official prediction evidence to see the calibration scorecards.";
    private Brush statusBrush = Brushes.SlateGray;
    private string coverageState = "—";
    private string usableCoverage = "—";
    private string cohortCount = "—";
    private string candidateCount = "—";
    private string evidenceIdentity = "Strategy identity has not been loaded.";
    private string readinessExplanation =
        "Metrics remain hidden until at least 95% of expected official outcomes are usable.";

    public ObservableCollection<ScorecardModelRow> Models { get; } = [];
    public ObservableCollection<ScorecardReliabilityRow> Reliability { get; } = [];
    public ObservableCollection<ScorecardDecileRow> Deciles { get; } = [];
    public ObservableCollection<ScorecardLensRow> Lenses { get; } = [];
    public ObservableCollection<ScorecardSliceRow> Slices { get; } = [];

    public bool IsLoading { get => isLoading; private set => Set(ref isLoading, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public Brush StatusBrush { get => statusBrush; private set => Set(ref statusBrush, value); }
    public string CoverageState { get => coverageState; private set => Set(ref coverageState, value); }
    public string UsableCoverage { get => usableCoverage; private set => Set(ref usableCoverage, value); }
    public string CohortCount { get => cohortCount; private set => Set(ref cohortCount, value); }
    public string CandidateCount { get => candidateCount; private set => Set(ref candidateCount, value); }
    public string EvidenceIdentity { get => evidenceIdentity; private set => Set(ref evidenceIdentity, value); }
    public string ReadinessExplanation { get => readinessExplanation; private set => Set(ref readinessExplanation, value); }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
            return;

        IsLoading = true;
        Status = "Reading immutable official Delphi predictions and outcomes…";
        StatusBrush = Brushes.SlateGray;
        try
        {
            var repository = new CalibrationOutcomeRepository();
            OfficialEvidenceIdentity identity =
                await repository.GetActiveOfficialEvidenceIdentityAsync(cancellationToken);
            OfficialPredictionEvidenceSet evidence =
                await repository.GetOfficialPredictionScorecardEvidenceAsync(identity, cancellationToken);
            OfficialPredictionScorecard report =
                OfficialPredictionScorecardCalculator.Build(evidence);
            Apply(report);
            Status = report.Coverage.PrimaryScoreAvailable
                ? "Official scorecards loaded · descriptive evidence only · Delphi unchanged"
                : "Official evidence loaded · advanced metrics are waiting for sufficient matured coverage";
            StatusBrush = report.Coverage.PrimaryScoreAvailable
                ? Brushes.MediumSeaGreen
                : Brushes.Goldenrod;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Scorecard refresh cancelled";
            StatusBrush = Brushes.SlateGray;
        }
        catch (Exception ex)
        {
            ClearRows();
            CoverageState = "Unavailable";
            EvidenceIdentity = "Official strategy identity unavailable.";
            Status = $"Scorecards unavailable · {ex.GetType().Name}: {ex.Message}";
            StatusBrush = Brushes.IndianRed;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply(OfficialPredictionScorecard report)
    {
        CalibrationCoverageScorecard coverage = report.Coverage;
        EvidenceIdentity =
            $"{report.Identity.StrategyVersionName} · {report.Identity.DecisionRef} · " +
            $"{report.Identity.IncludedOfficialRuns} runs included · " +
            $"{report.Identity.ExcludedOfficialRuns} earlier-identity runs excluded";
        CoverageState = coverage.State.ToString().ToUpperInvariant();
        UsableCoverage = coverage.UsableCoverage.ToString("P1");
        CohortCount = $"{coverage.Counts.MaturedCohorts} matured / {coverage.Counts.TotalCohorts} total";
        CandidateCount =
            $"{coverage.Counts.ValidOutcomes + coverage.Counts.DegradedOutcomes} usable / {coverage.Counts.ExpectedCandidates} expected";
        ReadinessExplanation = coverage.PrimaryScoreAvailable
            ? "Ready: lower Brier/ECE is better; higher AUC, decile lift, rank IC, and return lift is better. Results never change Delphi automatically."
            : $"Blocked below the {CalibrationCoverageCalculator.PrimaryCoverageFloor:P0} usable-outcome floor. Pending or invalid evidence is shown in coverage but cannot produce primary metrics.";

        Replace(Models, report.Models.Select(ScorecardModelRow.Create));
        Replace(
            Reliability,
            report.Models.SelectMany(model => model.Reliability.Select(bucket =>
                ScorecardReliabilityRow.Create(model.TaskType, bucket))));
        Replace(
            Deciles,
            report.Models.SelectMany(model => model.ProbabilityDeciles.Select(decile =>
                ScorecardDecileRow.Create(model.TaskType, decile))));
        Replace(
            Lenses,
            report.Lenses.SelectMany(lens => lens.Selections.DefaultIfEmpty().Select(selection =>
                ScorecardLensRow.Create(lens, selection))));
        Replace(Slices, report.Slices.Select(ScorecardSliceRow.Create));
    }

    private void ClearRows()
    {
        Models.Clear();
        Reliability.Clear();
        Deciles.Clear();
        Lenses.Clear();
        Slices.Clear();
    }

    private static string Metric(double? value, string format = "0.000") =>
        value.HasValue ? value.Value.ToString(format) : "—";

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source)
            target.Add(item);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public sealed record ScorecardModelRow(
        string Model,
        string Coverage,
        string Cohorts,
        string Brier,
        string Auc,
        string Ece,
        string TopDecileLift)
    {
        public static ScorecardModelRow Create(ProbabilityCalibrationReport report) => new(
            report.TaskType,
            report.PredictionCoverage.ToString("P1"),
            report.ContributingCohorts.ToString(),
            Metric(report.BrierScore),
            Metric(report.AreaUnderRocCurve),
            Metric(report.ExpectedCalibrationError),
            Metric(report.TopDecileEventLift, "+0.0%;-0.0%;0.0%"));
    }

    public sealed record ScorecardReliabilityRow(
        string Model,
        string ProbabilityRange,
        int Observations,
        int Cohorts,
        string MeanProbability,
        string ObservedRate)
    {
        public static ScorecardReliabilityRow Create(
            string taskType,
            ProbabilityReliabilityBucket bucket) => new(
                taskType,
                $"{bucket.LowerBound:P0}–{bucket.UpperBound:P0}",
                bucket.Observations,
                bucket.ContributingCohorts,
                bucket.MeanProbability.ToString("P1"),
                bucket.ObservedEventRate.ToString("P1"));
    }

    public sealed record ScorecardDecileRow(
        string Model,
        int Decile,
        int Observations,
        int Cohorts,
        string MeanProbability,
        string EventRate,
        string Lift)
    {
        public static ScorecardDecileRow Create(
            string taskType,
            ProbabilityDecileReport decile) => new(
                taskType,
                decile.Decile,
                decile.Observations,
                decile.ContributingCohorts,
                decile.MeanProbability.ToString("P1"),
                decile.ObservedEventRate.ToString("P1"),
                decile.EventRateLiftVersusRunBaseline.ToString("+0.0%;-0.0%;0.0%"));
    }

    public sealed record ScorecardLensRow(
        string Lens,
        string Eligible,
        string Cohorts,
        string RankIc,
        string Selection,
        string MeanReturn,
        string ExcessReturn,
        string Lift)
    {
        public static ScorecardLensRow Create(
            LensRankPerformanceReport lens,
            LensRankSelectionReport? selection) => new(
                lens.Lens,
                lens.EligibleObservations.ToString(),
                lens.ContributingCohorts.ToString(),
                Metric(lens.SpearmanRankInformationCoefficient),
                selection?.Selection ?? "—",
                Metric(selection?.MeanReturn10, "+0.0%;-0.0%;0.0%"),
                Metric(selection?.MeanExcessReturn10, "+0.0%;-0.0%;0.0%"),
                Metric(selection?.ReturnLiftVersusEligibleBaseline, "+0.0%;-0.0%;0.0%"));
    }

    public sealed record ScorecardSliceRow(
        string Dimension,
        string Value,
        int Observations,
        int Cohorts,
        string Return10,
        string Excess10,
        string UpRate,
        string DownRate,
        string BreakoutRate,
        string VolExpansionRate)
    {
        public static ScorecardSliceRow Create(PredictionSliceReport slice) => new(
            slice.Dimension,
            slice.Value,
            slice.Observations,
            slice.ContributingCohorts,
            slice.MeanReturn10.ToString("+0.0%;-0.0%;0.0%"),
            slice.MeanExcessReturn10.ToString("+0.0%;-0.0%;0.0%"),
            slice.UpEventRate.ToString("P1"),
            slice.DownEventRate.ToString("P1"),
            slice.BreakoutEventRate.ToString("P1"),
            slice.VolExpansionEventRate.ToString("P1"));
    }
}
