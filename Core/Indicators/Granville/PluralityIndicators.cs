using System.Collections.Generic;

namespace Core.Indicators.Granville;

/// <summary>
/// Granville's Plurality indicators (#1–#4).
/// 
/// Plurality is the daily difference between advancing and declining issues.
/// These four indicators compare that breadth signal against the benchmark (XIU)
/// direction to detect divergences and confirmations.
/// 
/// Reference: Granville, "A Strategy of Daily Stock Market Timing", pp. 66–69.
/// </summary>
public sealed class PluralityIndicators : IGranvilleIndicatorGroup
{
    public IndicatorCategory Category => IndicatorCategory.Plurality;
    public string Name => "Plurality";

    public IReadOnlyList<GranvilleResult> Evaluate(GranvilleMarketContext context)
    {
        var results = new List<GranvilleResult>(4);

        bool moreDeclines = context.Today.Decliners > context.Today.Advancers;
        bool moreAdvances = context.Today.Advancers > context.Today.Decliners;

        // XIU direction: compare today's close to yesterday's close
        bool xiuRose = context.Today.XiuClose.HasValue
                    && context.Yesterday.XiuClose.HasValue
                    && context.Today.XiuClose.Value > context.Yesterday.XiuClose.Value;

        bool xiuFell = context.Today.XiuClose.HasValue
                    && context.Yesterday.XiuClose.HasValue
                    && context.Today.XiuClose.Value < context.Yesterday.XiuClose.Value;

        // ── Indicator #1: Declines > Advances + XIU rising → verge of decline ──
        if (moreDeclines && xiuRose)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 1,
                Category: IndicatorCategory.Plurality,
                Name: "Plurality #1: Verge of Decline",
                Signal: IndicatorSignal.Bearish,
                GranvillePoints: -1,
                Description: $"Declines ({context.Today.Decliners}) > Advances ({context.Today.Advancers}) " +
                             $"while XIU rose ({context.Yesterday.XiuClose:F2} → {context.Today.XiuClose:F2}). " +
                             "Divergence suggests market is on the verge of a decline."));
        }

        // ── Indicator #2: Advances > Declines + XIU falling → verge of advance ──
        if (moreAdvances && xiuFell)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 2,
                Category: IndicatorCategory.Plurality,
                Name: "Plurality #2: Verge of Advance",
                Signal: IndicatorSignal.Bullish,
                GranvillePoints: +2,
                Description: $"Advances ({context.Today.Advancers}) > Declines ({context.Today.Decliners}) " +
                             $"while XIU fell ({context.Yesterday.XiuClose:F2} → {context.Today.XiuClose:F2}). " +
                             "Divergence suggests market is on the verge of an advance."));
        }

        // ── Indicator #3: Declines > Advances + XIU falling → decline will continue ──
        if (moreDeclines && xiuFell)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 3,
                Category: IndicatorCategory.Plurality,
                Name: "Plurality #3: Decline Will Continue",
                Signal: IndicatorSignal.StrongBearish,
                GranvillePoints: -1,
                Description: $"Declines ({context.Today.Decliners}) > Advances ({context.Today.Advancers}) " +
                             $"while XIU also fell ({context.Yesterday.XiuClose:F2} → {context.Today.XiuClose:F2}). " +
                             "Breadth confirms benchmark weakness — decline likely to continue."));
        }

        // ── Indicator #4: Advances > Declines + XIU rising → advance will continue ──
        if (moreAdvances && xiuRose)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 4,
                Category: IndicatorCategory.Plurality,
                Name: "Plurality #4: Advance Will Continue",
                Signal: IndicatorSignal.StrongBullish,
                GranvillePoints: +2,
                Description: $"Advances ({context.Today.Advancers}) > Declines ({context.Today.Decliners}) " +
                             $"while XIU also rose ({context.Yesterday.XiuClose:F2} → {context.Today.XiuClose:F2}). " +
                             "Breadth confirms benchmark strength — advance likely to continue."));
        }

        // If neither advances nor declines dominate (equal), report neutral
        if (results.Count == 0)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 0,
                Category: IndicatorCategory.Plurality,
                Name: "Plurality: Neutral",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: $"Advances ({context.Today.Advancers}) ≈ Declines ({context.Today.Decliners}) " +
                             "or XIU unchanged. No plurality signal."));
        }

        return results;
    }
}