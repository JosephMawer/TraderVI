using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.TMX;
using SMath = System.Math;

namespace Core.Indicators.Granville;

/// <summary>
/// Granville's Genuity indicators (#17–#20) — verify whether XIU's daily move is "genuine"
/// by checking confirmation against broad US benchmarks (S&amp;P 500 and NYSE Composite).
///
/// Granville's original thesis: a benchmark move that is NOT confirmed by a broader index is
/// suspect ("non-genuine"). We adapt this to TSX/XIU by treating ^GSPC (S&amp;P 500) and ^NYA
/// (NYSE Composite) as the confirming indices — XIU's macroeconomic correlation to US large-caps
/// is high enough that a divergence is informative (see ADR-0004).
///
/// All four indicators short-circuit to a single stale-data Neutral when the most recent US bar
/// trails XIU's most recent bar by more than one trading day. This is the agreed staleness gate.
/// </summary>
public sealed class GenuityIndicators : IGranvilleIndicatorGroup
{
    public IndicatorCategory Category => IndicatorCategory.Genuity;
    public string Name => "Genuity";

    /// <summary>
    /// Max calendar-day gap allowed between XIU's most recent bar and a US confirming index's
    /// most recent bar before we declare the data stale. One day covers the common
    /// "Canadian session has closed, US session hasn't published yet" race.
    /// </summary>
    private const int StalenessThresholdDays = 1;

    /// <summary>
    /// Minimum |return| (as a decimal) we consider a "real" move. Anything tighter than this
    /// is treated as flat and same-day confirmation indicators (#17, #18, #19) short-circuit
    /// to Neutral. Confirming a near-zero move is noise: there is no real move to confirm.
    /// 10 bps was chosen as a prior — small enough to fire on routine sessions, large enough
    /// to suppress dead-flat tape (see ADR-0004 §Magnitude floor).
    /// </summary>
    private const double FlatReturnEpsilon = 0.0010; // 10 bps

    public IReadOnlyList<GranvilleResult> Evaluate(GranvilleMarketContext context)
    {
        // XIU return for today.
        if (!context.Today.XiuClose.HasValue || !context.Yesterday.XiuClose.HasValue)
            return [NeutralResult(17, "XIU close not available — Genuity short-circuit.")];

        double xiuPrior = (double)context.Yesterday.XiuClose.Value;
        double xiuToday = (double)context.Today.XiuClose.Value;
        if (xiuPrior <= 0)
            return [NeutralResult(17, "Prior XIU close not positive — Genuity short-circuit.")];

        double xiuReturn = (xiuToday / xiuPrior) - 1.0;
        DateTime xiuDate = context.Today.Date;

        // US confirming data.
        if (context.UsIndexBars is null || context.UsIndexBars.Count == 0)
            return [NeutralResult(17, "No US index bars in context — Genuity short-circuit.")];

        if (!TryGetReturnAndDate(context, UsIndexSymbols.SP500, lookbackBars: 1,
                out double sp500Return, out DateTime sp500Date, out int sp500Bars))
        {
            return [NeutralResult(17, $"^GSPC unavailable or insufficient history ({sp500Bars} bars).")];
        }
        if (!TryGetReturnAndDate(context, UsIndexSymbols.NyseComposite, lookbackBars: 1,
                out double nyseReturn, out DateTime nyseDate, out int nyseBars))
        {
            return [NeutralResult(18, $"^NYA unavailable or insufficient history ({nyseBars} bars).")];
        }

        // ── Staleness gate ─────────────────────────────────────────────
        // If either confirming index trails XIU by more than the threshold, we cannot say
        // anything genuine vs. non-genuine — emit a single stale-data Neutral covering #17–#20.
        int sp500GapDays = (xiuDate - sp500Date).Days;
        int nyseGapDays  = (xiuDate - nyseDate).Days;
        if (sp500GapDays > StalenessThresholdDays || nyseGapDays > StalenessThresholdDays)
        {
            string diag =
                $"XIU={xiuDate:yyyy-MM-dd} " +
                $"^GSPC={sp500Date:yyyy-MM-dd} (gap={sp500GapDays}d) " +
                $"^NYA={nyseDate:yyyy-MM-dd} (gap={nyseGapDays}d) " +
                $"threshold={StalenessThresholdDays}d";
            return
            [
                new GranvilleResult(
                    IndicatorNumber: 17,
                    Category: IndicatorCategory.Genuity,
                    Name: "Genuity: Stale US data",
                    Signal: IndicatorSignal.Neutral,
                    GranvillePoints: 0,
                    Description: "US confirming bars are stale — suppressing #17–#20. " + diag)
            ];
        }

        // ── Indicators #17–#20 ─────────────────────────────────────────
        var results = new List<GranvilleResult>(4);

        // #17 — S&P 500 same-day directional confirmation.
        results.Add(BuildDirectionalConfirmation(
            number: 17,
            label: "S&P 500 confirmation",
            xiuReturn: xiuReturn,
            usReturn: sp500Return,
            usSymbol: UsIndexSymbols.SP500));

        // #18 — NYSE Composite same-day directional confirmation (broader breadth).
        results.Add(BuildDirectionalConfirmation(
            number: 18,
            label: "NYSE Composite confirmation",
            xiuReturn: xiuReturn,
            usReturn: nyseReturn,
            usSymbol: UsIndexSymbols.NyseComposite));

        // #19 — Magnitude proportionality vs S&P 500.
        results.Add(BuildMagnitudeProportionality(xiuReturn, sp500Return));

        // #20 — 5-day trend alignment with S&P 500.
        results.Add(BuildTrendAlignment(context, xiuReturn));

        return results;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static GranvilleResult BuildDirectionalConfirmation(
        int number, string label, double xiuReturn, double usReturn, string usSymbol)
    {
        bool xiuFlat = SMath.Abs(xiuReturn) < FlatReturnEpsilon;
        bool usFlat  = SMath.Abs(usReturn)  < FlatReturnEpsilon;

        string diag =
            $"XIU={xiuReturn.ToString("F4", CultureInfo.InvariantCulture)} " +
            $"{usSymbol}={usReturn.ToString("F4", CultureInfo.InvariantCulture)}";

        if (xiuFlat || usFlat)
        {
            return new GranvilleResult(
                IndicatorNumber: number,
                Category: IndicatorCategory.Genuity,
                Name: $"Genuity #{number}: {label}",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: $"Move too small to evaluate (epsilon={FlatReturnEpsilon:F4}). " + diag);
        }

        // Same sign → confirmation; signal magnitude follows XIU's direction.
        if (SMath.Sign(xiuReturn) == SMath.Sign(usReturn))
        {
            bool bullish = xiuReturn > 0;
            string directionWord = bullish ? "upside" : "downside";
            return new GranvilleResult(
                IndicatorNumber: number,
                Category: IndicatorCategory.Genuity,
                Name: $"Genuity #{number}: {label} ({directionWord} confirmed)",
                Signal: bullish ? IndicatorSignal.Bullish : IndicatorSignal.Bearish,
                GranvillePoints: bullish ? +1 : -1,
                Description: $"XIU {directionWord} move confirmed by {usSymbol} (same direction). " + diag);
        }

        // Opposite sign → non-genuine: invert XIU's directional implication.
        bool xiuUpButUsDown = xiuReturn > 0 && usReturn < 0;
        return new GranvilleResult(
            IndicatorNumber: number,
            Category: IndicatorCategory.Genuity,
            Name: $"Genuity #{number}: {label} (non-confirmation)",
            Signal: xiuUpButUsDown ? IndicatorSignal.Bearish : IndicatorSignal.Bullish,
            GranvillePoints: xiuUpButUsDown ? -1 : +1,
            Description: $"XIU and {usSymbol} disagree — non-genuine move. " + diag);
    }

    /// <summary>
    /// #19 — magnitude proportionality. If XIU's move is wildly out of proportion to the S&P's
    /// (more than 3× or less than 1/3×) while sharing direction, the move is suspect.
    /// </summary>
    private static GranvilleResult BuildMagnitudeProportionality(double xiuReturn, double sp500Return)
    {
        bool xiuFlat = SMath.Abs(xiuReturn) < FlatReturnEpsilon;
        bool spFlat  = SMath.Abs(sp500Return) < FlatReturnEpsilon;

        string diag =
            $"XIU={xiuReturn.ToString("F4", CultureInfo.InvariantCulture)} " +
            $"^GSPC={sp500Return.ToString("F4", CultureInfo.InvariantCulture)}";

        if (xiuFlat || spFlat || SMath.Sign(xiuReturn) != SMath.Sign(sp500Return))
        {
            return new GranvilleResult(
                IndicatorNumber: 19,
                Category: IndicatorCategory.Genuity,
                Name: "Genuity #19: Magnitude proportionality",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: "Direction disagreement or flat — magnitude check not applicable. " + diag);
        }

        double ratio = SMath.Abs(xiuReturn) / SMath.Abs(sp500Return);
        const double upperBound = 3.0;
        const double lowerBound = 1.0 / 3.0;

        if (ratio > upperBound || ratio < lowerBound)
        {
            // Disproportionate move in the same direction — invert XIU's directional implication.
            bool xiuBullish = xiuReturn > 0;
            return new GranvilleResult(
                IndicatorNumber: 19,
                Category: IndicatorCategory.Genuity,
                Name: "Genuity #19: Magnitude disproportionate",
                Signal: xiuBullish ? IndicatorSignal.Bearish : IndicatorSignal.Bullish,
                GranvillePoints: xiuBullish ? -1 : +1,
                Description:
                    $"XIU/^GSPC magnitude ratio={ratio.ToString("F2", CultureInfo.InvariantCulture)} " +
                    $"outside [{lowerBound:F2},{upperBound:F2}] — directional move not proportionate. " + diag);
        }

        return new GranvilleResult(
            IndicatorNumber: 19,
            Category: IndicatorCategory.Genuity,
            Name: "Genuity #19: Magnitude proportionate",
            Signal: IndicatorSignal.Neutral,
            GranvillePoints: 0,
            Description:
                $"XIU/^GSPC magnitude ratio={ratio.ToString("F2", CultureInfo.InvariantCulture)} " +
                $"within [{lowerBound:F2},{upperBound:F2}]. " + diag);
    }

    /// <summary>
    /// #20 — 5-day trend alignment between XIU and S&amp;P 500.
    /// Aligned = bullish/bearish per XIU; misaligned = invert XIU direction (suspect trend).
    /// </summary>
    private static GranvilleResult BuildTrendAlignment(GranvilleMarketContext context, double xiuReturn)
    {
        const int window = 5;

        // XIU 5d return from RecentHistory (already ordered ascending).
        var xiuHistory = context.RecentHistory
            .Where(e => e.XiuClose.HasValue)
            .ToList();
        if (xiuHistory.Count < window + 1)
        {
            return new GranvilleResult(
                IndicatorNumber: 20,
                Category: IndicatorCategory.Genuity,
                Name: "Genuity #20: Trend alignment",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: $"Insufficient XIU history for {window}-day trend (need ≥ {window + 1} bars).");
        }

        double xiuNow  = (double)xiuHistory[^1].XiuClose!.Value;
        double xiuPast = (double)xiuHistory[^(window + 1)].XiuClose!.Value;
        if (xiuPast <= 0)
        {
            return new GranvilleResult(20, IndicatorCategory.Genuity, "Genuity #20: Trend alignment",
                IndicatorSignal.Neutral, 0, "XIU window start not positive — skipped.");
        }
        double xiu5d = (xiuNow / xiuPast) - 1.0;

        // S&P 500 5d return.
        if (!TryGet5DayReturn(context, UsIndexSymbols.SP500, window, out double sp5d, out int sp500Bars))
        {
            return new GranvilleResult(20, IndicatorCategory.Genuity, "Genuity #20: Trend alignment",
                IndicatorSignal.Neutral, 0,
                $"^GSPC insufficient history for {window}-day trend (have {sp500Bars} bars).");
        }

        string diag =
            $"XIU5d={xiu5d.ToString("F4", CultureInfo.InvariantCulture)} " +
            $"^GSPC5d={sp5d.ToString("F4", CultureInfo.InvariantCulture)} " +
            $"todayXiuRet={xiuReturn.ToString("F4", CultureInfo.InvariantCulture)}";

        if (SMath.Abs(xiu5d) < FlatReturnEpsilon || SMath.Abs(sp5d) < FlatReturnEpsilon)
        {
            return new GranvilleResult(20, IndicatorCategory.Genuity, "Genuity #20: Trend alignment",
                IndicatorSignal.Neutral, 0, "5-day trend too small to evaluate. " + diag);
        }

        if (SMath.Sign(xiu5d) == SMath.Sign(sp5d))
        {
            bool bullish = xiu5d > 0;
            return new GranvilleResult(20, IndicatorCategory.Genuity,
                "Genuity #20: Trend aligned",
                bullish ? IndicatorSignal.Bullish : IndicatorSignal.Bearish,
                bullish ? +1 : -1,
                "XIU and ^GSPC 5-day trends agree — cross-border trend confirmed. " + diag);
        }

        bool xiuUp = xiu5d > 0;
        return new GranvilleResult(20, IndicatorCategory.Genuity,
            "Genuity #20: Trend divergence",
            xiuUp ? IndicatorSignal.Bearish : IndicatorSignal.Bullish,
            xiuUp ? -1 : +1,
            "XIU and ^GSPC 5-day trends disagree — trend lacks cross-border confirmation. " + diag);
    }

    private static bool TryGetReturnAndDate(
        GranvilleMarketContext ctx, string symbol, int lookbackBars,
        out double dailyReturn, out DateTime barDate, out int barsAvailable)
    {
        dailyReturn = 0;
        barDate = default;
        barsAvailable = 0;

        if (ctx.UsIndexBars is null) return false;
        if (!ctx.UsIndexBars.TryGetValue(symbol, out var bars) || bars is null) return false;
        barsAvailable = bars.Count;
        if (bars.Count < lookbackBars + 1) return false;

        var today = bars[^1];
        var prior = bars[^(lookbackBars + 1)];
        if (prior.Close <= 0) return false;

        dailyReturn = (today.Close / prior.Close) - 1.0;
        barDate = today.Date;
        return true;
    }

    private static bool TryGet5DayReturn(
        GranvilleMarketContext ctx, string symbol, int window,
        out double windowReturn, out int barsAvailable)
    {
        windowReturn = 0;
        barsAvailable = 0;
        if (ctx.UsIndexBars is null) return false;
        if (!ctx.UsIndexBars.TryGetValue(symbol, out var bars) || bars is null) return false;
        barsAvailable = bars.Count;
        if (bars.Count < window + 1) return false;

        double now  = bars[^1].Close;
        double past = bars[^(window + 1)].Close;
        if (past <= 0) return false;

        windowReturn = (now / past) - 1.0;
        return true;
    }

    private static GranvilleResult NeutralResult(int indicatorNumber, string description) =>
        new(
            IndicatorNumber: indicatorNumber,
            Category: IndicatorCategory.Genuity,
            Name: "Genuity: Neutral",
            Signal: IndicatorSignal.Neutral,
            GranvillePoints: 0,
            Description: description);
}
