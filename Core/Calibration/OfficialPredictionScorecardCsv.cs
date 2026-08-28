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
    public const int ExportSchemaVersion = 1;

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
        CalibrationCoverageCounts counts = scorecard.Coverage.Counts;
        var csv = NewCsv(
            "export_schema_version", "definition_id", "definition_name", "definition_version",
            "official_runs", "total_cohorts", "matured_cohorts", "expected_candidates",
            "valid_outcomes", "degraded_outcomes", "invalid_outcomes", "pending_outcomes",
            "completion_coverage", "usable_coverage", "performance_available", "state");
        Row(csv,
            ExportSchemaVersion,
            scorecard.Definition.OutcomeDefinitionId,
            scorecard.Definition.DefinitionName,
            scorecard.Definition.DefinitionVersion,
            counts.OfficialRuns,
            counts.TotalCohorts,
            counts.MaturedCohorts,
            counts.ExpectedCandidates,
            counts.ValidOutcomes,
            counts.DegradedOutcomes,
            counts.InvalidOutcomes,
            counts.PendingOutcomes,
            scorecard.Coverage.CompletionCoverage,
            scorecard.Coverage.UsableCoverage,
            scorecard.Coverage.PrimaryScoreAvailable,
            scorecard.Coverage.State);
        return csv.ToString();
    }

    private static string Models(OfficialPredictionScorecard scorecard)
    {
        var csv = NewCsv(
            "export_schema_version", "definition_id", "definition_version", "task_type",
            "expected_candidates", "usable_predictions", "missing_or_invalid_predictions",
            "contributing_cohorts", "prediction_coverage", "metrics_available", "brier_score",
            "auc", "expected_calibration_error", "top_decile_event_lift");
        foreach (ProbabilityCalibrationReport model in scorecard.Models)
        {
            Row(csv,
                ExportSchemaVersion,
                scorecard.Definition.OutcomeDefinitionId,
                scorecard.Definition.DefinitionVersion,
                model.TaskType,
                model.ExpectedCandidates,
                model.UsablePredictions,
                model.MissingOrInvalidPredictions,
                model.ContributingCohorts,
                model.PredictionCoverage,
                model.MetricsAvailable,
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
            "export_schema_version", "definition_id", "definition_version", "task_type", "report",
            "bucket", "lower_bound", "upper_bound", "observations", "contributing_cohorts",
            "cohort_weight", "mean_probability", "observed_event_rate", "event_rate_lift");
        foreach (ProbabilityCalibrationReport model in scorecard.Models)
        {
            foreach (ProbabilityReliabilityBucket bucket in model.Reliability)
            {
                Row(csv,
                    ExportSchemaVersion,
                    scorecard.Definition.OutcomeDefinitionId,
                    scorecard.Definition.DefinitionVersion,
                    model.TaskType,
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
                    scorecard.Definition.OutcomeDefinitionId,
                    scorecard.Definition.DefinitionVersion,
                    model.TaskType,
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
            "export_schema_version", "definition_id", "definition_version", "lens",
            "eligible_observations", "contributing_cohorts", "metrics_available", "rank_ic",
            "selection", "selected_observations", "selection_cohorts", "mean_return_10",
            "mean_excess_return_10", "return_lift_versus_eligible_baseline");
        foreach (LensRankPerformanceReport lens in scorecard.Lenses)
        {
            if (lens.Selections.Count == 0)
            {
                Row(csv,
                    ExportSchemaVersion,
                    scorecard.Definition.OutcomeDefinitionId,
                    scorecard.Definition.DefinitionVersion,
                    lens.Lens,
                    lens.EligibleObservations,
                    lens.ContributingCohorts,
                    lens.MetricsAvailable,
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
                    scorecard.Definition.OutcomeDefinitionId,
                    scorecard.Definition.DefinitionVersion,
                    lens.Lens,
                    lens.EligibleObservations,
                    lens.ContributingCohorts,
                    lens.MetricsAvailable,
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
            "export_schema_version", "definition_id", "definition_version", "dimension", "value",
            "observations", "contributing_cohorts", "mean_return_10", "mean_excess_return_10",
            "up_event_rate", "down_event_rate", "breakout_event_rate", "vol_expansion_event_rate");
        foreach (PredictionSliceReport slice in scorecard.Slices)
        {
            Row(csv,
                ExportSchemaVersion,
                scorecard.Definition.OutcomeDefinitionId,
                scorecard.Definition.DefinitionVersion,
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
