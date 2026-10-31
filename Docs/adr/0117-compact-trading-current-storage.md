# ADR-0117: Compact trading current-state storage

- **Status:** Accepted for the first disconnected implementation increment; not installed or activated
- **Date:** October 30, 2026
- **Domains:** architecture, data-pipeline
- **Related:** ADR-0103, ADR-0109; [restart design](../reviews/restart-recovery-design-20261030.md), [#19](https://github.com/JosephMawer/TraderVI/issues/19)

## Context

Repeated cumulative snapshots make storage and recovery depend on account age. The immediate task is
a compact persistence boundary; its parent task is ordinary restart without lifetime reconstruction.
The root goal is prompt resumed operation with current instructions and broker facts, preserving
original order identities and protection. Joseph accepted the compact proposal and authorized this
first coding step, with test results for review before further implementation. Live database changes
and engine restarts are explicitly excluded.

## Decision

Implement seven isolated tables and matching scalar records:

| Table | Purpose and ordinary read |
|---|---|
| TradingAccountCurrent | One current account row, current approval reference, buy/risk controls and running financial totals |
| TradingApprovedSettings | Immutable approved definition; resolve only the account's referenced row |
| TradingPositionCurrent | Current quantity, cost and protection for each position cycle; read open positions |
| TradingSymbolCurrent | Current bounded confirmation/re-entry progress; read relevant symbols |
| TradingOrder | Original request/decision identities and cumulative observed/applied fill totals; read only orders needing work |
| TradingRestrictionCurrent | Specific unresolved cause, affected scope/action and retry information; read active causes |
| TradingCheckpoint | Two reusable commit-marker slots per account; first save initializes one, second initializes the other |

All ordinary queries are account-scoped. Filtered indexes isolate active rows. Completed order rows
remain compact receipts, excluded from the ordinary read. They are not automatically deleted.
New approvals, new position cycles, receipts and inactive records can accumulate until separately
reviewed SQL maintenance under [#26](https://github.com/JosephMawer/TraderVI/issues/26), coordinated
with #16. This increment bounds repeated unchanged-book updates, not total lifetime database size.

The repository accepts an explicit open SQL connection factory. No existing engine registration,
broker calls, default production connection or startup migration are added. The account and changed
rows are saved with the next commit marker in one SQL transaction. An account application lock
coordinates readers/writers using this repository, and an expected version rejects stale updates.
Unchanged rows are skipped; a complete no-op makes no marker or historical row. Read batches return
one coherent current account boundary. Missing or inconsistent account/marker state rejects reads
and writes; the store neither fabricates repair nor clears a live recovery hold.

A marker contains version, operation identity/hash, operation kind and UTC time, not a snapshot.
The previous marker identifies the previous committed save; it cannot reconstruct that old account.
An identical retry still represented by either marker returns its saved version without rewriting.
After both markers rotate, the original expected version rejects stale replay. This is a SQL-save
contract, not proof of broker submission idempotency or recovery from an old backup.

Approved settings and original order intent cannot be rewritten. Unique account/request, decision and
broker-order identities prevent alternative rows for the same recorded action. Cumulative fill
observations can be converted to unapplied deltas; their account/position/order effects must be
submitted together by the future integration layer. This storage layer does not derive or certify
those financial effects. Terminal orders leave the work set only after their recorded fill totals
agree and reservations are cleared. Contradictory/regressing facts are rejected for explicit
reconciliation, not silently resubmitted. These are local invariants, not a new broker retry policy.

String sizes and numeric precision are explicit. JSON fields permit bounded scalar configuration
only; nested objects/arrays and duplicate keys are rejected by the repository. No cumulative orders,
snapshots or historical fills are serialized into current rows. Payload budgets measure returned
UTF-8 JSON separately from SQL allocation, indexes, transaction logs and broker responses.

## Validation and limits

Offline tests cover interrupted transactions, lost commit replies, stale writers, original identities,
repeated partial fills, current settings/protection/stop values, active-set boundaries and schema/migration
parity. The SQL project compiles with Visual Studio Build Tools/SSDT; the manual migration054 is
prepared only, with original binding prerequisite, no pre-existing compact objects and fresh verified
backup preconditions. Builds and tests do not execute it.

Native SQL locking, rollback/crash behavior, Dapper round trips, query plans, permissions, allocated/log
growth and p95/p99 timings remain unverified. They require a separately authorized isolated rehearsal.
There is no whole-solution health, production restart latency or production database reduction claim.
The implementation checklist owns dated test counts and measurements.

Before activation, map every existing reader/writer and derived policy value to this contract; prove
that original permission and provenance references do not pull legacy graphs into current reads.
Then review local-journal coordination, broker lookup coverage, action-specific eligibility and the
controlled swap as separate increments. Expanded damaged-state/old-backup recovery remains deferred
to #25. This ADR does not supersede installed ADR-0103/0109 recovery or execution contracts: a later
reviewed integration decision must identify the exact superseded boundaries and installation evidence.

## Alternatives and consequences

Retrofitting the cumulative session blobs would retain history-dependent reads and obscure field
ownership. Reusing two full snapshots would still copy the active book on every change. Separate scalar
rows and two markers make growth and changed-row costs measurable, at the cost of an explicit future
mapping and native SQL qualification. Keeping every checkpoint or applying cleanup in the engine was
rejected; SQL job design and retention remain separate reviewed work.
