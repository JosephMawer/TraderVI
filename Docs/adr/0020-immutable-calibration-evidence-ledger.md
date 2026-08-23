# ADR-0020: Immutable calibration evidence ledger

- **Status:** Accepted
- **Date:** 2026-08-23
- **Domains:** architecture, data-pipeline, decision-engine, machine-learning
- **Related:** ADR-0013, ADR-0018, ADR-0019, `Docs/concepts/paper-calibration-and-outcome-feedback.md`

## Context

The immediate problem is that `DailyPick` is overwritten when Delphi is rerun for a recommendation date and contains only published picks. The parent problem is that prediction and gate quality cannot be measured without the complete point-in-time evaluated population. The root goal is to improve the advisory strategy from reproducible evidence without allowing later code, data, or model state to rewrite what Delphi knew.

Operational picks, dossiers, manual trades, and calibration evidence have different lifecycles. Turning the operational tables into experiment history would break their existing semantics and still would not capture rejected candidates or both lens decisions.

## Decision

Add a purpose-built, append-only calibration ledger with three levels:

1. `CalibrationRun` stores one Delphi evaluation's identity, purpose, recommendation date, market-data session, creation time, strategy/configuration, model artifacts, code identity, schema versions, data-quality state, and universe counts.
2. `CalibrationCandidate` stores one shared model-evaluated symbol snapshot per run. Query-critical facts are normalized; a versioned JSON document preserves the complete signal, technical, market, and source snapshot.
3. `CalibrationLensEvaluation` stores one Continuation or Breakout decision per candidate, including eligibility, direction, rank, ranking key, publication intent, first failure, and a versioned full gate-trace JSON document.

An **official immutable run** is created only when a deliberate persisted Delphi execution commits a complete ledger batch with purpose `OfficialPaper`. The commit occurs after the canonical XIU session, effective configuration, models, candidate evaluations, and lens evaluations are known. A run is evidence even when no candidate passes. Operational `DailyPick` publication remains a separate current-state write and does not define or mutate the evidence identity.

Exploratory runs are persisted only when explicitly requested and use purpose `ExploratoryReplay`. Purpose is immutable and every official paper query must filter it explicitly. Future reconstruction from legacy picks uses `LegacyReconstruction`; it is degraded, never enters official full-universe results, and cannot manufacture missing candidates, gates, or provenance.

Capture every symbol that reaches model evaluation after Delphi's pre-scoring universe, liquidity, affordability, security-type, and strict-session eligibility checks. Pre-scoring exclusions remain run-level typed counts and audit details. This boundary avoids claiming predictions for symbols the models never evaluated while preserving every gate passer, gate failure, ranked candidate, and near miss.

Normalize stable, frequently queried values: run dates/purpose/status and version identifiers; symbol and observation OHLCV; the four enabled model probabilities; direction edge and composite; raw and Z-scored RS composites; OBV state/tilt; lens eligibility/direction/rank/ranking key/publication/first failure. Store the remainder in schema-versioned JSON. A breaking payload change requires a schema-version bump.

Record model identity as registry ID, task type, input/feature schema, training bounds, and SHA-256 of the loaded artifact; never persist an artifact or credential. Record code identity from `TRADERVI_CODE_VERSION` when supplied, otherwise from the repository Git commit discovered from `.git`. Also record working-tree state as `Clean`, `Dirty`, or `Unknown`. An official run is invalid if the commit or any loaded model identity/hash is unavailable. `Dirty` is permitted only when the exact effective payload schemas and configuration are captured and is visibly degraded; release-like runs should use a clean commit.

Historical official evidence begins prospectively. Existing `DailyPick` and dossier rows may be imported only into explicitly degraded legacy runs.

## Integrity and reporting rules

A run or outcome is **invalid** and excluded from performance claims when a required strategy/model/code identity is missing; the market session is mismatched; a future bar influenced the snapshot; a label implementation does not match its versioned definition; an official query includes a non-official purpose; lens identities are mixed; immutable keys conflict; or the benchmark needed by the metric is unavailable.

A result is **degraded** but may remain descriptive when optional fields are missing, only some otherwise valid outcomes are absent, an entry is delayed, or daily OHLC makes a path statistic ambiguous. Reports lead with audit state, official matured cohort count, symbol count, and coverage. A primary score is blocked below 95% eligible-outcome coverage; ambiguous rows are excluded only from the affected path metric and remain counted in coverage.

Use SQL tables as the durable source, a deterministic console summary as the first operator interface, and versioned CSV exports for analysis. A UI is deferred. Add SQL views only for stable, reviewed report contracts rather than embedding mutable metric logic in views.

## Alternatives considered

- **Extend `DailyPick` and `DecisionDossier`.** Rejected because rerun/current-state and Oracle-audit semantics differ from immutable full-universe experiment history.
- **Capture every loaded symbol.** Rejected because Delphi has not made a model prediction before pre-scoring eligibility and liquidity filters; record those exclusions as run audit counts instead.
- **Capture only lens passers or published picks.** Rejected because it creates selection bias and makes gate and threshold analysis impossible.
- **Reconstruct all history with today's code.** Rejected because it introduces code, model, universe, and survivorship leakage.

## Consequences

- Delphi gains a consequential append-only write in addition to its operational refresh writes.
- Candidate facts are stored once while lens attribution remains exact.
- Model hashing adds startup I/O but makes artifact identity independently verifiable.
- Official history accumulates prospectively; early reports will correctly have small samples.
- Migration remains additive and manual under ADR-0018; no DACPAC is published.

## Review questions

1. What exact event creates an official run, and why is it not the `DailyPick` delete/insert cycle?
2. Why does candidate capture begin at model evaluation rather than symbol load or lens passage?
3. Which provenance failures make a result invalid rather than merely degraded?
4. Why can legacy picks not be mixed with prospective official cohorts?
