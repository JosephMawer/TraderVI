# ADR-0099: Exact report sharing in Live snapshot writes

**Date:** October 21, 2026. **Status:** Accepted within the operator's request to remove remaining lag without changing risk controls.
**Domains:** architecture, data-pipeline.
**Related:** ADR-0082, ADR-0098.

The immediate problem is repeating report escaping and expanded-history allocation in paper writes.
The parent problem is paper action work delaying real evaluation beyond the existing freshness limit.
The root goal is reliable execution of the exact operator-reviewed strategy with preserved controls.

Build the typed compact snapshot from small records and shared immutable report strings. Stream
the same complete canonical SHA-256 fingerprint; preserve compact roots, all content hashes, item
membership/order, original report text and historical reconstruction. Keep the legacy raw-JSON path
for maintenance and old optional-property handling. Keep fresh SQL reads, write readback validation,
revision/lease fences, cash reconciliation and all existing financial/trading checks unchanged.

For reopened immutable historical quotes and completed actions, compare report strings ordinally
and compare every remaining field with the original exact serialized representation. This is
equivalent to comparing the complete serialization without re-escaping the report twice. Reference
equality remains valid only for the existing immutable in-process objects. Do not replace exact
checks with approximate/record equality, skip history, increase freshness/timeout limits or reset
continuity. Numeric representation, order and every changed field must still fail the original checks.

Offline hash, corruption, transition and allocation tests establish source behavior. Only native
timings after normal closure, reviewed installation and fresh approval can establish live capacity.
This grants no October 22 permission, next-day baseline, startup or order authority.

## Review questions

1. Why can sharing a report string remove allocation without omitting any fingerprint bytes?
2. Why must a reopened history comparison still check every non-report serialized field?
3. Why is an offline packing benchmark insufficient to declare tomorrow's execution ready?
