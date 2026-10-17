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
the accepted tradeoff, not a tested improvement. That original walkthrough evaluated no stop setting
or alternative fill. The subsequent limited arithmetic illustration below is separate from a strategy
replay. Five-minute closes do not describe every within-bar price or instantaneous receipt.

## Recommended first paper-study schedule — not accepted

The October 17 recommendation starts **extra tightening** once the highest observed purchase profit
reaches **+1.5%**. Existing loss/break-even protection continues below that point. Use three simple
bands rather than fitting a curve to DSV. The operator subsequently authorized an offline comparison
of these exact bands. They remain a starting hypothesis, not calibrated settings, acceptance as an
operating policy, a deployed rule or implementation authorization.

| Highest profit since purchase | Percentage gap below the observed high |
|---|---:|
| +1.5% to below +3% | 1.00% |
| +3% to below +5% | 0.75% |
| +5% and above | 0.50% minimum gap |

Rationale: leave the smallest initial moves alone, begin with a modest positive threshold after
ordinary modeled exit friction, then tighten fairly quickly because retaining profit is the operator's
priority. The 0.50% minimum bounds the initial proposal. There is no evidence these exact bands are
optimal across stocks, spreads or volatility. The recommendation uses the highest attained band;
falling profit cannot widen the gap or lower an established stop. It does not reset on a new morning.
Precise high/update observations, add-on/cost handling and version scope remain open before adoption.

### DSV arithmetic illustration

For illustration only, begin at the September 30 09:35 observation, with no prior-day high/activation
replay. Normalize purchase cost to 100 and consume saved five-minute closes in order. Test an existing
threshold before raising it from the next observed high; never use a later high to explain an earlier exit.

1. **09:55:** the first observed purchase profit above +1.5% is +1.55%. A 1% gap puts the nominal
   price threshold about **+0.53% above purchase cost**. This price was first received at 09:57.
2. **10:15:** purchase profit reaches +2.94%. The 1% gap still applies, because +3% was not reached.
   Normalized high 102.936761 multiplied by 0.99 gives threshold 101.907394: **+1.91% above cost**.
3. **10:25:** the saved close is **+1.79% above cost**, below the existing +1.91% threshold. It is first
   received about **10:27**, when that close-based observation could become known. No exit at 10:25,
   fill at the threshold or retained profit is assumed. The original actual paper sale was at 13:20.

This is a **day-only threshold illustration**, not a full position/strategy backtest: prior days,
within-bar lows, current fifteen-minute policy semantics and alternative executions are not replayed.
It cannot establish a realized +1.91% or +1.79% return. Execution/receipt timing and existing friction
must be preserved in a later comparison. The SEC also distinguishes a stop's trigger from its
unguaranteed execution price in its [order bulletin](https://www.investor.gov/introduction-investing/general-resources/news-alerts/alerts-bulletins/investor-bulletins-15);
that source supports order mechanics, not these recommended parameter values.

Offline reproduction: ignored `artifacts/morning-gain-review-20261017-expanded/trail-schedule-illustration.py`
and `.json`, using the existing `winner-walkthrough.json`. Decimal checks retain a nondecreasing
threshold, positive activation threshold and chronological first crossing. No Core/engine rule changed,
new SQL/market request, order, model run or alternative fill occurred.

## First broader saved-trade comparison

The [October 17 comparison](../reviews/progressive-trailing-trade-study-20261017.md) uses complete,
single-purchase closed Shadow trades, with receipt-aware signals and later bar-open fills under
the existing sell adjustment. After excluding DSV and duplicate copies across accounts, 53 distinct
paths yield seven earlier modeled exits: six profitable and two recorded losses converted to modeled
profits. Their median modeled return is +0.53%, versus +0.14% recorded; 46 paths retain their actual exit.
All seven affected returns improve in this limited subset. This is a conditional individual-trade
overlay, not an engine/portfolio backtest, realized profit or operational validation.

In that first sample, seven of eight activated distinct non-DSV paths only exercise the 1% gap, one
reaches 0.75%, and none reach 0.50%. The subsequent [larger-winner extension](../reviews/progressive-trailing-trade-study-20261017.md#larger-winner-extension-3-and-5-bands)
tests ARE/AGI from actual entry: the +3% band models retained profits of +2.64% for ARE and +3.06%/+2.10%
for the two AGI entries. Separate conditional already-held day windows give ARE +12.35% and CAS +4.55%
under the +5% band. They do not establish survival from entry under the lower bands or prior add-ons.
FVI illustrates an earlier lower-band exit before its later +5% high. Wider-gap comparisons sometimes
retain more profit; these examples do not establish optimal settings. The study owns the tables,
scope and independent checks. No historical repair is a prerequisite. The operating policy remains proposed.

## Open before an implementable policy

- Operator review of the proposed activation/gap bands above; a smooth curve or different schedule
  remains possible. The numeric recommendation is unapproved for operation; the bounded comparison
  above is not out-of-sample or general efficacy qualification, particularly for its upper bands.
- Exact high/profit reference and observation/update timing. Retain existing non-loosening protection;
  do not silently substitute this study's five-minute descriptive highs for a governing policy input.
  Prior add-ons' effect on cost/high references also remains open; a conditional day illustration does
  not decide that behavior or authorize resetting an existing stop each morning.
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
