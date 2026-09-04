# ADR-0048: Consequential operational transaction boundaries

- **Status:** Accepted
- **Date:** 2026-09-03
- **Domains:** architecture, data-pipeline, risk-management
- **Related:** ADR-0015, ADR-0020, ADR-0031, ADR-0035, ADR-0039, ADR-0047

## Context

The immediate problem is that tracked-position opening and same-day Delphi
publication were multi-write workflows without one commit boundary. An entry
could leave an unattached BUY or an unmatched position after interruption.
Delphi deleted and reinserted narratives, dossiers, and picks separately, and
it skipped replacement entirely when a successful rerun had no qualifying
Continuation picks. Readers could therefore observe an empty/partial projection
or stale same-date recommendations.

The parent problem is making the manual advisory/execution loop dependable
under retries, crashes, and concurrent UI activity. The root goal is protecting
real capital while preserving trustworthy operational and calibration evidence.

The manual Real-exit repository already inserts the SELL and closes the active
Real position in a serializable transaction. Its unresolved limitation is a
different issue: version 1 represents only one all-shares, zero-commission fill.
Partial fills and commissions require an explicit lifecycle and accounting
decision and are not inferred here.

## Decision

1. Insert a tracked BUY and its `ActivePosition` in one serializable
   transaction. Allocate both IDs first and attach the BUY to the position in
   its initial insert. Recheck the active-symbol duplicate guard under the same
   lock; a concurrent duplicate returns no new rows.
2. Replace one recommendation date's mutable Delphi projection in one
   serializable transaction. Delete child-to-parent (`LlmNarrative`,
   `DecisionDossier`, `DailyPick`) plus same-date `GranvilleIndicatorLog`, then
   insert the complete new picks, dossiers, and Granville rows before commit.
3. Treat a successful zero-result Delphi publication as an empty replacement.
   It must clear stale same-date operational rows rather than leave yesterday's
   result of an earlier same-date run visible.
4. Keep append-only `CalibrationRun`, candidate, and lens evidence outside the
   mutable operational-publication transaction. Evidence describes the
   evaluation; the operational projection is a replaceable view for one date.
5. Preserve the existing serializable full-Real-exit transaction. If an active
   Real row is already closed, return the one attached Real SELL as an
   already-recorded receipt instead of creating a duplicate. More than one such
   SELL is an integrity error requiring manual reconciliation.
6. Guard monitor price-snapshot updates with `IsActive = 1`. A monitor cycle
   holding a stale in-memory row cannot overwrite the exit snapshot after a
   concurrent manual close commits.
7. Require the operator to affirm that a Real exit is one all-shares fill with
   zero commission. Reject use of the workflow for partial or fee-bearing fills
   until their policy is decided.

## Alternatives considered

- **Rely on cleanup after a failed write.** Rejected because detection and
  repair are not equivalent to preventing an incomplete capital-tracking row.
- **Publish to staging tables and swap a generation pointer.** Deferred. A
  single SQL transaction gives atomic reader visibility for the current scale
  without a schema migration.
- **Include immutable calibration evidence in the publication transaction.**
  Rejected because append-only evidence and replaceable operational state have
  different retry semantics and lifetimes.
- **Add partial-fill or commission inputs now.** Rejected because fields alone
  do not decide remaining-share state, cost-basis allocation, fee attribution,
  retry identity, or how multiple fills close one position.

## Consequences

- A committed tracked entry always has exactly one attached BUY and one active
  position; a rolled-back entry exposes neither.
- A same-date Delphi reader sees the old complete projection or the new complete
  projection, including an intentionally empty result, but not a partial one.
- A retry after an ambiguous committed Real exit returns its durable receipt
  and cannot add another SELL.
- A stale monitor cycle cannot modify a position after it has closed.
- Partial fills and commissions remain unsupported and visibly blocked; their
  decision remains in `Docs/reviews/open-questions.md`.
- No schema migration, database deployment, model/gate/threshold change, broker
  operation, or operational run is introduced.

## Review questions

1. Why is calibration evidence outside the operational publication transaction?
2. What does a successful zero-result Delphi rerun publish?
3. Why is an all-shares/zero-commission confirmation safer than adding two
   ungoverned input fields?
