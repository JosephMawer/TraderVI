---
cssclasses:
  - executive-brief
---
# ADR-0090 — Progressive trailing protection for individual winners

**Date:** October 17, 2026. **Status:** Accepted for prospective implementation; not enabled.
**Domains:** risk-management, decision-engine.

> [!summary] Accepted implementation; disabled until separately enabled
> The operator wants an individual stock's allowed percentage pullback below its highest price
> to shrink as its profit since purchase grows. The original bands are accepted for implementation. Existing saved
> rules, stop ownership, execution contracts and protection remain authoritative.
> **Operator priority:** protecting profit matters more than capturing gains after an exit;
> a later rise alone does not make a profitable protective exit a failure.
> **Latest design choices:** retain the original +1.5%/+3%/+5% bands and 1%/0.75%/0.50% gaps.
> An add-on stays in one combined holding with updated average cost, preserving the existing stop
> and tightest earned gap. Raise stops from completed five-minute prices; trigger on the first fresh
> observed bid at or below the established stop, without waiting for a close.
> **Scope/input confirmed:** all portfolios, including and especially Delphi Live, use a fresh bid
> at or below the established stop as the sale trigger.
> **High/transition confirmed:** use the highest completed five-minute close since purchase. Include
> existing holdings when the new strategy is enabled, preserving stronger established protection.
> **Polling confirmed:** five minutes with more than 0.25% room above the stop, one minute nearer;
> request a new quote per check and reject quotes received more than 60 seconds ago. Market freshness
> is not yet qualified by that receipt-age limit.
> **Source investigation deferred:** the operator
> declines the additional market-hours/source-guarantee investigation as an implementation prerequisite;
> defers it while retaining the documented source limitation. Receipt age does not prove bid market age.

> [!important] Current implementation boundary — October 17
> The operator subsequently authorized implementation across all portfolios, especially Delphi Live,
> preserving protection and deferring quote investigation, with testing before any enablement. This
> supersedes the earlier no-implementation boundaries in the historical design notes below. New
> saved strategy versions opt in with `UseProgressiveProfitProtection`; all defaults remain false.
> Shared calculations, Daily Shadow variants, Delphi Live, recommendation and supported managed-account
> paths are implemented and covered by 166 focused tests, including Practice compatibility. Durable state retains earned gaps, highs,
> stronger floors, pending protective sales and observed exits through restart/handover. A separate
> quote-only Shadow wake and Live host ticks honor adaptive due times without waiting for AI/display.
> Migration 043 is prepared, not applied. Nothing has been installed, assigned, activated or operationally verified.

## Prospective adoption and preserved contracts

**October 18 rollout authorization:** the operator authorizes installation and prospective enablement
across all active portfolios, preserving protection and deferring quote investigation. The dedicated
save/assignment operation may run while invested only with new buys paused and only for the validated
progressive successor. Ordinary editing retains its pause-and-empty restriction. Restore pre-rollout
entry controls after preservation checks; do not clear capital-review or other safety holds.

This accepted record extends the risk rules in [ADR-0051](0051-system-shadow-portfolios.md),
[ADR-0053](0053-delphi-live-v1.md) and the fixed comparison-account contract in
[ADR-0067](0067-fixed-gate-paper-comparison-pairs.md) **only for explicitly assigned prospective versions**.
The original policies, comparison profiles, gate choices, source snapshots and past results remain
immutable. Comparison accounts use the narrow `AssignProgressiveProfitProtectionAsync` operation,
which permits the progressive opt-in only; ordinary comparison editing stays blocked. New Live
versions have distinct policy IDs. This changes future risk behavior and must not be presented as
an uninterrupted original paired experiment. Existing real-account activation requirements remain.

Shadow stores the new state in `ShadowProgressiveProtection`; Live uses its existing versioned ledger.
Activation seeds genuine completed five-minute history since the actual purchase, using contemporaneous
costs where additional buys occurred. Missing history is not invented. Raised stops are saved before
new trigger requests. A valid positive post-stop bid with no more than 60 seconds local receipt age
can establish an exit; missing/invalid bids cannot. Existing hard stops, closing exits and pending
protective actions retain their owners. Live still uses a separate post-decision fill quote; Shadow
retains its next-five-minute simulated fill/friction contract. A touch does not guarantee the stop price.

The historical design/research notes below retain the authorization boundaries in force when written;
the implementation authorization and current boundary above supersede those earlier restrictions.

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
- Keep installation and enablement separate from implemented/tested source. No operational change in this task.

This direction follows the October 17 stock-level scope correction. Whole-account results remain
background; no portfolio-wide cutoff, fixed profit target, noon sale or new-entry pause is selected.

## Relationship to current protection

[ADR-0051](0051-system-shadow-portfolios.md) and
[current Shadow source](../../Core/Trader/SystemShadowPolicy.cs) already provide a stop that never
moves down, using a cost-aware break-even floor and a configured fraction below the highest completed
fifteen-minute close. The proposed change concerns how that fraction tightens at larger profits.
It is not a second competing exit owner or permission to weaken an established stop.
[ADR-0053](0053-delphi-live-v1.md) retains Live's separate frozen protection/execution semantics.
Their original versions remain unchanged; the prospective opt-in extension is defined above.

## Evidence and tradeoff

The [DSV/ARE walkthrough](../reviews/stock-gain-paths-20261017.md#dsv-versus-a-winner-that-kept-climbing)
uses complete saved five-minute close paths from the same paper account on different dates.
DSV gives back its morning profit; ARE pulls back before a higher afternoon finish. The operator
accepts missing ARE's later advance if the exit protects profit. These contrasting examples explain
the accepted tradeoff, not a tested improvement. That original walkthrough evaluated no stop setting
or alternative fill. The subsequent limited arithmetic illustration below is separate from a strategy
replay. Five-minute closes do not describe every within-bar price or instantaneous receipt.

<a id="recommended-first-paper-study-schedule--not-accepted"></a>

## Original bands — retained for design, not implemented

After the profit-only and time-based comparisons, the operator chose to keep the original bands for
now. These numbers are the retained draft baseline, not authorization to change saved policies.
The earlier recommendation/testing history below is preserved. Morning/noon alternatives remain
research ideas rather than selected behavior.

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
The combined-holding add-on contract is confirmed below; precise high/update observations, validation
and version scope remain open before adoption.

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

## Time-of-day research proposal

The operator subsequently suggested more room early, tightening at **10 a.m. Toronto time** and
further at **noon**, clarified the clock times and authorized the proposed offline schedule plus a
few nearby alternatives. This is research authorization, not acceptance of a clock-based operating rule.
The [time comparison](../reviews/progressive-trailing-trade-study-20261017.md#time-of-day-gap-comparison)
owns the five fixed schedules and results. All retain +1.5% activation, the +3%/+5% profit bands and
a nondecreasing stop across dates; no original protection is loosened or daily reset adopted.

Against the original schedule, the agreed time version improves three of 55 distinct closed trade
returns, reduces one and leaves 51 unchanged. Gentler changes improve three/leave 52 unchanged in
that subset, but both versions reduce the separate larger ARE entry's profit from +2.64% to +0.92%.
Conditional already-held ARE benefits from morning room, without proving ownership from entry.
Recommendation: retain the original as baseline; preserve the time hypothesis without choosing an
operating adjustment. The 415 offline scenario checks do not constitute implementation or policy adoption.

<a id="open-before-an-implementable-policy"></a>

## Implementation readiness and remaining engineering

- Original numeric bands are retained for the design baseline; operating-policy adoption and
  implementation remain unapproved. The bounded comparisons are not general efficacy qualification,
  particularly for the upper band. Future alternatives remain possible without changing this baseline.
- Monitoring integration remains implementation work; additional provider freshness research is
  deferred and does not block coding. The polling/receipt-age settings below, highest completed
  five-minute close since purchase and fresh-bid triggers are agreed for design.
  The [five/fifteen-minute comparison](../reviews/progressive-trailing-trade-study-20261017.md#five-minute-versus-fifteen-minute-stop-updates)
  now isolates update cadence with five-minute closing-price breach checks in both versions. It does
  not itself qualify touch detection or replace current native low-based checks.
  Clock-based tightening is deferred from the retained baseline; its simulated receipt-time semantics
  and carried-stop behavior remain documented for a future research revisit.
  A bounded [saved add-on simulation](../reviews/progressive-trailing-trade-study-20261017.md#saved-add-on-trade-comparison)
  now tests the agreed contract. It covers only selected supported paths and no post-add-on 0.50%
  band; the prior conditional day illustrations remain separate and do not permit a daily stop reset.
- Prospective policy/version assignment across the agreed all-portfolio scope and comparison with existing rules, emphasizing profit actually
  retained after costs and giveback. Record later upside as secondary context, alongside later purchases
  and re-entry; do not turn capturing every subsequent rally into a success condition. Existing fill
  friction/causal timing still apply.

Keep ordinary supported paths first. Historical exceptions are documented and are not a prerequisite
to this design conversation. No code, schema, model, collector, broker or installed policy changed.
This is not implementation, training, migration, deployment or trading authorization.

The [concept note](../concepts/progressive-profit-trailing-draft.md) explains the mechanism;
[open questions](../reviews/open-questions.md#intraday-gain-retention-revisit-soon) own remaining decisions.

## Existing stops and additional purchases — design walkthrough

This walkthrough concerns the daily Shadow portfolios studied above. Their current source retains
the configured hard-loss stop (5% in V1), a cost-aware break-even floor and a nondecreasing trailing
stop. The current high is based on completed fifteen-minute closes, with five-minute risk checks;
the later five-minute design choice below does not change this current operating behavior.

Proposed integration: retain existing protection and earlier required exits. Within the existing
protection owner, a progressive candidate may raise the active stop but cannot lower it. It does not
create a competing order owner or override a pending exit. These price levels are triggers, not
guaranteed sale prices; existing execution timing/costs continue to matter.

Illustration before costs: purchase cost $100, high $101.50 gives a $100.485 candidate (1% gap);
high $103 gives $102.2275 (0.75%); high $105 gives $104.475 (0.50%). An already higher existing
stop remains higher; a pullback, date change or additional purchase cannot lower established protection.

Current Shadow permits one later-session top-up, following renewed selection, entry qualification,
profitability and existing cash/risk controls. The source updates quantity and blended purchase cost
without clearing the old high, armed state or trailing stop. This does not authorize a new add-on
eligibility rule. Live V1 has separate protection and no add-ons; its policy is not changed here.
Sources: [Shadow policy](../../Core/Trader/SystemShadowPolicy.cs),
[add-on qualification](../../Core/Trader/SystemShadowController.cs),
[blended accounting](../../Core/Db/SystemShadowRepository.cs), [Live V1](0053-delphi-live-v1.md).

**Confirmed add-on design:** the operator accepted one combined holding, with updated average cost
while preserving the existing stop and tightest earned gap. Retain its actual price-high history.
After a filled add-on, use the updated weighted average cost for current purchase-profit reporting
and future band qualification. Do not recalculate historical profit as though the additional shares
had been owned earlier. A lower blended profit percentage cannot undo a band already earned.
Recheck the cost-aware break-even candidate and retain the higher protection. One common protective
exit covers all remaining shares. A fully closed holding followed by an actual new entry starts fresh.

For example, 100 shares at $100, then 25 at $105, gives 125 shares at $101 average cost. If the first
holding had already earned the 0.50% gap at a $105 high, retain its $104.475 stop and 0.50% gap,
although current blended purchase profit at $105 is now about 3.96%. The new shares can lose
individually at that stop; the combined holding remains profitable in this illustration. Preserving
a price stop does not promise an unchanged profit percentage after adding capital.

The subsequent authorized [saved add-on comparison](../reviews/progressive-trailing-trade-study-20261017.md#saved-add-on-trade-comparison)
supports seven records: three ARE exits after adding retain +1.58%/+1.93%, one earlier ARE exit
prevents its later add-on and retains +2.64%; MATR/CAS keep original exits below activation. The
recorded ARE sales are larger profits, so this protects profit sooner without improving those actual
sale returns. Six included add-on cost transitions verify, including three active ARE transitions
that preserve the stop/0.75% gap; four hypothetical first crossings/fills independently verify. Scope and exclusions live in
the report. This is a bounded close-based overlay, not a full engine replay or implementation.

The original bands and add-on contract remain agreed for design. Governing price/high inputs, version
scope and operational adoption remain outstanding. No new SQL or operational verification occurred.

## Update interval comparison — research only

The operator authorized comparing five-minute versus fifteen-minute completed-price stop raises,
with five-minute breach checks in both. The controlled saved-price test produces 61 paired distinct
closed trade/cost histories: 56 equal returns, four higher with five-minute updates and one higher
with fifteen-minute updates (DSV: +2.03% versus +1.29%). The earlier ARE interval favors five-minute
updates (+1.25% versus +0.82%); all add-on ARE outcomes are unchanged. The recent subset splits one
improvement each, so no universal winner is established. All 85 prior five-minute baseline records
reproduce; 170 scenario outcomes independently verify. The linked report owns source/cadence semantics,
deduplication and limits. This is not operating-input acceptance, native engine replay or implementation.

## Five-minute updates and observed-touch triggers — agreed design

On October 17, after reviewing the comparison, the operator selected completed five-minute price
updates for raising the stop. The operator then accepted triggering a sale on the first fresh price
observed at or below the established stop, without waiting for a five-minute close. This prioritizes
protecting profit over allowing a brief dip to recover. A touch can therefore exit before a rebound.

A newly created or raised stop applies only to subsequent fresh observations. Do not use a low from
earlier in the same completed bar to trigger against the stop just raised at its close. Observed touch
means detection when valid fresh data actually arrives, not a promise of continuous monitoring or a
broker-native stop. Fresh bid is now the agreed trigger input for all portfolios, especially Delphi
Live; freshness/monitoring and prospective version integration remain open. Existing protection
ownership and causal execution/cost contracts still apply;
a trigger does not guarantee the sale price.

The saved comparisons used five-minute **closing-price** breaches, so their returns do not measure
the newly agreed touch design. Current Shadow checks lows of received completed bars; that does not
establish real-time first-touch detection. Live has a separate fresh-bid contract. No new simulation,
code, operating policy or application change occurred in this decision step.

<a id="all-portfolio-scope-and-bid-price-recommendation"></a>

## All-portfolio scope and fresh-bid input — agreed design

The operator subsequently requested coverage of **all portfolios, including and especially Delphi
Live**, while explicitly retaining the no-implementation boundary. This replaces the earlier open
family-coverage question; individual-stock protection remains the unit of action. Prospective policy
identities and family-specific transition integration still require engineering design. The operator
now wants existing holdings included when the strategy is enabled, without waiting for new purchases. Existing
protection owners, add-on eligibility and real-account activation/execution gates remain authoritative.
This scope choice does not rewrite frozen policies or authorize activation or broker actions.

**Confirmed after explanation:** use a fresh valid positive **bid**, the price buyers are offering,
at or below the established stop. This relates the trigger to the sell side of the quote. A stop of
$104.50 and a subsequent observed bid of $104.48 would trigger an exit without waiting for the next
completed close. A short dip can therefore trigger even if the price later recovers. The trigger
does not promise a sale at $104.50 or $104.48.

Source inspection confirms Delphi Live already checks a bid received after the floor was persisted
against that floor, then obtains a separate post-decision quote for its simulated fill. See
[safety policy](../../Core/Trader/DelphiLive/DelphiLiveSafetyPolicy.cs) and
[existing Live contract](../concepts/delphi-live.md#provisional-v1-profit-protection).
Using a fresh bid across other portfolios still needs source/freshness/monitoring qualification;
saved bar-close/low studies cannot establish quote-touch returns. No fallback price is selected;
polling intervals are now agreed below. See [SEC order types](https://www.investor.gov/introduction-investing/investing-basics/how-stock-markets-work/types-orders)
for sell-side bid context and the distinction between quoted and executed prices.

### Remaining decisions before implementation

1. **High — now confirmed:** use the highest completed five-minute closing price since purchase.
   Preserve the agreed high history across sessions and additional purchases; no daily reset.
2. **Bid monitoring — now confirmed after the example:** check every five minutes when
   `bid > stop * 1.0025`; check every minute when `stop < bid <= stop * 1.0025`. An observed valid
   `bid <= stop` triggers immediately under the agreed design, not after another monitoring interval.
   Request a new quote at each check and reject observations more than 60 seconds old **since receipt**.
   For a $100 stop: $100.80 permits five-minute checks; $100.20 switches to one minute; a later $99.98
   triggers a sale. A fall can cross the stop between polls. Faster existing protection checks must
   not be slowed; usable quotes already obtained by shared protection may also be evaluated.
   These are accepted design settings, not implemented or proof of feed freshness.
3. **Existing holdings — now confirmed:** apply the new strategy to existing as well as future
   holdings as soon as it is enabled. Preserve stronger existing stops and earned protection.
   This answers the rollout-scope question; it does not authorize implementation or present activation.
   Prospective policy identities and safe initialization/handover remain engineering work under the
   existing ownership and execution boundaries, without rewriting historical actions.

**Freshness limit:** the accepted 60 seconds measures local receipt age, not proven market-data age.
Current `TmxQuoteFieldsV1` returns price/bid/ask and a local receipt timestamp; it does not expose a
bid-event timestamp in the Live receipt. A successful new fetch alone cannot establish that the
provider's market quote is current. Qualify that source contract before claiming current-market
protection; no external feed was queried here. See
[quote source](../../Core/Trader/DelphiLive/TmxDelphiLiveMarketDataSource.cs) and
[causal observation](../../Core/Trader/DelphiLive/DelphiLiveExecutionPolicy.cs).

Separately, implementation/validation work must test fresh-bid triggers and integration with current
protection/execution owners, especially Live. Prior closing-price simulations do not answer that test.
This is remaining work, not another unresolved choice of bands, portfolios or trigger-price type.

### Quote-source freshness check — October 17, 2026

**Finding: the current source is not yet qualified for the new one-minute fresh-bid protection.**
This is a current source/saved-evidence review, not proof that every present bid is stale.

- Live calls `GetQuotesBySymbolsAsync` / `getQuoteForSymbols`; the current query, DTO, canonical
  snapshot and Live receipt carry no bid-update/provider-as-of time. The adapter records local UTC
  receipt time. The action workflow accepts a positive bid received during the session and after
  the established floor; it cannot verify the underlying market quote's age. This does not establish
  that the provider offers no other timestamped product or field.
- Independently rereading the September 29 saved market-hours probe gives 22 requests/attempts,
  22 current quotes, zero positive two-sided bid/ask pairs and 22 pairs on the separate delayed
  endpoint. The diagnostic's pair flag does not establish whether a bid alone was absent. Its delayed
  `lasttradedatetime` dates a trade, not the bid. No sample qualifies bid freshness. This is September
  evidence, not a new October 17 market observation. Evidence: ignored local
  `artifacts/ai-risk-values-20260929/quote-timing-native.log` (13:00–13:01 Toronto).
- [TMX's public website guidance](https://money.tmx.com/en/terms-of-use) says market information is
  generally delayed by 15 minutes or more. That broad warning does not measure this API's exact lag.
  Polling more often and enforcing a 60-second local receipt limit cannot establish upstream freshness.

**Subsequent direction:** the operator does not consider that additional market-hours/source-guarantee
investigation worthwhile before implementation. Defer it; do not add a new implementation or promotion
gate. The source limitation remains explicit, without claiming the bids' market age is verified.
The design is ready for implementation and focused tests using the existing quote source. Engineering
work still includes prospective policy identities, preserved state for existing holdings, shared
protection ownership, adaptive scheduling and bid-trigger tests. No further trading-rule choice or
additional research round is required to start coding. Earlier no-implementation instructions remain
in force until implementation is requested; existing activation/execution boundaries still apply.

Do not substitute last-sale prices, silently switch to the delayed endpoint, weaken the agreed bid
rule or expand this into general gap repair. Existing source/code
and older operational evidence remain unchanged. October 17 is Saturday; no new market/broker calls,
SQL reads/writes, probes, runtime tests or application changes ran for this review.

## Review questions

1. How does a shrinking percentage gap differ from a fixed percentage trailing stop?
2. Why does ARE's later rally not by itself invalidate a profitable protective exit?
3. Which parts of this direction are confirmed, and which operating settings remain unapproved?
