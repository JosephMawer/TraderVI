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

See also: [by-domain index](by-domain.md).

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
