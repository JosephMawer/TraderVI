# ADR-0103: Real execution independent of paper research

**Date:** October 21, 2026. **Status:** Accepted architectural direction only; detailed design is a draft, implementation has not been requested.
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

This accepts the dependency direction, not a particular process layout, storage format, changed
integrity contract or retention policy. [The design draft](../concepts/independent-real-processing-design.md)
proposes separate operational and research scheduling, incremental account state and shared immutable
evidence. Those mechanisms require review before implementation.

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
- Current ADR-0082/0098 full-verification behavior remains authoritative until a replacement integrity
  contract is agreed and validated. A current-state checkpoint does not by itself justify skipping
  existing historical verification.

ADRs 0083/0085 remain compatible with this direction. No existing storage or runtime contract is
superseded by this direction-only ADR. There is no approval to implement, migrate/delete data, prune
artifacts, install a scheduler, start a host, renew trading permission or place an order.

## Review questions

1. Which may be shared: market facts and strategy code, or portfolio balances and confirmation state?
2. Why is moving paper research into another process insufficient while broad SQL locks remain?
3. Which storage and verification choices are still proposals rather than accepted decisions?
