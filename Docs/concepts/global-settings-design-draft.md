# Global settings design draft

- **Status:** Decision history; accepted design implemented and rolled out on 2026-09-07
- **Domains:** architecture, user-interface, decision-engine
- **Related ADRs:** ADR-0051, ADR-0053, ADR-0055, ADR-0057, ADR-0059, ADR-0060

The operator subsequently authorized implementation. ADR-0060 and the
[implementation review](../reviews/central-settings-implementation-20260907.md) now define the concrete
source behavior. Migration, actual assignment, training and operational rollout remain separate actions.

## Goal and current state

Make configuration easy to find across TraderVI while keeping each change's scope explicit and preserving
the identity of independently evaluated strategies. Daily Delphi currently has its own model-set and
eight-gate settings view (ADR-0059), now hosted by central Settings alongside the other policy editors.

## Decision record

**Accepted:** One top-level Settings destination, organized by system, with contextual shortcuts from
operational pages. Four areas: General & operations, Systems, Strategies, and Portfolios & accounts.
Reuse the daily editor. Strategy definitions own models and trading behavior; accounts own their state
and assignments. A central UI must not imply shared balances or cross-family policy compatibility.

**Accepted — save versus assign:** Saving a revised strategy creates a new preserved version and leaves
all system/account assignments unchanged. Assigning a version is a separate explicit action identifying
the target system/account. The operator accepted this separation after the navigation decision. It
supersedes the combined edit-and-apply interaction as the intended broader design; the existing daily
implementation now follows the separate Save Version and Assign flow. Saving also does not confer performance approval
or bypass system-specific eligibility and promotion rules.

**Accepted — immediate reassignment:** The operator rejected waiting until holdings and orders clear.
A successful explicit assignment immediately replaces the target's governing version for all existing
positions, pending actions and future decisions. There is one current assignment per target; old rules
do not remain assigned to carried positions. An assignment must initiate reevaluation under the new
rules without waiting for the portfolio to empty or requiring a separate manual evaluation command.
Already completed fills and decisions remain historical facts, attributed to their original versions.

**Required implementation consequences:** Validate compatibility before committing a replacement, then
switch the target as one coordinated operation. Older in-flight evaluations cannot publish decisions or
execute pending actions after the assignment boundary. Pending internal actions must be revalidated
under the new version before execution; superseded actions retain their audit history. Reevaluation
must use eligible data, with unavailable evidence reported explicitly, rather than manufacturing an
instant fill. Distinguish assignment completion from reevaluation completion in the UI. Performance
spanning a reassignment must identify the transition instead of claiming a pure new-version track record.

**Accepted — inherited factual state:** Preserve actual entry prices, quantities, cash, observed price
highs and account loss/drawdown history when changing versions. Apply the new rules to those existing
facts. The operator confirmed this explicitly; assignment is not a new account or a reset of its history.

**Implemented — rule-derived state:** Recompute profit floors from retained entry costs and observed
highs; they may tighten or loosen under the new rules. Clear target confirmation counters. Supersede
pending internal buys and sells, preserving original dossiers and events. Existing daily-loss and
capital-review holds remain latched for the existing reviewed resume flow. Do not apply new floors to
pre-assignment intrabar lows or invent missing observations.

**Accepted — simple ownership of trading limits:** The assigned strategy owns trading limits, including
position count, sizing and risk thresholds, alongside entry/exit rules and models. Do not introduce a
second configurable account-level cap or override layer in the initial settings design. Accounts retain
their own identity, capital/cash, holdings, history, execution mode and strategy assignment. Available
cash and existing execution-authority constraints remain real constraints; this decision does not make
them configurable strategy overrides or remove current operational checks. The operator chose this
simplification after discussing the competing strategy/account limits example.

**Implemented — failure and readiness:** Commit assignment and state conversion atomically after
compatibility checks and worker fencing. Begin reevaluation afterward; report failure or unavailable
data without silently reverting the assignment. Normal host cycles retry using the assigned rules.

**Open — subsequent capabilities:** Fresh cross-family comparison runs, configurable Shadow lens/slot
definitions, scheduling/provider editors, deposits/withdrawals and resuming automatic research after
a manual Live assignment remain separate work.

**Deferred:** Optional account-specific trading caps until a concrete need emerges; a universal
cross-family promotion mechanism, broker execution, and making every source
constant editable. Existing evidence requirements remain authoritative. Immediate portfolio reassignment
is an accepted future behavior change to reconcile explicitly with the current frozen policy contracts;
it has not changed their implementation. For future broker integration, an already submitted order
requires broker-confirmed cancellation/amendment and reconciliation; an assignment cannot rewrite fills
or assume an external order has changed merely because internal configuration changed.

## Proposed acceptance criteria, pending behavioral decisions

- A page's Settings shortcut opens the same editor as central navigation.
- Each control identifies its owning application/service, system, strategy version or account.
- Trading limits are edited in the strategy; the initial account settings have no duplicate trading
  caps or override hierarchy. Account facts and execution-authority boundaries remain separate.
- Saving creates a version without altering assignments, ongoing evaluations, positions or pending actions.
- Assignment is a separate reviewed action with an explicit target and effective boundary; eligibility
  remains governed by the target system's existing policy and evidence requirements.
- A successful assignment governs all existing holdings and pending internal actions immediately and
  starts reevaluation. No old-version worker may subsequently publish or execute a stale decision.
- A review shows changed values, affected consumers and effective timing before a consequential apply.
- Historical model bindings, strategy versions, outcomes, fills and account history remain unchanged.
- Assignment preserves entry prices, quantities, cash, observed highs and loss/drawdown history and
  applies the new rules to that state without resetting the portfolio.
- Unsupported settings or incompatible strategy assignments are explained rather than silently ignored.
- Browsing, editing and saving do not initiate training, collection, an evaluation run or a broker
  operation. Explicit assignment starts reevaluation; it does not grant new broker execution authority.

## Implementation readiness

Source implementation and focused validation are complete. The separately authorized backup, migration
027 and updated-host launch completed on 2026-09-07 with preserved prior state. No actual strategy
selection was performed. Old binaries do not participate in the new settings fence and must not be
used alongside the settings writer. See the [system map](settings-system-map.md) for current scope and
the proposed future research-comparison refinement.

## Review questions

1. What is the difference between saving a strategy version and assigning it to a running system?
2. Why must the effects on open positions be decided before adding an Apply button for exit rules?

## Decision history

The proposed wait-until-empty transition was rejected. The operator requires immediate whole-target
reassignment, including existing holdings and pending orders, without simultaneous old/new rule assignments.
The proposed account-cap hierarchy was also set aside in favor of strategy-owned trading limits for
the initial design. Revisit optional caps only when an actual account-specific requirement warrants them.
