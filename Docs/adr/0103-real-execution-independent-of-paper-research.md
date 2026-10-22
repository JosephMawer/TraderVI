# ADR-0103: Real execution independent of paper research

**Date:** October 21, 2026. **Status:** Independence and immediate/historical verification separation accepted; detailed mechanisms remain draft, implementation has not been requested.
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
proposes the mechanics. Audit frequency, failure/recovery behavior, checkpoint trust, process layout,
storage format and retention remain draft; this is not a complete replacement integrity contract.

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
superseded in the running system by this design ADR. There is no approval to implement, migrate/delete data, prune
artifacts, install a scheduler, start a host, renew trading permission or place an order.

## Review questions

1. Which may be shared: market facts and strategy code, or portfolio balances and confirmation state?
2. Why is moving paper research into another process insufficient while broad SQL locks remain?
3. Which storage and verification choices are still proposals rather than accepted decisions?
