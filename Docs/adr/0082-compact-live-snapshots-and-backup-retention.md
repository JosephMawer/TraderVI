# ADR-0082: Compact Live snapshots and verified backup retention

**Date:** 2026-09-17  
**Status:** Accepted and installed with explicit operator authority. Migrations 037/038, historical
conversion, full preservation verification, backup retention and WPF restart acceptance completed.  
**Domains:** architecture, data-pipeline  
**Related:** ADR-0053, ADR-0067, ADR-0078

## Problem

The immediate problem is rapid database and full-backup growth. The parent problem is reliable
nightly preparation within available disk space; the root goal is dependable live operation with
reconstructible evidence. The initial inspection found 4,449 Live portfolio revisions occupying
about 64 GiB. Each protection quote copied its entry report, and every revision copied all earlier
quotes, fills, marks and reports again. This repeated history dominated the database.

## Decision

Keep every revision, quote, fill, action, mark, protection value, report and original attribution.
Normalize the six portfolio collections into immutable content and item-version tables. A content
hash is a SHA-256 identifier for the exact saved JSON; an item version records which content applied
at a particular portfolio revision and array position. Unchanged items and identical reports share
one saved value. Changed positions and actions receive new versions.

Each compact root preserves all scalar fields and candidate states, empties the six stored arrays,
and adds a versioned manifest with their counts and a fingerprint of the complete original snapshot.
Reconstruction selects only item versions at or before that root's revision, clips arrays to their
original counts, restores the exact report strings, and checks the full fingerprint. Object-property
ordering is ignored by the full fingerprint; array order and values are preserved. Missing, corrupted
or wrong-revision content fails closed. Business logic continues to receive the original domain type.
Packing hashes each distinct report once per snapshot; reconstruction validates and decodes each
distinct report once, then reuses its immutable string. This avoids repeated work for identical report
text while retaining the full-snapshot fingerprint check.

All readers and writers, including settings, comparisons and promotions, use this boundary.
Runtime writes refuse a legacy current root until historical conversion finishes for that portfolio;
this prevents new revisions from depending on a partially converted historical prefix. SQL
triggers reject old-format writes and changes to compact historical records/content. Older binaries
also reject the new manifest instead of interpreting empty stored arrays as empty holdings.

The user corrected the retention choice to **the latest two completed full backups in each location**.
Hermes prunes only after SQL checksum verification and a SHA-256-verified local OneDrive copy. Failed
staging backups retain a `.partial` name; final names identify completed SQL backups. Cleanup recognizes
only exact timestamped TraderDB full-backup names confirmed completed by SQL backup history, inspects both locations first, and never deletes the
just-verified backup or a newer potentially concurrent backup. It ignores unrelated/incomplete files,
rejects links/junctions and permits OneDrive cloud reparse tags. Cleanup failures are reported as warnings
after a successful backup. Local hash verification does not establish completed cloud synchronization.

## Installation and history conversion

Migration 037 adds two tables and four triggers in a transaction, requires a recent checksum backup
and repeats `RESTORE VERIFYONLY`, and refuses installation with an active Live lease. It neither
converts nor deletes history. Review and separately authorize installation plus the maintenance
conversion before stopping the app and changing production data.

Migration 038 preserves 037 unchanged and moves historical-update validation into an `INSTEAD OF
UPDATE` trigger. The same new-format, identity, provenance and old-format-only checks execute before
the physical large-value replacement. Insert/delete protection stays in the original trigger. Native
rollback measurement reduced a representative update from 3–4 seconds to about 0.1 seconds; checking
SQL Server 2019's deleted large value after replacement caused millions of repeated logical reads.
Multirow attempts containing an immutable row still fail as a whole. Both trigger definitions are
installed transactionally, with a fresh verified backup and the exclusive maintenance lock.

The maintenance converter holds an exclusive storage lock, processes original revision order, and
replaces only one exact row's JSON per transaction after reconstructing and fingerprint-checking it.
All keys, timestamps, lease fences, settings attribution and events stay intact. Already-converted
rows are verified on restart. Current roots are converted last and must equal their matching historical
revision. An interrupted run is resumable with updated readers; do not restore an old executable over
partly converted state. Database restore is a separately approved recovery action with its own data-loss
window. No table drop, history deletion, broker operation or automatic database-file shrink is included.

## Consequences and validation

Storage growth tracks new observations and changed state instead of repeatedly copying all history.
Reads still reconstruct the full in-memory portfolio, so this does not bound the memory cost of very
long-running accounts. Root candidate-state snapshots and other evidence tables can still grow.
Freed SQL pages become reusable; operating-system free space does not automatically increase by the
same amount. A one-time data-file shrink, if needed, requires separate review after measured conversion.

Offline tests cover round trips, report deduplication, old optional fields, fingerprint integrity,
old-reader rejection and retention safety. A read-only production preview checked 45 first/last daily
and current snapshots: about 410 MiB of JSON became about 4 MiB of root and unique-content payloads,
excluding SQL index/row overhead. This is a sample, not a full-database size or backup forecast.
SSDT build and migration parsing/canonical parity pass. Nine native SQL write/read samples verify
temporary-table lifetime, report sharing, hash collation and historical isolation, with every test
write rolled back. Eight update/delete/identity/provenance checks also leave original rows unchanged.
Full conversion and independent reconstruction of all 4,469 historical revisions and three current
portfolios passed. Original counts, provenance/timestamps and event hashes are unchanged; all 45
captured fingerprints match. The verified full backup fell from 16.27 GB to 535 MB, with two completed
files retained in each location. Updated WPF resumed at 12:54 Toronto and saved four new compact
revisions by 12:55. Today's coverage remains blocked with a recorded host gap; no gate was relaxed.

## Review questions

1. Why must a historical read choose item versions using the saved revision rather than current state?
2. Why does backup cleanup wait for both SQL and copy verification?
3. Why does compacting stored rows not immediately return the same space to Windows?
