# Architecture Decision Records (ADRs)

This folder records meaningful design decisions made on TraderVI. Each ADR
captures *why* a choice was made, what alternatives were rejected, and what
review questions can be used to test understanding later.

We use **lightweight ADRs**: any meaningful decision gets one, even
indicator-level choices. The goal is dense review material, not formal
architecture documentation.

## Conventions

- **Filename:** `NNNN-kebab-title.md` (numeric prefix, zero-padded to 4).
- **Status flow:** `Proposed` → `Accepted` → optionally `Superseded by ADR-XXXX`.
- **Domains:** every ADR lists 1–4 domain tags (see taxonomy below). Tags
  drive the [by-domain index](by-domain.md) and review-mode filtering.
- **Review questions:** every ADR ends with 2–4 questions that will be used
  to quiz the author in future review sessions.

## Domain taxonomy

| Tag | Scope |
|---|---|
| `architecture` | Project structure, plug-in patterns, DI, separation of concerns |
| `data-pipeline` | Hermes, importers, DB schema, data quality |
| `data-sources` | External APIs, benchmark feeds, provider selection, staleness |
| `machine-learning` | Hercules, model training, feature engineering, AUC/thresholds |
| `llm` | LLM integration (placeholder until introduced) |
| `time-series` | EMAs, vol calc, rolling windows, lagging/leading |
| `technical-indicators` | Granville, breadth, momentum, RSI-like signals |
| `market-microstructure` | Order types, slippage, liquidity |
| `risk-management` | Stop-loss, position sizing, capital preservation rules |
| `decision-engine` | Delphi, ranking, gates, composite scoring |
| `math-statistics` | Probability, normalization, z-scores, distributions |
| `finance-fundamentals` | Index construction, weighting schemes, sector classification |

## Index

| # | Title | Status | Domains |
|---|---|---|---|
| [0001](0001-granville-plugin-architecture.md) | Granville indicator plug-in architecture | Accepted | architecture, technical-indicators |
| [0002](0002-xiu-as-benchmark-index.md) | XIU as the system benchmark index | Accepted | finance-fundamentals, decision-engine |
| [0003](0003-weighting-indicator-narrow-advance.md) | Weighting indicator (Granville #15–#16) — narrow-advance warning gate | Accepted | technical-indicators, decision-engine, math-statistics, finance-fundamentals |
| [0004](0004-genuity-us-confirming-indices.md) | Genuity indicators (Granville #17–#20) — US confirming-index source & staleness gate | Accepted | technical-indicators, decision-engine, data-sources, finance-fundamentals |
| [0005](0005-defer-granville-dullness-21-22.md) | Defer Granville Dullness indicators (#21 and #22) | Accepted | technical-indicators, decision-engine, data-pipeline, math-statistics |
| [0006](0006-granville-light-volume-25-28.md) | Granville Light Volume indicators (#25–#28) — tape × leadership-quality | Accepted | technical-indicators, decision-engine, market-microstructure |
| [0007](0007-liquidity-floor-universe-filter.md) | Liquidity floor on Delphi's universe filter (price ≥ $1, 20d vol ≥ 50k) | Accepted | decision-engine, market-microstructure, risk-management, data-pipeline |
| [0008](0008-genuity-19-magnitude-tolerance-band.md) | Genuity #19 magnitude-ratio ±5% tolerance buffer (refines ADR-0004) | Accepted | technical-indicators, decision-engine, math-statistics |
| [0009](0009-exclude-leveraged-inverse-etps-from-delphi-universe.md) | Exclude leveraged/inverse ETPs from Delphi's ranking universe | Accepted | decision-engine, data-pipeline, risk-management, market-microstructure |
| [0010](0010-rs-z-score-composite-additive.md) | RS Z-score composite (`CompositeScoreZ`) — additive only, no ranking change | Accepted | technical-indicators, decision-engine, math-statistics |
| [0011](0011-rs-equal-weighted-with-direction-edge-in-ranking.md) | RS equal-weighted with `DirectionEdge` in Delphi pick ranking | Accepted (scoped to Breakouts lens by ADR-0013/0014) | decision-engine, technical-indicators, math-statistics |
| [0012](0012-sector-index-historical-backfill.md) | Sector-index historical backfill from TMX `getTimeSeriesData` | Accepted | data-pipeline, technical-indicators, decision-engine |
| [0013](0013-multi-lens-decision-architecture.md) | Multi-lens decision architecture (lens = thesis × gate stack × ranking key) | Accepted | architecture, decision-engine |
| [0014](0014-continuations-lens-trend-confirmation.md) | Continuations lens — trend-confirmation gate + RS-primary ranking (executed) | Accepted | decision-engine, technical-indicators, risk-management |
| [0015](0015-trade-logging-ghost-execution-and-position-lifecycle.md) | Manual trade logging with ghost execution and position lifecycle | Accepted | architecture, risk-management, market-microstructure |
| [0016](0016-obv-per-symbol-soft-ranking-signal.md) | On-Balance Volume as a per-symbol soft ranking signal | Accepted | decision-engine, technical-indicators, data-pipeline, time-series |
| [0017](0017-codex-native-instructions-and-project-status.md) | Codex-native instructions and explicit project-status structure | Accepted | architecture |
| [0018](0018-manual-migrations-and-simple-recovery-backups.md) | Manual database migrations and SIMPLE-recovery backups | Accepted | architecture, data-pipeline, risk-management |
| [0019](0019-delphi-strict-history-freshness-eligibility.md) | Delphi strict history-freshness eligibility | Accepted | decision-engine, data-pipeline, risk-management |
| [0020](0020-immutable-calibration-evidence-ledger.md) | Immutable calibration evidence ledger | Accepted | architecture, data-pipeline, decision-engine, machine-learning |
| [0021](0021-calibration-outcome-and-paper-execution-contract.md) | Calibration outcome and paper-execution contract | Accepted | architecture, math-statistics, market-microstructure, risk-management |
| [0022](0022-champion-challenger-evidence-and-promotion.md) | Champion/challenger evidence and promotion | Accepted | decision-engine, machine-learning, math-statistics, risk-management |
| [0023](0023-primary-swing-policy-and-experimental-opening-confirmation.md) | Primary swing policy and experimental opening confirmation | Accepted | architecture, decision-engine, market-microstructure, risk-management |
| [0024](0024-coverage-scorecard-and-market-session-cohorts.md) | Coverage scorecard and market-session cohort identity | Accepted | architecture, data-pipeline, decision-engine, math-statistics |
| [0025](0025-three-session-swing-mark-to-market-outcome.md) | Three-session swing mark-to-market outcome | Accepted | architecture, math-statistics, market-microstructure, risk-management |

See also:

---

## ADR template

Copy this for new ADRs. Replace `NNNN` with the next number.

```
# ADR-NNNN: <short title>

- **Status:** Proposed
- **Date:** YYYY-MM-DD
- **Domains:** tag1, tag2

## Context
What problem are we solving? What did we know going in?

## Decision
What did we choose? Stated as an imperative ("Use X for Y").

## Alternatives considered
- **Option A** — pros/cons, why rejected.
- **Option B** — pros/cons, why rejected.

## Consequences
What does this lock us into? What's harder now? What's easier? What
would tell us this decision was wrong?

## Review questions
1. ...
2. ...
3. ...
```
