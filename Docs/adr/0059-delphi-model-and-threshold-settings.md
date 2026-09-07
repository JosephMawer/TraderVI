# ADR-0059: Delphi model-set and threshold settings

- **Status:** Implemented in source; operational selection has not been exercised
- **Date:** 2026-09-06
- **Domains:** architecture, decision-engine, machine-learning, user-interface
- **Related:** ADR-0034, ADR-0055, ADR-0057, ADR-0058

**Interaction update:** ADR-0060 supersedes the original nested-tab and combined Apply flow below.
The editor is now under top-level Settings; Save Version and Assign are separate, and Assign initiates
an official reevaluation. The preserved-model and daily selection-lock contracts remain applicable.

## Context

The immediate problem is selecting trained models and editing daily decision thresholds from Delphi.
The parent problem is keeping the selected model set, strategy, and runtime behavior consistent across
desktop and scheduled evaluations. The root goal remains identifiable, trustworthy strategy evidence
before human-approved recommendations and independently authorized broker execution.

The operator requested the settings page and its apply capability. This authorizes implementation,
not an arbitrary choice of new active settings during development.

## Decision

The Settings tab lives inside the daily Delphi workspace. Selecting a saved model set loads its associated
strategy and all four profit-model identities, features, training dates, signal thresholds, weights and
hashes. The unit of selection is the complete preserved set, because one isolated model is not a complete
daily strategy. Strategies without a preserved assignment remain visible but cannot be applied.
Unassigned Hercules candidates must first receive ADR-0057's reviewed strategy assignment; this page
does not turn training success into approval or mix unrelated candidate files.

The eight persisted decision-gate values are editable: composite, up probability, breakout probability,
direction edge, down-probability veto, breadth veto, strong breakout override and strong edge override.
Probability/composite values are finite fractions in [0,1]; edge/breadth values are finite in [-1,1].
These are permissible input ranges, not recommended trading values. Existing values load without tuning.
Model signal thresholds are displayed separately from decision gates. Risk/account policies, model
weights, deterministic indicators and nonpersisted dials are not edited through this page.

Applying an unchanged saved configuration selects that existing strategy. Editing a gate instead requires
a unique name and creates a new StrategyVersion, cloned associations and an immutable model binding;
the previous strategy, bindings and historical evidence remain intact. Every change requires a reason
and a concrete confirmation showing the target strategy, models, policy and exact gate values.
The source's DecisionRef is deliberately preserved: current runtime uses it for dated-input and observed
benchmark dispatch. ADR-0059 and the review reason are recorded in the new version's notes, binding
review and selection event. A new version's InitialCodeCommit explicitly uses `assembly-mvid:<guid>`
(the loaded Core build's module identity), rather than claiming the current Git HEAD contains that build.
Training source identity remains separately preserved in the model set; run provenance is unchanged.

Review verifies the exact model bytes and the runtime prediction schema without reading market data or
running predictions. Apply verifies again while holding read handles that prevent replacing those files,
then uses one serializable SQL transaction. It rechecks the complete active/source strategy snapshots and
binding hashes, registers an edited version when needed, switches active flags and appends a selection
event atomically. Stale reviews, conflicting names and unavailable files fail before commit. An uncertain
commit response asks the operator to reload, avoiding a blind repeat of the transition.

Daily Delphi holds a shared SQL application lock from before strategy loading through publication.
Settings and the existing manual selection script require the exclusive form of that same lock.
Settings also acquires the existing nightly file lock. This prevents a selection from racing any updated
daily host, and coordinates with an in-progress nightly pipeline. Already running binaries predating this
change must be closed before operating settings; standalone older hosts do not hold the new SQL lease.

The next daily desktop or nightly evaluation reads the selection. Applying does not run Delphi, replace
saved picks, change independently assigned paper accounts, or place an order. This is daily configuration,
not ADR-0055's common cross-strategy performance promotion.

## Validation and operational boundary

Focused tests cover switching, immutable clones, invalid/nonfinite values, unassigned/corrupt model sets,
stale active/source snapshots, name races, multiple active strategies and mismatched model bytes.
Use an isolated synthetic WPF preview for layout and binding checks. No migration is needed: the feature
uses the installed migration-026 tables. Database commit/rollback behavior requires a separately reviewed
operational check; no strategy selection is applied merely to validate this source change.

Validation on 2026-09-06: all 690 Core tests passed; affected Debug WPF and Delphi CLI builds passed;
three synthetic layouts rendered without binding errors, with editor validation and selection clearing
checked. The SELECT-only installed catalog returned seven strategies and three selectable preserved
sets, with exactly one active strategy and matching hashes for its four models. The sandbox could not
establish SQL encryption; the same explicitly authorized read-only check passed outside the sandbox.
The full solution/SSDT build was not run for this change. Existing nullable/unused-variable compiler
warnings remain. Separately, test restore assets report dependency-security advisories for
System.Data.SqlClient (moderate/high), System.DirectoryServices.Protocols (moderate),
System.Drawing.Common (critical), and System.Security.Cryptography.Xml (moderate).

## Review questions

1. Why does editing a threshold create a new strategy instead of changing the existing version?
2. How are model signal thresholds different from daily decision-gate thresholds?
3. When does an applied model-set selection affect recommendations already on screen?
