---
cssclasses:
  - executive-brief
---
# Progressive profit trailing — accepted behavior

**October 17, 2026. Implemented/tested; disabled and not installed.**
Domains: risk-management, decision-engine.

A trailing stop follows a stock's highest observed price. Its gap is the percentage pullback
allowed below that high. With a fixed gap, the stop rises as the stock rises, but the same
percentage pullback remains allowed. The operator confirmed a stronger direction: **as profit
since purchase grows, that percentage gap should shrink**. After the saved comparisons, the operator
kept +1.5%/+3%/+5% with 1%/0.75%/0.50% gaps and subsequently authorized implementation/testing.
No operating assignment or enablement has run.

This would give an individual winner less room to surrender profit as it becomes more profitable.
It also makes an ordinary pullback more likely to end ownership before a later advance.
The operator explicitly prioritizes protecting profit over those later gains; missing a subsequent
advance does not by itself make a profitable protective exit unsuccessful.
The [saved DSV/ARE comparison](../reviews/stock-gain-paths-20261017.md#dsv-versus-a-winner-that-kept-climbing)
shows both concerns. It does not establish a setting that improves either trade or the portfolio.

The day's price move and profit since purchase remain separate. A recovery today can still be
below purchase cost; a stock bought later starts its own history at purchase. Tightening one
winner's stop does not decide whether to make that later purchase.

Existing protection must never be loosened or moved under a competing owner. Prospective version
integration and synthetic validation are complete; additional source investigation is deferred,
not a coding prerequisite. The high is the highest completed
five-minute close since purchase.
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
behavior is implemented with durable state; prospective assignments remain unapplied.

Raising a stop and checking for a breach are separate timings. The operator's [five/fifteen-minute
comparison](../reviews/progressive-trailing-trade-study-20261017.md#five-minute-versus-fifteen-minute-stop-updates)
keeps five-minute closing-price breach checks in both: DSV retains more with fifteen-minute updates,
the earlier ARE retains more with five-minute updates, and add-on ARE results are equal.

**Subsequent agreed design:** raise stops using completed five-minute prices; trigger a sale on the
first fresh observed price at or below the established stop, without waiting for a close. This favors
profit protection but can sell during a dip that later recovers. A new or raised stop applies only to
later observations, never an earlier low from the bar that raised it. Actual detection depends on
available fresh data; continuous monitoring is not established. Source limits remain documented;
prospective assignment remains separate. Prior close-based results do not test touch-trigger returns.
[Decision and boundaries](../adr/0090-progressive-individual-profit-trailing.md#five-minute-updates-and-observed-touch-triggers--agreed-design).

**Scope now confirmed:** all portfolios, including and especially Delphi Live, still protecting each
stock individually. **Input now confirmed:** a fresh bid, meaning the price buyers
are offering. If the established stop is $104.50, a later observed bid of $104.48 triggers the exit;
no five-minute close is required. Live already uses fresh bids for its existing floor; the progressive
bands now share the new opt-in implementation. Source freshness is unverified; prospective assignments remain work.
[Scope and source boundary](../adr/0090-progressive-individual-profit-trailing.md#all-portfolio-scope-and-bid-price-recommendation).
The operator now confirms the highest completed five-minute close since purchase and inclusion of
existing holdings as soon as the strategy is enabled, preserving stronger protection. After the example,
the operator accepted five-minute checks above 0.25% over the stop, one-minute checks nearer, a new
quote each time and at most 60 seconds since receipt. These choices are implemented/tested;
receipt age does not prove market-data age. Safe handover and synthetic fresh-bid tests pass.
[Decision list/example](../adr/0090-progressive-individual-profit-trailing.md#remaining-decisions-before-implementation).
The [October 17 source check](../adr/0090-progressive-individual-profit-trailing.md#quote-source-freshness-check--october-17-2026)
cannot qualify the existing Live quote feed: it has local receipt time but no bid-update time; the
September 29 saved samples do not establish usable fresh bids. No new market requests or source changes.
The operator subsequently declined the extra market-hours/source-guarantee investigation as an
implementation prerequisite. It is deferred, with the limitation preserved. The agreed design is
implemented/tested using the existing source. Next review installation and prospective assignments
separately. All defaults remain false; migration 043 is prepared and unapplied. Historical policies,
comparison profiles, pending protective sells and stronger stops are preserved. Daily Shadow keeps its
existing simulated-fill contract; Live uses a separate post-decision quote, so a touch is not a promise
of the stop price. [Rollout boundary](../running.md#progressive-profit-protection--implemented-disabled).

## Review questions

1. Why does a stop moving upward not necessarily mean its percentage gap is tightening?
2. What can be lost by protecting a larger share of a winner's profit?
