# ADR-0011 — Equal-weighted additive RS into the Delphi pick ranking

- **Status:** Accepted
- **Date:** 2026-05-23
- **Tags:** decision-engine, technical-indicators, math-statistics
- **Supersedes:** —
- **Related:** ADR-0002 (XIU benchmark), ADR-0010 (RS Z-score composite),
  ADR-0012 (sector-index historical backfill — prerequisite for reliable RS),
  ADR-0013 (multi-lens architecture), ADR-0014 (Continuations lens).

> **Scope note (added 2026-05-24, ADR-0013/0014):** This ranking key —
> `DirectionEdge + RScomp` — is now the key of the **Breakouts lens** only, which
> is *journaled, not executed*. The **executed** lens is Continuations, which
> ranks RS-first (`primaryKey = RScomp`, `DirectionEdge` as confirmation) per
> ADR-0014. The decision below stands unchanged *for the Breakouts lens*.

## Context

Up through 2026-05-23, Delphi ranked candidates inside `RankingMode.Probability`
using `DirectionEdge` (`P(up10) − P(down10)`) as the primary key and treated
`RsCompositeScores` only as a deep tiebreaker. As long as sector-index history
was too short to compute `RScomp` (the raw RS composite — a weighted blend of
10-day stock-vs-XIU, stock-vs-sector, and sector-vs-XIU return differences),
that tiebreaker was effectively dead — `RsCompositeScores` was `null` for the
14 passing names and the ordering collapsed to pure `DirectionEdge`.

The 2026-05-22 backfill (see ADR-0012) made `RScomp` reliable: 110/110 sector
bars, 0 null composites, 19 fallback-to-XIU symbols (those without a sector
mapping fall back to stock-vs-benchmark, which is structural, not a coverage
gap). The same Delphi run made the side-effect of the old policy visible:

- Winner **PEY**: `Edge +16.5%`, `RScomp +0.022`, `CompZ +0.67`
- #9 **BTE**: `Edge +8.9%`, `RScomp +0.944`, `CompZ +2.18`, `RS10d +1.18`

PEY won purely on forward-probability conviction; BTE was a ~2σ RS leader
buried below several Edge-stronger names. The system goal in
[`copilot-instructions.md`](../../.github/copilot-instructions.md) explicitly
calls for **ensemble confidence** ("use multiple diverse signals... to
increase conviction"). Letting Edge dominate alone violates that goal once
RS is trustworthy.

The strategy is also explicitly **aggressive momentum rotation**: prior
realized leadership (RS) is exactly the orthogonal signal that should pull
weight away from pure model-implied probability when the two disagree.

## Decision

In `Core/Trader/TradeDecisionEngine.cs`, replace the
`DirectionEdge`-then-RS-tiebreaker ordering with an **equal-weighted additive
combination** as the primary ranking key:

```
primaryKey  =  DirectionEdge  +  RScomp
secondaryKey =  DirectionEdge          // tiebreak when sum ties
tertiaryKey  =  CompositeScore         // engine composite as final fallback
```

`RScomp` here is `RsCompositeScores[symbol]` — i.e., the raw additive
`CompositeScore` from `RelativeStrengthRow`, not the Z-normalized
`CompositeScoreZ`. Missing values default to `0`, which neither rewards nor
penalizes symbols lacking RS coverage. Buy direction still sorts ahead of
Hold/Sell.

## Alternatives considered

- **(a) Keep DirectionEdge-only ranking; show RS as diagnostic.**
  Rejected: silently drops the ensemble property the strategy is built on,
  and means weeks of backfill work do nothing to the picks the system
  actually emits.
- **(b) Add RS as a soft *kicker*** (e.g., `Edge + 0.05 × clamp(CompZ, ±2)`).
  Rejected for now as too conservative: CompZ ±2 only moves composite by
  ±0.10, which still lets weak-RS Edge winners beat strong-RS challengers.
  This is a defensible later refinement once we measure outcomes.
- **(c) RS *gate*** requiring `CompZ ≥ 0` (or `≥ −0.5`) to pass.
  Rejected: too aggressive — risks vetoing legitimate early-sector-rotation
  entries where the leading stock is *just starting* its RS run (`CompZ`
  near zero) and Edge has caught the inflection first.
- **(d) Weight `CompZ` instead of raw `RScomp`.**
  Rejected for v1: `RScomp` and `DirectionEdge` already live on roughly the
  same numeric scale (~±0.2 in typical conditions), so a plain sum approximates
  equal influence without re-scaling. Mixing `CompZ` (units of σ, range ~±2)
  would silently *over*-weight RS by ~10×, contradicting the "equal" intent.
  Revisit if empirical distribution of `RScomp` proves materially wider or
  narrower than `DirectionEdge` over a measurement window.

## Consequences

**Locks us into:**
- A specific *equal-weight* interpretation that assumes `RScomp` and
  `DirectionEdge` share a comparable scale. If that assumption breaks
  (e.g., RS becomes systematically larger after a regime shift), this ADR
  must be revisited.
- Picks now depend on `dbo.SectorIndices` history. If that table degrades
  (deletes, gaps), `RScomp` drops to 0 for affected symbols and the
  ranking silently reverts to Edge-only for those names. Coverage
  diagnostics in Delphi's "RS Coverage" block (added 2026-05-23) are the
  alert path.

**Easier:**
- Ensemble disagreement is now *visible* in the ordering, not just in the
  diagnostic columns — strong-RS challengers can climb above weak-RS Edge
  winners.
- Future work to refine RS weighting (option (b) or (d)) has a clear v1
  baseline to measure against.

**Harder:**
- Backtest comparability — picks dated before this ADR cannot be compared
  one-for-one to picks after. Any longitudinal performance study must
  segment around this date.
- Symbols that fall back to XIU (no sector mapping) compete on a slightly
  different RS computation than sector-mapped symbols. This is the same
  asymmetry that existed before; equal-weighting just amplifies its effect
  on ranking.

**Would tell us this was wrong:**
- Repeated cases where the new #1 has lower realized 1–5 day return than
  the pre-ADR Edge-only #1 across a 2–4 week paper-trade window.
- `RScomp` distribution drifts so far from `DirectionEdge` that one signal
  consistently dominates the sum (defeating "equal weight").

## Review questions

1. What two signals form the primary ranking key after ADR-0011, and how
   are they combined?
2. Why was raw `RScomp` chosen over `CompZ` for the additive combination?
3. Name two specific events that would invalidate this decision.
4. What is the structural reason `RsCompositeScores` can be missing for a
   symbol even when sector history is healthy?
