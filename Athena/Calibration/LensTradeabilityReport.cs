#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Core.Calibration;

public sealed record LensTradeabilityRunEvidence(Guid RunId, DateTime MarketDataAsOf);

public sealed record LensTradeabilityEvidenceRow(
    Guid RunId,
    DateTime MarketDataAsOf,
    string Lens,
    int Rank,
    Guid CandidateId,
    string Symbol,
    string? MarkMaturityState,
    string? MarkAuditState,
    string? MarkOutcomeJson,
    string? ExcursionMaturityState,
    string? ExcursionAuditState,
    string? ExcursionOutcomeJson);

public sealed record LensTradeabilityEvidenceSet(
    IReadOnlyList<LensTradeabilityRunEvidence> OfficialRuns,
    IReadOnlyList<LensTradeabilityEvidenceRow> Recommendations);

public sealed record LensTradeabilityCoverage(
    int OfficialRuns,
    int TotalCohorts,
    int MaturedCohorts,
    int ExpectedRecommendations,
    int EnteredValid,
    int EnteredDegraded,
    int NoEntryValid,
    int NoEntryDegraded,
    int InvalidRecommendations,
    int PendingRecommendations,
    double CompletionCoverage,
    double UsableCoverage,
    bool PrimaryScoreAvailable,
    CalibrationCoverageState State)
{
    public int EnteredRecommendations => EnteredValid + EnteredDegraded;
    public int NoEntryRecommendations => NoEntryValid + NoEntryDegraded;
}

public sealed record LensTradeabilityHorizonReport(
    int Sessions,
    int EnteredRecommendations,
    int ContributingCohorts,
    double? MeanNetReturn,
    double? ProfitableRate,
    double? MeanNetExcessReturn,
    double? MeanMfeReturn,
    double? MeanMaeReturn,
    double? MeanMfeSessionOrdinal,
    double? MeanMaeSessionOrdinal);

public sealed record LensTradeabilityReport(
    string Lens,
    LensTradeabilityCoverage Coverage,
    double? NoEntryRate,
    IReadOnlyList<LensTradeabilityHorizonReport> Horizons);

public static class LensTradeabilityReportCalculator
{
    public const string ContinuationLens = "Continuation";
    public const string BreakoutLens = "Breakout";

    private enum EvidenceState
    {
        Pending,
        EnteredValid,
        EnteredDegraded,
        NoEntryValid,
        NoEntryDegraded,
        Invalid
    }

    private sealed record ClassifiedEvidence(
        LensTradeabilityEvidenceRow Source,
        EvidenceState State,
        SwingMarkToMarketOutcomeV1? Mark,
        SwingExcursionOutcomeV1? Excursion);

    private sealed record MetricObservation(
        Guid RunId,
        DateTime MarketDataAsOf,
        double NetReturn,
        double Profitable,
        double NetExcessReturn,
        double MfeReturn,
        double MaeReturn,
        double MfeSessionOrdinal,
        double MaeSessionOrdinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<LensTradeabilityReport> BuildReports(
        IReadOnlyList<LensTradeabilityEvidenceRow> evidence)
    {
        if (evidence is null) throw new ArgumentNullException(nameof(evidence));
        var runs = evidence
            .Select(x => new LensTradeabilityRunEvidence(x.RunId, x.MarketDataAsOf.Date))
            .Distinct()
            .ToList();
        return BuildReports(new LensTradeabilityEvidenceSet(runs, evidence));
    }

    public static IReadOnlyList<LensTradeabilityReport> BuildReports(
        LensTradeabilityEvidenceSet evidenceSet)
    {
        if (evidenceSet is null) throw new ArgumentNullException(nameof(evidenceSet));
        if (evidenceSet.OfficialRuns is null || evidenceSet.Recommendations is null)
            throw new ArgumentException("Run and recommendation evidence collections are required.", nameof(evidenceSet));

        var duplicateRun = evidenceSet.OfficialRuns.GroupBy(x => x.RunId).FirstOrDefault(x => x.Count() > 1);
        if (duplicateRun is not null)
            throw new ArgumentException("Duplicate official run evidence is not allowed.", nameof(evidenceSet));

        var runDates = evidenceSet.OfficialRuns.ToDictionary(x => x.RunId, x => x.MarketDataAsOf.Date);
        if (evidenceSet.Recommendations.Any(x =>
                !runDates.TryGetValue(x.RunId, out DateTime date) || date != x.MarketDataAsOf.Date))
            throw new ArgumentException("Every recommendation must match an official run and cohort.", nameof(evidenceSet));

        var unknownLenses = evidenceSet.Recommendations
            .Select(x => x.Lens)
            .Where(x => !string.Equals(x, ContinuationLens, StringComparison.Ordinal) &&
                        !string.Equals(x, BreakoutLens, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (unknownLenses.Count > 0)
            throw new ArgumentException($"Unknown calibration lens: {string.Join(", ", unknownLenses)}.", nameof(evidenceSet));

        var duplicate = evidenceSet.Recommendations
            .GroupBy(x => (x.RunId, x.CandidateId, x.Lens))
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException("Duplicate run/candidate/lens evidence is not allowed.", nameof(evidenceSet));

        return new[] { ContinuationLens, BreakoutLens }
            .Select(lens => BuildLens(
                lens,
                evidenceSet.OfficialRuns,
                evidenceSet.Recommendations.Where(x => x.Lens == lens).ToList()))
            .ToList();
    }

    private static LensTradeabilityReport BuildLens(
        string lens,
        IReadOnlyList<LensTradeabilityRunEvidence> officialRunEvidence,
        IReadOnlyList<LensTradeabilityEvidenceRow> evidence)
    {
        var classified = evidence.Select(Classify).ToList();
        int expected = classified.Count;
        int enteredValid = classified.Count(x => x.State == EvidenceState.EnteredValid);
        int enteredDegraded = classified.Count(x => x.State == EvidenceState.EnteredDegraded);
        int noEntryValid = classified.Count(x => x.State == EvidenceState.NoEntryValid);
        int noEntryDegraded = classified.Count(x => x.State == EvidenceState.NoEntryDegraded);
        int invalid = classified.Count(x => x.State == EvidenceState.Invalid);
        int pending = classified.Count(x => x.State == EvidenceState.Pending);
        int officialRuns = officialRunEvidence.Count;
        var cohortDates = officialRunEvidence.Select(x => x.MarketDataAsOf.Date).Distinct().ToList();
        int totalCohorts = cohortDates.Count;
        int maturedCohorts = cohortDates.Count(date => classified
            .Where(x => x.Source.MarketDataAsOf.Date == date)
            .All(row => row.State != EvidenceState.Pending));
        int maturedRecommendationCohorts = cohortDates.Count(date => classified
            .Where(x => x.Source.MarketDataAsOf.Date == date)
            .Any() && classified
            .Where(x => x.Source.MarketDataAsOf.Date == date)
            .All(row => row.State != EvidenceState.Pending));

        double completionCoverage = expected == 0 ? 0 : (double)(expected - pending) / expected;
        int usable = enteredValid + enteredDegraded + noEntryValid + noEntryDegraded;
        double usableCoverage = expected == 0 ? 0 : (double)usable / expected;
        bool primaryAvailable = expected > 0 && maturedRecommendationCohorts > 0 &&
            usableCoverage >= CalibrationCoverageCalculator.PrimaryCoverageFloor;
        CalibrationCoverageState state = expected == 0
            ? CalibrationCoverageState.NoEvidence
            : !primaryAvailable
                ? CalibrationCoverageState.Blocked
                : enteredDegraded > 0 || noEntryDegraded > 0 || invalid > 0 || pending > 0
                    ? CalibrationCoverageState.Degraded
                    : CalibrationCoverageState.Ready;

        var coverage = new LensTradeabilityCoverage(
            officialRuns,
            totalCohorts,
            maturedCohorts,
            expected,
            enteredValid,
            enteredDegraded,
            noEntryValid,
            noEntryDegraded,
            invalid,
            pending,
            completionCoverage,
            usableCoverage,
            primaryAvailable,
            state);

        if (!primaryAvailable)
            return new LensTradeabilityReport(lens, coverage, null, Array.Empty<LensTradeabilityHorizonReport>());

        double? noEntryRate = CohortWeightedAverage(
            classified,
            x => x.State is EvidenceState.NoEntryValid or EvidenceState.NoEntryDegraded ? 1.0 : 0.0);
        var entered = classified
            .Where(x => x.State is EvidenceState.EnteredValid or EvidenceState.EnteredDegraded)
            .ToList();
        var horizons = Enumerable.Range(1, SwingMarkToMarketOutcomeCalculator.HorizonSessions)
            .Select(sessions => BuildHorizon(sessions, entered))
            .ToList();

        return new LensTradeabilityReport(lens, coverage, noEntryRate, horizons);
    }

    private static LensTradeabilityHorizonReport BuildHorizon(
        int sessions,
        IReadOnlyList<ClassifiedEvidence> entered)
    {
        var observations = entered.Select(x =>
        {
            SwingHorizonMark mark = x.Mark!.Horizons.Single(y => y.Sessions == sessions);
            SwingExcursionHorizonV1 excursion = x.Excursion!.Horizons.Single(y => y.Sessions == sessions);
            return new MetricObservation(
                x.Source.RunId,
                x.Source.MarketDataAsOf.Date,
                mark.NetReturn,
                mark.NetReturn > 0 ? 1 : 0,
                mark.NetExcessReturn,
                excursion.MfeReturn,
                excursion.MaeReturn,
                excursion.MfeSessionOrdinal,
                excursion.MaeSessionOrdinal);
        }).ToList();

        return new LensTradeabilityHorizonReport(
            sessions,
            observations.Count,
            observations.Select(x => x.MarketDataAsOf).Distinct().Count(),
            CohortWeightedAverage(observations, x => x.NetReturn),
            CohortWeightedAverage(observations, x => x.Profitable),
            CohortWeightedAverage(observations, x => x.NetExcessReturn),
            CohortWeightedAverage(observations, x => x.MfeReturn),
            CohortWeightedAverage(observations, x => x.MaeReturn),
            CohortWeightedAverage(observations, x => x.MfeSessionOrdinal),
            CohortWeightedAverage(observations, x => x.MaeSessionOrdinal));
    }

    private static ClassifiedEvidence Classify(LensTradeabilityEvidenceRow source)
    {
        if (source.Rank <= 0 || string.IsNullOrWhiteSpace(source.Symbol))
            return new ClassifiedEvidence(source, EvidenceState.Invalid, null, null);

        if (IsPending(source.MarkMaturityState) || IsPending(source.ExcursionMaturityState))
            return new ClassifiedEvidence(source, EvidenceState.Pending, null, null);

        bool markUsableAudit = IsUsableAudit(source.MarkAuditState);
        bool excursionUsableAudit = IsUsableAudit(source.ExcursionAuditState);
        if (!markUsableAudit || !excursionUsableAudit)
            return new ClassifiedEvidence(source, EvidenceState.Invalid, null, null);

        bool degraded = source.MarkAuditState == nameof(CalibrationAuditState.Degraded) ||
                        source.ExcursionAuditState == nameof(CalibrationAuditState.Degraded);
        bool markNoEntry = source.MarkMaturityState == nameof(CalibrationOutcomeMaturityState.NoEntry);
        bool excursionNoEntry = source.ExcursionMaturityState == nameof(CalibrationOutcomeMaturityState.NoEntry);
        if (markNoEntry || excursionNoEntry)
        {
            if (!markNoEntry || !excursionNoEntry)
                return new ClassifiedEvidence(source, EvidenceState.Invalid, null, null);

            try
            {
                var markNoEntryOutcome = JsonSerializer.Deserialize<NoEntrySwingOutcomeV1>(
                    source.MarkOutcomeJson ?? string.Empty, JsonOptions);
                var excursionNoEntryOutcome = JsonSerializer.Deserialize<NoEntrySwingOutcomeV1>(
                    source.ExcursionOutcomeJson ?? string.Empty, JsonOptions);
                if (!ValidNoEntryPair(source, markNoEntryOutcome, excursionNoEntryOutcome))
                    return new ClassifiedEvidence(source, EvidenceState.Invalid, null, null);
            }
            catch (JsonException)
            {
                return new ClassifiedEvidence(source, EvidenceState.Invalid, null, null);
            }

            return new ClassifiedEvidence(
                source,
                degraded ? EvidenceState.NoEntryDegraded : EvidenceState.NoEntryValid,
                null,
                null);
        }

        if (source.MarkMaturityState != nameof(CalibrationOutcomeMaturityState.Matured) ||
            source.ExcursionMaturityState != nameof(CalibrationOutcomeMaturityState.Matured) ||
            string.IsNullOrWhiteSpace(source.MarkOutcomeJson) ||
            string.IsNullOrWhiteSpace(source.ExcursionOutcomeJson))
            return new ClassifiedEvidence(source, EvidenceState.Invalid, null, null);

        try
        {
            var mark = JsonSerializer.Deserialize<SwingMarkToMarketOutcomeV1>(source.MarkOutcomeJson, JsonOptions);
            var excursion = JsonSerializer.Deserialize<SwingExcursionOutcomeV1>(source.ExcursionOutcomeJson, JsonOptions);
            if (!ValidPair(source, mark, excursion))
                return new ClassifiedEvidence(source, EvidenceState.Invalid, null, null);

            return new ClassifiedEvidence(
                source,
                degraded ? EvidenceState.EnteredDegraded : EvidenceState.EnteredValid,
                mark,
                excursion);
        }
        catch (JsonException)
        {
            return new ClassifiedEvidence(source, EvidenceState.Invalid, null, null);
        }
        catch (InvalidOperationException)
        {
            return new ClassifiedEvidence(source, EvidenceState.Invalid, null, null);
        }
    }

    private static bool ValidPair(
        LensTradeabilityEvidenceRow source,
        SwingMarkToMarketOutcomeV1? mark,
        SwingExcursionOutcomeV1? excursion)
    {
        if (mark is null || excursion is null ||
            mark.SchemaVersion != SwingMarkToMarketOutcomeCalculator.SchemaVersion ||
            excursion.SchemaVersion != SwingMarkToMarketOutcomeCalculator.SchemaVersion ||
            mark.ObservationDate.Date != source.MarketDataAsOf.Date ||
            mark.ObservationDate.Date != excursion.ObservationDate.Date ||
            mark.InitialEligibleSession.Date != excursion.InitialEligibleSession.Date ||
            mark.EntrySession.Date != excursion.EntrySession.Date ||
            mark.EntryDelaySessions != excursion.EntryDelaySessions ||
            System.Math.Abs(mark.RawEntryOpen - excursion.RawEntryOpen) > 0.000001 ||
            SwingMarkToMarketOutcomeCalculator.NormalizeUtc(mark.RunStartedUtc) !=
                SwingMarkToMarketOutcomeCalculator.NormalizeUtc(excursion.RunStartedUtc) ||
            mark.Horizons is null || excursion.Horizons is null ||
            mark.Horizons.Count != SwingMarkToMarketOutcomeCalculator.HorizonSessions ||
            excursion.Horizons.Count != SwingMarkToMarketOutcomeCalculator.HorizonSessions)
            return false;

        for (int sessions = 1; sessions <= SwingMarkToMarketOutcomeCalculator.HorizonSessions; sessions++)
        {
            SwingHorizonMark? markHorizon = mark.Horizons.SingleOrDefault(x => x.Sessions == sessions);
            SwingExcursionHorizonV1? excursionHorizon = excursion.Horizons.SingleOrDefault(x => x.Sessions == sessions);
            if (markHorizon is null || excursionHorizon is null ||
                markHorizon.ExitSession.Date != excursionHorizon.HorizonSession.Date ||
                !Finite(markHorizon.NetReturn) || !Finite(markHorizon.NetExcessReturn) ||
                !Finite(excursionHorizon.MfeReturn) || !Finite(excursionHorizon.MaeReturn) ||
                excursionHorizon.MfeReturn < 0 || excursionHorizon.MaeReturn > 0 ||
                excursionHorizon.MfeSessionOrdinal < 1 || excursionHorizon.MfeSessionOrdinal > sessions ||
                excursionHorizon.MaeSessionOrdinal < 1 || excursionHorizon.MaeSessionOrdinal > sessions ||
                (excursionHorizon.ExcursionOrderState != SwingMarkToMarketOutcomeCalculator.FavorableFirst &&
                 excursionHorizon.ExcursionOrderState != SwingMarkToMarketOutcomeCalculator.AdverseFirst &&
                 excursionHorizon.ExcursionOrderState != SwingMarkToMarketOutcomeCalculator.SameSessionUnknown))
                return false;
        }

        return true;
    }

    private static bool ValidNoEntryPair(
        LensTradeabilityEvidenceRow source,
        NoEntrySwingOutcomeV1? mark,
        NoEntrySwingOutcomeV1? excursion) =>
        mark is not null && excursion is not null &&
        mark.SchemaVersion == SwingMarkToMarketOutcomeCalculator.SchemaVersion &&
        excursion.SchemaVersion == SwingMarkToMarketOutcomeCalculator.SchemaVersion &&
        mark.ObservationDate.Date == source.MarketDataAsOf.Date &&
        mark.ObservationDate.Date == excursion.ObservationDate.Date &&
        SwingMarkToMarketOutcomeCalculator.NormalizeUtc(mark.RunStartedUtc) ==
            SwingMarkToMarketOutcomeCalculator.NormalizeUtc(excursion.RunStartedUtc) &&
        mark.InitialEligibleSession.Date == excursion.InitialEligibleSession.Date &&
        mark.EligibleSessionsInspected == SwingMarkToMarketOutcomeCalculator.EntrySessionAllowance &&
        excursion.EligibleSessionsInspected == SwingMarkToMarketOutcomeCalculator.EntrySessionAllowance &&
        mark.ReasonCode == excursion.ReasonCode &&
        !string.IsNullOrWhiteSpace(mark.ReasonCode);

    private static bool IsPending(string? maturityState) =>
        maturityState is null or nameof(CalibrationOutcomeMaturityState.Pending);

    private static bool IsUsableAudit(string? auditState) =>
        auditState is nameof(CalibrationAuditState.Valid) or nameof(CalibrationAuditState.Degraded);

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static double? CohortWeightedAverage<T>(
        IReadOnlyList<T> rows,
        Func<T, double> selector)
        where T : notnull
    {
        if (rows.Count == 0) return null;

        var runValues = rows
            .GroupBy(x => RunKey(x))
            .Select(x => new { x.Key.MarketDataAsOf, Value = x.Average(selector) })
            .ToList();
        return runValues
            .GroupBy(x => x.MarketDataAsOf)
            .Select(x => x.Average(y => y.Value))
            .Average();
    }

    private static (Guid RunId, DateTime MarketDataAsOf) RunKey<T>(T row) => row switch
    {
        ClassifiedEvidence evidence => (evidence.Source.RunId, evidence.Source.MarketDataAsOf.Date),
        MetricObservation observation => (observation.RunId, observation.MarketDataAsOf.Date),
        _ => throw new ArgumentException($"Unsupported cohort row type {typeof(T).Name}.", nameof(row))
    };
}
