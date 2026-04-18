using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.RelativeStrength;

/// <summary>
/// Pure computation of relative strength features from price series.
/// No DB or I/O — takes aligned price arrays, returns RS values.
/// Used by both Delphi (live) and Hermes (backfill).
/// </summary>
public static class RelativeStrengthCalculator
{
    public static readonly int[] Horizons = [5, 10, 20, 60];

    /// <summary>
    /// Computes all RS features for a single stock on a single date.
    /// Price arrays must be sorted ascending by date, with the last element being the target date.
    /// </summary>
    /// <param name="stockCloses">Stock close prices, ascending by date. Minimum length = max horizon + Z window.</param>
    /// <param name="sectorCloses">Sector index close prices, same dates/length as stock.</param>
    /// <param name="marketCloses">Market benchmark (XIU) close prices, same dates/length as stock.</param>
    /// <param name="symbol">Stock ticker.</param>
    /// <param name="date">Date for this row.</param>
    /// <param name="sectorIndexSymbol">The sector index symbol (e.g., ^TTFS).</param>
    /// <param name="zWindow">Lookback for Z-score normalization of RS. Default 20.</param>
    public static RelativeStrengthRow Compute(
        IReadOnlyList<double> stockCloses,
        IReadOnlyList<double> sectorCloses,
        IReadOnlyList<double> marketCloses,
        string symbol,
        DateOnly date,
        string sectorIndexSymbol,
        int zWindow = 20)
    {
        int n = System.Math.Min(stockCloses.Count, System.Math.Min(sectorCloses.Count, marketCloses.Count));

        // Compute return differences per horizon
        double? svs5 = ReturnDiff(stockCloses, sectorCloses, n, 5);
        double? svs10 = ReturnDiff(stockCloses, sectorCloses, n, 10);
        double? svs20 = ReturnDiff(stockCloses, sectorCloses, n, 20);
        double? svs60 = ReturnDiff(stockCloses, sectorCloses, n, 60);

        double? svm5 = ReturnDiff(stockCloses, marketCloses, n, 5);
        double? svm10 = ReturnDiff(stockCloses, marketCloses, n, 10);
        double? svm20 = ReturnDiff(stockCloses, marketCloses, n, 20);
        double? svm60 = ReturnDiff(stockCloses, marketCloses, n, 60);

        double? secvm5 = ReturnDiff(sectorCloses, marketCloses, n, 5);
        double? secvm10 = ReturnDiff(sectorCloses, marketCloses, n, 10);
        double? secvm20 = ReturnDiff(sectorCloses, marketCloses, n, 20);
        double? secvm60 = ReturnDiff(sectorCloses, marketCloses, n, 60);

        // Z-scores: rolling std of the 10d RS over the last zWindow days
        double? zSvs = ComputeRsZ(stockCloses, sectorCloses, n, 10, zWindow);
        double? zSvm = ComputeRsZ(stockCloses, marketCloses, n, 10, zWindow);
        double? zSecvm = ComputeRsZ(sectorCloses, marketCloses, n, 10, zWindow);

        // Composite: weighted blend of 10d horizons (initial defaults)
        double? composite = (svm10, svs10, secvm10) switch
        {
            (not null, not null, not null) =>
                0.5 * svm10.Value + 0.3 * svs10.Value + 0.2 * secvm10.Value,
            _ => null
        };

        return new RelativeStrengthRow
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
        };
    }

    /// <summary>
    /// Return(a, horizon) - Return(b, horizon).
    /// Returns null if not enough data.
    /// </summary>
    private static double? ReturnDiff(
        IReadOnlyList<double> a, IReadOnlyList<double> b, int n, int horizon)
    {
        if (n <= horizon) return null;
        double retA = (a[n - 1] - a[n - 1 - horizon]) / a[n - 1 - horizon];
        double retB = (b[n - 1] - b[n - 1 - horizon]) / b[n - 1 - horizon];
        return retA - retB;
    }

    /// <summary>
    /// Z-score of today's RS(horizon) relative to a rolling window of RS(horizon) values.
    /// RS_Z = (RS_today - mean(RS_window)) / std(RS_window)
    /// </summary>
    private static double? ComputeRsZ(
        IReadOnlyList<double> a, IReadOnlyList<double> b, int n, int horizon, int zWindow)
    {
        // Need at least horizon + zWindow data points
        if (n < horizon + zWindow) return null;

        // Build RS series for the last zWindow days
        var rsValues = new double[zWindow];
        for (int i = 0; i < zWindow; i++)
        {
            int idx = n - zWindow + i; // end index for this day
            double retA = (a[idx] - a[idx - horizon]) / a[idx - horizon];
            double retB = (b[idx] - b[idx - horizon]) / b[idx - horizon];
            rsValues[i] = retA - retB;
        }

        double mean = rsValues.Average();
        double variance = rsValues.Select(v => (v - mean) * (v - mean)).Average();
        double std = System.Math.Sqrt(variance);

        if (std < 1e-10) return 0.0; // flat RS → Z = 0

        return (rsValues[^1] - mean) / std;
    }
}