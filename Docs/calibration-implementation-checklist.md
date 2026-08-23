# Paper calibration implementation checklist

- **Last updated:** 2026-08-23
- **Authoritative design:** ADR-0020, ADR-0021, ADR-0022
- **Background:** `Docs/concepts/paper-calibration-and-outcome-feedback.md`
- **Database rollout script:** `TraderDB/Migrations/20260823_011_AddCalibrationEvidenceLedger.sql`

## Status legend

- `[x]` Complete and validated in source.
- `[ ]` Not complete.
- **Operational step** means it changes the local database or runs a consequential producer and requires explicit authorization.

## Current milestone

The design and first source implementation are complete. The five calibration tables were found already applied and empty during the 2026-08-23 operational audit; their live columns, constraints, foreign keys, and indexes match the committed schema. A fresh verified post-migration backup and hash-matched OneDrive copy now exist. No official calibration run has been captured and Athena has not written an outcome. The next milestone is one controlled Delphi run and evidence verification.

## Phase A — measurement contract and decisions

- [x] Separate prediction outcomes from tradeable outcomes.
- [x] Define official, exploratory, and legacy-reconstruction run purposes.
- [x] Define the model-evaluated candidate population and pre-scoring exclusion boundary.
- [x] Define normalized columns versus versioned JSON snapshots.
- [x] Define code commit, working-tree, strategy, and model-artifact provenance.
- [x] Define invalid versus degraded evidence and the 95% reporting-coverage floor.
- [x] Define label-aligned 1/5/10/20-session prediction outcomes.
- [x] Define next-eligible-session-open trade timing, costs, exits, and OHLC ambiguity handling.
- [x] Define Top-1, Top-3, Top-5, and rank-weighted research policies.
- [x] Define champion/challenger evidence tiers and human promotion rules.
- [x] Accept ADR-0020 through ADR-0022 and add review cards.

## Phase B — immutable evidence capture

### Source implementation

- [x] Add canonical `CalibrationRun` schema.
- [x] Add canonical `CalibrationCandidate` schema.
- [x] Add canonical `CalibrationLensEvaluation` schema.
- [x] Add canonical `CalibrationOutcomeDefinition` schema.
- [x] Add canonical `CalibrationCandidateOutcome` schema.
- [x] Add one additive, transactional migration containing every new SQL object.
- [x] Add calibration domain records and schema-version constants.
- [x] Add transactional batch validation and persistence.
- [x] Capture the complete model-evaluated universe rather than only top picks.
- [x] Keep shared candidate facts separate from Continuation and Breakout decisions.
- [x] Capture configuration, market context, gate traces, model registry IDs, artifact hashes, and Git provenance.
- [x] Keep exploratory runs from refreshing official operational tables.
- [x] Add the new tables and migration to the SQL project.
- [x] Add Athena and the ADRs to the Visual Studio solution.

### Validation completed

- [x] Core tests pass: 31 passed, 0 failed on 2026-08-23.
- [x] Delphi focused build succeeds with no compiler warnings.
- [x] Athena focused build succeeds with no compiler warnings.
- [x] SQL project builds successfully with SSDT and includes all calibration objects.
- [x] Migration and canonical schema definitions compile together.

### Operational rollout

- [ ] A fresh immediately pre-migration backup was not independently observed; the newest recorded backup before the discovered schema application was the valid 2026-08-22 full backup.
- [x] Review `20260823_011_AddCalibrationEvidenceLedger.sql` against the target database.
- [x] Migration application discovered from live object creation timestamps: all five tables were created together on 2026-08-23 at 16:58:29 and contained zero rows when audited.
- [x] Verify all five tables, foreign keys, checks, unique constraints, and indexes exist and match the committed canonical definitions.
- [ ] Existing operational row-count preservation cannot be proven retrospectively because no immediate pre-migration counts were captured; the migration is additive and the five new tables were empty when inspected.
- [x] Create and verify a fresh post-migration full backup with checksums: `TraderDB_FULL_20260823_170922_281.bak` (31.23 MB).
- [x] Copy the backup to the approved OneDrive directory and verify matching SHA-256 `B1F19BDD3C2919DE5D1204BEF357F06276D174F9C46715D050D158E83EE5C2C3`.
- [ ] Confirm the OneDrive client reports cloud synchronization complete.
- [ ] **Operational step:** run one controlled official Delphi evaluation.
- [ ] Verify exactly one immutable run, one candidate per evaluated symbol, and two lens rows per candidate.
- [ ] Verify strategy, code, model hashes, market-data session, counts, and audit state.
- [ ] Rerun Delphi deliberately and verify it appends a new run without overwriting the first.
- [ ] Run an explicit exploratory Delphi evaluation and verify it cannot enter official paper queries or refresh operational picks.

## Phase C — prediction outcomes

### Implemented

- [x] Add the separate local Athena console application.
- [x] Reuse enabled `ProfitModelRegistry` `ILabeler` implementations.
- [x] Compute aligned 1-, 5-, 10-, and 20-session returns.
- [x] Compute 10-session XIU return and excess return.
- [x] Refuse to substitute a later symbol bar for a missing XIU-aligned session.
- [x] Add deterministic IDs and idempotent inserts for initial outcome definitions.
- [x] Add idempotent matured prediction-outcome persistence.
- [x] Add focused maturity, session-alignment, label reuse, and evidence-integrity tests.

### Remaining

- [ ] **Operational step:** run Athena after the first official cohorts mature.
- [ ] Persist explicit invalid/degraded records for missing or mismatched future sessions instead of leaving them pending indefinitely.
- [ ] Add coverage-first deterministic scorecards.
- [ ] Add Brier score, reliability buckets, calibration error, AUC where supported, and probability-decile lift.
- [ ] Add Spearman rank information coefficient and top-1/top-3/top-5/top-decile lift.
- [ ] Add gate pass/fail, OBV-state, regime, liquidity, volatility, sector, and lens slices.
- [ ] Add versioned CSV export.
- [ ] Add integrity tests for duplicate outcomes, wrong-session joins, mixed purposes/lenses, and definition-version changes.

## Phase D — tradeable recommendation outcomes

- [ ] Add a versioned tradeable-outcome definition and persistence shape.
- [ ] Select the first eligible entry session using run time in `America/Toronto`.
- [ ] Implement the three-session missing-entry-bar allowance and `NoEntry` result.
- [ ] Apply raw open plus separately persisted 10 bps slippage and 15 bps half-spread per side.
- [ ] Compute gross/net returns, XIU excess return, MFE, MAE, and time to excursions.
- [ ] Implement warning diagnostics and versioned hard-stop fills, including gap-through-stop handling.
- [ ] Flag conservative same-day path ambiguity.
- [ ] Restrict tradeable outcomes to published lens recommendations.
- [ ] Add recommendation-level Continuation and Breakout tradeability reports.

## Phase E — shadow portfolios

- [ ] Implement normalized fractional Top-1 selection-quality portfolio.
- [ ] Implement normalized fractional Top-3 and Top-5 equal-weight portfolios.
- [ ] Implement the versioned rank-weighted formula.
- [ ] Implement capital-constrained integer-share versions using historical capital and reserve.
- [ ] Implement ten-session holding, vacancy filling, duplicate-symbol handling, and no-early-rotation v1 rules.
- [ ] Add equity curves, drawdown, turnover, costs, utilization, and recovery metrics.
- [ ] Keep Continuation and Breakout portfolio results separate.

## Phase F — calibration experiments and promotion

- [ ] Add calibration experiment/proposal persistence.
- [ ] Record hypothesis, champion, challenger, attempted variants, windows, primary metric, and guardrails.
- [ ] Add one-variable replay/ablation support for OBV weight, raw versus Z-scored RS, and gate thresholds.
- [ ] Add untouched forward-validation windows and cohort-aware uncertainty estimates.
- [ ] Enforce the 10, 20–30, 60, and 120-cohort evidence tiers from ADR-0022.
- [ ] Record approval identity/time and link approved changes to strategy, model, policy, ADR, and code versions.
- [ ] Preserve the former champion for rollback comparisons.
- [ ] Confirm no code path can automatically activate a challenger.

## Phase G — optional narration

- [ ] Revisit LLM narration only after deterministic scorecards and fixtures are stable.
- [ ] Keep calculations, proposal approval, and activation outside the LLM.
- [ ] Reconcile any proposal-narration role with Oracle Rule R1.

## Promotion-readiness tracker

- [ ] 10 matured official cohorts: measurement system validated.
- [ ] 20–30 matured cohorts: provisional challengers may be formed with aligned historical evidence.
- [ ] 60 matured cohorts plus regime/forward-window requirements: ordinary promotion proposals may be reviewed.
- [ ] 120 matured cohorts plus stronger downside evidence: safety-gate or concentration changes may be reviewed.

## How to ask for status

Useful prompts include:

- “Update the paper-calibration checklist and tell me the next safe step.”
- “What is complete in Phase B, and what blocks the first official calibration run?”
- “Show me everything left before Athena can produce its first scorecard.”
- “Which operational calibration steps require a database backup or explicit authorization?”
