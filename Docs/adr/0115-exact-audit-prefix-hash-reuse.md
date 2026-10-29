# ADR-0115: Exact audit prefix hash reuse

- **Status:** Accepted implementation under the requested recovery-performance repair; not installed
- **Date:** 2026-10-28
- **Domains:** architecture, data-pipeline
- **Amends:** [ADR-0104](0104-scoped-recovery-and-sequential-history-audits.md), specifically repeated hashing of unchanged version-2 prefixes

## Problem and decision

The immediate problem is a multi-hour Paper recovery over unchanged legacy archive data. The parent
problem is repeated processing of accumulated history despite content sharing and compact current
state. The root goal is prompt verified recovery without losing original evidence or blocking Real
work on unrelated Paper history. Joseph requests finishing recovery, proving actual new-format Paper
operation and fixing performance, then asks to develop the fix before waiting for the running pass.

Retain every original version-2 SHA-256 fingerprint. Inside one fresh guarded audit reader, retain
bounded clones of the platform `IncrementalHash` state for exact canonical byte prefixes of the last
fingerprint-verified root. Compare each next root's header bytes exactly and its historical items by
identity within the reader's private immutable, hash-verified decoded-content cache. Reuse only the
identical leading sequence before the first changed token; a later matching suffix grants no reuse.
Clone the closest preceding digest state, hash any remaining prefix tokens and the entire changed
remainder, then require equality with that revision's original complete fingerprint.

This supersedes the requirement to feed unchanged leading bytes through SHA-256 again on every
revision. It does not supersede any original fingerprint, content hash, membership, ordering, identity,
transition, current-state, guard or clearance check. It stores computation state, not a successful
recovery decision. Provisional clones are retained only after the current fingerprint passes. Failed
checks dispose provisional clones and preserve the prior verified cache. Fresh passes, owners and
resumed readers start fresh; no cache survives a restart or skips the existing preceding-state
rehydration. Original SQL history, plan, cursor and receipt schemas are unchanged.

## Bounds and validation

Keep at most 65,536 token references, 1 MiB of raw header/punctuation bytes and 128 native digest
checkpoints at 512-token intervals, with a final checkpoint if space remains. Old and provisional
sets coexist during one check, so transient use may include two bounded sets. The existing decoded
content and escaped-string allowances remain. Exceeding prefix-cache capacity falls back to hashing
the remainder normally, not accepting unverified bytes. Dispose native digest states when changing
readers/passes. This uses the installed .NET 10 `IncrementalHash.Clone` implementation, not custom
cryptography or a replacement hash.

Validation must cover original canonical fingerprints including escaping/numeric representation,
appends, earlier-field changes, reordered/shrunk history, rejected modified content, failed-read
isolation, fresh readers and disabled/tiny cache budgets. Compare identical synthetic adjacent-revision
workloads before/after. Synthetic speedups do not prove whole-archive timing: early changes reduce
reuse, cold reads still hash the entire state, and legacy research-input verification remains separate.
Installed continuation, actual throughput and post-clearance version-3 activity require native evidence.

The running recovery continues during source/offline work. A normal drained stop and reviewed binary
replacement can preserve its durable cursor, but replacing Core also changes the Real build identity;
no financial approval is implied. Do not stop or install an unvalidated candidate, rewrite/delete old
records, start a duplicate pass or silently renew the Real grant.

## Review questions

1. Why can an exact cloned SHA-256 prefix still produce the original full-state fingerprint?
2. Why does a changed earlier field invalidate reuse of a later unchanged quote history?
3. What evidence distinguishes a passing offline benchmark from completed operational recovery?
