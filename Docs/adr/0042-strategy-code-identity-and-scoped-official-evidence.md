# ADR-0042: Strategy/code identity and scoped official evidence

- **Status:** Accepted
- **Date:** 2026-09-01
- **Domains:** architecture, data-pipeline, decision-engine, math-statistics
- **Related:** ADR-0020, ADR-0022, ADR-0024, ADR-0027, ADR-0038, ADR-0041

## Context

The immediate problem is that ADR-0041 corrected the sessions consumed by relative-strength ranking,
but the active `StrategyVersion` does not explicitly identify the code decision that defines it. Every
official `CalibrationRun` captures both `StrategyVersionId` and its exact Git commit, yet current
coverage and performance queries pool all non-invalid `OfficialPaper` runs regardless of strategy
identity.

The parent problem is that the correction can change candidate ranks even though no threshold, model,
weight, gate, ranking formula, or execution policy intentionally changed. Pooling pre- and
post-correction runs would describe two implementations as one unchanged strategy and could make a
comparative score misleading.

The root goal is to let TraderVI evolve while every official performance claim remains attributable to
one reproducible strategy/code contract.

## Decision

Create an explicit identity boundary for official evidence:

1. Add nullable `InitialCodeCommit` and `DecisionRef` fields to `StrategyVersion`. Existing versions
   remain valid historical identities with both fields null; new official identities must provide both.
   `InitialCodeCommit` records the first code commit implementing the strategy's behavioral/code
   boundary, while each
   `CalibrationRun.CodeCommit` continues to record the exact later commit that produced that run.
2. Prepare manual migration 016 to clone the one active strategy, including every threshold and
   `StrategyVersionModel` association, into identity `v3.1-rs-date-aligned`. Its initial code commit is
   `c51c0849fd1311b3797cc664a19988e553bbe122` and its decision reference is `ADR-0041`. Activating it
   changes identity only; it does not tune behavior.
3. Do not edit, relabel, invalidate, or delete existing `CalibrationRun` rows. They remain immutable and
   attached to their original strategy identity. Outcome evaluation may continue to append outcomes for
   those candidates under the existing outcome contracts.
4. Scope comparative coverage, prediction, lens-tradeability, and delayed-intraday reports to one
   explicitly identified active `StrategyVersionId`. Report the selected strategy name, code boundary,
   decision reference, included official-run count, and excluded earlier-run count.
5. Reject a mixed-strategy evidence set in the pure scorecard calculator. Repository filters are not the
   sole integrity boundary.
   The five official-prediction CSV artifacts move to export schema version 2 and stamp strategy ID/name,
   initial code commit, and decision reference on every artifact.
6. Refuse a persisted `OfficialPaper` Delphi run when there is not exactly one active strategy or when
   that active strategy lacks `InitialCodeCommit` or `DecisionRef`. Non-persisted inspection and explicit
   exploratory workflows do not create official evidence and do not use this publication guard.
7. Keep earlier official runs available for an explicitly scoped historical report. They are excluded
   from the active strategy's comparative claims, not classified as bad or erased.
8. Apply migration 016 only through the manual database workflow after a fresh verified backup and
   separate authorization. Building the database project never deploys it.

The migration's fixed version ID is `99D52317-8D16-4F2A-8B97-AE9698972F55`. A later behavioral code,
configuration, model, or policy change must create another appropriate identity rather than reusing or
editing this row.

## Alternatives considered

- **Use only `CalibrationRun.CodeCommit`.** Rejected because exact commits identify executions but do
  not state which commits belong to one reviewed strategy contract.
- **Put the boundary in `StrategyVersion.Notes`.** Rejected because free-form text cannot be required,
  safely queried, or validated by official-report code.
- **Mark pre-correction runs invalid.** Rejected because they faithfully record what the earlier code
  produced. Incomparability with the new identity is not evidence corruption.
- **Update old runs to the new strategy ID.** Rejected because it would rewrite provenance and falsely
  claim that old recommendations used corrected inputs.
- **Pool identities but add a warning.** Rejected because a warning does not prevent a downstream
  metric or export from making the invalid comparison.

## Consequences

**Easier:**

- An official report names the exact strategy/code boundary it measures.
- Historical evidence stays immutable while current evidence begins a clean cohort sequence.
- Future code-level strategy changes have a reusable identity contract.

**Harder:**

- Immediately after activation, current-strategy reports correctly contain zero cohorts and cannot
  borrow maturity from earlier identities.
- Strategy activation now requires a reviewed migration and explicit provenance fields.
- Callers must select an official evidence identity before loading comparative reports.

**Would tell us this was wrong:**

- A future approved analysis establishes a version-aware hierarchical estimator that can combine
  strategy identities without representing them as one unchanged implementation. That would require a
  new report contract and must retain per-identity results.

## Operational rollout

Migration 016 was separately authorized, applied, and verified on 2026-09-02 after
`TraderDB_FULL_20260902_002015_223.bak` passed checksum verification and its staging/OneDrive copies
matched SHA-256 `33C5A08493BE4A2941341CC22EFECAB773BD854AA908C289DB6D6F5E15573EFF`.

The first execution exposed a SQL Server column-resolution error in the schema batch and stopped before
activation. `XACT_ABORT` rolled the transaction back completely; the identity schema remained absent and
all captured rows were preserved. The column and constraint operations were separated into consecutive
dynamic batches inside the same transaction, rebuilt with SSDT, independently reviewed, and committed at
`d391db8` before successful execution.

Postflight verified the exact active identity, inactive predecessor, expected nullable columns, an
enabled/trusted constraint with no violations, identical thresholds and model mappings, and unchanged
calibration counts. No historical run was rewritten: the new scope begins with zero included official
runs and seven excluded earlier-identity runs.

## Review questions

1. Why is `CalibrationRun.CodeCommit` necessary but insufficient as the strategy identity?
2. Why are pre-ADR-0041 runs excluded rather than invalidated or updated?
3. What behavior changes when `v3.1-rs-date-aligned` is activated?
4. Why must the calculator reject mixed identities even when SQL already filters them?
