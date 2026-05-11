using Core.Config;
using Core.Db;
using Core.ML;

namespace Tools.Backtest.Weighting;

/// <summary>
/// Throwaway calibration script for Granville Weighting indicators (#15, #16).
///
/// Computes ScoreB (top-K concentration) and ScoreC (breadth) for the
/// XIU 60 constituents across all available history in dbo.DailyBars,
/// then prints a distribution summary and writes a per-day CSV.
///
/// Outputs: console report + Tools/Backtest.Weighting/output/xiu-breadth-{date}.csv
///
/// See Docs/reviews/open-questions.md (Weighting category) for the questions
/// this script is meant to answer empirically.
/// </summary>
internal static class Program
{
    private const string XiuSymbol = "XIU";
    private const int TopK = 3;
    private const int MinConstituentsRequired = 50;

    private static async Task<int> Main()
    {
        Console.WriteLine("=== XIU Breadth Calibration (Granville #15/#16) ===\n");

        var quotes = new QuoteRepository();

        Console.WriteLine($"Loading XIU bars...");
        var xiuBars = await quotes.GetDailyBarsAsync(XiuSymbol);
        if (xiuBars.Count == 0)
        {
            Console.Error.WriteLine($"ERROR: no bars for {XiuSymbol} in DailyBars. Run Hermes first.");
            return 1;
        }
        Console.WriteLine($"  {xiuBars.Count} XIU bars ({xiuBars[0].Date:yyyy-MM-dd}..{xiuBars[^1].Date:yyyy-MM-dd})");

        var symbols = Xiu60Constituents.Symbols;
        Console.WriteLine($"\nLoading bars for {symbols.Count} XIU constituents (list reviewed {Xiu60Constituents.LastReviewedUtc:yyyy-MM-dd})...");

        var bySymbol = new Dictionary<string, Dictionary<DateTime, DailyBar>>(StringComparer.OrdinalIgnoreCase);
        var missingSymbols = new List<string>();
        int loaded = 0;

        foreach (var sym in symbols)
        {
            var bars = await quotes.GetDailyBarsAsync(sym);
            if (bars.Count == 0)
            {
                missingSymbols.Add(sym);
                continue;
            }
            bySymbol[sym] = bars.ToDictionary(b => b.Date, b => b);
            loaded++;
        }

        Console.WriteLine($"  Loaded: {loaded}/{symbols.Count}");
        if (missingSymbols.Count > 0)
        {
            Console.WriteLine($"  Missing from DailyBars ({missingSymbols.Count}): {string.Join(", ", missingSymbols)}");
        }

        // ── Compute scores per XIU trading day ───────────────────────────
        Console.WriteLine($"\nComputing scores (TopK={TopK}, min constituents={MinConstituentsRequired})...");

        var rows = new List<DayRow>();
        DailyBar? prevXiu = null;

        foreach (var xiu in xiuBars)
        {
            if (prevXiu is null)
            {
                prevXiu = xiu;
                continue;
            }

            var xiuReturn = (xiu.Close - prevXiu.Close) / prevXiu.Close;
            int xiuDir = Math.Sign(xiuReturn);

            // Skip flat-XIU days: ScoreC has no meaningful "with-index" direction,
            // and ScoreB's same-direction set degenerates. These days are not
            // relevant to a narrow-leadership signal anyway.
            if (xiuDir == 0)
            {
                prevXiu = xiu;
                continue;
            }

            // Gather constituent (close, prevClose) pairs for this date
            var contribs = new List<(string Symbol, double Price, double Return)>();
            foreach (var (sym, byDate) in bySymbol)
            {
                if (!byDate.TryGetValue(xiu.Date, out var today)) continue;
                if (!byDate.TryGetValue(prevXiu.Date, out var yest)) continue;
                if (yest.Close <= 0) continue;

                var ret = (today.Close - yest.Close) / yest.Close;
                contribs.Add((sym, today.Close, ret));
            }

            if (contribs.Count < MinConstituentsRequired)
            {
                prevXiu = xiu;
                continue;
            }

            // Price-weighted contribution (Dow-style proxy):
            //   weight_i = price_i / Σ price_j
            //   contribution_i = weight_i × return_i
            double sumPrice = contribs.Sum(c => c.Price);
            var weighted = contribs
                .Select(c => (c.Symbol, Contribution: (c.Price / sumPrice) * c.Return, c.Return))
                .ToList();

            // ScoreB: concentration. Top-K of same-direction contributions
            // divided by sum of all same-direction contributions.
            var sameDir = weighted
                .Where(w => xiuDir == 0 ? false : Math.Sign(w.Contribution) == xiuDir)
                .OrderByDescending(w => Math.Abs(w.Contribution))
                .ToList();

            double scoreB = 0.0;
            string topNames = "";
            if (sameDir.Count > 0)
            {
                double topK = sameDir.Take(TopK).Sum(w => Math.Abs(w.Contribution));
                double total = sameDir.Sum(w => Math.Abs(w.Contribution));
                scoreB = total > 0 ? topK / total : 0.0;
                topNames = string.Join("|", sameDir.Take(TopK).Select(w => w.Symbol));
            }

            // ScoreC: narrowness = 1 - fraction-with-index.
            // "With index" = same sign as XIU; ties (return==0) excluded from numerator and denominator.
            int withIdx = 0;
            int directional = 0;
            foreach (var w in weighted)
            {
                int s = Math.Sign(w.Return);
                if (s == 0) continue;
                directional++;
                if (xiuDir != 0 && s == xiuDir) withIdx++;
            }
            double scoreC = directional > 0 ? 1.0 - ((double)withIdx / directional) : 0.0;

            rows.Add(new DayRow(
                Date: xiu.Date,
                XiuClose: xiu.Close,
                XiuReturn: xiuReturn,
                ConstituentsPresent: contribs.Count,
                ScoreB: scoreB,
                ScoreC: scoreC,
                TopContributors: topNames));

            prevXiu = xiu;
        }

        Console.WriteLine($"  Days scored: {rows.Count}");
        if (rows.Count == 0)
        {
            Console.Error.WriteLine("ERROR: no days had enough constituent coverage to score.");
            return 2;
        }

        // ── Distribution summary ─────────────────────────────────────────
        PrintDistribution("ScoreB (concentration)", rows.Select(r => r.ScoreB).ToList());
        PrintDistribution("ScoreC (narrowness)",   rows.Select(r => r.ScoreC).ToList());
        PrintHistogram("ScoreB", rows.Select(r => r.ScoreB).ToList());
        PrintHistogram("ScoreC", rows.Select(r => r.ScoreC).ToList());

        // Joint trigger rate at proposed v1 thresholds
        const double thresholdB = 0.50;
        const double thresholdC = 0.60;
        int triggers = rows.Count(r => r.ScoreB >= thresholdB && r.ScoreC >= thresholdC);
        Console.WriteLine($"\nProposed v1 thresholds: ScoreB ≥ {thresholdB:F2} AND ScoreC ≥ {thresholdC:F2}");
        Console.WriteLine($"  Days that would trigger: {triggers} / {rows.Count} ({100.0 * triggers / rows.Count:F1}%)");

        // ── Forward-return validation ────────────────────────────────────
        // For each scored day, compute the cumulative XIU return at +1, +5, +10
        // sessions (close-to-close, no overlap correction). Then for each
        // candidate rule, compare triggered days against the full sample
        // baseline. The "narrow advance" hypothesis predicts triggered days
        // should have *worse* forward returns than baseline.
        var xiuByDate = xiuBars
            .Select((b, i) => (b, i))
            .ToDictionary(t => t.b.Date, t => t.i);

        var fwd = new List<FwdRow>(rows.Count);
        foreach (var r in rows)
        {
            if (!xiuByDate.TryGetValue(r.Date, out var i)) continue;
            double? f1 = ForwardReturn(xiuBars, i, 1);
            double? f5 = ForwardReturn(xiuBars, i, 5);
            double? f10 = ForwardReturn(xiuBars, i, 10);
            fwd.Add(new FwdRow(r, f1, f5, f10));
        }

        var rules = new (string Name, Func<DayRow, bool> Pred)[]
        {
            ("Baseline (all days)",     _ => true),
            ("Baseline up-days",        r => r.XiuReturn > 0),
            ("Baseline down-days",      r => r.XiuReturn < 0),
            ("v1: B>=0.50 AND C>=0.60", r => r.ScoreB >= 0.50 && r.ScoreC >= 0.60),
            ("v1 ∩ up-days",            r => r.ScoreB >= 0.50 && r.ScoreC >= 0.60 && r.XiuReturn > 0),
            ("v1 ∩ down-days",          r => r.ScoreB >= 0.50 && r.ScoreC >= 0.60 && r.XiuReturn < 0),
            ("Opt1: B>=0.68 AND C>=0.51", r => r.ScoreB >= 0.68 && r.ScoreC >= 0.51),
            ("Opt1 ∩ up-days",          r => r.ScoreB >= 0.68 && r.ScoreC >= 0.51 && r.XiuReturn > 0),
            ("Opt1 ∩ down-days",        r => r.ScoreB >= 0.68 && r.ScoreC >= 0.51 && r.XiuReturn < 0),
            ("Opt2: C>=0.55 (alone)",   r => r.ScoreC >= 0.55),
            ("Opt2 ∩ up-days",          r => r.ScoreC >= 0.55 && r.XiuReturn > 0),
            ("Opt2 ∩ down-days",        r => r.ScoreC >= 0.55 && r.XiuReturn < 0),
        };

        Console.WriteLine("\n── Forward XIU return after trigger (close-to-close, %) ──");
        Console.WriteLine($"  {"Rule",-32} {"N",6} {"%",6}   {"mean1d",8} {"hit1d",7}   {"mean5d",8} {"hit5d",7}   {"mean10d",9} {"hit10d",7}");
        Console.WriteLine($"  {new string('-', 110)}");

        foreach (var (name, pred) in rules)
        {
            var subset = fwd.Where(f => pred(f.Day)).ToList();
            if (subset.Count == 0)
            {
                Console.WriteLine($"  {name,-32} {0,6} {0.0,6:F1}   (no matches)");
                continue;
            }
            var (m1, h1) = MeanHit(subset.Select(s => s.Fwd1));
            var (m5, h5) = MeanHit(subset.Select(s => s.Fwd5));
            var (m10, h10) = MeanHit(subset.Select(s => s.Fwd10));
            double pct = 100.0 * subset.Count / fwd.Count;
            Console.WriteLine(
                $"  {name,-32} {subset.Count,6} {pct,6:F1}   " +
                $"{m1 * 100,8:F3} {h1 * 100,6:F1}%   " +
                $"{m5 * 100,8:F3} {h5 * 100,6:F1}%   " +
                $"{m10 * 100,9:F3} {h10 * 100,6:F1}%");
        }

        Console.WriteLine("\n  (Narrow-advance hypothesis: triggered means should be LOWER than baseline.)");

        // ── Sub-period robustness check (in-sample / out-of-sample by date) ─
        // Split the scored days roughly in half by calendar so each window has
        // similar N. If v1 ∩ up-days shows a negative 1d/5d mean and depressed
        // hit-rate in BOTH halves, the narrow-advance edge is regime-stable.
        // If it appears in only one half, it's regime-specific and the ADR
        // must say so.
        var ordered = fwd.OrderBy(f => f.Day.Date).ToList();
        var splitDate = ordered[ordered.Count / 2].Day.Date;

        Console.WriteLine($"\n── Sub-period robustness (split at {splitDate:yyyy-MM-dd}) ──");
        Console.WriteLine($"  {"Rule",-28} {"Window",-22} {"N",4}  {"mean1d",8} {"hit1d",6}   {"mean5d",8} {"hit5d",6}   {"mean10d",9} {"hit10d",6}");
        Console.WriteLine($"  {new string('-', 112)}");

        var subPeriodRules = new (string Name, Func<DayRow, bool> Pred)[]
        {
            ("Baseline up-days",         r => r.XiuReturn > 0),
            ("v1 ∩ up-days",             r => r.ScoreB >= 0.50 && r.ScoreC >= 0.60 && r.XiuReturn > 0),
            ("v1 ∩ down-days",           r => r.ScoreB >= 0.50 && r.ScoreC >= 0.60 && r.XiuReturn < 0),
        };

        foreach (var (name, pred) in subPeriodRules)
        {
            PrintWindow(fwd, pred, name, "early", f => f.Day.Date <  splitDate);
            PrintWindow(fwd, pred, name, "late ", f => f.Day.Date >= splitDate);
        }

        // ── Write CSV ────────────────────────────────────────────────────
        var outDir = Path.Combine(AppContext.BaseDirectory, "output");
        Directory.CreateDirectory(outDir);
        var csvPath = Path.Combine(outDir, $"xiu-breadth-{DateTime.UtcNow:yyyyMMdd}.csv");

        await using (var sw = new StreamWriter(csvPath))
        {
            await sw.WriteLineAsync("Date,XiuClose,XiuReturnPct,ConstituentsPresent,ScoreB,ScoreC,TopContributors");
            foreach (var r in rows)
            {
                await sw.WriteLineAsync(
                    $"{r.Date:yyyy-MM-dd},{r.XiuClose:F4},{r.XiuReturn * 100:F4},{r.ConstituentsPresent}," +
                    $"{r.ScoreB:F4},{r.ScoreC:F4},{r.TopContributors}");
            }
        }

        Console.WriteLine($"\nCSV written: {csvPath}");
        Console.WriteLine("\n=== Done ===");
        return 0;
    }

    private static void PrintDistribution(string label, List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        double Pct(double p) => sorted[(int)Math.Clamp(Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1)];

        Console.WriteLine($"\n── {label} ──");
        Console.WriteLine($"  count  {sorted.Count}");
        Console.WriteLine($"  min    {sorted[0]:F4}");
        Console.WriteLine($"  p10    {Pct(0.10):F4}");
        Console.WriteLine($"  p25    {Pct(0.25):F4}");
        Console.WriteLine($"  p50    {Pct(0.50):F4}");
        Console.WriteLine($"  mean   {sorted.Average():F4}");
        Console.WriteLine($"  p75    {Pct(0.75):F4}");
        Console.WriteLine($"  p90    {Pct(0.90):F4}");
        Console.WriteLine($"  p95    {Pct(0.95):F4}");
        Console.WriteLine($"  p99    {Pct(0.99):F4}");
        Console.WriteLine($"  max    {sorted[^1]:F4}");
    }

    private static void PrintHistogram(string label, List<double> values)
    {
        Console.WriteLine($"\n── {label} histogram (10 bins, 0.0–1.0) ──");
        const int bins = 10;
        var counts = new int[bins];
        foreach (var v in values)
        {
            int idx = (int)Math.Clamp(Math.Floor(v * bins), 0, bins - 1);
            counts[idx]++;
        }
        int max = counts.Max();
        const int barWidth = 40;
        for (int i = 0; i < bins; i++)
        {
            double lo = i / (double)bins;
            double hi = (i + 1) / (double)bins;
            int barLen = max == 0 ? 0 : (int)Math.Round(counts[i] * (double)barWidth / max);
            Console.WriteLine($"  [{lo:F1}–{hi:F1})  {new string('█', barLen),-40}  {counts[i]}");
        }
    }

    private static double? ForwardReturn(IReadOnlyList<DailyBar> bars, int i, int horizon)
    {
        int target = i + horizon;
        if (target >= bars.Count) return null;
        double from = bars[i].Close;
        double to = bars[target].Close;
        if (from <= 0) return null;
        return (to - from) / from;
    }

    private static (double mean, double hitRate) MeanHit(IEnumerable<double?> rets)
    {
        var vs = rets.Where(r => r.HasValue).Select(r => r!.Value).ToList();
        if (vs.Count == 0) return (0.0, 0.0);
        return (vs.Average(), vs.Count(r => r > 0) / (double)vs.Count);
    }

    private static void PrintWindow(
        List<FwdRow> fwd,
        Func<DayRow, bool> rule,
        string ruleName,
        string windowLabel,
        Func<FwdRow, bool> windowPred)
    {
        var subset = fwd.Where(windowPred).Where(f => rule(f.Day)).ToList();
        if (subset.Count == 0)
        {
            Console.WriteLine($"  {ruleName,-28} {windowLabel,-22} {0,4}  (no matches)");
            return;
        }
        var first = subset.Min(s => s.Day.Date);
        var last  = subset.Max(s => s.Day.Date);
        var label = $"{windowLabel} {first:yyyy-MM}..{last:yyyy-MM}";
        var (m1, h1)   = MeanHit(subset.Select(s => s.Fwd1));
        var (m5, h5)   = MeanHit(subset.Select(s => s.Fwd5));
        var (m10, h10) = MeanHit(subset.Select(s => s.Fwd10));
        Console.WriteLine(
            $"  {ruleName,-28} {label,-22} {subset.Count,4}  " +
            $"{m1 * 100,8:F3} {h1 * 100,5:F1}%   " +
            $"{m5 * 100,8:F3} {h5 * 100,5:F1}%   " +
            $"{m10 * 100,9:F3} {h10 * 100,5:F1}%");
    }

    private sealed record FwdRow(DayRow Day, double? Fwd1, double? Fwd5, double? Fwd10);

    private sealed record DayRow(
        DateTime Date,
        double XiuClose,
        double XiuReturn,
        int ConstituentsPresent,
        double ScoreB,
        double ScoreC,
        string TopContributors);
}
