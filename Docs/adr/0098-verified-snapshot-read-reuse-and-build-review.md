# ADR-0098: Verified snapshot read reuse and dated build review

**Status:** Accepted October 21, 2026, within the operator's request to fix the slowdown now.
**Domains:** architecture, risk-management

The immediate problem is repeated reconstruction of large identical paper snapshots. The parent
problem is paper cycles exceeding collection deadlines and restarting signal continuity. The root
goal is reliable execution of the deliberately approved strategy under its existing risk controls.

## Decision

Keep ADR-0082's stored format, full fingerprints, immutable content and revision selection unchanged.
After every fresh SQL read of the exact revision's root, item membership and content strings, permit
reuse of an immutable typed snapshot only when all those bytes match a previously fully verified
load. A changed root, ordinal, content key/value or missing part takes the original full validation
path. Copy membership into immutable collections. Bound retention to four entries and a conservative
256 MiB retained-size allowance, at most one revision per portfolio; oversized snapshots bypass
reuse. Writes and historical JSON reconstruction continue full verification. Canonical string encoding may be reused within one full
fingerprint operation; every canonical byte still passes through the original SHA256 algorithm.

A Core change changes the real execution build identity. Add an append-only updated-build review
only for the existing October 21 cash-only, source-switched continuation before its original 16:00
Toronto close. Require the host off, fresh exact source/storage/account checks, no orders/positions/
protection/holds, and the same complete definition except build identity. Preserve original
approval, continuation, source switch, financial history, risk references, spending ceiling, operator
modes and signal memory. Recheck freshness/build/host after I/O; exact lost-acknowledgement recovery
creates no second approval. The durable repository rejects hidden changes and deterministic-ID
tampering. Older JSON omits the absent optional record so historical hashes stay stable.

Approval records no order or host grant. Separate operator enable must match the new exact strategy
and build and occur after its review. No next-day authority, qualification replay or new baseline is
granted. Other account states require separate recovery; this workflow fails closed if a trade
appears before installation/review.

## Alternatives and limits

Caching by revision/hash alone would conceal changed or missing stored content; rejected. Skipping
SQL, financial reconciliation, collection deadlines or continuity resets would change safety; rejected.
Broad timeout/retry changes do not establish that this expensive work is removed and remain separate.
The earlier SQL-save retry draft stays parked outside compiled source.

Offline speed and corruption tests establish the implementation boundary, not live capacity. Normal
operator closure precedes replacement of loaded Release files. Fresh operator build approval/enable
and sustained native timing/continuity evidence remain required before claiming operational recovery.

## Review questions

The October21-only build-review restriction is superseded by
[ADR-0107](0107-build-review-within-existing-dated-permission.md) for an existing explicitly approved
dated continuation. Financial scope, expiry and separate operator approval/enable remain unchanged.

1. Why must a cache hit compare fresh content strings as well as revision and hashes?
2. Which financial and signal facts may an updated-build approval change?
3. Why does a passing benchmark not prove collection deadlines or live buying readiness?
