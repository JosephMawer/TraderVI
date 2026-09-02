# ADR-0040: Reconstructible delayed-intraday outcome fill

- **Status:** Accepted
- **Date:** 2026-08-28
- **Domains:** architecture, data-pipeline, market-microstructure, math-statistics, risk-management
- **Related:** ADR-0021, ADR-0024, ADR-0027, ADR-0028, ADR-0030, ADR-0039

## Context

The immediate problem is assigning a reproducible simulated fill after the
delayed intraday policy detects an exit. The parent problem is measuring whether
ADR-0028 improves published Delphi recommendations without borrowing prices that
were unavailable when the alert was known. The root goal is trustworthy,
versioned calibration evidence that remains separate from operator-timed Ghost
and Real trades.

The accepted policy rejects its trigger threshold as a fill, but the immutable
ledger stores completed five- and fifteen-minute bars rather than the mutable
quote snapshot used by the operational Ghost executor. Wealthsimple currently
charges no commission for Canadian equity trades. The existing 25-basis-point
assumption represents a conservative spread/slippage sensitivity, not a broker
commission.

## Decision

For delayed-intraday outcome version 1:

1. Detect policy exits from completed fifteen-minute bars using their first
   durable receipt time.
2. Assign the raw simulated exit to the open at the exact next five-minute
   boundary during a regular session. If detection occurs after the last
   five-minute start, use the next observed regular-session open. Never award
   the earlier trigger or trailing threshold or silently substitute a later
   in-session bar.
3. Report the raw zero-commission gross return separately from a conservative
   return that applies 25 basis points per side for spread/slippage sensitivity.
   Do not label the sensitivity as a Wealthsimple fee or commission.
4. Require an XIU five-minute bar at the identical simulated-fill timestamp and
   report both raw and conservative excess return versus XIU.
5. Collect XIU five- and fifteen-minute evidence once per WPF monitor cycle,
   independently of which Delphi-linked positions are open. A benchmark failure
   is audited and surfaced but cannot authorize or suppress a position action.
6. Keep the outcome population tied to immutable official published Delphi
   candidates. Operational Ghost/Real mode, account, actual fill, and P/L do not
   determine the calculated outcome.
7. Require the replayed fifteen-minute path to begin at the official entry,
   remain on the Toronto regular-session grid, preserve consecutive bars and
   session ordinals, and keep first-receipt times non-decreasing. Once later
   evidence proves an omitted policy or exact fill bar, persist an audited
   invalid outcome. When no later evidence proves the apparent tail gap, keep
   the candidate pending.

The operator restated the essential fill rule before acceptance: the simulated
outcome receives a price observed at the first five-minute checkpoint after the
alert. The implementation makes the boundary exact as the first five-minute bar
start at or after the recorded detection time.

## Alternatives considered

- **Use the trigger price.** Rejected because delayed evidence proves the alert
  was known later and the threshold may no longer have been achievable.
- **Use the operational Ghost or Real fill.** Rejected because operator choices,
  broker timing, and selected positions would contaminate official calibration.
- **Subtract no sensitivity at all.** Rejected as the only reported view because
  a zero commission does not eliminate bid-ask spread or slippage. The raw view
  remains available and explicit.
- **Call 25 basis points a commission.** Rejected because it misstates the broker
  contract and obscures what the model is testing.

## Consequences

- Every simulated fill can be reconstructed from immutable event and receipt
  times without a mutable quote snapshot.
- Raw and conservative results answer different questions and must remain
  visibly separate in reports.
- Matching intraday XIU evidence becomes a prerequisite for a matured outcome.
- A later available bar can prove that an exact required bar is missing, but an
  empty evidence tail cannot; this keeps `Invalid` distinct from `Pending`.
- Receipt-order validation prevents a late historical backfill from being
  replayed as though it had been known before subsequently received evidence.
- Existing SGY evidence collected before XIU polling begins may remain pending
  where no aligned benchmark bar exists; the evaluator must not invent one.
- This ADR does not change SGY, any live holding, Delphi ranking, or the accepted
  exit thresholds.
- Athena persists this as a fifth `Tradeable` definition and reports
  Continuation and Breakout separately with equal market-session cohort
  weighting. Migration 015 was operator-applied and the definition was verified
  active on 2026-09-01; the associated backup was not independently verified.
- A Real sale remains the operator-reported broker fill in Trade history. It is
  not replaced by, or copied into, this standardized outcome.

## Review questions

1. Why is the alert threshold not an achievable simulated fill?
2. What does the 25-basis-point result represent if Wealthsimple charges no commission?
3. Why must the official outcome ignore the actual Ghost or Real trade fill?
