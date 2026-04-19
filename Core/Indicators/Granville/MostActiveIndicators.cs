using System.Collections.Generic;
using System.Linq;

namespace Core.Indicators.Granville;

/// <summary>
/// Granville's Most Active indicators (#11–#14).
///
/// Compares the direction of the 15 most active stocks by volume against the
/// benchmark (XIU) to detect divergences and confirmations — mirroring the
/// same logic as Plurality (#1–#4) but scoped to the most-traded issues.
///
/// "Losses predominate" = more than half of the most-active list closed down.
/// "Gains predominate"  = more than half of the most-active list closed up.
///
/// Reference: Granville, "A Strategy of Daily Stock Market Timing".
/// </summary>
public sealed class MostActiveIndicators : IGranvilleIndicatorGroup
{
    /// <summary>
    /// Minimum list size before evaluation is attempted.
    /// Fewer than this suggests the data pull was incomplete.
    /// </summary>
    private const int MinRequiredStocks = 8;

    /// <summary>
    /// Majority threshold: strictly more than half must be gains/losses.
    /// For the default 15-stock list this means ≥ 8.
    /// </summary>
    private static bool Majority(int count, int total) => count > total / 2;

    public IndicatorCategory Category => IndicatorCategory.Features;  // fix: was MostActive
    public string Name => "Most Active";

    public IReadOnlyList<GranvilleResult> Evaluate(GranvilleMarketContext context)
    {
        // Graceful degradation — return neutral when data isn't available.
        if (context.MostActiveStocks is null || context.MostActiveStocks.Count < MinRequiredStocks)
        {
            return
            [
                new GranvilleResult(
                    IndicatorNumber: 0,
                    Category: IndicatorCategory.Features,  // fix: was MostActive
                    Name: "Most Active: No Data",
                    Signal: IndicatorSignal.Neutral,
                    GranvillePoints: 0,
                    Description: context.MostActiveStocks is null
                        ? "Most-active stock list not provided — skipping #11–#14."
                        : $"Insufficient most-active data ({context.MostActiveStocks.Count} stocks, need ≥ {MinRequiredStocks}).")
            ];
        }

        var stocks = context.MostActiveStocks;
        int total  = stocks.Count;
        int gains  = stocks.Count(s => s.IsGain);
        int losses = stocks.Count(s => s.IsLoss);

        bool gainsPredominant  = Majority(gains,  total);
        bool lossesPredominant = Majority(losses, total);

        // XIU direction
        bool xiuFell = context.Today.XiuClose.HasValue
                    && context.Yesterday.XiuClose.HasValue
                    && context.Today.XiuClose.Value < context.Yesterday.XiuClose.Value;

        bool xiuRose = context.Today.XiuClose.HasValue
                    && context.Yesterday.XiuClose.HasValue
                    && context.Today.XiuClose.Value > context.Yesterday.XiuClose.Value;

        string activesSummary =
            $"{gains}/{total} most-active closed up, {losses}/{total} closed down " +
            $"| XIU {context.Yesterday.XiuClose:F2} → {context.Today.XiuClose:F2}";

        var results = new List<GranvilleResult>(2);

        // ── #11: Losses predominate + XIU down → expect further decline next day ──
        // Both the most-active list and the benchmark confirm weakness.
        // Mirrors #3 (Plurality: Decline Will Continue) → StrongBearish.
        if (lossesPredominant && xiuFell)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 11,
                Category: IndicatorCategory.Features,  // fix: was MostActive
                Name: "Most Active #11: Decline Expected to Continue",
                Signal: IndicatorSignal.StrongBearish,
                GranvillePoints: -2,
                Description: $"Losses predominate among most-active stocks AND XIU fell. " +
                             $"Market expected to fall tomorrow. ({activesSummary})"));
        }

        // ── #12: Gains predominate + XIU down → expect up day tomorrow ──
        // Most-active breadth is positive despite a falling benchmark — divergence bullish.
        // Mirrors #2 (Plurality: Verge of Advance) → Bullish.
        if (gainsPredominant && xiuFell)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 12,
                Category: IndicatorCategory.Features,  // fix: was MostActive
                Name: "Most Active #12: Advance Expected Tomorrow",
                Signal: IndicatorSignal.Bullish,
                GranvillePoints: +1,
                Description: $"Gains predominate among most-active stocks despite XIU decline — " +
                             $"divergence suggests an up day tomorrow. ({activesSummary})"));
        }

        // ── #13: Losses predominate + XIU up → market expected to go down next day ──
        // Most-active list is weak despite a rising benchmark — bearish divergence.
        // Mirrors #1 (Plurality: Verge of Decline) → Bearish.
        if (lossesPredominant && xiuRose)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 13,
                Category: IndicatorCategory.Features,  // fix: was MostActive
                Name: "Most Active #13: Decline Expected Tomorrow",
                Signal: IndicatorSignal.Bearish,
                GranvillePoints: -1,
                Description: $"Losses predominate among most-active stocks despite XIU advance — " +
                             $"divergence suggests a down day tomorrow. ({activesSummary})"));
        }

        // ── #14: Gains predominate + XIU up → market expected to rise next day ──
        // Both most-active breadth and benchmark confirm strength.
        // Mirrors #4 (Plurality: Advance Will Continue) → StrongBullish.
        if (gainsPredominant && xiuRose)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 14,
                Category: IndicatorCategory.Features,  // fix: was MostActive
                Name: "Most Active #14: Advance Expected to Continue",
                Signal: IndicatorSignal.StrongBullish,
                GranvillePoints: +2,
                Description: $"Gains predominate among most-active stocks AND XIU rose. " +
                             $"Market expected to rise tomorrow. ({activesSummary})"));
        }

        // Neutral when neither gains nor losses achieve a clear majority, or XIU unchanged.
        if (results.Count == 0)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 0,
                Category: IndicatorCategory.Features,  // fix: was MostActive
                Name: "Most Active: Neutral",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: $"No clear majority among most-active stocks or XIU unchanged. ({activesSummary})"));
        }

        return results;
    }
}