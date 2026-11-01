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
broker-order identities prevent alternative rows for the same recorded action. The October 31 writer
change gives the compact store ownership of purchase allowance totals. Callers supply the account's
last saved totals (zero on first creation). For each changed buy the writer looks up the original
receipt and applies the difference between its old and new contributions. It commits the account,
receipt, caller-supplied position changes and checkpoint together. Sales contribute zero.

An open buy counts its original quantity × reference price against the ceiling. A terminal buy counts
its latest cumulative net cost. Within that charge, the applied portion counts as committed and the
balance as reserved. The receipt retains the full applied fill cost even if an open buy exceeds its
estimate. Reconciliation then charges the full terminal cost. For a $100 buy with $38 applied, the
account carries $38 committed and $62 reserved. Cancelling the balance clears the $62 reservation.
An identical cancellation arriving with a new check time preserves the completed receipt and totals.
The offline recovery component maps opening buys and protective sells from original broker
observations and applies cumulative position deltas. Terminal orders leave the work set only after their recorded fill totals
agree and reservations are cleared. Contradictory/regressing facts are rejected for explicit
reconciliation, not silently resubmitted. These are local invariants, not a new broker retry policy.

String sizes and numeric precision are explicit. JSON fields permit bounded scalar configuration
only; nested objects/arrays and duplicate keys are rejected by the repository. No cumulative orders,
snapshots or historical fills are serialized into current rows. Payload budgets measure returned
UTF-8 JSON separately from SQL allocation, indexes, transaction logs and broker responses.

## Validation and limits

The disconnected account rehearsal restores these inputs together from the compact book.

| Saved input | How the offline restart uses it | Production source to qualify before switching |
|---|---|---|
| Account binding, assignment, strategy, approval, phase and execution flag | A new simulated sale requires active execution and a current sell permission. Order-status reads continue when submission is disabled. | Effective broker approval, installed build and valid host grant under the existing execution host |
| Stop-buying flag, risk holds, purchase ceiling and reservations | Preserve each control and reconcile each original buy independently. The pass has no new-buy operation. | Current risk evidence and buying-mode writers |
| Position ID, security, remaining shares, protection owner and saved exit decision/reason/time | Dispatch the already triggered protective exit first. An existing sell reservation or overlapping original request prevents a second sale. | Strategy protection state, original trigger identity and local broker journal |
| Scoped restrictions and dated broker account observations | Read the account snapshot once after exit handling. Record cash and buying power, compare security quantities, and restrict a mismatch to that security. A newer agreement can clear only the comparison's own restriction. | Authenticated account binding, observation ordering, external trades and broker coverage |

Only the pass that successfully reserves a **new** protective request may dispatch it. The receipt and
share reservation commit together before the simulated send. Later passes look up that request.
An ambiguous save before dispatch may leave zero submissions and an unresolved reservation.
Its original ID stays visible for reconciliation. Rejected or partially cancelled exits retain
the trigger on the unsold shares and report the original outcome for review.

Position, receipt, allowance and existing symbol exit memory update together on a sell fill.
The rehearsal uses a simulated transport, synthetic opening terms and current-state reads after changes.
Production admission still requires native single-owner fencing, real grant/build validation, final
authorization through dispatch, broker timeouts, journal coordination and end-to-end measurements.

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
