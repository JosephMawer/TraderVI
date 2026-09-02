#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.RelativeStrength;

/// <summary>
/// Pure computation of relative-strength features from dated price series.
/// No DB or I/O: inputs are aligned to canonical market sessions and missing endpoints stay missing.
/// </summary>
public static class RelativeStrengthCalculator
{
    public static IReadOnlyList<int> Horizons { get; } = Array.AsReadOnly(new[] { 5, 10, 20, 60 });
    public const int DefaultZWindow = 20;
    private const int ZScoreHorizon = 10;

    /// <summary>
    /// Canonical sessions needed for every currently emitted feature. A 60-session return needs
    /// 61 endpoints; the 10-session Z-score needs <c>10 + zWindow</c> observations.
    /// </summary>
    public static int RequiredCanonicalSessionCount(int zWindow = DefaultZWindow)
    {
        if (zWindow < 1)
            throw new ArgumentOutOfRangeException(nameof(zWindow));

        return System.Math.Max(Horizons.Max() + 1, ZScoreHorizon + zWindow);
    }

    /// <summary>
    /// Computes all relative-strength features for one stock on one target session.
    /// A metric is null when one of its exact required session endpoints is unavailable;
    /// histories are never clipped or aligned by list position.
    /// </summary>
    public static RelativeStrengthCalculationResult Compute(
        IReadOnlyList<RelativeStrengthPricePoint> stockCloses,
        IReadOnlyList<RelativeStrengthPricePoint> sectorCloses,
        IReadOnlyList<RelativeStrengthPricePoint> marketCloses,
        string symbol,
        DateOnly date,
        string sectorIndexSymbol,
        int zWindow = DefaultZWindow)
    {
        ArgumentNullException.ThrowIfNull(stockCloses);
        ArgumentNullException.ThrowIfNull(sectorCloses);
        ArgumentNullException.ThrowIfNull(marketCloses);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectorIndexSymbol);

        int requiredCanonicalSessions = RequiredCanonicalSessionCount(zWindow);
        PriceSeries stockSeries = BuildSeries(stockCloses);
        PriceSeries sectorSeries = BuildSeries(sectorCloses);
        PriceSeries marketSeries = BuildSeries(marketCloses);
        IReadOnlyDictionary<DateOnly, double> stockByDate = stockSeries.ValidCloses;
        IReadOnlyDictionary<DateOnly, double> sectorByDate = sectorSeries.ValidCloses;
        IReadOnlyDictionary<DateOnly, double> marketByDate = marketSeries.ValidCloses;

        DateOnly[] canonicalDates = marketSeries.Sessions
            .Where(session => session <= date)
            .OrderBy(session => session)
            .ToArray();
        bool hasTargetMarketSession = marketSeries.Sessions.Contains(date);
        bool hasTargetMarketClose = marketByDate.ContainsKey(date);
        IReadOnlyList<DateOnly> calculationDates = hasTargetMarketSession ? canonicalDates : [];

        DateOnly[] coverageDates = canonicalDates
            .TakeLast(requiredCanonicalSessions)
            .ToArray();
        DateOnly[] missingStockSessions = coverageDates
            .Where(session => !stockByDate.ContainsKey(session))
            .ToArray();
        DateOnly[] missingSectorSessions = coverageDates
            .Where(session => !sectorByDate.ContainsKey(session))
            .ToArray();
        DateOnly[] unavailableMarketCloseSessions = coverageDates
            .Where(session => !marketByDate.ContainsKey(session))
            .ToArray();
        DateOnly[] duplicateStockSessions = coverageDates
            .Where(stockSeries.DuplicateSessions.Contains)
            .ToArray();
        DateOnly[] duplicateSectorSessions = coverageDates
            .Where(sectorSeries.DuplicateSessions.Contains)
            .ToArray();
        DateOnly[] duplicateMarketSessions = coverageDates
            .Where(marketSeries.DuplicateSessions.Contains)
            .ToArray();

        var coverage = new RelativeStrengthCoverage(
            date,
            requiredCanonicalSessions,
            coverageDates,
            missingStockSessions,
            missingSectorSessions,
            unavailableMarketCloseSessions,
            duplicateStockSessions,
            duplicateSectorSessions,
            duplicateMarketSessions,
            hasTargetMarketSession,
            hasTargetMarketClose,
            HasGapAfterFirstObservation(coverageDates, stockByDate),
            HasGapAfterFirstObservation(coverageDates, sectorByDate));

        double? svs5 = ReturnDiff(stockByDate, sectorByDate, calculationDates, 5);
        double? svs10 = ReturnDiff(stockByDate, sectorByDate, calculationDates, 10);
        double? svs20 = ReturnDiff(stockByDate, sectorByDate, calculationDates, 20);
        double? svs60 = ReturnDiff(stockByDate, sectorByDate, calculationDates, 60);

        double? svm5 = ReturnDiff(stockByDate, marketByDate, calculationDates, 5);
        double? svm10 = ReturnDiff(stockByDate, marketByDate, calculationDates, 10);
        double? svm20 = ReturnDiff(stockByDate, marketByDate, calculationDates, 20);
        double? svm60 = ReturnDiff(stockByDate, marketByDate, calculationDates, 60);

        double? secvm5 = ReturnDiff(sectorByDate, marketByDate, calculationDates, 5);
        double? secvm10 = ReturnDiff(sectorByDate, marketByDate, calculationDates, 10);
        double? secvm20 = ReturnDiff(sectorByDate, marketByDate, calculationDates, 20);
        double? secvm60 = ReturnDiff(sectorByDate, marketByDate, calculationDates, 60);

        double? zSvs = ComputeRsZ(stockByDate, sectorByDate, calculationDates, ZScoreHorizon, zWindow);
        double? zSvm = ComputeRsZ(stockByDate, marketByDate, calculationDates, ZScoreHorizon, zWindow);
        double? zSecvm = ComputeRsZ(sectorByDate, marketByDate, calculationDates, ZScoreHorizon, zWindow);

        double? composite = (svm10, svs10, secvm10) switch
        {
            (not null, not null, not null) =>
                0.5 * svm10.Value + 0.3 * svs10.Value + 0.2 * secvm10.Value,
            _ => null
        };

        double? compositeZ = (zSvm, zSvs, zSecvm) switch
        {
            (not null, not null, not null) =>
                0.5 * zSvm.Value + 0.3 * zSvs.Value + 0.2 * zSecvm.Value,
            _ => null
        };

        var features = new RelativeStrengthRow
        {
            Symbol = symbol,
            Date = date,
            SectorIndexSymbol = sectorIndexSymbol,
            RS_StockVsSector_5d = svs5,
            RS_StockVsSector_10d = svs10,
            RS_StockVsSector_20d = svs20,
            RS_StockVsSector_60d = svs60,
            RS_StockVsMarket_5d = svm5,
            RS_StockVsMarket_10d = svm10,
            RS_StockVsMarket_20d = svm20,
            RS_StockVsMarket_60d = svm60,
            RS_SectorVsMarket_5d = secvm5,
            RS_SectorVsMarket_10d = secvm10,
            RS_SectorVsMarket_20d = secvm20,
            RS_SectorVsMarket_60d = secvm60,
            RS_Z_StockVsSector = zSvs,
            RS_Z_StockVsMarket = zSvm,
            RS_Z_SectorVsMarket = zSecvm,
            CompositeScore = composite,
            CompositeScoreZ = compositeZ,
        };

        return new RelativeStrengthCalculationResult(features, coverage);
    }

    private static double? ReturnDiff(
        IReadOnlyDictionary<DateOnly, double> a,
        IReadOnlyDictionary<DateOnly, double> b,
        IReadOnlyList<DateOnly> canonicalDates,
        int horizon)
    {
        if (canonicalDates.Count <= horizon)
            return null;

        DateOnly endDate = canonicalDates[^1];
        DateOnly startDate = canonicalDates[canonicalDates.Count - 1 - horizon];
        if (!a.TryGetValue(endDate, out double endA) ||
            !a.TryGetValue(startDate, out double startA) ||
            !b.TryGetValue(endDate, out double endB) ||
            !b.TryGetValue(startDate, out double startB))
        {
            return null;
        }

        double retA = (endA - startA) / startA;
        double retB = (endB - startB) / startB;
        return retA - retB;
    }

    private static double? ComputeRsZ(
        IReadOnlyDictionary<DateOnly, double> a,
        IReadOnlyDictionary<DateOnly, double> b,
        IReadOnlyList<DateOnly> canonicalDates,
        int horizon,
        int zWindow)
    {
        if (canonicalDates.Count < horizon + zWindow)
            return null;

        var rsValues = new double[zWindow];
        for (int i = 0; i < zWindow; i++)
        {
            int endIndex = canonicalDates.Count - zWindow + i;
            DateOnly endDate = canonicalDates[endIndex];
            DateOnly startDate = canonicalDates[endIndex - horizon];
            if (!a.TryGetValue(endDate, out double endA) ||
                !a.TryGetValue(startDate, out double startA) ||
                !b.TryGetValue(endDate, out double endB) ||
                !b.TryGetValue(startDate, out double startB))
            {
                return null;
            }

            double retA = (endA - startA) / startA;
            double retB = (endB - startB) / startB;
            rsValues[i] = retA - retB;
        }

        double mean = rsValues.Average();
        double variance = rsValues.Select(value => (value - mean) * (value - mean)).Average();
        double std = System.Math.Sqrt(variance);

        if (std < 1e-10)
            return 0.0;

        return (rsValues[^1] - mean) / std;
    }

    private static PriceSeries BuildSeries(IReadOnlyList<RelativeStrengthPricePoint> points)
    {
        var sessions = new HashSet<DateOnly>();
        var validCloses = new Dictionary<DateOnly, double>();
        var duplicateSessions = new HashSet<DateOnly>();
        foreach (RelativeStrengthPricePoint point in points)
        {
            if (!sessions.Add(point.Date))
            {
                // Multiple closes for one session are ambiguous. Do not choose one by source order;
                // make that exact observation unavailable and expose the duplicate in coverage.
                duplicateSessions.Add(point.Date);
                validCloses.Remove(point.Date);
                continue;
            }

            // Invalid source prices are unavailable observations, not zero-valued market facts.
            // Exact metrics that need this session remain null and coverage identifies the gap.
            if (!duplicateSessions.Contains(point.Date) &&
                double.IsFinite(point.Close) &&
                point.Close > 0)
                validCloses.Add(point.Date, point.Close);
        }

        return new PriceSeries(sessions, validCloses, duplicateSessions);
    }

    private static bool HasGapAfterFirstObservation(
        IReadOnlyList<DateOnly> canonicalDates,
        IReadOnlyDictionary<DateOnly, double> observations)
    {
        int firstObservedIndex = -1;
        for (int i = 0; i < canonicalDates.Count; i++)
        {
            if (observations.ContainsKey(canonicalDates[i]))
            {
                firstObservedIndex = i;
                break;
            }
        }

        if (firstObservedIndex < 0)
            return false;

        for (int i = firstObservedIndex; i < canonicalDates.Count; i++)
        {
            if (!observations.ContainsKey(canonicalDates[i]))
                return true;
        }

        return false;
    }

    private sealed record PriceSeries(
        IReadOnlySet<DateOnly> Sessions,
        IReadOnlyDictionary<DateOnly, double> ValidCloses,
        IReadOnlySet<DateOnly> DuplicateSessions);
}
