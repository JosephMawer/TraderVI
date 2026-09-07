# ADR-0057: Preserved model sets and explicit selection

- **Status:** Accepted; implemented and operationally validated for the supported daily workflow
- **Date:** 2026-09-06
- **Domains:** architecture, machine-learning, data-pipeline, decision-engine
- **Related:** ADR-0022, ADR-0042, ADR-0046, ADR-0055, ADR-0056

## Context

The immediate problem is making corrected daily inputs usable without training silently replacing the
models Delphi already uses. The parent problem is keeping evidence attached to an identifiable complete
strategy. The root goal is trustworthy paper comparison before human-approved real-account
recommendations and separately authorized broker execution.

The operator explicitly authorized checking saved models, reusing them only if compatibility is proven,
otherwise training separate replacements, registering a corrected strategy and switching the nightly
runner. The operator accepted preserving the current set, reviewing replacements before selection, and
retaining the previous set for deliberate rollback. This supersedes earlier restrictions for these
specific operations. It does not authorize market collection, historical evidence rewrites, broker
operations, commits or deployment.

## Decision

1. **Training creates candidates by default.** Hercules uses `ProfitInputs.XiuDatedV1`, creates a unique
   private candidate directory, preserves the predecessor, and writes every model with create-new
   semantics. New model rows remain disabled. Failed/incomplete sets remain recorded and cannot become
   the active selection. No training entry invokes strategy selection.
2. **Retain training evidence.** Completed sets record exact artifact hashes, model IDs, feature/label
   definitions, source identity/archive, a checksummed private input-data snapshot, actual training/test
   feature-window dates, exclusions and metrics. The existing split, embargo, labels, algorithms and
   feature formulas remain unchanged. Preserve predecessor signal thresholds; optimized thresholds
   remain research metrics and are not automatically adopted. Model files and private snapshots stay out
   of Git. Failed runs retain their files and reason rather than deleting contrary evidence.
3. **Assign fixed models to each strategy.** `StrategyModelBinding` holds an immutable JSON snapshot
   and checksum of IDs, file locations, hashes, input contract, metadata, signal thresholds, roles,
   weights and review identity. One strategy has at most one assignment; changing it requires a new
   strategy version. Runtime verifies the stored checksum and actual loaded bytes and uses stored model
   metadata independently of mutable global `ModelRegistry.IsEnabled` flags.
4. **Preserve a truthful previous baseline.** The predecessor keeps its strategy ID and legacy input
   behavior. A new prospective binding preserves its actual current files/metadata for rollback. This
   does not claim historical training compatibility or rewrite previous evidence. The corrected version
   requires ADR-0056 and the dated contract. Dated candidates cannot use legacy inputs.
5. **Select explicitly and atomically.** Reviewed selection clones the predecessor's strategy thresholds
   unchanged, registers the new strategy/model associations, and changes the active strategy in one
   serializable SQL transaction. Check the expected predecessor, settings, model metadata and files;
   append a `StrategyModelSelectionEvent`. A nightly file lock and SQL application lock serialize the
   transition. Repeating a completed event is idempotent; old events cannot be replayed after later
   transitions. Global model-enable flags stay unchanged.
6. **Rollback is a separate reviewed transition.** Verify the retained previous files and binding,
   require the expected corrected strategy still to be active, change selection flags, and append a new
   event. Never delete newer models or rewrite recommendations, fills, outcomes or account history.
7. **Every Delphi host resolves the stored assignment.** The shared workflow covers the nightly CLI and
   WPF. No per-host compatibility file is needed after registration, and a supplied file cannot replace
   the stored assignment. Unbound legacy strategies retain their old path during migration; an unbound
   corrected strategy fails before evaluation.
8. **This is a correctness repair with fresh evidence.** The operator authorized replacement training
   and a corrected daily baseline because the older training contract cannot be established. It is not
   a claim of superior returns, a cross-family winner, real-account recommendation promotion, or a
   waiver of ADR-0022/0053 performance evidence requirements. New results accumulate under the new
   identity. Thresholds, gates and frozen Delphi Live V1 remain unchanged. Common comparison and
   recommendation promotion remain separate work.
9. **Identify uncommitted source honestly.** The operator forbids commits. For this authorized repair,
   the existing `InitialCodeCommit` field stores an explicitly prefixed working-tree SHA-256 identity,
   not a fabricated Git commit. The candidate's training-source identity/archive and actual base-commit/
   dirty-state provenance remain separate. This limited source-work boundary does not relax later
   performance-promotion requirements or claim that the base commit contains the correction.

## Operations and validation

Migration 026 adds two tables and two immutability triggers without changing existing application rows.
It was built with Visual Studio MSBuild/SSDT, reviewed and applied manually after a verified backup.
The operator request authorized this prerequisite; no DACPAC deployment was involved.

`ModelLifecycle` inspects artifacts, verifies synthetic predictions and prepares previous/corrected
assignments. `Operations/Set-TraderVIModelSelection.ps1` defaults to preparing reviewable SQL. Explicit
modes verify a rolled-back transaction, apply selection or perform a later authorized rollback. Neither
tool launches the nightly pipeline or contacts market services.

The [operational review](../reviews/model-input-cutover-20260906.md) records actual results. The
[audit checklist](../strategy-alignment-implementation-checklist.md) retains unfinished policy dispatch,
risk-default ownership, historical outcome definitions and common promotion. Model identity alone does
not resolve those capabilities.
