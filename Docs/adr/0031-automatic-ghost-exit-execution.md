# ADR-0031: Automatic policy-triggered ghost exit execution

- **Status:** Accepted
- **Date:** 2026-08-26
- **Domains:** architecture, market-microstructure, risk-management
- **Related:** ADR-0015, ADR-0028, ADR-0029, ADR-0030
- **Supersedes in part:** ADR-0029's requirement for a human to record every
  paper exit after an alert. It does not authorize live brokerage execution.

## Context

The immediate problem is that the first ADR-0029 pilot emitted correct exit
alerts but required Codex and the user to notice the terminal and separately
record a sale. The parent problem is that a paper portfolio cannot test the
accepted policy honestly when execution depends on conversational memory or
operator availability. The root goal is to tune Delphi from reproducible paper
evidence under the user's strategic control while preserving capital.

The user has now authorized the paper monitor to close a ghost position when
the accepted ADR-0028 policy produces an exit alert. TMX remains delayed, so an
alert threshold is not an achievable fill price and cannot be awarded as one.

## Decision

Automatically execute **ghost-only** exits for Delphi-linked paper positions
when the versioned ADR-0028 policy emits `ExitAlert`.

### Confirmed direction

- Policy decisions use completed direct fifteen-minute TMX evidence on the
  accepted fifteen-minute polling cadence.
- Each poll persists its five- and fifteen-minute receipt audit and completed
  evidence through ADR-0030 before the result is treated as durable.
- The paper position closes without a second human prompt when the policy emits
  `ExitAlert`.
- No code path created by this decision may submit a broker order. Live routing
  remains separately deferred and unauthorized.
- Operational pilot results remain outside official Athena scorecards and
  cannot promote a Delphi change.

### Accepted implementation defaults

1. Fetch and persist the direct fifteen-minute batch, then replay the identical
   completed evidence from that durable batch from entry through the new
   observation.
2. If an exit is detected, make a second, post-detection five-minute TMX
   request. Use the newest price returned by that later receipt as the ghost
   exit price. Record its event time, receipt time, forming/completed state,
   policy reason, and delayed-price limitation in the trade audit notes.
3. If no post-detection price is available, keep the position open, retain the
   alert, and retry at the next eligible poll. Never substitute the earlier
   threshold or a guessed price.
4. Record the SELL trade and position closure in one SQL transaction guarded by
   the active-position row so concurrent or restarted monitors cannot produce a
   second exit.
5. Continue processing other symbols when one symbol fails. Persist a bounded
   failed-poll audit when the source request itself fails.
6. Keep one manual ghost `sell` command for corrections and explicitly timed
   user overrides; automatic exits use a distinct policy reason.

## Alternatives considered

- **Continue advisory-only alerts.** Rejected because the first pilot proved
  that this makes the simulated outcome depend on operator attention rather
  than the accepted paper policy.
- **Use the crossed trail or stop as the fill.** Rejected because delayed data
  cannot prove that price remained available after detection.
- **Use the newest price already present in the alert-producing response.**
  Rejected as the default because it was received before the decision was made;
  a second receipt establishes an honest post-detection observation.
- **Enable real brokerage execution at the same time.** Rejected because paper
  automation is not evidence that broker identity, order type, liquidity,
  partial fills, retries, or capital controls are safe.

## Consequences

- The paper portfolio follows the accepted exit policy without requiring Codex
  or the user to remember a pending action.
- Exit prices can be worse than the triggering trail or stop; that is an honest
  consequence of the delayed source and polling cadence.
- The monitor becomes a consequential SQL writer and external-data consumer.
- A missing or failed post-detection observation delays the ghost exit and must
  remain visible in the dashboard.
- This decision supersedes only the human paper-exit step. It does not promote
  the intraday challenger, alter Delphi ranking, or authorize live orders.

## Review questions

1. Why is the crossed threshold not used as the ghost fill price?
2. What makes an automatic ghost exit safe against duplicate monitors?
3. Which part of ADR-0029 changes, and which paper/calibration boundaries remain?
