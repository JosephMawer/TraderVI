using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Db;
using Core.ML;

namespace Sandbox.Probes;

/// <summary>
/// Calibration backtest for the "Dullness" thresholds used by Granville
/// indicators #21 and #22.
///
/// Definition under test (Option C — all three conditions must hold):
///   D1: today's XIU volume &lt; <see cref="VolRatioCut"/> × rolling-20-day median volume.
///   D2: today's intraday range ratio (High-Low)/Close &lt; <see cref="RangeRatioCut"/>
///       × rolling-20-day median range ratio.
///   D3: |today's close-to-close % change| &lt; <see cref="PctMoveCut"/> (default 0.25%).
///
/// "Prior trend" classifier (placeholder — will be firmed up before the rule ships):
///   sign of the 5-trading-day return into t. Positive ⇒ post-advance ⇒ #21 (bearish);
///   negative ⇒ post-decline ⇒ #22 (bullish); flat ⇒ neither.
///
/// Outputs three things:
///   1. Per-condition fire rates and combined "Dullness" fire rate.
///   2. Hit-rate / mean-forward-return tables for #21 and #22 at h ∈ {1,3,5,10}.
///   3. A single-axis sensitivity sweep over each of the three thresholds.
///
/// Also dumps a per-day CSV to <c>dullness-backtest.csv</c> in the current
/// working directory so the raw data can be re-analyzed in Excel / pandas
/// without rerunning the probe.
/// </summary>
public sealed class DullnessCalibrationProbe : IProbe
{
    public string Slug => "dullness-calibrate";
    public string Description => "Backtest Dullness (Granville #21/#22) thresholds against historical XIU bars.";

    // ── Default thresholds under test (the initial proposals from the design conversation) ──
    private const double VolRatioCut = 0.70;    // D1: today's vol / median20(vol)
    private const double RangeRatioCut = 0.60;  // D2: today's range-ratio / median20(range-ratio)
    private const double PctMoveCut = 0.0025;   // D3: |close-to-close %| (0.25%)

    private const int RollingWindow = 20;       // baseline window for medians
    private const int PriorTrendWindow = 20;    // lookback window for the "prior advance / decline" classifier

    // Proximity-based trend classifier (tightened version after first calibration showed
    // 5-day-return signs were too noisy):
    //   Advance = today's close >= AdvanceHighProximity × max(close, prior W days)
    //   Decline = today's close <= DeclineLowProximity  × min(close, prior W days)
    // Anything else (or both true on a very flat window) = Flat.
    private const double AdvanceHighProximity = 0.98; // within 2% of 20-day peak
    private const double DeclineLowProximity  = 1.02; // within 2% of 20-day trough

    private static readonly int[] ForwardHorizons = [1, 3, 5, 10];

    private const string Symbol = "XIU";
    private const string CsvOutputFile = "dullness-backtest.csv";

    public async Task RunAsync()
    {
        Console.WriteLine($"=== Dullness calibration backtest — {Symbol} ===");
        Console.WriteLine($"Defaults under test:  D1 vol<{VolRatioCut:P0}·med20    D2 range<{RangeRatioCut:P0}·med20    D3 |Δclose|<{PctMoveCut:P2}");
        Console.WriteLine($"Baseline window: {RollingWindow} d   |   Prior-trend: close within 2% of {PriorTrendWindow}-d high (advance) or low (decline)   |   Forward horizons: {string.Join(",", ForwardHorizons)}");
        Console.WriteLine();

        var repo = new QuoteRepository();
        var bars = await repo.GetDailyBarsAsync(Symbol);
        if (bars.Count < RollingWindow + ForwardHorizons.Max() + PriorTrendWindow + 5)
        {
            Console.WriteLine($"⚠️  Not enough {Symbol} bars to backtest (have {bars.Count}). Aborting.");
            return;
        }

        // Sort ascending defensively — repo already returns ASC but pay the O(n log n) for safety.
        bars = bars.OrderBy(b => b.Date).ToList();
        Console.WriteLine($"Loaded {bars.Count} bars   {bars[0].Date:yyyy-MM-dd} → {bars[^1].Date:yyyy-MM-dd}");
        Console.WriteLine();

        var rows = BuildRows(bars);

        // ── 1. Per-condition fire-rate table ──
        PrintFireRates(rows);

        // ── 2. Hit-rate / forward-return tables for #21 and #22 ──
        PrintHitRateTables(rows);

        // ── 3. Single-axis sensitivity sweep ──
        PrintSensitivitySweep(rows);

        // ── 4. CSV dump ──
        var csvPath = Path.GetFullPath(CsvOutputFile);
        WriteCsv(rows, csvPath);
        Console.WriteLine();
        Console.WriteLine($"Wrote per-day backtest data → {csvPath}");
    }

    // ════════════════════════════════════════════════════════════════════
    //  Row construction
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Per-day backtest record. All ratio/return fields are NaN when the lookback
    /// window for that field hasn't been satisfied yet (warmup days).
    /// </summary>
    private sealed record BacktestRow(
        DateTime Date,
        double Close,
        long Volume,
        double RangeRatio,           // (High-Low)/Close
        double VolMedian20,
        double RangeMedian20,
        double VolRatio,             // today / median20
        double RangeRatioToMed,      // today / median20
        double PctMove,              // signed close-to-close % vs. yesterday
        double HighProximity,        // today's close / max(close, prior PriorTrendWindow days). 1.0 = at peak.
        double LowProximity,         // today's close / min(close, prior PriorTrendWindow days). 1.0 = at trough.
        double[] FwdReturns          // aligned with ForwardHorizons
    )
    {
        public bool D1(double cut) => !double.IsNaN(VolRatio) && VolRatio < cut;
        public bool D2(double cut) => !double.IsNaN(RangeRatioToMed) && RangeRatioToMed < cut;
        public bool D3(double cut) => !double.IsNaN(PctMove) && Math.Abs(PctMove) < cut;
        public bool IsDull(double c1, double c2, double c3) => D1(c1) && D2(c2) && D3(c3);

        public PriorTrend Trend
        {
            get
            {
                if (double.IsNaN(HighProximity) || double.IsNaN(LowProximity)) return PriorTrend.Unknown;
                bool nearHigh = HighProximity >= AdvanceHighProximity;
                bool nearLow  = LowProximity  <= DeclineLowProximity;
                // On extremely tight 20-day ranges (< ~4%) a day can technically satisfy both.
                // Treat that as a chop / no-clear-trend state rather than double-counting.
                if (nearHigh && nearLow) return PriorTrend.Flat;
                if (nearHigh) return PriorTrend.Advance;
                if (nearLow)  return PriorTrend.Decline;
                return PriorTrend.Flat;
            }
        }
    }

    private enum PriorTrend { Unknown, Advance, Decline, Flat }

    private static List<BacktestRow> BuildRows(IReadOnlyList<DailyBar> bars)
    {
        int n = bars.Count;
        int H = ForwardHorizons.Max();
        var rows = new List<BacktestRow>(n);

        // Precompute per-day range ratio.
        var rangeRatio = new double[n];
        for (int i = 0; i < n; i++)
        {
            float close = bars[i].Close;
            rangeRatio[i] = close > 0 ? (bars[i].High - bars[i].Low) / (double)close : double.NaN;
        }

        for (int i = 0; i < n; i++)
        {
            double volMed = double.NaN, rngMed = double.NaN, volRatio = double.NaN, rngRatioToMed = double.NaN;
            double pctMove = double.NaN, highProx = double.NaN, lowProx = double.NaN;
            var fwd = new double[ForwardHorizons.Length];
            Array.Fill(fwd, double.NaN);

            // Rolling medians require RollingWindow prior bars (strict, not including today).
            if (i >= RollingWindow)
            {
                volMed = Median(bars, i - RollingWindow, i, b => b.Volume);
                rngMed = Median(rangeRatio, i - RollingWindow, i);

                if (volMed > 0) volRatio = bars[i].Volume / volMed;
                if (rngMed > 0) rngRatioToMed = rangeRatio[i] / rngMed;
            }

            // % move vs. yesterday.
            if (i >= 1 && bars[i - 1].Close > 0)
                pctMove = (bars[i].Close - bars[i - 1].Close) / (double)bars[i - 1].Close;

            // Prior-trend classifier: today's close vs. max/min of the prior PriorTrendWindow closes.
            // We use STRICTLY PRIOR closes (i-W .. i-1), not including today, so a single-day spike
            // does not auto-classify itself as "near the high."
            if (i >= PriorTrendWindow)
            {
                float hi = float.MinValue, lo = float.MaxValue;
                for (int k = i - PriorTrendWindow; k < i; k++)
                {
                    if (bars[k].Close > hi) hi = bars[k].Close;
                    if (bars[k].Close < lo) lo = bars[k].Close;
                }
                if (hi > 0) highProx = bars[i].Close / (double)hi;
                if (lo > 0) lowProx  = bars[i].Close / (double)lo;
            }

            // Forward returns.
            for (int h = 0; h < ForwardHorizons.Length; h++)
            {
                int j = i + ForwardHorizons[h];
                if (j < n && bars[i].Close > 0)
                    fwd[h] = (bars[j].Close - bars[i].Close) / (double)bars[i].Close;
            }

            rows.Add(new BacktestRow(
                bars[i].Date, bars[i].Close, bars[i].Volume,
                rangeRatio[i], volMed, rngMed, volRatio, rngRatioToMed,
                pctMove, highProx, lowProx, fwd));
        }

        return rows;
    }

    private static double Median(IReadOnlyList<DailyBar> bars, int startInclusive, int endExclusive, Func<DailyBar, double> selector)
    {
        int len = endExclusive - startInclusive;
        var buf = new double[len];
        for (int k = 0; k < len; k++) buf[k] = selector(bars[startInclusive + k]);
        return MedianInPlace(buf);
    }

    private static double Median(IReadOnlyList<double> src, int startInclusive, int endExclusive)
    {
        int len = endExclusive - startInclusive;
        var buf = new double[len];
        for (int k = 0; k < len; k++) buf[k] = src[startInclusive + k];
        return MedianInPlace(buf);
    }

    private static double MedianInPlace(double[] buf)
    {
        Array.Sort(buf);
        int n = buf.Length;
        return (n % 2 == 1) ? buf[n / 2] : 0.5 * (buf[n / 2 - 1] + buf[n / 2]);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Reports
    // ════════════════════════════════════════════════════════════════════

    private static void PrintFireRates(IReadOnlyList<BacktestRow> rows)
    {
        // Eligible days = those with all three components computable.
        var eligible = rows.Where(r =>
            !double.IsNaN(r.VolRatio) && !double.IsNaN(r.RangeRatioToMed) && !double.IsNaN(r.PctMove)).ToList();

        int total = eligible.Count;
        int d1 = eligible.Count(r => r.D1(VolRatioCut));
        int d2 = eligible.Count(r => r.D2(RangeRatioCut));
        int d3 = eligible.Count(r => r.D3(PctMoveCut));
        int dull = eligible.Count(r => r.IsDull(VolRatioCut, RangeRatioCut, PctMoveCut));

        Console.WriteLine("── 1. Fire rates (eligible days = {0}) ─────────────────────────", total);
        Console.WriteLine($"{"Condition",-22} {"Fires",8} {"% of days",12}");
        Console.WriteLine(new string('─', 46));
        Console.WriteLine($"{"D1 (vol<med×cut)",-22} {d1,8} {(double)d1 / total,12:P2}");
        Console.WriteLine($"{"D2 (range<med×cut)",-22} {d2,8} {(double)d2 / total,12:P2}");
        Console.WriteLine($"{"D3 (|Δclose|<cut)",-22} {d3,8} {(double)d3 / total,12:P2}");
        Console.WriteLine($"{"ALL THREE (Dullness)",-22} {dull,8} {(double)dull / total,12:P2}");
        Console.WriteLine();
    }

    private static void PrintHitRateTables(IReadOnlyList<BacktestRow> rows)
    {
        var dullRows = rows.Where(r => r.IsDull(VolRatioCut, RangeRatioCut, PctMoveCut)).ToList();
        var postAdvance = dullRows.Where(r => r.Trend == PriorTrend.Advance).ToList();
        var postDecline = dullRows.Where(r => r.Trend == PriorTrend.Decline).ToList();
        var flat        = dullRows.Where(r => r.Trend == PriorTrend.Flat).ToList();
        var unknown     = dullRows.Where(r => r.Trend == PriorTrend.Unknown).ToList();

        Console.WriteLine("── 2. Forward-return performance of Dullness (all three D1∧D2∧D3) ──");
        Console.WriteLine($"Total dull days = {dullRows.Count}   post-advance = {postAdvance.Count}   post-decline = {postDecline.Count}   flat = {flat.Count}   unknown = {unknown.Count}");
        Console.WriteLine();

        Console.WriteLine("Indicator #21 — Dull AFTER an advance (expect XIU DOWN ⇒ hit = forward return < 0)");
        PrintHorizonRow(postAdvance, expectDown: true);
        Console.WriteLine();

        Console.WriteLine("Indicator #22 — Dull AFTER a decline (expect XIU UP ⇒ hit = forward return > 0)");
        PrintHorizonRow(postDecline, expectDown: false);
        Console.WriteLine();
    }

    private static void PrintHorizonRow(IReadOnlyList<BacktestRow> rows, bool expectDown)
    {
        Console.WriteLine($"{"Horizon",-10} {"N",6} {"Hit %",10} {"Mean fwd ret",16} {"Median fwd ret",16}");
        Console.WriteLine(new string('─', 60));

        for (int h = 0; h < ForwardHorizons.Length; h++)
        {
            int horizon = ForwardHorizons[h];
            var fwd = rows.Select(r => r.FwdReturns[h]).Where(v => !double.IsNaN(v)).ToList();
            if (fwd.Count == 0)
            {
                Console.WriteLine($"h={horizon,-8} {"—",6} {"—",10} {"—",16} {"—",16}");
                continue;
            }

            int hits = fwd.Count(v => expectDown ? v < 0 : v > 0);
            double mean = fwd.Average();
            double median = MedianInPlace(fwd.ToArray());

            Console.WriteLine($"h={horizon,-8} {fwd.Count,6} {(double)hits / fwd.Count,10:P2} {mean,16:P3} {median,16:P3}");
        }
    }

    private static void PrintSensitivitySweep(IReadOnlyList<BacktestRow> rows)
    {
        Console.WriteLine("── 3. Single-axis sensitivity sweep (each row: vary one cut, hold others at default) ──");
        Console.WriteLine("Reported: Dullness fire rate, then 5-day post-advance / post-decline hit rates.");
        Console.WriteLine();

        SweepAxis("D1 vol cut",   [0.50, 0.60, 0.70, 0.80, 0.90], rows, (r, c) => r.IsDull(c, RangeRatioCut, PctMoveCut));
        Console.WriteLine();
        SweepAxis("D2 range cut", [0.40, 0.50, 0.60, 0.70, 0.80], rows, (r, c) => r.IsDull(VolRatioCut, c, PctMoveCut));
        Console.WriteLine();
        SweepAxis("D3 |Δ%| cut",  [0.0015, 0.0025, 0.0035, 0.0050, 0.0075], rows, (r, c) => r.IsDull(VolRatioCut, RangeRatioCut, c));
    }

    private static void SweepAxis(string label, double[] values, IReadOnlyList<BacktestRow> rows, Func<BacktestRow, double, bool> isDull)
    {
        int eligible = rows.Count(r => !double.IsNaN(r.VolRatio) && !double.IsNaN(r.RangeRatioToMed) && !double.IsNaN(r.PctMove));
        int h5Idx = Array.IndexOf(ForwardHorizons, 5);

        Console.WriteLine($"{label}");
        Console.WriteLine($"  {"cut",10} {"fires",8} {"fire%",10} {"#21 hit% (5d)",18} {"#22 hit% (5d)",18}");
        Console.WriteLine($"  {new string('─', 70)}");
        foreach (var v in values)
        {
            var fires = rows.Where(r => isDull(r, v)).ToList();
            int N = fires.Count;

            string adv = "—", dec = "—";
            if (h5Idx >= 0)
            {
                var advReturns = fires.Where(r => r.Trend == PriorTrend.Advance).Select(r => r.FwdReturns[h5Idx]).Where(x => !double.IsNaN(x)).ToList();
                var decReturns = fires.Where(r => r.Trend == PriorTrend.Decline).Select(r => r.FwdReturns[h5Idx]).Where(x => !double.IsNaN(x)).ToList();
                if (advReturns.Count > 0) adv = $"{(double)advReturns.Count(x => x < 0) / advReturns.Count:P1} (n={advReturns.Count})";
                if (decReturns.Count > 0) dec = $"{(double)decReturns.Count(x => x > 0) / decReturns.Count:P1} (n={decReturns.Count})";
            }

            string formatCut = v < 0.1 ? v.ToString("P2", CultureInfo.InvariantCulture) : v.ToString("F2", CultureInfo.InvariantCulture);
            Console.WriteLine($"  {formatCut,10} {N,8} {(double)N / eligible,10:P2} {adv,18} {dec,18}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CSV dump
    // ════════════════════════════════════════════════════════════════════

    private static void WriteCsv(IReadOnlyList<BacktestRow> rows, string path)
    {
        var sb = new StringBuilder();
        sb.Append("Date,Close,Volume,RangeRatio,VolMedian20,RangeMedian20,VolRatio,RangeRatioToMed,PctMove,HighProximity,LowProximity,Trend,D1,D2,D3,IsDull");
        foreach (var h in ForwardHorizons) sb.Append(",Fwd").Append(h).Append('d');
        sb.AppendLine();

        foreach (var r in rows)
        {
            sb.Append(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Fmt(r.Close)).Append(',');
            sb.Append(r.Volume).Append(',');
            sb.Append(Fmt(r.RangeRatio)).Append(',');
            sb.Append(Fmt(r.VolMedian20)).Append(',');
            sb.Append(Fmt(r.RangeMedian20)).Append(',');
            sb.Append(Fmt(r.VolRatio)).Append(',');
            sb.Append(Fmt(r.RangeRatioToMed)).Append(',');
            sb.Append(Fmt(r.PctMove)).Append(',');
            sb.Append(Fmt(r.HighProximity)).Append(',');
            sb.Append(Fmt(r.LowProximity)).Append(',');
            sb.Append(r.Trend).Append(',');
            sb.Append(r.D1(VolRatioCut) ? 1 : 0).Append(',');
            sb.Append(r.D2(RangeRatioCut) ? 1 : 0).Append(',');
            sb.Append(r.D3(PctMoveCut) ? 1 : 0).Append(',');
            sb.Append(r.IsDull(VolRatioCut, RangeRatioCut, PctMoveCut) ? 1 : 0);
            for (int h = 0; h < ForwardHorizons.Length; h++)
            {
                sb.Append(',').Append(Fmt(r.FwdReturns[h]));
            }
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string Fmt(double v) =>
        double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
