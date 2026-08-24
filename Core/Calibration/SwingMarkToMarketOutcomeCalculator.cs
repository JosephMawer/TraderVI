#nullable enable

using Core.ML;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Calibration;

public enum SwingOutcomeReadinessState
{
    Pending,
    Matured,
    NoEntry,
    Invalid
}

public sealed record SwingOutcomeReadiness(
    SwingOutcomeReadinessState State,
    int RequiredSessions,
    int BenchmarkSessionsAvailable,
    DateTime? InitialEligibleSession,
    DateTime? EntrySession,
    int? EntryDelaySessions,
    DateTime? FirstInvalidSession,
    string? ReasonCode);

public sealed record SwingHorizonMark(
    int Sessions,
    DateTime ExitSession,
    double RawExitClose,
    double AdjustedExitPrice,
    double GrossReturn,
    double NetReturn,
    double XiuRawExitClose,
    double XiuGrossReturn,
    double NetExcessReturn);

public sealed record SwingMarkToMarketOutcomeV1(
    int SchemaVersion,
    DateTime ObservationDate,
    DateTime RunStartedUtc,
    DateTime InitialEligibleSession,
    DateTime EntrySession,
    int EntryDelaySessions,
    double RawEntryOpen,
    double AdjustedEntryPrice,
    double XiuRawEntryOpen,
    double EntrySlippageRate,
    double EntryHalfSpreadRate,
    double ExitSlippageRate,
    double ExitHalfSpreadRate,
    IReadOnlyList<SwingHorizonMark> Horizons);

public sealed record SwingExcursionHorizonV1(
    int Sessions,
    DateTime HorizonSession,
    double MfeReturn,
    DateTime MfeSession,
    int MfeSessionOrdinal,
    double MaeReturn,
    DateTime MaeSession,
    int MaeSessionOrdinal,
    string ExcursionOrderState);

public sealed record SwingExcursionOutcomeV1(
    int SchemaVersion,
    DateTime ObservationDate,
    DateTime RunStartedUtc,
    DateTime InitialEligibleSession,
    DateTime EntrySession,
    int EntryDelaySessions,
    double RawEntryOpen,
    IReadOnlyList<SwingExcursionHorizonV1> Horizons);

public sealed record NoEntrySwingOutcomeV1(
    int SchemaVersion,
    DateTime ObservationDate,
    DateTime RunStartedUtc,
    DateTime InitialEligibleSession,
    int EligibleSessionsInspected,
    string ReasonCode);

public sealed record InvalidSwingOutcomeV1(
    int SchemaVersion,
    DateTime ObservationDate,
    DateTime RunStartedUtc,
    DateTime? InitialEligibleSession,
    DateTime? EntrySession,
    int? EntryDelaySessions,
    int BenchmarkSessionsAvailable,
    DateTime? FirstInvalidSession,
    string ReasonCode);

public static class SwingMarkToMarketOutcomeCalculator
{
    public const int SchemaVersion = 1;
    public const int HorizonSessions = 3;
    public const int EntrySessionAllowance = 3;
    public const double SlippageRatePerSide = 0.0010;
    public const double HalfSpreadRatePerSide = 0.0015;
    public const double TotalCostRatePerSide = SlippageRatePerSide + HalfSpreadRatePerSide;

    public const string FavorableFirst = "FavorableFirst";
    public const string AdverseFirst = "AdverseFirst";
    public const string SameSessionUnknown = "SameSessionUnknown";

    private sealed record SessionBars(DateTime Date, IReadOnlyList<DailyBar> Bars)
    {
        public DailyBar Single => Bars.Single();
    }

    public static SwingOutcomeReadiness AssessReadiness(
        DateTime observationDate,
        DateTime runStartedUtc,
        IReadOnlyList<DailyBar> symbolBars,
        IReadOnlyList<DailyBar> xiuBars)
    {
        DateTime observationSession = observationDate.Date;
        DateTime normalizedRunStartedUtc = NormalizeUtc(runStartedUtc);
        var benchmarkSessions = xiuBars
            .Where(x => x.Date.Date > observationSession)
            .GroupBy(x => x.Date.Date)
            .OrderBy(x => x.Key)
            .Select(x => new SessionBars(x.Key, x.ToList()))
            .Where(x => SessionOpenUtc(x.Date) > normalizedRunStartedUtc)
            .ToList();

        if (benchmarkSessions.Count == 0)
            return Pending(0, null, null, null);

        DateTime initialEligibleSession = benchmarkSessions[0].Date;
        var symbolSessions = symbolBars
            .Where(x => x.Date.Date > observationSession)
            .GroupBy(x => x.Date.Date)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<DailyBar>)x.ToList());

        int entryIndex = -1;
        DateTime? deferredInvalidSession = null;
        string? deferredInvalidReason = null;
        int entrySearchCount = System.Math.Min(EntrySessionAllowance, benchmarkSessions.Count);
        for (int i = 0; i < entrySearchCount; i++)
        {
            SessionBars benchmark = benchmarkSessions[i];
            if (benchmark.Bars.Count != 1)
            {
                deferredInvalidSession = benchmark.Date;
                deferredInvalidReason = "DuplicateBenchmarkSession";
                break;
            }

            if (!symbolSessions.TryGetValue(benchmark.Date, out var bars))
                continue;

            if (bars.Count != 1)
            {
                deferredInvalidSession = benchmark.Date;
                deferredInvalidReason = "DuplicateSymbolSession";
                break;
            }

            if (bars[0].Open <= 0)
            {
                deferredInvalidSession = benchmark.Date;
                deferredInvalidReason = "NonPositiveEntryPrice";
                break;
            }

            entryIndex = i;
            break;
        }

        if (deferredInvalidReason is not null)
        {
            if (benchmarkSessions.Count < EntrySessionAllowance)
                return Pending(benchmarkSessions.Count, initialEligibleSession, null, null);

            return Invalid(
                benchmarkSessions.Count,
                initialEligibleSession,
                null,
                null,
                deferredInvalidSession,
                deferredInvalidReason);
        }

        if (entryIndex < 0)
        {
            if (benchmarkSessions.Count < EntrySessionAllowance)
                return Pending(benchmarkSessions.Count, initialEligibleSession, null, null);

            return new SwingOutcomeReadiness(
                SwingOutcomeReadinessState.NoEntry,
                HorizonSessions,
                benchmarkSessions.Count,
                initialEligibleSession,
                null,
                null,
                null,
                "NoSymbolBarWithinEntryAllowance");
        }

        DateTime entrySession = benchmarkSessions[entryIndex].Date;
        if (benchmarkSessions.Count < entryIndex + HorizonSessions)
            return Pending(benchmarkSessions.Count, initialEligibleSession, entrySession, entryIndex);

        for (int i = entryIndex; i < entryIndex + HorizonSessions; i++)
        {
            SessionBars benchmark = benchmarkSessions[i];
            if (benchmark.Bars.Count != 1)
                return Invalid(benchmarkSessions.Count, initialEligibleSession, entrySession, entryIndex,
                    benchmark.Date, "DuplicateBenchmarkSession");

            if (!symbolSessions.TryGetValue(benchmark.Date, out var bars))
                return Invalid(benchmarkSessions.Count, initialEligibleSession, entrySession, entryIndex,
                    benchmark.Date, "MissingSymbolSession");

            if (bars.Count != 1)
                return Invalid(benchmarkSessions.Count, initialEligibleSession, entrySession, entryIndex,
                    benchmark.Date, "DuplicateSymbolSession");

            if (bars[0].Close <= 0 || benchmark.Single.Close <= 0 || (i == entryIndex && benchmark.Single.Open <= 0))
                return Invalid(benchmarkSessions.Count, initialEligibleSession, entrySession, entryIndex,
                    benchmark.Date, "NonPositiveRequiredPrice");
        }

        return new SwingOutcomeReadiness(
            SwingOutcomeReadinessState.Matured,
            HorizonSessions,
            benchmarkSessions.Count,
            initialEligibleSession,
            entrySession,
            entryIndex,
            null,
            null);
    }

    public static SwingMarkToMarketOutcomeV1 Calculate(
        DateTime observationDate,
        DateTime runStartedUtc,
        IReadOnlyList<DailyBar> symbolBars,
        IReadOnlyList<DailyBar> xiuBars)
    {
        var readiness = AssessReadiness(observationDate, runStartedUtc, symbolBars, xiuBars);
        if (readiness.State != SwingOutcomeReadinessState.Matured ||
            readiness.InitialEligibleSession is null || readiness.EntrySession is null || readiness.EntryDelaySessions is null)
            throw new InvalidOperationException($"Swing outcome is not mature: {readiness.State}.");

        DateTime normalizedRunStartedUtc = NormalizeUtc(runStartedUtc);
        DateTime entrySession = readiness.EntrySession.Value;
        var benchmarkPath = xiuBars
            .Where(x => x.Date.Date >= entrySession)
            .GroupBy(x => x.Date.Date)
            .OrderBy(x => x.Key)
            .Take(HorizonSessions)
            .Select(x => x.Single())
            .ToList();
        var symbolByDate = symbolBars
            .Where(x => x.Date.Date >= entrySession)
            .GroupBy(x => x.Date.Date)
            .ToDictionary(x => x.Key, x => x.Single());

        DailyBar entryBar = symbolByDate[entrySession];
        double rawEntry = entryBar.Open;
        double adjustedEntry = rawEntry * (1 + TotalCostRatePerSide);
        double xiuEntry = benchmarkPath[0].Open;
        var horizons = new List<SwingHorizonMark>(HorizonSessions);

        for (int i = 0; i < HorizonSessions; i++)
        {
            DailyBar benchmarkBar = benchmarkPath[i];
            DailyBar symbolBar = symbolByDate[benchmarkBar.Date.Date];
            double rawExit = symbolBar.Close;
            double adjustedExit = rawExit * (1 - TotalCostRatePerSide);
            double grossReturn = rawExit / rawEntry - 1;
            double netReturn = adjustedExit / adjustedEntry - 1;
            double xiuGrossReturn = benchmarkBar.Close / xiuEntry - 1;
            horizons.Add(new SwingHorizonMark(
                i + 1,
                benchmarkBar.Date.Date,
                rawExit,
                adjustedExit,
                grossReturn,
                netReturn,
                benchmarkBar.Close,
                xiuGrossReturn,
                netReturn - xiuGrossReturn));
        }

        return new SwingMarkToMarketOutcomeV1(
            SchemaVersion,
            observationDate.Date,
            normalizedRunStartedUtc,
            readiness.InitialEligibleSession.Value,
            entrySession,
            readiness.EntryDelaySessions.Value,
            rawEntry,
            adjustedEntry,
            xiuEntry,
            SlippageRatePerSide,
            HalfSpreadRatePerSide,
            SlippageRatePerSide,
            HalfSpreadRatePerSide,
            horizons);
    }

    public static SwingOutcomeReadiness AssessExcursionReadiness(
        DateTime observationDate,
        DateTime runStartedUtc,
        IReadOnlyList<DailyBar> symbolBars,
        IReadOnlyList<DailyBar> xiuBars)
    {
        var readiness = AssessReadiness(observationDate, runStartedUtc, symbolBars, xiuBars);
        if (readiness.State != SwingOutcomeReadinessState.Matured || readiness.EntrySession is null)
            return readiness;

        DateTime entrySession = readiness.EntrySession.Value;
        var pathDates = xiuBars
            .Where(x => x.Date.Date >= entrySession)
            .GroupBy(x => x.Date.Date)
            .OrderBy(x => x.Key)
            .Take(HorizonSessions)
            .Select(x => x.Key)
            .ToList();
        var symbolByDate = symbolBars
            .Where(x => x.Date.Date >= entrySession)
            .GroupBy(x => x.Date.Date)
            .ToDictionary(x => x.Key, x => x.Single());

        foreach (DateTime pathDate in pathDates)
        {
            DailyBar bar = symbolByDate[pathDate];
            if (bar.Open <= 0 || bar.High <= 0 || bar.Low <= 0 || bar.Close <= 0)
                return Invalid(
                    readiness.BenchmarkSessionsAvailable,
                    readiness.InitialEligibleSession,
                    entrySession,
                    readiness.EntryDelaySessions,
                    pathDate,
                    "NonPositiveExcursionPrice");

            if (bar.Low > System.Math.Min(bar.Open, bar.Close) ||
                bar.High < System.Math.Max(bar.Open, bar.Close))
                return Invalid(
                    readiness.BenchmarkSessionsAvailable,
                    readiness.InitialEligibleSession,
                    entrySession,
                    readiness.EntryDelaySessions,
                    pathDate,
                    "InconsistentSymbolOhlc");
        }

        return readiness;
    }

    public static SwingExcursionOutcomeV1 CalculateExcursions(
        DateTime observationDate,
        DateTime runStartedUtc,
        IReadOnlyList<DailyBar> symbolBars,
        IReadOnlyList<DailyBar> xiuBars)
    {
        var readiness = AssessExcursionReadiness(observationDate, runStartedUtc, symbolBars, xiuBars);
        if (readiness.State != SwingOutcomeReadinessState.Matured ||
            readiness.InitialEligibleSession is null || readiness.EntrySession is null || readiness.EntryDelaySessions is null)
            throw new InvalidOperationException($"Swing excursion outcome is not mature: {readiness.State}.");

        DateTime entrySession = readiness.EntrySession.Value;
        var pathDates = xiuBars
            .Where(x => x.Date.Date >= entrySession)
            .GroupBy(x => x.Date.Date)
            .OrderBy(x => x.Key)
            .Take(HorizonSessions)
            .Select(x => x.Key)
            .ToList();
        var symbolByDate = symbolBars
            .Where(x => x.Date.Date >= entrySession)
            .GroupBy(x => x.Date.Date)
            .ToDictionary(x => x.Key, x => x.Single());

        double rawEntry = symbolByDate[entrySession].Open;
        double maximumHigh = rawEntry;
        double minimumLow = rawEntry;
        DateTime mfeSession = entrySession;
        DateTime maeSession = entrySession;
        int mfeSessionOrdinal = 1;
        int maeSessionOrdinal = 1;
        var horizons = new List<SwingExcursionHorizonV1>(HorizonSessions);

        for (int i = 0; i < HorizonSessions; i++)
        {
            DateTime pathDate = pathDates[i];
            DailyBar bar = symbolByDate[pathDate];
            if (bar.High > maximumHigh)
            {
                maximumHigh = bar.High;
                mfeSession = pathDate;
                mfeSessionOrdinal = i + 1;
            }

            if (bar.Low < minimumLow)
            {
                minimumLow = bar.Low;
                maeSession = pathDate;
                maeSessionOrdinal = i + 1;
            }

            string orderState = mfeSessionOrdinal < maeSessionOrdinal
                ? FavorableFirst
                : maeSessionOrdinal < mfeSessionOrdinal
                    ? AdverseFirst
                    : SameSessionUnknown;
            horizons.Add(new SwingExcursionHorizonV1(
                i + 1,
                pathDate,
                maximumHigh / rawEntry - 1,
                mfeSession,
                mfeSessionOrdinal,
                minimumLow / rawEntry - 1,
                maeSession,
                maeSessionOrdinal,
                orderState));
        }

        return new SwingExcursionOutcomeV1(
            SchemaVersion,
            observationDate.Date,
            NormalizeUtc(runStartedUtc),
            readiness.InitialEligibleSession.Value,
            entrySession,
            readiness.EntryDelaySessions.Value,
            rawEntry,
            horizons);
    }

    public static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static SwingOutcomeReadiness Pending(
        int available,
        DateTime? initialEligibleSession,
        DateTime? entrySession,
        int? entryDelaySessions) =>
        new(
            SwingOutcomeReadinessState.Pending,
            HorizonSessions,
            available,
            initialEligibleSession,
            entrySession,
            entryDelaySessions,
            null,
            null);

    private static SwingOutcomeReadiness Invalid(
        int available,
        DateTime? initialEligibleSession,
        DateTime? entrySession,
        int? entryDelaySessions,
        DateTime? invalidSession,
        string reasonCode) =>
        new(
            SwingOutcomeReadinessState.Invalid,
            HorizonSessions,
            available,
            initialEligibleSession,
            entrySession,
            entryDelaySessions,
            invalidSession,
            reasonCode);

    private static DateTime SessionOpenUtc(DateTime sessionDate)
    {
        DateTime localOpen = DateTime.SpecifyKind(sessionDate.Date.AddHours(9).AddMinutes(30), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localOpen, TorontoTimeZone.Value);
    }

    private static readonly Lazy<TimeZoneInfo> TorontoTimeZone = new(() =>
    {
        foreach (string id in new[] { "America/Toronto", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        throw new TimeZoneNotFoundException("Neither America/Toronto nor Eastern Standard Time is available.");
    });
}
