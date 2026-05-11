using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Core.Indicators.Granville;

/// <summary>
/// Granville's Weighting indicators (#15 / #16) — reformulated as a single
/// long-side narrow-advance warning gate for the cap-weighted XIU basket.
///
/// On a rising-XIU day, if a small number of names carried the move (high ScoreB)
/// AND most constituents moved against the index (high ScoreC), the advance is
/// "narrow" and we emit a single Bearish <see cref="GranvilleResult"/> worth −1 point.
/// Otherwise the group returns Neutral.
///
/// Down-days never trigger — empirically they showed mean-reversion, not narrow-decline
/// continuation (see ADR-0003 — Alternatives Considered and Consequences).
/// </summary>
public sealed class WeightingIndicators : IGranvilleIndicatorGroup
{
    public IndicatorCategory Category => IndicatorCategory.Weighting;
    public string Name => "Weighting";

    public IReadOnlyList<GranvilleResult> Evaluate(GranvilleMarketContext context)
    {
        // Need both XIU closes to compute the day's return.
        if (!context.Today.XiuClose.HasValue || !context.Yesterday.XiuClose.HasValue)
        {
            return NeutralResult("XIU close not available — skipping #15/#16.");
        }

        double prior = (double)context.Yesterday.XiuClose.Value;
        double today = (double)context.Today.XiuClose.Value;
        if (prior <= 0)
        {
            return NeutralResult("Prior XIU close not positive — skipping #15/#16.");
        }

        double xiuReturn = (today / prior) - 1.0;

        // Flat-XIU days are skipped entirely (no meaningful "with-index" direction).
        if (xiuReturn == 0.0)
        {
            return NeutralResult("XIU unchanged — narrowness has no direction. No Weighting signal.");
        }

        var snapshot = WeightingCalculator.Compute(context.XiuConstituentBars, xiuReturn);

        if (snapshot.Degraded)
        {
            return NeutralResult(
                $"Constituent coverage {snapshot.ConstituentsObserved}/{snapshot.ConstituentsRequired} " +
                "below minimum — graceful degradation, no Weighting signal.");
        }

        // Build a stable, machine-parseable diagnostic suffix used by reporting + log analysis.
        string topList = snapshot.TopContributors.Count > 0
            ? string.Join(",", snapshot.TopContributors.Select(t => t.Symbol))
            : "—";
        string diag =
            $"ScoreB={snapshot.ScoreB.ToString("F3", CultureInfo.InvariantCulture)} " +
            $"ScoreC={snapshot.ScoreC.ToString("F3", CultureInfo.InvariantCulture)} " +
            $"XiuRet={xiuReturn.ToString("F4", CultureInfo.InvariantCulture)} " +
            $"top={topList} " +
            $"cov={snapshot.ConstituentsObserved}/60 " +
            $"triggered={(snapshot.Triggered ? "true" : "false")}";

        if (snapshot.Triggered)
        {
            return
            [
                new GranvilleResult(
                    IndicatorNumber: 15,
                    Category: IndicatorCategory.Weighting,
                    Name: "Weighting #15/#16: Narrow Advance",
                    Signal: IndicatorSignal.Bearish,
                    GranvillePoints: -1,
                    Description:
                        "Narrow advance: a small group of names carried XIU higher while most constituents " +
                        $"moved against the index. " + diag)
            ];
        }

        return
        [
            new GranvilleResult(
                IndicatorNumber: 0,
                Category: IndicatorCategory.Weighting,
                Name: "Weighting: Neutral",
                Signal: IndicatorSignal.Neutral,
                GranvillePoints: 0,
                Description: "No narrow-advance condition. " + diag)
        ];
    }

    private static IReadOnlyList<GranvilleResult> NeutralResult(string description) =>
    [
        new GranvilleResult(
            IndicatorNumber: 0,
            Category: IndicatorCategory.Weighting,
            Name: "Weighting: Neutral",
            Signal: IndicatorSignal.Neutral,
            GranvillePoints: 0,
            Description: description)
    ];
}
