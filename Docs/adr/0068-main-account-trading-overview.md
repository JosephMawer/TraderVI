---
cssclasses:
  - executive-brief
---
# ADR-0068 — Main-account Trading overview

**Date:** 2026-09-09 · **Status:** Accepted; main workspace observed running. Unified portfolio list verified offline; runtime rebuild/restart pending.

**Domains:** user-interface

> [!summary] Decision
> Open Trading on the operator's main real account, with earnings, account value, holdings and the
> assigned strategy as the primary content. Keep attention items visible in a supporting role and
> provide a clear, short path from an independently running paper portfolio to strategy review.
>
> This sets the UI direction. It does not change promotion eligibility, accounting, risk rules or
> execution authority. Automated Wealthsimple trading remains the accepted future destination.

## Problem and decision

The immediate problem is an initial overview proposal that prioritizes attention items over the
operator's real-account results. The parent problem is unclear boundaries between Trading, independent
paper portfolios and the daily/intraday engines. The root goal is to watch actual account earnings,
understand paper strategies in operation, and deliberately select the main account's strategy on the
path toward separately designed automated execution.

The operator explicitly retained an overview while rejecting attention as its main focus. The design
therefore adopts these priorities:

1. **Trading is the main real-account landing page.** Put its earnings, account value, cash, holdings
   and active strategy ahead of paper activity and routine operational messages.
2. **Portfolios is the paper-account workspace.** Make independent accounts easy to inspect in action,
   including holdings, orders, decisions, results and comparisons. List every paper account together
   by default, including fixed Normal / Portfolio rules only pairs. Show gate profiles as row facts,
   not separate destinations. The operator's subsequent clarification supersedes the initial split.
3. **Make strategy review directly accessible.** An eligible portfolio should have an obvious route to
   reviewing its complete strategy for the main account. The UI must explain any current block.
4. **Attention remains present.** Show a compact summary and surface material issues beside the affected
   account or holding. Do not make a general attention queue the normal landing page.

## Boundaries and consequences

Promotion selects rules for the main account's own financial state; it never transfers a paper
account's capital, holdings, simulated fills or historical returns. The main account's earnings history
continues when its active strategy changes. Display the assignment and effective time without
attributing the entire account history to the current strategy.

[ADR-0062](0062-complete-strategy-recommendation-promotion.md) continues to require paused/empty
Trading and an appropriate reconciled account state for approval. Resume remains separate. The
fixed gate-comparison accounts remain ineligible under
[ADR-0067](0067-fixed-gate-paper-comparison-pairs.md). The request for quick access does not silently
waive either rule; any requested change needs its own behavior decision.

The current product uses operator-reported Real facts and recommendation authority. The UI should be
able to show a future connected account and separately authorized execution status, but must represent
the actual state of the integration. No broker connection or automated execution is authorized here.

## Financial display and remaining evidence work

The implemented overview shows total account gain as unavailable until complete contribution,
withdrawal, income and cost history exists. It separately shows recorded Real-account sale gains
over the last 30 Toronto calendar days and unrealized gains on that account's recorded open holdings.
These are scoped ledger figures, not complete broker earnings. Missing sale profit or holding marks
remain unavailable. Marks must match the account, position, shares and cost; saved position update
times are labelled without claiming they are synchronized market quotes.

Value and cash come from the selected account's operator report. The report time, prior-session state
and changed-ledger warning remain visible. The app does not infer synchronized net value from saved
holding prices. Full-account returns, their eventual selectable periods and attribution across
strategy changes still need an accounting evidence contract. Deposits must never become earnings.

The [implementation record](../concepts/trading-workspace-redesign-draft.md) carries the detailed design,
visual iterations, unresolved choices and validation. The initial attention-first suggestion
is superseded as the landing-page hierarchy; the connected overview and four-screen scope remain.

## Validation and implementation status

Following explicit implementation authorization, the four WPF workspaces now share side navigation,
main-account context and direct inspection/review links. Portfolios exposes account activity and
adjacent Normal / Portfolio rules only comparisons. Delphi identifies its saved daily source; Delphi
Live explicitly shows one selected paper account's candidates. Choosing a view does not reassign an
account or combine same-symbol observations from different accounts. Review retains existing approval
guards and fixed-pair exclusion.

Validation used .NET 10.0.401: isolated WPF Release build passed; the full Core suite passed 827 tests.
Fourteen synthetic WPF renders cover 1280×820 and 1600×1000 layouts plus unavailable/review states;
offline binding checks found no errors. The main-account panel was instantiated from compiled BAML;
operational views were rendered from their XAML with event handlers and service controls removed.
This verifies presentation, not database-backed workflows or all keyboard/display-scaling states.
Fixtures include five complete pairs, ten accounts, ordinary-scope exclusion and disabled pair promotion.

Builds retain existing compiler warnings; tests report cached dependency-security advisories separately.
No complete solution/SSDT build, fresh dependency audit, runtime-host restart, database workflow,
market-service access, model training or broker operation was performed. The isolated build, preview
sources, logs and images are under ignored `artifacts/workspace-ui-20260909/`. A controlled host update
remains pending; the new UI is not yet claimed to be running against operational data.

## Navigation follow-up (earlier iteration)

The operator's subsequent running-app screenshot confirmed the new Portfolios workspace. A presentation
follow-up adds collapsible icon navigation and makes the fixed-pair shortcut explicit as **Daily gates
on / off**, including loaded counts and applied/skipped labels. Release build and 19 offline renders /
automation checks passed. Runtime rebuild/restart for this follow-up remains pending; it changes no
gate profile or approval rules. See the implementation record for details.

## Unified portfolio list — subsequent clarification

The operator preferred seeing every portfolio in the strategy comparison grid. A separate gate-only
view helped frame the experiment but concealed accounts in ordinary use; that navigation is superseded.
The opening section is now **All portfolios**, with standard and paired accounts in one grid. A daily
gates column distinguishes Applied / Normal, Skipped / Portfolio rules only, and Per strategy for
ordinary accounts whose admission follows their assignment. Paired rows share default ordering and
retain immutable pair identity in detail. Operator-reported reference accounts are optional additions.

Visibility and comparability remain distinct: returns use the shared saved-date window and stay
unavailable without matching endpoints. Bringing accounts into one list does not make fixed profiles
editable or promotable. Core trading behavior and the current paused/empty approval requirement remain.
Release build, 15 focused comparison/gate tests and 19 offline renders/automation checks passed.
Fixtures contain all 15 paper accounts, verify selected-account and reference-checkbox behavior, and
keep newer-account returns unavailable. Runtime rebuild/restart remains pending. No database or
external workflow ran; existing compiler warnings and cached dependency-security advisories remain.

## Daily portfolio gains — selected trading day

The operator also needs to see what each portfolio earned within a particular day. A shared-period
return alone hides useful first-day results and makes running accounts appear empty. The unified
grid now adds **Gains for** and **Day gain**, showing money and percentage change for the same selected
date across every paper account. Account details expose that day's opening value, saved ending value,
evidence and timestamp. The latest saved session is selected initially; a chosen older date survives
refresh. Changing this date does not move the longer comparison window or the selected account.

Day gain is the saved ending valuation minus that session's saved opening valuation; the percentage
divides by that opening valuation. It includes held-stock value changes and costs already recorded by
the engine. This uses each engine's existing opening baseline, not a newly reconstructed synchronized
market-open price or lifetime starting capital. First-day accounts therefore have a daily result with
one session. These paper ledgers have no in-session cash flows; reference accounts remain unavailable
without cash-flow accounting.

Completed Shadow sessions use their saved close. The latest unfinished Shadow session may use current
saved cash/holding marks only when all open holding marks belong to that session's Toronto date and
the portfolio has not moved into a later session/date. These partial values may combine different
holding observation times. Live uses its saved opening and latest same-day checkpoint/close; an
incomplete latest mark never falls back to an earlier valid result. A checkpoint without a close is
labelled **Partial**, with its evidence time. Missing dates/valuations remain unavailable, while a
valued cash-only day shows zero. History before account activation is never invented.

This is a SELECT-only history/presentation change. It does not add snapshots, recover missing market
coverage, change execution cadence, alter strategy/gate rules or change promotion eligibility.

## Stable portfolio display refresh

The operator reported periodic after-hours flicker. The desktop's 30-second timer unconditionally
reread portfolio/dashboard data; collection replacement cleared rows, date choices and account details
even for identical saved results. Automatic display reads now run during a weekday 09:30–16:10 Toronto
window and once on leaving it, allowing the existing closing workflow to settle. Startup, page reopen,
manual refresh and explicit account operations still read saved data outside that window.

This is a presentation schedule, not a market calendar or execution condition. Engine/status database
checks, Live pre-open heartbeat, session finalization and operator safety work remain on their existing
paths. Engine work precedes display reads; overlapping desktop timer turns are skipped. Identical
snapshots retain row objects and date choices. Actual changes update individual rows, while changing
accounts immediately clears the prior account's details. No new quote/trade suppression or execution
schedule was introduced.

## Compact comparison grid and wider account details

The operator preferred reclaiming the repeated Evidence and Max decline columns for account details.
All portfolios now keeps five scanning columns: Strategy / account, Daily gates, Day gain, Period
return and Account value. The selected-account panel receives one-third of the available column space
(previously one-quarter), with a 320-unit minimum width. Evidence is grouped directly below the selected
period return; maximum closing decline remains in the account details. Period-return cells also expose
their evidence as a tooltip, including missing endpoints. No performance calculation or refresh behavior
changes with this layout adjustment.

## Controls belong to the comparison card

The operator requested removing the page-wide Main Trading strategy banner. All portfolios now puts
the compact shared-period selector and reference-account checkbox in the Strategy comparison card's
top-right header, with the saved date range inside that card. The Performance chart has equivalent
compact filters and date context, bound to the same selection. The main strategy remains visible in
Trading and Strategy review, where it supports account management and promotion. This reclaims the
banner row without changing daily-gain dates, comparison math, reference-account scope or refresh rules.

## October 18 visual refinement direction

The operator selected the **Workspace** concept (table beside persistent selection context) and
requested agreeing Trading's main layout before moving to the other three screens. Both Buy and Sell
belong in the holding panel. These accepted visual choices retain the account-first overview above;
the operator subsequently accepts the account-summary/strategy/holdings/activity arrangement and asks
to refine the top gains/performance overview next. The operator then selects **Performance first**:
selected-period dollar gain and percentage lead, with the period visible below; account
value/cash and separately scoped recorded gains follow. Net deposits were initially shown but are
subsequently removed from the overview; cash-movement records and return calculations are unchanged.
The operator selects **Today**
as default with an **Overall** option, and removes the redundant Trading heading/right-hand badge.
The subsequent refinement removes the whole Main account / Example TFSA / Canadian dollars heading
block in the single-account concept and reduces the overview figures/spacing; currency remains beside
figures and account/source/authority contracts still apply. Daily-return source/baseline/cash-flow/percentage
qualification is still required; an overall broker return cannot stand in for missing daily performance.
Overall retains the supplied broker period and valuation time. Portfolio choice is explicitly
deferred. The suggested Find stock route remains a draft. One Delphi Live engine can run several
independent paper portfolios; no existing engine/family or comparison-selection contract changes.
Broker-reported return and complete gain calculated from TraderVI records are different evidence
sources; a UI must retain each source's period, original valuation time and availability. The display
period choice is accepted; no new performance calculation or source qualification is established. Detailed views and order tickets
follow later. No new WPF
implementation, accounting change, broker operation or execution authority follows from this selection.
The next holdings refinement accepts Stock, Position value, Open gain and Strategy status as the table
columns, with shares only in the selected holding panel alongside average cost. The concept proposes
grouping position facts before the strategy explanation and retains both Buy/Sell below; the operator
accepts that grouping. The next refinement requires a clear Hold/Sell reason without repeated “saved
strategy evaluation” wording. Decision, concrete reason and next trigger/step are proposed, with actual
check time, missing-data state and order-submission state distinct. Fictional explanation thresholds do
not define new rules. One selected strategy appears in a dropdown at the former portfolio placeholder
as the entry point for manual switching; actual portfolio selection remains deferred. Existing exact
strategy review, ownership, protection and handover contracts remain (ADRs 0079/0081/0085/0086).
The mock's dropdown changes local example state only; real switch/review interaction remains to design.
No holding, valuation or execution behavior changes.
The subsequent explicit request authorizes implementing this design in **Trading only**, leaving other
tabs/views/pages alone. The operator confirms queuing old-strategy-only exits when checks allow, even
at a loss; shared holdings/protection remain and required exits reconcile before new buys (ADR-0079).
The first WPF increment connects the dropdown to the existing exact-source transition preview and
keeps manual Buy/Sell commands in the selected holding panel. Today and unverified decisions remain
unavailable; existing return and linked management facts retain their actual context. WPF build,
194 focused tests and four synthetic renders pass. This does not enable automatic switching/trading
or qualify/alter family/comparison support. See the linked implementation record for limits.
See the [current draft](../concepts/trading-workspace-redesign-draft.md#trading-workspace-refinement).

## Review questions

1. What should lead the Trading overview, and where should attention items appear?
2. When a paper strategy becomes the main strategy, which account's financial facts are used?
3. Why must the main account's earnings history remain separate from the current strategy's start date?
