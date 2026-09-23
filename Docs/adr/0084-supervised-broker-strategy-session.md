# ADR-0084: Durable supervised broker strategy session

**Date:** September 22, 2026 Toronto (September 23 UTC)
**Status:** First-session source and offline tests complete; migrations 035/039/040 installed and
storage verified September 22 at 23:34 Toronto under separate authority. Production evidence remains
incomplete; no operational baseline, host rollout or execution approval.
**Scope clarification:** [ADR-0085](0085-portfolio-wide-ongoing-tfsa-management.md) accepts any portfolio
and ongoing management. The first-session expiry and family limits below remain enforced in source
until that broader workflow is implemented and verified.
**Domains:** architecture, risk-management, market-microstructure
**Related:** ADR-0062, ADR-0066, ADR-0067, ADR-0079–0083

The immediate problem is joining the existing broker account, risk and order components into one
recoverable workflow. This belongs to the real-account handover; the root goal is one deliberately
approved complete strategy managing the permanent TFSA, independently of its paper history.

The operator selected **ungated Breakout, three positions** as the review target. Preserve the
comparison account's `PortfolioRulesOnlyV1` admission profile, pinned daily/model identity and Shadow
policy. This bypasses only the comparison's daily eligibility filter. Published ranks 1–3, prospective
source adoption, intraday confirmation, position sizing, losses, protection and session rules remain.
ADR-0062's recommendation-only exception remains unchanged and cannot qualify execution.

## Execution and recovery contract

`BrokerManagedSessionWorkflow` uses the existing cumulative order importer, permanent binding,
baseline/continuation workflows, recommendation engine and manual buy/sell clients. Its SQL account
lease serializes ticks across hosts; the existing encrypted order journal and semaphore also exclude
manual/native submissions. Migration 040 adds immutable session revisions after 035 and 039.
Session append transactions finish before broker calls. The existing manual-buy safety read fence
remains held through preflight/transmission; it also excludes risk-continuation writers during the
final reference recheck. The account session lease remains held throughout the cycle.

An explicit review identifies the exact strategy, build, account facts, actual position cycles and
quantities, required exits, retained positions/protections, session date, estimated purchase ceiling
and scoped qualification evidence. Approval persists transition authority and submits nothing.
The supervised caller must separately invoke a cycle. No background host or scheduler is enabled.

Every order intent commits before transmission and retains its original external ID. Only one order
may remain unresolved. Timeout, absent broker history, an uncertain database acknowledgement or a
restart never cause retransmission. Recovery reads the same ID through the existing manual workflow.
A terminal partially filled cancellation may produce a distinct residual exit only after the original
order and remaining holdings reconcile. Required exits must all finish before activation or any buy.
Outside fills/corrections invalidate the reviewed transition; they cannot expand a liquidation.

The broker projection uses actual cost and permanent position-cycle identity. Partial sales preserve
identity; a close/reopen starts a new cycle. Net cost and fees remain financial facts. Cumulative order
endpoints are not fabricated individual fills; an entry spanning sessions needs individual timing
evidence for age rules. Cash for valuation remains distinct from the lower of cash/buying power used
for purchases. Completed sells provide no funds until the broker reports usable reconciled cash.

Current protection stays with its operator/advisory owner during transition. Handover carries known
levels once, retains real costs/age and initializes missing trailing state without fictional highs.
Completed protective bars are replayed after the causal boundary; a breach followed by a rebound
cannot disappear on restart. Carried price floors remain independent obligations. New strategy fills
receive their own protection boundary. Existing thresholds, re-entry limits and market-order policy
are unchanged. Market orders have no guaranteed price: the reviewed purchase ceiling constrains
estimated commitments, not the broker's final execution price.

Synchronization replaces cached daily/high references with the authoritative continued references,
including accepted percentage-preserving cash adjustments. It preserves daily and capital holds.
Unexplained cash restricts new risk; it does not fabricate a daily opening or release protection.
Pause still allows read/reconciliation recovery. The supervised authority expires at the session
boundary; a new session or strategy replacement requires a separately implemented reviewed renewal.

## Production boundary

Qualification records are evidence references, not operator tick-box overrides. This increment issues
no production qualification. `UnqualifiedBrokerRiskEvidenceSource` explicitly refuses to manufacture
complete effective cash events or matched boundary books from activity occurrence times/net totals.
The production orchestration can apply qualified continuation requests, but the native evidence
producer is unfinished. Strategy evidence, native lifecycle and SQL crash/lease qualification remain.

The source contains an account setup screen, production composition factory and read-only verifier.
No operational session host is installed. This leaves no UI control that can turn incomplete evidence into
execution. A prepared implementation and successful read-only login are not a launch approval.
See the [activation review](../reviews/supervised-session-20260923.md) for the exact remaining checks.

## Alternatives and consequences

Reusing paper cash/positions, resetting references at launch, interpreting net deposit totals as a
complete ledger, or retrying unknown submissions would violate accepted contracts. A separate order
stack would duplicate broker safety and recovery. Immutable revisions reuse those boundaries and
make intent/recovery demonstrable, at the cost of growing snapshots and explicit unsupported cases.
Bounded SQL history reads and a compact managed-session representation are follow-up scaling work.

## Review questions

1. What distinguishes an acknowledgement from a reconciled terminal exit?
2. Why must a cash withdrawal replace the engine's cached reference as well as the database reference?
3. Which evidence is still missing when holdings and the day graph both read successfully?
