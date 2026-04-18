using System.Collections.Generic;

namespace Core.Indicators.Granville;

/// <summary>
/// Granville's Leadership indicators (#7–#10).
///
/// Leadership measures which stocks or groups are doing the heavy lifting,
/// across three layers:
///   1. New-High/New-Low breadth (NHNL_10) — are winners still being produced?
///   2. Active-stock breadth (ActiveBreadth_10) — where is the urgent capital going?
///   3. Large-cap relative strength (LargeCapRS_20) — are leaders isolated or confirmed?
///
/// The indicators compare leadership quality (improving / deteriorating) against
/// the market's directional leg (upswing / downswing) to detect divergences
/// and confirmations.
///
/// Upswing/downswing is determined by the composite leadership state, not a
/// single day's price movement. See <see cref="LeadershipCalculator"/> for
/// the EMA-based swing definition.
///
/// #7: Quality deteriorates on an upswing   → near-term decline likely (Bearish)
/// #8: Quality deteriorates on a downswing  → near-term advance likely (Bullish)
/// #9: Quality improves on a downswing      → decline likely to continue (StrongBearish)
/// #10: Quality improves on an upswing      → advance likely to continue (StrongBullish)
///
/// Reference: Granville, "A Strategy of Daily Stock Market Timing", Leadership section.
/// </summary>
public sealed class LeadershipIndicators : IGranvilleIndicatorGroup
{
    public IndicatorCategory Category => IndicatorCategory.Leadership;
    public string Name => "Leadership";

    private readonly LeadershipCalculator _calculator = new();

    public IReadOnlyList<GranvilleResult> Evaluate(GranvilleMarketContext context)
    {
        var results = new List<GranvilleResult>(4);

        if (context.LeadershipHistory is not { Count: >= 12 })
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 0,
                Category: IndicatorCategory.Leadership,
                Name: "Leadership: No Data",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: "Insufficient leadership history (need ≥ 12 days). Skipping leadership analysis."));
            return results;
        }

        var state = _calculator.Compute(context.LeadershipHistory);
        var quality = _calculator.ComputeQuality(context.LeadershipHistory);

        // If either dimension is indeterminate, we can't fire a signal
        if (state == LeadershipState.Indeterminate || quality == LeadershipQuality.Indeterminate
            || quality == LeadershipQuality.Stable)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 0,
                Category: IndicatorCategory.Leadership,
                Name: "Leadership: Neutral",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: $"Leadership state: {state}, quality: {quality}. No clear divergence or confirmation."));
            return results;
        }

        // ── #7: Quality deteriorates + market upswing → bearish divergence ──
        if (quality == LeadershipQuality.Deteriorating && state == LeadershipState.Upswing)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 7,
                Category: IndicatorCategory.Leadership,
                Name: "Leadership #7: Deteriorating on Upswing",
                Signal: IndicatorSignal.Bearish,
                GranvillePoints: -1,
                Description: "Leadership quality is deteriorating (net new highs falling, active breadth weakening) " +
                             "while the market's leadership composite is in an upswing. " +
                             "Divergence suggests a near-term decline is in the making."));
        }

        // ── #8: Quality deteriorates + market downswing → bullish divergence ──
        if (quality == LeadershipQuality.Deteriorating && state == LeadershipState.Downswing)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 8,
                Category: IndicatorCategory.Leadership,
                Name: "Leadership #8: Deteriorating on Downswing",
                Signal: IndicatorSignal.Bullish,
                GranvillePoints: +2,
                Description: "Leadership quality is deteriorating while the market is already in a downswing. " +
                             "This exhaustion pattern suggests a near-term advance is in the making."));
        }

        // ── #9: Quality improves + market downswing → decline continues ──
        if (quality == LeadershipQuality.Improving && state == LeadershipState.Downswing)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 9,
                Category: IndicatorCategory.Leadership,
                Name: "Leadership #9: Improving on Downswing",
                Signal: IndicatorSignal.StrongBearish,
                GranvillePoints: -1,
                Description: "Leadership quality is improving but the market remains in a downswing. " +
                             "Improving leadership during a decline indicates the decline is likely to continue " +
                             "(leaders strengthening within a weak tape — not yet enough to reverse the trend)."));
        }

        // ── #10: Quality improves + market upswing → advance continues ──
        if (quality == LeadershipQuality.Improving && state == LeadershipState.Upswing)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 10,
                Category: IndicatorCategory.Leadership,
                Name: "Leadership #10: Improving on Upswing",
                Signal: IndicatorSignal.StrongBullish,
                GranvillePoints: +2,
                Description: "Leadership quality is improving and the market is in an upswing. " +
                             "Breadth confirms leadership — advance likely to continue."));
        }

        if (results.Count == 0)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 0,
                Category: IndicatorCategory.Leadership,
                Name: "Leadership: Neutral",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: $"Leadership state: {state}, quality: {quality}. No indicator triggered."));
        }

        return results;
    }
}