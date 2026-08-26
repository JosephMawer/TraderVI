# ADR-0030: Intraday evidence and poll-audit ledger

- **Status:** Accepted
- **Date:** 2026-08-26
- **Domains:** architecture, data-pipeline, data-sources, market-microstructure, risk-management
- **Related:** ADR-0018, ADR-0020, ADR-0021, ADR-0028, ADR-0029

## Context

The immediate problem is that ADR-0029's pilot monitor can replay currently available TMX history but cannot prove what evidence TraderVI received at each decision time. The parent problem is producing calibration-grade delayed entry and exit outcomes without look-ahead, silent data repair, or optimistic fills. The root goal is to tune Delphi from honest paper evidence under human strategic control.

ADR-0028 accepts completed five-minute bars as the fine-grained version-1 storage resolution and direct completed fifteen-minute bars as the operational policy input. The market-hours probe showed that forming bars change, one-minute sequences can contain gaps, and even five-minute components can be absent for thinly traded symbols. A durable design must preserve both actual request/receipt history and the completed bars used by the policy without pretending a missing component is a flat interval.

## Decision

Create two additive, immutable evidence tables through one reviewed manual migration:

1. `IntradayPollObservation` records one source request for one symbol and interval. A shared `PollCycleId` groups the requests made by one scheduled monitor cycle. It preserves purpose, policy/collector/source-contract versions, code provenance, requested bounds, fetch and receipt times, transport counts, returned/completed counts, newest event times, and an explicit audit state/code.
2. `IntradayEvidenceBar` records a completed OHLCV bar and links it to the first observation in which TraderVI received it. Its natural identity is symbol, interval, and market-event time.

Persist completed five-minute bars as fine-grained evidence and direct completed fifteen-minute bars as the exact policy inputs. Never persist a forming bar as completed evidence. Never synthesize a missing five-minute interval. When all three exact five-minute components exist, their aggregate is a consistency check against the corresponding direct fifteen-minute bar; it does not replace a missing source component.

Keep source evidence independent from `ActivePosition`, `DailyPick`, and calibration outcome rows. A market bar is a fact about a symbol and time that can support several positions or later evaluators. Position decisions and achievable post-detection fills will reference this ledger in a separately versioned outcome/decision change.

Treat a repeated natural-key bar with identical OHLCV as idempotent. Treat different OHLCV for an already completed natural key as conflicting evidence: retain the first evidence unchanged, mark the later poll observation invalid with a bounded audit code, and surface the conflict. Never silently update or choose the more favourable version.

### Confirmed direction

- The source is delayed and event time remains distinct from receipt/detection time.
- Version 1 stores completed five-minute evidence and the direct completed fifteen-minute policy input.
- The monitor continues on a fifteen-minute cadence and remains advisory-only.
- Database rollout uses a manually reviewed migration after a fresh verified backup; DACPAC deployment remains blocked.

### Accepted implementation defaults

- Use `DATETIME2` UTC fields with separate event, fetch-start, receipt, and insertion times.
- Use `DECIMAL(19,6)` for OHLC values and `BIGINT` for volume so persistence does not introduce binary floating-point drift.
- Allow only five- and fifteen-minute intervals in version 1.
- Use `Valid`, `Degraded`, `Invalid`, and `Failed` poll audit states. Persist bounded error codes rather than exception text, credentials, or connection details.
- Preserve source rows indefinitely in version 1. Retention or compaction requires evidence about volume and a later decision; no cleanup job is authorized.
- Use schema version 1 and explicit provider/source-contract/collector/policy/code fields so later request or policy changes do not reinterpret old evidence.

## Alternatives considered

- **Store bars without poll observations.** Rejected because a bar timestamp alone cannot prove when TraderVI received it or reveal missed/failed polling.
- **Store every forming snapshot.** Deferred because version 1 needs reproducible completed evidence; mutable snapshot research is a separate, much larger dataset.
- **Store only five-minute bars.** Rejected because the operational policy consumes direct fifteen-minute bars, and thin trading can leave an incomplete five-minute triplet.
- **Store only fifteen-minute bars.** Rejected because it discards the accepted fine-grained path needed for later timing and fill analysis.
- **Attach every bar directly to a position.** Rejected because it duplicates shared market facts and complicates multi-position or historical evaluation.
- **Upsert provider revisions.** Rejected because overwriting the evidence first seen would destroy the delayed decision's audit trail.

## Consequences

- A later evaluator can reconstruct the first evidence actually available to TraderVI without relying on today's TMX response.
- Failed and degraded polls remain measurable rather than disappearing from coverage.
- The collector must fetch both five-minute storage evidence and direct fifteen-minute policy evidence, validate them, and persist each request transactionally.
- Bar conflicts require an explicit invalid audit path and human-visible diagnostic.
- Storage will grow until measured retention is designed; version 1 deliberately favours auditability over early cleanup.
- No database object is active until the migration is separately reviewed, backed up, authorized, applied, and verified.

## Review questions

1. Why must a poll observation be stored even when it returns no new completed bar?
2. Why are direct fifteen-minute bars retained when five-minute bars are the accepted fine-grained resolution?
3. What happens if TMX later returns different OHLCV for an already stored completed bar?
