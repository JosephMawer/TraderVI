# ADR-0103: Real execution independent of paper research

**Date:** October 21, 2026. **Status:** Accepted architecture; implementation explicitly requested, including the independent engine followed by history/storage changes tonight. Implementation underway; no native cutover or migration performed.
**Domains:** architecture, data-pipeline.
**Related:** ADR-0053, ADR-0062, ADR-0082, ADR-0083, ADR-0085, ADR-0086, ADR-0098, ADR-0099.

## Problem and decision

The immediate problem is real execution waiting for multi-minute paper work. The parent problem is
shared hosting, whole-cycle settings coordination and repeated history processing. The root goal is
timely, reliable real trading with responsive supervision and unchanged financial controls.

The operator explicitly accepts: **real trading should be independent of paper research**, and asks
to see the revised architecture before implementation. Real execution must not require completion
of optional paper/research work. Shared market facts and strategy code may be reused; paper account
progress, balances, fills and confirmation state cannot become real execution authority.

The initial decision accepted the dependency direction. The operator subsequently accepts the proposed
separation of immediate trading checks from full historical audits: verify current state, new events
and exact decision inputs immediately, with complete historical checking performed separately. This
acknowledges later detection of corruption in unrelated older records, without permitting known bad
evidence to authorize trading. [The design draft](../concepts/independent-real-processing-design.md)
proposes the mechanics. The operator subsequently adopts the audit policy below. Checkpoint trust,
storage mechanics, exact cutover and retention still need a complete design; this is not
yet an implementation-ready replacement integrity contract.

## Accepted historical audit policy

- Check new history after each trading session, including any unverified backlog. Target complete
  older-archive coverage weekly in bounded, resumable batches outside market hours; qualify capacity.
  Verify affected state after abnormal recovery, restore or migration before relying on it.
- Report actual coverage, last success and overdue/incomplete work. Interrupted audits retain
  incomplete status; they cannot count as a clean full pass or erase earlier failures.
- Isolated research failures invalidate dependent results and notify the operator. Real trading can
  continue only when its state and dependencies are verified unaffected.
- Trading-relevant or uncertain failures stop new exposure for affected accounts. Continue protection
  only with independently trustworthy required account, order, state, authorization and price evidence.
  Otherwise prominently report that automatic protection is not assured and require reconciliation.
- Preserve evidence and durable holds across restarts. Resolve the cause, verify recovery and explicitly
  clear the hold before dependent trading resumes. No automatic financial reset, restore or liquidation.

This accepts policy, not installation of audit jobs. Exact scheduling/resource budgets, trusted
checkpoint construction and durable incident/release mechanics are engineering work under this policy.
The [draft](../concepts/independent-real-processing-design.md) separates remaining operator choices
from implementation details; do not require the operator to choose hashing, transaction or batching algorithms.

## Accepted lifecycle and delivery direction

Closing the dashboard leaves the separate engine running within its existing authority. Provide an
explicit engine-stop control, distinct from stopping new buys. Reopening the dashboard reconnects to
the same owner; it must not create another broker session/order writer. A deliberate engine stop must
report completion or unresolved work accurately; it does not liquidate holdings or cancel broker orders
implicitly. Existing permission expiry, reconciliation and protection requirements remain.

The operator accepts staged delivery on the condition that it is done tonight. Treat tonight as the
requested target, not a demonstrated completion estimate. Use compact implementation/verification
checkpoints within the same effort; do not invent a multi-day trial requirement. The full storage
conversion, migration and native acceptance have not been estimated or qualified. A deadline cannot
turn failed checks into a successful cutover or claim market-hours evidence obtained overnight.

The operator subsequently requests implementation of the revised architecture tonight. The first
increment extracts UI-owned broker services and process-owned scheduling; independent host/client,
direct inputs and incremental storage follow. This authority covers source changes and offline tests,
not an installed native switch, database migration or financial activation.

## Boundaries and consequences

### Prepared append contract

The first transaction change preserves full historical verification, but moves it before the write
transaction. The next snapshot is prepared from that verified immutable revision. Under the live
lease, the writer locks and compares the exact original current-root bytes, rejects concurrent
advancement, and writes/read-verifies only new content and changed item versions. It then publishes
the new root, observation projection and events atomically and rechecks the lease before commit.
An ambiguous committed retry still returns only an exactly matching verified snapshot. This removes
large history preparation from the shared lease lock; it does not yet remove repeated pre-transaction
history reads or qualify native latency. Existing version-2 archive representation is preserved.

### Durable consumer handoff

The producer publishes one immutable checkpoint only after its exact collection cycle and all referenced
inputs are saved. Context is content-addressed; consumer receipts record original cycle identities.
Paper consumes the newest pending checkpoint; late checkpoints cannot create retrospective buys.
Admission and the final paper-fill boundary both recheck the original seven-minute signal limit.
Research consumes checkpoints in original sequence using their frozen context. Failure or backlog
in either consumer cannot await the producer or real-host schedule. Paper protection is separately
scheduled and serialized only with paper financial actions; historical research has a separate gate.
The producer retains its lease outside market hours until engine stop so admitted consumers can finish.

Research recovery runs off-hours. An experiment boundary applies before the session freeze only when
old cohorts have been settled for that date. If recovery is unfinished or busy, current assignments
remain authoritative and the change stays pending for a prepared session; a later research completion
cannot relabel an already frozen session. This is conservative deferral of an unready change, not
permission to extend a trading signal or bypass a promotion check. Native contention, reconnect,
consumer crash recovery and capacity still require qualification before cutover.

### Shared broker revision records

Independent-host broker revisions now support a versioned content-addressed manifest in the existing
immutable revision table. Original nested financial/approval/order values are recovered from exact
content hashes, with a canonical complete-state fingerprint and unchanged row identity checks.
Repeated nested records are stored once; an append transmits only fragments absent from the verified
current revision. Fragments and the manifest commit atomically. Legacy inline revisions remain readable;
ordinary appends never rewrite historical rows, original order identifiers or authorizations. The
existing eight-MiB expanded-state ceiling remains; depth/reference/expansion limits reject damaged
graphs. This reduces repeated storage, not the size of the logical financial history.

The source reader and continuation path understand both formats. SQL diagnostics that inspect nested
JSON directly must disclose the shared format rather than treating missing inline fields as absent
orders/decisions; qualify the diagnostic reader before cutover. No existing data conversion or deletion
is performed by this increment. Existing default writers remain inline until opting into shared storage
or continuing an already shared revision.

### Prepared historical verification and incident controls

Source now contains a separate off-hours audit lane. A saved plan fixes each paper/broker revision
boundary and each shared-input/content sequence boundary; one original record is verified per wake.
The first nightly run includes the backlog; subsequent runs resume after the last completed nightly
boundary. Weekly older-archive runs also inspect the frozen legacy Live inputs. These are specifically
Live paper, managed-broker and shared-input archives, not a claim to audit every TraderDB table.
Paper financial transitions, authorized settings/promotions, the original September engineering-trial
exception, broker append authority and exact referenced content are checked. Historical strategy
decisions are not silently recomputed with today's implementation. Complete linked content is reopened
by audits even when an operational cache is later introduced.

Interrupted/failed progress stays incomplete. Verification defects create immutable findings;
broker defects have a binding scope, while shared/paper defects remain conservatively global until
isolation can be proven. They block new exposure and, where prerequisite state is untrusted, automatic
protection. Manual purchase preflight and automatic sell preflight retain serializable hold reads
through transmission; an already admitted transmission precedes a concurrently discovered hold.
An explicit recovery run itself adds a global hold. Clearing selected original incidents requires a
completed recovery run, unchanged history heads, enabled immutable guards and an operator reason.
It appends a resolution; it never erases the finding or renews trading permission. Newer incidents
cannot be cleared with an older proof. The dashboard exposes coverage and these explicit controls.

Prepared migration050 adds these records and retires legacy Live evaluation inserts while freezing
that older archive against update/delete. It is a cutover-only migration requiring old writers stopped
and a fresh verified checksum backup; it has not been applied. Existing old-version rollback would
need a separately reviewed writer transition. Weekly capacity, native SQL/repair/restart behavior,
complete dependent readers and current-state checkpoint integration remain unqualified; the real
host's readiness block remains. This checkpoint does not relax installed ADR-0082/0098 verification.

### Retained boundaries

- Retain real-account ownership, original order recovery, held-position protection, financial history,
  source freshness, maturity/confirmation, risk controls and explicit dated approval/host enable.
- Shared collection must remain available without a running paper research cycle. It cannot be
  stopped merely because optional research is paused. Protection of paper-held positions also remains
  operational work rather than an optional historical report.
- Resolve broad settings/write coordination rather than merely deleting the desktop barrier. One
  owner must retain order authority; independent consumers need bounded resource use and coherent
  committed inputs. Separate processes alone cannot prevent contention in a shared database.
- The proposed replacement for paper-ledger freshness must directly establish exact strategy/input
  identity and operational progress. It needs an explicit reviewed contract, not a bypass of stale-input
  guards. No freshness duration changes are accepted.
- The accepted verification separation is a target change to ADR-0082/0098's timing. Their installed
  full-verification behavior remains until a complete replacement contract is agreed and validated.
  A current-state checkpoint alone does not establish trust or permit ignoring a known integrity failure.

ADRs 0083/0085 remain compatible with this direction. No existing storage or runtime contract is
superseded in the running system by this design ADR. There is no approval to migrate/delete data, prune
artifacts, install a scheduler, start a host, renew trading permission or place an order.

## Review questions

Source-only current-state detail: the [implementation contract](../concepts/independent-real-processing-design.md#current-state-implementation-contract-in-progress)
uses version-3 collection roots and stable archive ordinals, with a separate authenticated working
checkpoint. It retains latest-exit/original-entry context and archived cash/profit totals without changing
starting capital. The pure kernel is tested; SQL integration and native qualification remain required.
Legacy snapshots stay readable and immutable. No installed verification contract changes yet.

1. Which may be shared: market facts and strategy code, or portfolio balances and confirmation state?
2. Why is moving paper research into another process insufficient while broad SQL locks remain?
3. Which storage and verification choices are still proposals rather than accepted decisions?
