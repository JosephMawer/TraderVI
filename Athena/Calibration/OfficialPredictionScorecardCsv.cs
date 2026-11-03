#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Core.Calibration;

public sealed record OfficialPredictionScorecardCsvArtifact(string FileName, string Content);

public static class OfficialPredictionScorecardCsv
{
    public const int ExportSchemaVersion = 3;

    public static IReadOnlyList<OfficialPredictionScorecardCsvArtifact> Build(
        OfficialPredictionScorecard scorecard)
    {
        ArgumentNullException.ThrowIfNull(scorecard);
        string prefix = $"official-prediction-v{ExportSchemaVersion}";
        return
        [
            new($"{prefix}-coverage.csv", Coverage(scorecard)),
            new($"{prefix}-models.csv", Models(scorecard)),
            new($"{prefix}-reliability.csv", Reliability(scorecard)),
            new($"{prefix}-rank.csv", Rank(scorecard)),
            new($"{prefix}-slices.csv", Slices(scorecard))
        ];
    }

    private static string Coverage(OfficialPredictionScorecard scorecard)
    {
        PredictionOutcomeCoverage coverage = scorecard.Coverage;
        var csv = NewCsv(
            "export_schema_version", "strategy_version_id", "strategy_version_name",
            "initial_code_commit", "decision_ref", "excluded_official_runs",
            "definition_id", "definition_name", "definition_version",
            "official_runs", "total_cohorts", "due_cohorts", "due_candidates", "waiting_for_ten_sessions",
            "graded_candidates", "valid_outcomes", "degraded_outcomes", "invalid_outcomes", "due_missing_outcomes",
            "graded_fraction_of_due", "score_state");
        Row(csv,
            ExportSchemaVersion,
            scorecard.Identity.StrategyVersionId,
            scorecard.Identity.StrategyVersionName,
            scorecard.Identity.InitialCodeCommit,
            scorecard.Identity.DecisionRef,
            scorecard.Identity.ExcludedOfficialRuns,
            scorecard.Definition.OutcomeDefinitionId,
            scorecard.Definition.DefinitionName,
            scorecard.Definition.DefinitionVersion,
            scorecard.Identity.IncludedOfficialRuns,
            coverage.TotalCohorts,
            coverage.DueCohorts,
            coverage.Grades.Due,
            coverage.WaitingCandidates,
            coverage.Grades.Graded,
            coverage.ValidOutcomes,
            coverage.DegradedOutcomes,
            coverage.InvalidOutcomes,
            coverage.PendingDue,
            coverage.Grades.Rate,
            coverage.Grades.State);
        return csv.ToString();
    }

    private static string Models(OfficialPredictionScorecard scorecard)
    {
        var csv = NewCsv(
            "export_schema_version", "strategy_version_id", "strategy_version_name",
            "initial_code_commit", "decision_ref", "definition_id", "definition_version", "task_type",
            "due_candidates", "usable_predictions", "missing_or_invalid_predictions",
            "contributing_cohorts", "prediction_coverage", "score_state", "brier_score",
            "auc", "expected_calibration_error", "top_decile_event_lift");
        foreach (ProbabilityCalibrationReport model in scorecard.Models)
        {
            Row(csv,
                ExportSchemaVersion,
                scorecard.Identity.StrategyVersionId,
                scorecard.Identity.StrategyVersionName,
                scorecard.Identity.InitialCodeCommit,
                scorecard.Identity.DecisionRef,
                scorecard.Definition.OutcomeDefinitionId,
                scorecard.Definition.DefinitionVersion,
                model.TaskType,
                model.Grades.Due,
                model.Grades.Graded,
                model.Grades.Due - model.Grades.Graded,
                model.ContributingCohorts,
                model.Grades.Rate,
                model.Grades.State,
                model.BrierScore,
                model.AreaUnderRocCurve,
                model.ExpectedCalibrationError,
                model.TopDecileEventLift);
        }

        return csv.ToString();
    }

    private static string Reliability(OfficialPredictionScorecard scorecard)
    {
        var csv = NewCsv(
            "export_schema_version", "strategy_version_id", "strategy_version_name",
            "initial_code_commit", "decision_ref", "definition_id", "definition_version", "task_type", "score_state", "report",
            "bucket", "lower_bound", "upper_bound", "observations", "contributing_cohorts",
            "cohort_weight", "mean_probability", "observed_event_rate", "event_rate_lift");
        foreach (ProbabilityCalibrationReport model in scorecard.Models)
        {
            foreach (ProbabilityReliabilityBucket bucket in model.Reliability)
            {
                Row(csv,
                    ExportSchemaVersion,
                    scorecard.Identity.StrategyVersionId,
                    scorecard.Identity.StrategyVersionName,
                    scorecard.Identity.InitialCodeCommit,
                    scorecard.Identity.DecisionRef,
                    scorecard.Definition.OutcomeDefinitionId,
                    scorecard.Definition.DefinitionVersion,
                    model.TaskType,
                    model.Grades.State,
                    "ReliabilityBucket",
                    bucket.Bucket,
                    bucket.LowerBound,
                    bucket.UpperBound,
                    bucket.Observations,
                    bucket.ContributingCohorts,
                    bucket.CohortWeight,
                    bucket.MeanProbability,
                    bucket.ObservedEventRate,
                    null);
            }

            foreach (ProbabilityDecileReport decile in model.ProbabilityDeciles)
            {
                Row(csv,
                    ExportSchemaVersion,
                    scorecard.Identity.StrategyVersionId,
                    scorecard.Identity.StrategyVersionName,
                    scorecard.Identity.InitialCodeCommit,
                    scorecard.Identity.DecisionRef,
                    scorecard.Definition.OutcomeDefinitionId,
                    scorecard.Definition.DefinitionVersion,
                    model.TaskType,
                    model.Grades.State,
                    "ProbabilityDecile",
                    decile.Decile,
                    null,
                    null,
                    decile.Observations,
                    decile.ContributingCohorts,
                    null,
                    decile.MeanProbability,
                    decile.ObservedEventRate,
                    decile.EventRateLiftVersusRunBaseline);
            }
        }

        return csv.ToString();
    }

    private static string Rank(OfficialPredictionScorecard scorecard)
    {
        var csv = NewCsv(
            "export_schema_version", "strategy_version_id", "strategy_version_name",
            "initial_code_commit", "decision_ref", "definition_id", "definition_version", "lens",
            "eligible_observations", "contributing_cohorts", "metrics_available", "report_state", "rank_ic",
            "selection", "selected_observations", "selection_cohorts", "mean_return_10",
            "mean_excess_return_10", "return_lift_versus_eligible_baseline");
        foreach (LensRankPerformanceReport lens in scorecard.Lenses)
        {
            if (lens.Selections.Count == 0)
            {
                Row(csv,
                    ExportSchemaVersion,
                    scorecard.Identity.StrategyVersionId,
                    scorecard.Identity.StrategyVersionName,
                    scorecard.Identity.InitialCodeCommit,
                    scorecard.Identity.DecisionRef,
                    scorecard.Definition.OutcomeDefinitionId,
                    scorecard.Definition.DefinitionVersion,
                    lens.Lens,
                    lens.EligibleObservations,
                    lens.ContributingCohorts,
                    lens.MetricsAvailable,
                    scorecard.Coverage.Grades.State,
                    lens.SpearmanRankInformationCoefficient,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                continue;
            }

            foreach (LensRankSelectionReport selection in lens.Selections)
            {
                Row(csv,
                    ExportSchemaVersion,
                    scorecard.Identity.StrategyVersionId,
                    scorecard.Identity.StrategyVersionName,
                    scorecard.Identity.InitialCodeCommit,
                    scorecard.Identity.DecisionRef,
                    scorecard.Definition.OutcomeDefinitionId,
                    scorecard.Definition.DefinitionVersion,
                    lens.Lens,
                    lens.EligibleObservations,
                    lens.ContributingCohorts,
                    lens.MetricsAvailable,
                    scorecard.Coverage.Grades.State,
                    lens.SpearmanRankInformationCoefficient,
                    selection.Selection,
                    selection.SelectedObservations,
                    selection.ContributingCohorts,
                    selection.MeanReturn10,
                    selection.MeanExcessReturn10,
                    selection.ReturnLiftVersusEligibleBaseline);
            }
        }

        return csv.ToString();
    }

    private static string Slices(OfficialPredictionScorecard scorecard)
    {
        var csv = NewCsv(
            "export_schema_version", "strategy_version_id", "strategy_version_name",
            "initial_code_commit", "decision_ref", "definition_id", "definition_version", "report_state", "dimension", "value",
            "observations", "contributing_cohorts", "mean_return_10", "mean_excess_return_10",
            "up_event_rate", "down_event_rate", "breakout_event_rate", "vol_expansion_event_rate");
        foreach (PredictionSliceReport slice in scorecard.Slices)
        {
            Row(csv,
                ExportSchemaVersion,
                scorecard.Identity.StrategyVersionId,
                scorecard.Identity.StrategyVersionName,
                scorecard.Identity.InitialCodeCommit,
                scorecard.Identity.DecisionRef,
                scorecard.Definition.OutcomeDefinitionId,
                scorecard.Definition.DefinitionVersion,
                scorecard.Coverage.Grades.State,
                slice.Dimension,
                slice.Value,
                slice.Observations,
                slice.ContributingCohorts,
                slice.MeanReturn10,
                slice.MeanExcessReturn10,
                slice.UpEventRate,
                slice.DownEventRate,
                slice.BreakoutEventRate,
                slice.VolExpansionEventRate);
        }

        return csv.ToString();
    }

    private static StringBuilder NewCsv(params string[] columns)
    {
        var csv = new StringBuilder();
        Row(csv, columns.Cast<object?>().ToArray());
        return csv;
    }

    private static void Row(StringBuilder csv, params object?[] values) =>
        csv.AppendLine(string.Join(",", values.Select(Value)));

    private static string Value(object? value)
    {
        string text = value switch
        {
            null => string.Empty,
            bool boolean => boolean ? "true" : "false",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        return text.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? text
            : $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
