using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Indicators.Granville;

/// <summary>
/// Pure scoring logic for the Weighting (Granville #15/#16) indicator group.
///
/// Translates Granville's price-weighted "narrow advance / narrow decline" idea
/// to the cap-weighted XIU basket by computing a Dow-style price-weighted
/// contribution proxy. See ADR-0003 and Docs/concepts/price-weighted-contribution.md.
///
/// Inputs are decoupled from any data source so the same logic powers the live
/// indicator group, future backtests, and report-builder surfacing.
/// </summary>
public static class WeightingCalculator
{
    /// <summary>
    /// Minimum constituents (both closes present) before evaluation runs.
    /// Below this threshold the indicator degrades to Neutral.
    /// </summary>
    public const int MinConstituentsRequired = 50;

    /// <summary>Number of top contributors counted for ScoreB (the concentration head).</summary>
    public const int TopK = 3;

    /// <summary>ScoreB threshold for the v1 trigger (calibrated empirically — see ADR-0003).</summary>
    public const double ScoreBThreshold = 0.50;

    /// <summary>ScoreC threshold for the v1 trigger (calibrated empirically — see ADR-0003).</summary>
    public const double ScoreCThreshold = 0.60;

    /// <summary>
    /// Computes a <see cref="WeightingSnapshot"/> from raw constituent bars and the day's XIU return.
    /// Always returns a snapshot — set <see cref="WeightingSnapshot.Degraded"/> to detect insufficient data.
    /// </summary>
    public static WeightingSnapshot Compute(
        IReadOnlyList<XiuConstituentBar>? bars,
        double xiuReturn)
    {
        var usable = bars?.Where(b => b.IsUsable).ToList() ?? new List<XiuConstituentBar>();

        if (usable.Count < MinConstituentsRequired)
        {
            return new WeightingSnapshot(
                ConstituentsObserved: usable.Count,
                ConstituentsRequired: MinConstituentsRequired,
                XiuReturn: xiuReturn,
                ScoreB: 0,
                ScoreC: 0,
                TopContributors: Array.Empty<WeightingContributor>(),
                Triggered: false,
                Degraded: true);
        }

        // Price weight = today's close / Σ today's closes (Dow-style proxy on a cap-weighted basket).
        double priceSum = usable.Sum(b => b.TodayClose);

        // Sign of XIU's move dictates "with-index" vs "against-index".
        // Flat XIU is filtered upstream (Evaluate); guard here returns a non-triggered snapshot.
        int xiuSign = System.Math.Sign(xiuReturn);

        // Build per-constituent contribution rows.
        var rows = usable
            .Select(b =>
            {
                double weight = priceSum > 0 ? b.TodayClose / priceSum : 0;
                double contribution = weight * b.Return;
                int sign = b.Return > 0 ? 1 : b.Return < 0 ? -1 : 0;
                return (Bar: b, Weight: weight, Contribution: contribution, Sign: sign);
            })
            .ToList();

        // ScoreC (narrowness): of the constituents that moved at all (excluding ties),
        // what fraction moved AGAINST XIU's direction?
        int moved = rows.Count(r => r.Sign != 0);
        int against = xiuSign == 0
            ? 0
            : rows.Count(r => r.Sign != 0 && r.Sign != xiuSign);
        double scoreC = moved > 0 ? (double)against / moved : 0.0;

        // ScoreB (concentration): of the constituents moving WITH XIU, what fraction of
        // total |contribution| is captured by the top-K names?
        var withIndex = xiuSign == 0
            ? new List<(XiuConstituentBar Bar, double Weight, double Contribution, int Sign)>()
            : rows.Where(r => r.Sign == xiuSign).ToList();

        double withIndexAbsTotal = withIndex.Sum(r => System.Math.Abs(r.Contribution));
        var topK = withIndex
            .OrderByDescending(r => System.Math.Abs(r.Contribution))
            .Take(TopK)
            .ToList();

        double scoreB = withIndexAbsTotal > 0
            ? topK.Sum(r => System.Math.Abs(r.Contribution)) / withIndexAbsTotal
            : 0.0;

        var topContributors = topK
            .Select(r => new WeightingContributor(r.Bar.Symbol, r.Weight, r.Bar.Return, r.Contribution))
            .ToList();

        bool triggered =
            xiuReturn > 0
            && scoreB >= ScoreBThreshold
            && scoreC >= ScoreCThreshold;

        return new WeightingSnapshot(
            ConstituentsObserved: usable.Count,
            ConstituentsRequired: MinConstituentsRequired,
            XiuReturn: xiuReturn,
            ScoreB: scoreB,
            ScoreC: scoreC,
            TopContributors: topContributors,
            Triggered: triggered,
            Degraded: false);
    }
}
