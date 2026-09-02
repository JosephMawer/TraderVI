#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Core.Calibration;

public sealed record DelayedIntradayLensEvidenceRow(
    Guid RunId,
    DateTime MarketDataAsOf,
    string Lens,
    Guid CandidateId,
    string? MaturityState,
    string? AuditState,
    string? OutcomeJson);

public sealed record DelayedIntradayLensReport(
    string Lens,
    int ExpectedRecommendations,
    int MaturedRecommendations,
    int NoEntryRecommendations,
    int InvalidRecommendations,
    int PendingRecommendations,
    int ContributingCohorts,
    double CompletionCoverage,
    double UsableCoverage,
    bool MetricsAvailable,
    double? MeanGrossReturn,
    double? MeanConservativeNetReturn,
    double? MeanGrossExcessReturn,
    double? MeanConservativeNetExcessReturn);

public static class DelayedIntradayLensReportCalculator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<DelayedIntradayLensReport> Build(
        IReadOnlyList<DelayedIntradayLensEvidenceRow> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.GroupBy(row => (row.RunId, row.CandidateId, row.Lens)).Any(group => group.Count() > 1))
            throw new ArgumentException("Duplicate run/candidate/lens evidence is not allowed.", nameof(evidence));

        return new[] { LensTradeabilityReportCalculator.ContinuationLens, LensTradeabilityReportCalculator.BreakoutLens }
            .Select(lens => BuildLens(lens, evidence.Where(row => row.Lens == lens).ToList()))
            .ToList();
    }

    private static DelayedIntradayLensReport BuildLens(
        string lens,
        IReadOnlyList<DelayedIntradayLensEvidenceRow> rows)
    {
        var parsed = rows.Select(row => (Row: row, Outcome: ParseValidOutcome(row))).ToList();
        int expected = rows.Count;
        int matured = parsed.Count(item => item.Outcome is not null);
        int noEntry = rows.Count(row => row.MaturityState == nameof(CalibrationOutcomeMaturityState.NoEntry) &&
                                       row.AuditState != nameof(CalibrationAuditState.Invalid));
        int invalid = parsed.Count(item =>
            item.Row.AuditState == nameof(CalibrationAuditState.Invalid) ||
            (item.Row.MaturityState == nameof(CalibrationOutcomeMaturityState.Matured) &&
             item.Outcome is null));
        int pending = expected - matured - noEntry - invalid;
        double completion = expected == 0 ? 0 : (double)(expected - pending) / expected;
        double usable = expected == 0 ? 0 : (double)(matured + noEntry) / expected;
        bool available = matured > 0 && usable >= CalibrationCoverageCalculator.PrimaryCoverageFloor;
        int cohorts = parsed.Where(item => item.Outcome is not null)
            .Select(item => item.Row.MarketDataAsOf.Date)
            .Distinct()
            .Count();

        return new DelayedIntradayLensReport(
            lens,
            expected,
            matured,
            noEntry,
            invalid,
            pending,
            cohorts,
            completion,
            usable,
            available,
            available ? CohortMean(parsed, outcome => outcome.GrossReturn) : null,
            available ? CohortMean(parsed, outcome => outcome.ConservativeNetReturn) : null,
            available ? CohortMean(parsed, outcome => outcome.GrossExcessReturn) : null,
            available ? CohortMean(parsed, outcome => outcome.ConservativeNetExcessReturn) : null);
    }

    private static DelayedIntradayOutcomeV1? ParseValidOutcome(DelayedIntradayLensEvidenceRow row)
    {
        if (row.MaturityState != nameof(CalibrationOutcomeMaturityState.Matured) ||
            row.AuditState is not (nameof(CalibrationAuditState.Valid) or nameof(CalibrationAuditState.Degraded)) ||
            string.IsNullOrWhiteSpace(row.OutcomeJson))
            return null;

        try
        {
            DelayedIntradayOutcomeV1? outcome = JsonSerializer.Deserialize<DelayedIntradayOutcomeV1>(
                row.OutcomeJson,
                JsonOptions);
            return outcome is not null &&
                   outcome.SchemaVersion == DelayedIntradayOutcomeCalculator.SchemaVersion &&
                   outcome.FillConvention == DelayedIntradayOutcomeCalculator.FillConvention
                ? outcome
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double CohortMean(
        IReadOnlyList<(DelayedIntradayLensEvidenceRow Row, DelayedIntradayOutcomeV1? Outcome)> rows,
        Func<DelayedIntradayOutcomeV1, double> selector) =>
        rows.Where(item => item.Outcome is not null)
            .GroupBy(item => item.Row.MarketDataAsOf.Date)
            .Select(cohort => cohort.Average(item => selector(item.Outcome!)))
            .Average();
}
