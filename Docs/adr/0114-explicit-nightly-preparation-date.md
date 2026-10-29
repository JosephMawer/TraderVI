# ADR-0114: Explicit nightly preparation date

- **Status:** Accepted
- **Date:** 2026-10-28
- **Domains:** architecture, data-pipeline
- **Supersedes:** the wall-clock-only date and duplicate key in [ADR-0046](0046-guarded-nightly-operations-runner.md) and [ADR-0058](0058-daily-ingestion-and-observed-benchmark-confirmation.md)
- **Related:** [ADR-0113](0113-nightly-data-audit-report.md)

## Decision and rationale

The immediate problem is running the upcoming nightly pipeline early to verify its new audit. The
parent problem is preserving the next day's preparation when skipping midnight. The root goal is
dependable daily advice from completed observations. Joseph explicitly accepted early-run support,
then an October 29 preparation run on October 28 evening and skipping the upcoming 00:30 occurrence.

The runner accepts `-PreparationDate yyyy-MM-dd`, defaulting to the actual Toronto date. Only today
or tomorrow is allowed; tomorrow requires at least 16:00 Toronto. Hermes and Delphi independently
validate the reviewed calendar's immediately preceding TSX session has closed before any provider or
database operation. Missing coverage fails closed. This clock check does not prove provider data
availability: existing benchmark, freshness and per-stock checks still apply unchanged.

Pass the resolved date to stages through `TRADERVI_PREPARATION_DATE`. Hermes uses it for completed
daily collection cutoffs and calculation windows; Delphi uses it for its existing recommendation date
and persistence keys. For example, October 28 evening preparing October 29 collects through October 28
and requires October 28 XIU/SPY. The normal October 29 00:30 run has the same date contract.

Real start/completion/receipt timestamps and undated live-source attribution retain the actual clock.
In particular, leadership movers may only attach to the actual market date under their existing
dated-anchor policy. No system clock change, future observation timestamp, relaxed freshness rule,
strategy change, training, schema operation or broker authority is introduced. Athena and DataAudit
retain their existing data-driven behavior; the complete report belongs to the enclosing preparation.

Status records `preparationDate` separately from actual `localRunDate`. Completed Succeeded/Attention
runs suppress another run for that preparation date; older status falls back to its local run date.
Failed runs never count as complete. Explicit `-Force`, exclusive ownership, immutable source capture
and no automatic operational retries retain their meanings. An early failure requires review.

For the authorized one-time schedule change, preserve the existing task and advance only its trigger
start boundary past October 29 00:30, verifying October 30 00:30 is next. Later daily recurrence remains
enabled. Keep a private task backup and receipts. This is separate from the duplicate guard and does
not install a permanently earlier schedule.

## Review questions

1. How is a preparation date different from an observation or publication timestamp?
2. Why must both completed-session validation and existing data-quality checks remain?
3. What prevents the next midnight invocation from publishing the early preparation twice?
