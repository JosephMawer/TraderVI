---
cssclasses:
  - executive-brief
---
# ADR-0090 — Progressive trailing protection for individual winners

**Date:** October 17, 2026. **Status:** Proposed operating policy; design direction confirmed.
**Domains:** risk-management, decision-engine.

> [!summary] Confirmed direction; no trading-rule change
> The operator wants an individual stock's allowed percentage pullback below its highest price
> to shrink as its profit since purchase grows. The exact policy is still a draft. Existing saved
> rules, stop ownership, execution contracts and protection remain authoritative.
> **Operator priority:** protecting profit matters more than capturing gains after an exit;
> a later rise alone does not make a profitable protective exit a failure.

## Problem and goal

The immediate problem is an individual winner giving back profit, illustrated by DSV. The parent
problem is retaining realized profit rather than surrendering it while waiting for a larger move.
The root goal is dependable portfolio returns from individually understood paths. ARE illustrates
later upside the operator explicitly accepts missing when that tradeoff protects profit.

## Confirmed design direction

- Analyze individual positions, showing day-move and purchase-profit separately.
- Use a trailing stop whose **percentage gap shrinks as purchase profit grows**. The operator
  explicitly confirmed that this means more than a stop rising with a fixed percentage gap.
- Keep later purchases tied to their own positions/entry times. No earlier unowned gain is credited.
- Prioritize protecting profit over capturing subsequent gains after a sale. Later price increases
  remain context, not a reason by themselves to reject a profitable protective exit or require re-entry.
- Stay in research/design. The operator explicitly requested no trading-rule changes yet.

This direction follows the October 17 stock-level scope correction. Whole-account results remain
background; no portfolio-wide cutoff, fixed profit target, noon sale or new-entry pause is selected.

## Relationship to current protection

[ADR-0051](0051-system-shadow-portfolios.md) and
[current Shadow source](../../Core/Trader/SystemShadowPolicy.cs) already provide a stop that never
moves down, using a cost-aware break-even floor and a configured fraction below the highest completed
fifteen-minute close. The proposed change concerns how that fraction tightens at larger profits.
It is not a second competing exit owner or permission to weaken an established stop.
[ADR-0053](0053-delphi-live-v1.md) retains Live's separate frozen protection/execution semantics.
Neither ADR is superseded by this proposed record; saved policies are not retrospectively rewritten.

## Evidence and tradeoff

The [DSV/ARE walkthrough](../reviews/stock-gain-paths-20261017.md#dsv-versus-a-winner-that-kept-climbing)
uses complete saved five-minute close paths from the same paper account on different dates.
DSV gives back its morning profit; ARE pulls back before a higher afternoon finish. The operator
accepts missing ARE's later advance if the exit protects profit. These contrasting examples explain
the accepted tradeoff, not a tested improvement. No stop threshold, trigger or fill
was simulated. Five-minute closes do not describe every within-bar price or instantaneous receipt.

## Open before an implementable policy

- Activation profit, initial/minimum allowed gap, and how the gap shrinks: steps, a smooth curve
  or another explicitly reviewed schedule. No numeric examples are accepted settings.
- Exact high/profit reference and observation/update timing. Retain existing non-loosening protection;
  do not silently substitute this study's five-minute descriptive highs for a governing policy input.
- Intended family/version scope and comparison with the existing rules, emphasizing profit actually
  retained after costs and giveback. Record later upside as secondary context, alongside later purchases
  and re-entry; do not turn capturing every subsequent rally into a success condition. Existing fill
  friction/causal timing still apply.

Keep ordinary supported paths first. Historical exceptions are documented and are not a prerequisite
to this design conversation. No code, schema, model, collector, broker or installed policy changed.
This is not implementation, training, migration, deployment or trading authorization.

The [concept note](../concepts/progressive-profit-trailing-draft.md) explains the mechanism;
[open questions](../reviews/open-questions.md#intraday-gain-retention-revisit-soon) own remaining decisions.

## Review questions

1. How does a shrinking percentage gap differ from a fixed percentage trailing stop?
2. Why does ARE's later rally not by itself invalidate a profitable protective exit?
3. Which parts of this direction are confirmed, and which operating settings remain unapproved?
