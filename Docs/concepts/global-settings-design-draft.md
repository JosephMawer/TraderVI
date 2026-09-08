# Global settings: agreed operating workflow

- **Status:** Accepted and implemented in source, 2026-09-07; migration 028 rollout tracked in project status.
- **Related:** ADR-0060, [system map](settings-system-map.md), [implementation review](../reviews/central-settings-implementation-20260907.md).

The immediate problem is changing trading rules without changing the rules underneath an open trade.
The parent problem is clear configuration ownership. The root goal is dependable operation and an
explainable history of which strategy produced each decision and result.

## Pause, close, configure, resume

1. **Pause new buys** for the affected system. This is a durable operator control: it survives restart,
   blocks new simulated entries and cancels internal pending buys. Exits and monitoring continue.
2. **Sell each holding separately.** A simulated Sell records an immutable request and waits for eligible
   later market evidence; it never erases the position or fabricates an immediate fill. Real holdings use
   Record sale after an actual broker fill. The app has no broker-order connection.
3. **Wait for zero holdings and pending orders.** Until the system is both paused and empty, its strategy
   fields, Save Version and Assign stay read-only. Backend checks repeat this under the same lock that
   fences trading cycles, so a stale screen cannot bypass the rule.
4. **Edit a complete strategy draft.** Entry, Exit, Sizing and Account risk tabs share one draft and one
   review/save action. Daily Delphi saves its model-set references and gates together. Navigation and
   reload preserve drafts in memory; closing the app warns before discarding them. Drafts are not a
   crash-persistent workspace. There is no cross-system Save All.
5. **Save Version, then Assign.** Saving creates a preserved strategy version; it does not change any
   assignment. Assignment requires paused, empty accounts and leaves the pause in place.
6. **Resume buys explicitly.** Future eligible decisions use the assigned rules. Resume neither assigns
   the version merely selected on screen nor clears loss/capital-review holds.

## Ownership and scope

The initial lock and pause are **per system family**, not per row: all Delphi Live accounts, all System
Shadow portfolios, or positions governed by the tracked monitor. This deliberately simple scope prevents
one occupied account from having its family's rules edited through another empty account. Other families
remain independent. The tracked scope remains linked Delphi Ghost positions and reported Real positions;
unlinked legacy Ghost records are not silently enrolled in this monitor.

Daily Delphi is shared upstream input. Its pause blocks dependent entries in all three families. Editing
its models/gates requires all dependent families paused and empty. A separate family pause is retained
when the shared Daily pause resumes. Data collection, recommendations and protective exits continue.

Live and Shadow freeze picks for a session. A different daily strategy does not rewrite those picks.
Entries stay blocked until a fresh compatible session is available; tracked simulated entries also reject
a pick belonging to an earlier daily strategy. Changing only a family's execution strategy does not
require retraining the daily models.

## What needs a version

A **model version** identifies a preserved trained artifact. A **strategy version** is a saved snapshot
of decision rules and their model references. Editing thresholds, entry/exit conditions, sizing or risk
creates a new strategy version when the combined draft is saved. Several edits become one version;
keystrokes do not create versions. Rule-only changes reuse compatible trained artifacts.

Names and descriptions are metadata. Pause/resume and explicit exits are audited operator actions.
Neither changes strategy identity, but both affect the interpretation of performance. Every editable
strategy field includes behavioral help and units; each system banner explains its purpose and scope.

## Preserved history and deferred work

Exit requests retain operator, time, reason, symbol, target, position identity and the original holding
snapshot. Existing order, fill, trade and ledger records remain the execution authority. Pauses and resumes
also have immutable audit events. No sale, assignment or resume resets capital or historical losses.

An empty-account switch avoids trades spanning strategies, but does not make account periods a controlled
comparison. Live manual assignments, shared/Live pauses and Live operator exits keep the existing broad
research-promotion guard engaged. A reviewed prospective research restart remains deferred; earlier
session evidence is assessed using the intervention timestamp. There is no automatic promotion restart.

Other deferred work: editable Shadow lens/slot definitions, service scheduling/provider editors, partial
sales and commissions in the new Real row dialog, account-specific cap overrides, durable cross-restart
drafts, and broker order routing/reconciliation. The new Real dialog records a complete, zero-commission
sale, matching the existing manual tracker contract.
