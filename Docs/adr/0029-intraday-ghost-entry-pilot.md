# ADR-0029: Intraday ghost-entry pilot

- **Status:** Accepted
- **Date:** 2026-08-26
- **Domains:** architecture, market-microstructure, risk-management
- **Related:** ADR-0015, ADR-0020, ADR-0021, ADR-0028

## Context

The immediate problem is to begin exercising ADR-0028 with Delphi picks during the current trading session instead of waiting for another session open. The parent problem is proving the complete paper-position workflow—selection, entry, monitoring, advice, and later manual exit—before the durable intraday evidence ledger and outcome evaluator exist. The root goal is to tune Delphi from honest paper evidence under human strategic control.

ADR-0021's official calibration outcome enters at the first eligible session open. An intraday entry selected by the user cannot silently replace that definition. It is still useful as an operational pilot if its limitations are explicit and it cannot enter Athena's official scorecards or promotion evidence.

## Decision

Allow a user-selected set of today's persisted Delphi Continuation buys to be opened as one-share ghost positions during regular TSX hours. Preflight the entire requested set before writing: every symbol must have today's persisted Continuation `Buy` pick and must not already have an active position.

Use the close of TMX's newest returned five-minute interval as the observed paper-entry price. The interval may still be forming. Record its market-event time, receipt time, forming/completed state, linked `DailyPick` ID, Delphi rank, and composite score. This is an observed delayed paper price, not a broker quote or guaranteed achievable fill.

Keep this pilot distinct from official calibration evidence:

- create only ghost `TradeLog` and `ActivePosition` records; never send a broker order;
- use one share per symbol so the existing ledger can exercise position lifecycle while percentage returns remain meaningful;
- do not treat the position, its observed entry, or monitor replay as an ADR-0021 official tradeable outcome;
- do not count it in Athena scorecards, cohort totals, or champion/challenger promotion evidence;
- require a later immutable intraday evidence/outcome contract before results become calibration-grade.

Poll the monitor every fifteen minutes, two minutes after each quarter-hour boundary, through a final 4:02 p.m. Toronto poll. Retrieve TMX's direct fifteen-minute evidence and ignore mutable/incomplete source intervals for policy decisions. Evaluate only completed policy bars beginning at or after the exact ghost-entry time. Completed five-minute bars remain the accepted finer resolution for the later evidence ledger and exact aggregation checks; the replay-only pilot does not persist them.

The pilot may update the existing active-position price, profit/loss, high-water, drawdown, and session snapshot. It emits `Hold` or `ExitAlert` advice only. It never records a sale or places an order automatically. Until the fresh post-entry Delphi join is implemented, the optional ADR-0028 exception to the 10% loss alert is disabled; this is the conservative behavior.

Completed five-minute bars are the accepted version-1 intraday evidence resolution for the later durable collector. One-minute evidence is not selected because the market-hours probe found gaps, while five-minute evidence was gap-free and exactly reconstructed every comparable fifteen-minute bar. The monitor cadence remains fifteen minutes.

## Alternatives considered

- **Wait for the next eligible session open.** Rejected for this operational pilot because it would postpone testing the position-management loop; it remains the official ADR-0021 outcome convention.
- **Call the intraday entry an official outcome.** Rejected because that would mix incompatible entry definitions and overstate the evidence available for tuning.
- **Use a forming interval for exit decisions.** Rejected because the probe showed forming bars can change before completion.
- **Automatically close on an alert.** Rejected because TMX is delayed and the user retains execution control.
- **Persist one-minute evidence.** Rejected for version 1 because observed gaps reduce replay reliability without changing the accepted fifteen-minute decision cadence.

## Consequences

- The first five-position ghost cohort can be opened and watched immediately without confusing it with official calibration evidence.
- Entry provenance is inspectable, but the existing ledger is only a bridge and does not replace the planned immutable intraday schema.
- A process interruption or missed poll can be replayed from currently available TMX history for operational advice, but receipt-time history and decisions are not yet durable enough for a calibration-grade outcome.
- Any exit alert still requires a human decision and a separately recorded ghost/manual fill.

## Review questions

1. Why can today's intraday ghost positions not enter ADR-0021's official scorecards?
2. Why may the observed entry use a forming interval while exit decisions may not?
3. What does the later intraday evidence ledger add that this operational bridge does not preserve?
