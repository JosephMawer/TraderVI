---
cssclasses:
  - executive-brief
---
# Progressive profit trailing — design draft

**October 17, 2026. Draft, not an accepted operating policy or implementation authorization.**
Domains: risk-management, decision-engine.

A trailing stop follows a stock's highest observed price. Its gap is the percentage pullback
allowed below that high. With a fixed gap, the stop rises as the stock rises, but the same
percentage pullback remains allowed. The operator confirmed a stronger direction: **as profit
since purchase grows, that percentage gap should shrink**. After the saved comparisons, the operator
kept +1.5%/+3%/+5% with 1%/0.75%/0.50% gaps as the draft baseline. No implementation or operating-policy
change is authorized.

This would give an individual winner less room to surrender profit as it becomes more profitable.
It also makes an ordinary pullback more likely to end ownership before a later advance.
The operator explicitly prioritizes protecting profit over those later gains; missing a subsequent
advance does not by itself make a profitable protective exit unsuccessful.
The [saved DSV/ARE comparison](../reviews/stock-gain-paths-20261017.md#dsv-versus-a-winner-that-kept-climbing)
shows both concerns. It does not establish a setting that improves either trade or the portfolio.

The day's price move and profit since purchase remain separate. A recovery today can still be
below purchase cost; a stock bought later starts its own history at purchase. Tightening one
winner's stop does not decide whether to make that later purchase.

Existing protection must never be loosened or moved under a competing owner. Governing price/high
inputs, monitoring, policy/version scope and validation remain outstanding.
Current rules and saved histories stay unchanged. [ADR-0090](../adr/0090-progressive-individual-profit-trailing.md)
records confirmed direction and the boundary of the proposal.

The operator also proposed giving winners more room early, then tightening at 10 a.m. Toronto time
and again at noon. That remains a research hypothesis. The [offline time comparison](../reviews/progressive-trailing-trade-study-20261017.md#time-of-day-gap-comparison)
finds mixed results and recommends keeping the original bands as the baseline. A stop raised yesterday
remains raised this morning: a wider morning candidate cannot move it down. No clock-based policy or
daily reset is adopted.

**Confirmed add-on design:** buying more stays within one combined holding. Update the weighted
average purchase cost and reported profit, while keeping the existing stop, actual price-high history
and tightest earned gap. A lower profit percentage after adding shares cannot undo earned protection.
One protective exit covers the combined shares; new shares can lose individually while the combined
holding remains profitable. See the [worked example and source boundary](../adr/0090-progressive-individual-profit-trailing.md#existing-stops-and-additional-purchases--design-walkthrough).
The [bounded saved add-on test](../reviews/progressive-trailing-trade-study-20261017.md#saved-add-on-trade-comparison)
now illustrates this in ARE: +1.58%–1.93% combined profit after an add-on despite a -1.48% return on
the added shares. It protects a smaller profit earlier than the actual recorded ARE exits. The
decision is still unimplemented, and governing price/high inputs and version scope remain open.

Raising a stop and checking for a breach are separate timings. The operator's [five/fifteen-minute
comparison](../reviews/progressive-trailing-trade-study-20261017.md#five-minute-versus-fifteen-minute-stop-updates)
keeps five-minute closing-price breach checks in both: DSV retains more with fifteen-minute updates,
the earlier ARE retains more with five-minute updates, and add-on ARE results are equal.

**Subsequent agreed design:** raise stops using completed five-minute prices; trigger a sale on the
first fresh observed price at or below the established stop, without waiting for a close. This favors
profit protection but can sell during a dip that later recovers. A new or raised stop applies only to
later observations, never an earlier low from the bar that raised it. Actual detection depends on
available fresh data; continuous monitoring is not established. Governing price source/monitoring and
family/version scope remain open. Prior close-based results do not test touch-trigger returns. No
implementation or operating-policy change is authorized. [Decision and boundaries](../adr/0090-progressive-individual-profit-trailing.md#five-minute-updates-and-observed-touch-triggers--agreed-design).

## Review questions

1. Why does a stop moving upward not necessarily mean its percentage gap is tightening?
2. What can be lost by protecting a larger share of a winner's profit?
