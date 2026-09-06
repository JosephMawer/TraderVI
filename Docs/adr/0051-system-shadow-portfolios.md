# ADR-0051: System-selected Shadow V1 portfolios

- **Status:** Accepted
- **Date:** 2026-09-04
- **Domains:** architecture, data-pipeline, decision-engine, market-microstructure, risk-management
- **Related:** ADR-0020, ADR-0028, ADR-0030, ADR-0031, ADR-0032, ADR-0037, ADR-0039, ADR-0048

## Context

The immediate problem is that Delphi publishes daily ranked candidates, but the only operational Ghost
trades are chosen and timed by the operator. That is useful for testing individual ideas, but it cannot tell
us how a repeatable system-selected portfolio would have behaved with the same capital available to the
real TFSA.

The parent problem is selection bias: a result based only on trades the operator happened to choose does
not measure the automatic system we may eventually want. The root goal is a safe path from advice, to an
auditable automatic simulation, to optional operator-followed trades, and only much later to separately
authorized broker execution.

Shadow V1 is not Athena. Athena measures immutable candidate outcomes at fixed checkpoints so Delphi can be
calibrated fairly. Shadow measures one capital-constrained portfolio policy, including missed entries,
vacancies, sizing, rotation, and execution friction. Neither result may be silently substituted for the
other.

## Decision

### Identity and capital

1. Keep execution mode (`Ghost` or `Real`) separate from selector (`Operator`, `System`, or future `AI`).
   Shadow V1 is always `System` selected and `Ghost` executed. It has no broker adapter.
2. Run four independent alternative portfolios: Continuation Top 3, Continuation Top 5, Breakout Top 3,
   and Breakout Top 5. Each starts with the full manually entered TFSA value, because each answers “what if
   this one policy controlled the available account?” Their values must never be summed as shared wealth.
3. Treat Top 3 and Top 5 as maximum concurrent distinct holdings. Use equal-weight target slots and
   whole-share virtual purchases. The first buy uses 75% of one slot; one later-session Delphi
   reaffirmation may add the remaining 25% only while the position is profitable and rising.
4. Record later real-account snapshots separately. They update the comparison, not Shadow cash or past
   performance. Deposits and withdrawals require explicit capital events before they may be mirrored.

### Daily evidence and entry

5. At 09:30 Toronto, freeze the newest `Valid` `OfficialPaper` Delphi run for that recommendation date.
   Each portfolio uses only its published ranks 1..K from that run. A later rerun cannot rewrite the day.
   If no such run exists, record `NoValidDelphiRun`, take no new risk, and continue protecting holdings.
6. The normal first decision is after the opening 15 minutes plus one five-minute interval, approximately
   09:50. Same-session activation is allowed: clicking Start establishes a baseline and requires the first
   completed five-minute bar received after activation. Earlier bars are never replayed into a trade.
7. A candidate qualifies when its newest completed five-minute close is at or above both its previous
   completed daily close and the immediately preceding completed five-minute close. Flat counts as “not
   going down.” Missing, late, conflicting, incomplete, or unavailable evidence blocks new risk.
8. Recheck the frozen candidates every five minutes until close. Fill a vacancy with the highest-ranked
   candidate that qualifies then; never expand Top 3 to rank 4 or Top 5 to rank 6. At close, expire unfilled
   buy signals and record no-entry reasons.

### Carry, rotation, and exits

9. A holding keeps its slot until an exit even when it drops out of the next Delphi list. There is no
   maximum profitable holding period. A next-session Delphi reaffirmation never overrides risk protection.
10. An exited slot may be reused the same day. One same-day re-entry is allowed after a price-based exit,
    only after a new completed five-minute bar and full requalification. A data-safety shutdown cannot be
    re-entered, and a same-day re-entry cannot also receive an add-on.
11. Entry day is Session 1. At the first Session-2 checkpoint, a losing incumbent that also fails the entry
    momentum test may be replaced by the highest-ranked qualifying unheld contender. If none qualifies,
    keep monitoring. A position still below cost-aware break-even at Session-2 close receives an exit
    signal; its fill remains pending until a later price can be observed.
12. Persist the rotation decision and incumbent identity for counterfactual review. This version uses the
    durable event and intraday-evidence history; a richer counterfactual report may be added without
    changing trade behavior.

### Risk and causal execution

13. Apply a provisional hard stop 5% below blended average cost on every completed five-minute bar. Once a
    completed fifteen-minute close reaches cost-aware break-even, maintain a stop at the higher of
    break-even or 5% below the highest completed fifteen-minute close. The stop never moves down. Persist
    the start time of the last fifteen-minute bar applied to each position and consume a bar at most once.
    A bar that first arms or raises the trail cannot then test its earlier low against that new trail.
14. A 3% loss from session-opening NAV blocks buys, add-ons, re-entries, and rotations for the rest of that
    session; exits continue. A 10% decline from highest closing NAV puts that portfolio in
    `CapitalReviewRequired`; exits continue, but only an explicit operator resume can allow new risk.
    Resume re-arms the drawdown guard from the reviewed current NAV; the threshold breach and review remain
    in the immutable event history.
15. A decision exists only after the completed bar is received. Persist it as a pending order and simulate
    its fill at the open of the first completed five-minute bar whose start is strictly after the signal
    receipt. Buy prices are raised and sell prices lowered by 0.25%. Whole-share quantity is computed from
    the adjusted buy price and current cash, so a portfolio cannot overspend. A pending buy is valid only
    for that exact immediate fill bar. If a restart or a later completed bar proves the window was missed,
    cancel the order and require the frozen candidate to pass the full current entry test before creating a
    new order. Never fill the stale commitment at a later convenient open.
16. Persist sessions, frozen candidates, decisions, orders, positions, cash, capital snapshots, and events.
    Restarting may continue pending sells and future monitoring, but it must not invent polls or missed
    trades while the WPF host was closed. Pending sells remain protective after restart. Same-session buys
    whose immediate window was missed require requalification, and prior-session pending buys expire.

### Hosting and presentation

17. Host the shared Core controller in WPF for V1 and keep it off by default. The existing five-minute
    schedule invokes it while the app is open. Pause blocks only new risk; it never disables exits.
18. Add a Portfolios tab showing the Real comparison, Operator Ghost records, and all four System
    alternatives. Show status, NAV, cash, holdings, P/L, returns, drawdown, freshness, holdings, and audit
    events. Display names are editable, while stable portfolio codes and generations are immutable.
19. Dividends, splits, and unsupported corporate actions are excluded from V1. Surface that limitation in
    documentation; do not invent adjustments. A headless host, AI selector, broker reconciliation, and
    real execution all require later decisions.

## Alternatives considered

- **Use only operator-selected Ghost trades.** Rejected as the sole measurement because operator timing and
  choice create selection bias.
- **One combined Continuation/Breakout portfolio.** Rejected because the selector would need a new
  cross-lens ranking rule and would obscure which Delphi thesis supplied the result.
- **Use unlimited virtual cash.** Rejected because returns would not answer what the available TFSA capital
  could have earned.
- **Fill at the signal bar close.** Rejected because that price was already complete when the signal became
  knowable; it would create look-ahead bias.
- **Run Shadow inside Athena.** Rejected because fixed candidate outcomes and a capital-constrained trading
  policy answer different questions and have different missing-data semantics.
- **Start with broker execution.** Rejected because the selection, risk, restart, and reconciliation state
  must be observed safely with virtual money first.

## Consequences

**Easier:**

- Operator and system selection can be compared without pretending they are the same experiment.
- Every fill and skipped decision has durable, causal evidence and can survive a process restart.
- A trailing bar cannot manufacture an exit by being evaluated once to establish a stop and again against
  that newly established stop.
- A delayed or restarted process cannot turn an hours-old buy decision into a current fill without a fresh
  qualification decision.
- Top 3 versus Top 5 and Continuation versus Breakout can be compared under the same initial capital.
- The system now has a concrete, no-broker stepping stone toward future automation.

**Harder:**

- WPF must remain open for V1 polling; no evidence or trade is invented while it is closed.
- Four alternatives increase source requests and ledger volume even though their candidate symbols overlap.
- Manual TFSA snapshots and unsupported corporate actions limit the accuracy of Real-versus-Shadow
  comparison until later integrations exist.
- The 5%, 3%, 10%, two-session, 75/25, and 0.25% values are provisional policy choices that require observed
  Shadow evidence before revision.

**Would tell us this was wrong:**

- The delayed source cannot reliably support causal five-minute decisions, ledger restarts create duplicate
  orders, or the provisional rotation/risk rules consistently discard later winners without improving
  capital preservation.

## Review questions

1. Why are there four independent portfolios instead of one combined list or one shared cash pool?
2. Why does a signal create a pending order rather than fill at the signal bar's close?
3. What happens when there is no valid Delphi run at the 09:30 freeze?
4. Which Shadow results may Athena use automatically?
