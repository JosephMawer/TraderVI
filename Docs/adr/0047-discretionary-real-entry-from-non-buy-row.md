# ADR-0047: Discretionary Real entry from a non-Buy Delphi row

- **Status:** Accepted
- **Date:** 2026-09-03
- **Domains:** architecture, decision-engine, market-microstructure, risk-management
- **Related:** ADR-0015, ADR-0037, ADR-0039, ADR-0044
- **Transaction boundary:** ADR-0048

## Context

The immediate problem is that an operator can already have an actual broker
position in a symbol that appears in Delphi's saved rows with direction `Hold`,
but the WPF entry path refuses to record it because ADR-0037 requires `Buy` for
every tracked entry. The parent problem is preserving the difference between a
Delphi recommendation and a discretionary broker action while still keeping the
operational position ledger truthful. The root goal is to supervise real capital
without fabricating model provenance, weakening Ghost experiments, or implying
that TraderVI placed an order.

ADR-0044 permits unlinked Real holdings because actual broker truth can exist
without Delphi provenance. The selected saved row is still useful context for a
new discretionary fill, but attaching it as `OriginalPickId` would falsely make
the position look recommendation-derived and would grant eligibility for the
fresh-Delphi loss exception.

## Decision

1. Continue requiring a saved `Buy` direction for every Ghost entry. A
   confirmation flag cannot bypass this rule.
2. Continue linking a Ghost or Real entry made from a saved `Buy` row to that
   row's `PickId` and entry composite.
3. Permit a non-`Buy` row to seed a Real entry only after the UI explicitly
   confirms both that the broker fill already happened and that the entry is a
   discretionary override of Delphi's saved direction.
4. Store the discretionary Real position with `OriginalPickId = null` and the
   trade's `EntryComposite = null`. Preserve the selected row's ID, date, lens,
   rank, and direction in durable notes as context rather than recommendation
   attribution.
5. Because the position is unlinked and Real, ADR-0044 includes it in the
   dashboard and monitor but denies the fresh-Delphi loss exception. It remains
   outside official calibration evidence.
6. Keep all existing duplicate-position, positive-share, positive-fill, account,
   manual-Real-exit, and no-broker-routing rules unchanged.

## Alternatives considered

- **Attach the selected Hold row as `OriginalPickId`.** Rejected because it
  would represent a vetoed row as the provenance for a recommended entry and
  would accidentally grant policy behavior reserved for a Delphi-linked Buy.
- **Keep refusing the entry.** Rejected because the database would then omit an
  actual holding that TraderVI is expected to supervise under ADR-0044.
- **Allow the same override for Ghost positions.** Rejected because a simulated
  experiment has no independent broker fact that requires reconciliation; it
  must remain attributable to a saved Buy recommendation.
- **Add an arbitrary-symbol entry form.** Deferred. The selected Delphi row
  already supplies reviewed symbol context for this case, and a broader manual
  form would expand the UI and validation surface.

## Consequences

- The operator can record an already-executed GGD Real fill even though the
  saved Continuation direction is Hold.
- The confirmation dialog makes the disagreement visible before any write.
- The resulting holding is monitored honestly as a discretionary Real position,
  while its notes retain why and from which row it was entered.
- No schema migration, broker operation, model change, threshold change, or
  official calibration mutation is introduced.

## Review questions

1. Why is the selected Hold row kept in notes instead of `OriginalPickId`?
2. Why can a Real entry override Delphi while a Ghost entry cannot?
3. Which monitoring exception is deliberately unavailable to the override?
