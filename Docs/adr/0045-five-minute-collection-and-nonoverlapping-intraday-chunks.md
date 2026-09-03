# ADR-0045: Five-minute collection and non-overlapping intraday chunks

- **Status:** Accepted
- **Date:** 2026-09-02
- **Domains:** architecture, data-pipeline, data-sources, market-microstructure, risk-management
- **Related:** ADR-0028, ADR-0030, ADR-0032, ADR-0040, ADR-0044

## Context

The immediate problem has two parts. The operator wants the WPF market monitor
to refresh market evidence every five minutes, and SGY's first two 2026-09-02
cycles failed at the 15-minute fetch. The parent problem is reliable
multi-position monitoring that remains reconstructible when a position needs a
wide historical request. The root goal is trustworthy advisory supervision with
durable source evidence and no automatic Real execution.

A read-only diagnostic reproduced SGY's exact production window from
2026-08-28 13:30 UTC through the current poll. The client divided it at the
five-calendar-day boundary, but both inclusive requests returned the bar at
2026-09-02 13:30 UTC. TMX supplied different values for that shared boundary
bar, so the existing conflict guard correctly rejected the merged batch. The
problem was overlapping request windows, not the position record or symbol.

ADR-0028 used “polling cadence” for both evidence collection and policy-event
evaluation. The requested change exposes the need to name those separately.
Additional five-minute collection cycles can refresh five-minute evidence and
position snapshots, but a policy decision still consumes only completed
15-minute bars. Re-evaluating an already processed 15-minute event is a no-op.

## Decision

1. Schedule WPF market collection every five minutes from the existing first
   safe poll at 09:47 Toronto through 16:02. Keep the thirty-second SQL display
   refresh separate.
2. Preserve `PolicyIntervalMinutes = 15`, all policy thresholds, Real/Ghost
   execution boundaries, and the first safe poll. A five-minute scheduler tick
   does not create a five-minute exit-policy bar.
3. Record new observations as `IntradayEvidenceCollectorV2`. Preserve
   `DelayedIntradaySwingV1` because its accepted 15-minute event sequence,
   decision rules, and fill convention do not change. This ADR narrows
   ADR-0028's earlier cadence-version statement by distinguishing collection
   cadence from policy-bar cadence.
4. Build wide TMX requests from minute-contiguous, non-overlapping windows no
   longer than five calendar days. When another window is required, end the
   earlier request one minute before the next aligned start.
5. Retain exact duplicate removal and conflicting-duplicate rejection as
   defense in depth. Do not use last-write-wins to hide contradictory evidence.
6. Continue requiring exactly one monitor host. The faster cadence increases
   source traffic and is not permission to run multiple WPF or CLI monitors.

## Alternatives considered

- **Accept the later duplicate boundary bar.** Rejected because it would choose
  evidence after observing a conflict and make replay source-order dependent.
- **Keep overlapping windows and retry.** Rejected because TMX repeatedly
  revised the shared boundary; retries do not remove the ambiguity.
- **Change the policy to five-minute bars.** Rejected because the user requested
  faster polling, not new stop/trailing rules, and that would require a new
  calibrated policy definition.
- **Keep the 15-minute scheduler.** Rejected by the operator in favor of more
  frequent position and evidence refresh.

## Consequences

- SGY and other positions whose monitoring horizon crosses a chunk boundary can
  be collected without requesting the same minute twice.
- The WPF app performs about three times as many collection cycles. Source
  failures remain visible and transient transport failures retain bounded
  retry behavior.
- Five-minute evidence can be received closer to its completion time, while
  exit decisions remain based on completed 15-minute bars.
- Existing durable observations remain immutable under collector v1. New rows
  carry collector v2; the evidence schema, source contract, and policy version
  remain unchanged.
- No database migration, strategy/ranking change, broker integration, or Real
  automatic-exit capability is introduced.

## Review questions

1. Why does a five-minute collection schedule not turn the exit policy into a five-minute policy?
2. Why must adjacent TMX chunk requests not share a boundary minute?
3. Why does the merge guard still reject conflicting duplicates after request overlap is removed?
