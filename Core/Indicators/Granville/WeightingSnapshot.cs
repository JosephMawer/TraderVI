using System.Collections.Generic;

namespace Core.Indicators.Granville;

/// <summary>
/// Structured output of the Weighting (Granville #15/#16) calculation for a single day.
/// Exposed alongside the <see cref="GranvilleResult"/> so report builders and downstream
/// consumers can render ScoreB / ScoreC / top contributors without parsing description strings.
///
/// See ADR-0003 for the empirical basis of the trigger rule and thresholds.
/// </summary>
/// <param name="ConstituentsObserved">Number of XIU constituents with both today/yesterday closes available.</param>
/// <param name="ConstituentsRequired">Minimum count needed before the indicator will evaluate (graceful-degradation gate).</param>
/// <param name="XiuReturn">Day-over-day return on XIU (from the A/D line context).</param>
/// <param name="ScoreB">Top-K concentration: share of |contribution| in the K largest with-index movers.</param>
/// <param name="ScoreC">Narrowness: fraction of constituents that moved against XIU's direction.</param>
/// <param name="TopContributors">The top-K contributors (by |contribution|) that moved with XIU.</param>
/// <param name="Triggered">True if all three trigger conditions held (ScoreB ≥ threshold AND ScoreC ≥ threshold AND XiuReturn &gt; 0).</param>
/// <param name="Degraded">True when constituent coverage was below the minimum and no scoring was performed.</param>
public sealed record WeightingSnapshot(
    int ConstituentsObserved,
    int ConstituentsRequired,
    double XiuReturn,
    double ScoreB,
    double ScoreC,
    IReadOnlyList<WeightingContributor> TopContributors,
    bool Triggered,
    bool Degraded);

/// <summary>One constituent's signed contribution to the price-weighted proxy move.</summary>
/// <param name="Symbol">Constituent ticker.</param>
/// <param name="Weight">Price weight (today's price / sum of today's prices).</param>
/// <param name="Return">Day-over-day return.</param>
/// <param name="Contribution"><see cref="Weight"/> × <see cref="Return"/>.</param>
public sealed record WeightingContributor(
    string Symbol,
    double Weight,
    double Return,
    double Contribution);
