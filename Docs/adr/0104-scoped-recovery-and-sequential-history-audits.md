# ADR-0104: Scoped recovery and sequential history audits

**Date:** October 22, 2026. **Status:** Accepted direction; installed, real-only recovery/clearance verified; Paper coverage and full native capacity qualification remain pending.
**Domains:** architecture, data-pipeline. **Related:** ADR-0103, ADR-0082, ADR-0098.

## Problem

Recovery repeatedly reconstructed adjacent complete paper snapshots, then made the real account wait
for the entire paper archive. The parent problem is coupling operational readiness to research history.
The root goal remains timely real operation with trustworthy state and responsive supervision.
The operator explicitly requests a fix that processes history once and separates real recovery from
paper research, prioritizing operational availability while keeping real trading disabled until review.

## Decision

Use a fresh sequential reader for each fixed-boundary audit pass. Read the immutable paper membership
change index once per portfolio, fetch/hash/decode each distinct referenced content once, and carry the
previous verified state forward. Page original revision headers and legacy inputs. Retain no additional
archive copy on disk. A restart resumes the durable verified cursor and hydrates its preceding state;
it does not restart completed coverage. A later scheduled audit reads fresh evidence again.

Reuse is private to one audit run/database identity, with immutable guards checked before every batch.
Memory is bounded: at most one million membership changes per owner, a conservative 384MiB content/
decoded-state allowance, and 32MiB escaped-report cache. Exceeding capacity fails closed. Small bounded
header/input pages and the original snapshot limits still apply. No operational read cache qualifies an
audit. Original roots, exact content hashes, transition rules, cash reconciliation and checkpoint
qualification remain required. Legacy version-two snapshots commit a checksum to the entire expanded
state, so those original bytes still pass through SHA-256 for each original revision. This decision
does not authorize skipping old checksums, converting history or deleting records. Version-three
archive roots retain their existing verification contract.

Active audits admit up to 4,096 records or five seconds of work, then yield for one second. The time
budget is checked between records; an admitted large record may exceed it. Idle checks retain their
30-second interval. Ordinary audits retain ADR-0103's nightly/weekly off-hours policy. Explicit recovery
may run now. Real recovery has priority; immutable original full-recovery progress is preserved.

Offer explicit Real, Paper, and All recovery scopes using optional hashed plan metadata in the existing
tables. Real covers broker history, required shared input/content and the exact current real subscription;
Paper covers paper histories and frozen legacy research inputs. Scoped plans include database recovery
identity. Original plans remain readable and unchanged. No additional schema migration is needed.

A completed real proof can resolve known real findings or narrow known global continuity/initial-
recovery/legacy-paper findings. Under one serializable transaction, recheck exact current relevant
heads, database identity, guards, saved audit receipts and the current broker/subscription, then create
a linked paper restriction before resolving the original global row. Unknown/shared integrity failures
remain global. Preserve original discovery boundaries through the explicit resolution link, allowing
the already-running full proof to qualify the remaining paper restriction without restarting it.
Original failed runs, incidents and resolutions remain immutable. Paper/research progress cannot
invalidate a real proof; new real/shared evidence does require a fresh real proof before clearance.

Paper holds also suspend paper subscriptions, settings overlays, experimental boundary work, tracked
and Shadow work. Collection uses the independently verified real subscription without opening these
held paper paths. Real definition/source checks and freshness remain required. Clearing recovery is
an explicit operator control and does not renew permission, enable execution or place an order.
The installed verification mode remains the qualification boundary until results review.

Legacy compatibility discovered during native qualification: original broker approval hashes depend
on process-local dictionary ordering. Audit-only evidence may reconstruct Memory JSON representation
from immutable records, preserving every value and requiring the exact original SHA256. For a source
switch that cleared nested signal dictionaries, at most32later broker records within the fixed boundary
may supply property ordering; each row's checksum is verified and its transition must still pass later.
This bounded exception rereads a short broker interval, never paper history or external data. Current
approval/write validation has no historical override. Missing proof remains a failure. Paper settings
timestamps accept only the established exact legacy SQL datetime projection, not a time tolerance.
Failed audit receipts remain immutable; a corrected retry is a new explicit pass, not resumed success.

## Alternatives and consequences

Keeping a single global recovery proof retains the observed multi-hour coupling. Dropping fingerprints
would weaken evidence. Rewriting legacy history to a new hash format would need separately reviewed
migration authority and preserve old evidence; it is deferred. A disk cache would duplicate data and
need another trust/recovery mechanism; it is unnecessary for this increment.

Native throughput and memory must be measured. The source still traverses original state to verify
legacy fingerprints and financial transitions; no linear total-byte-work or complete ETA is claimed.
Broker revisions retain the existing full logical-state size bound. This fixes repeated reads/decodes
and recovery coupling, without claiming that every storage or operational acceptance item is complete.

## Review questions

1. Why can a real recovery proof finish while paper history remains unverified?
2. Why must an old full-state checksum still be checked after content is read and decoded once?
3. What evidence must be preserved when a global recovery restriction becomes paper-only?
