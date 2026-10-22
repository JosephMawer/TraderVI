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

1. Which may be shared: market facts and strategy code, or portfolio balances and confirmation state?
2. Why is moving paper research into another process insufficient while broad SQL locks remain?
3. Which storage and verification choices are still proposals rather than accepted decisions?
