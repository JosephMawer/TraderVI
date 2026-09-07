# ADR-0060: Central settings and scoped configuration

- **Status:** Accepted; implemented, migration 027 applied and updated host launched on 2026-09-07
- **Date:** 2026-09-06
- **Domains:** architecture, user-interface, decision-engine
- **Related:** ADR-0051, ADR-0053, ADR-0055, ADR-0057, ADR-0059

## Context

The immediate problem is locating settings for daily Delphi, Delphi Live, Shadow and portfolio management.
The parent problem is making the ownership and effects of configuration changes understandable across
systems. The root goal is reliable comparison of independent complete strategies before explicit adoption.

After the daily Delphi Settings implementation, the operator clarified that the desired scope includes
all systems, models, entry/exit behavior and portfolio controls. The operator accepted one central Settings
area organized by system, with a shared strategy editor and shortcuts from operational pages.
In the subsequent exchange, the operator explicitly accepted separating saving strategy versions from
assigning those versions to systems or portfolios.
The operator then rejected a proposed wait-until-empty transition: an assigned version must take effect
immediately for existing positions and orders as well as future decisions.
The operator also confirmed that the new version inherits existing entry prices, quantities, cash,
observed price highs and account loss history and applies its rules to that state.
After reviewing a possible account-cap override layer, the operator chose the simpler initial design:
trading limits belong to the strategy, with additional account-specific trading caps deferred.

## Decision

1. Provide one top-level Settings destination. Operational pages link to their relevant section of that
   same destination instead of maintaining duplicate configuration editors.
2. Organize configuration into General & operations, Systems, Strategies, and Portfolios & accounts.
   System sections include daily Delphi, Delphi Live and daily System Shadow. Distinguish daily System
   Shadow from the challenger accounts within Delphi Live's own research framework.
3. Shared application/service options belong under General & operations. System operating options and
   strategy selection belong under Systems. Models, decision thresholds, entry/exit behavior, sizing and
   risk rules belong to an identifiable strategy. Account identity, capital/state, execution mode and
   assignments belong under Portfolios & accounts. A common editor does not imply interchangeable
   policies across system families.
4. Show the affected systems/accounts and effective timing before applying consequential changes.
   Preserve historical strategy identity and evidence. Source implementation does not itself change an
   operational assignment; explicit Assign is the transition in point 7.
5. Reuse the daily Delphi settings work from ADR-0059 within this broader structure. Its current scoped
   combined edit/apply behavior is superseded by the separate actions below.
6. Saving a changed strategy creates a new preserved version without altering any existing assignment.
   Assigning a version is a separate explicit action naming the target system/account. This replaces the
   combined daily edit-and-apply interaction in the current source.
   Saving is not strategy promotion; existing system-specific eligibility and evidence requirements
   still govern assignment.
7. A successful assignment immediately replaces the governing version for the entire target, including
   existing positions and pending internal actions. Do not wait for an empty portfolio or retain the
   old rules for carried positions. Begin reevaluation using the new rules and eligible data. Prevent
   old-version in-flight work from publishing or executing after the switch. Preserve completed events
   and record the exact assignment boundary; account history spanning it contains multiple versions.
   Compatibility checks precede the switch. Pending internal actions are superseded; failures of subsequent
   reevaluation retain the new assignment and are reported for retry. Future broker-submitted orders require external acknowledgement
   and reconciliation; this decision cannot make configuration changes retroactively alter actual fills.
8. Assignment preserves actual entry prices, quantities, cash, observed price highs and account
   loss/drawdown history. The new version applies its rules to those facts. Do not reset the account
   or its history as a side effect of assignment. Recompute profit floors from existing entry costs/highs,
   clear target confirmation state, and retain existing loss/capital-review holds until explicitly reviewed.
9. Keep trading limits in the assigned strategy for the initial design. Do not add a second layer of
   configurable account trading caps or overrides. Accounts retain their own actual capital, holdings
   and history; available-cash checks and existing execution-authority boundaries remain enforced.
   Additional account caps are deferred until a concrete use case justifies the complexity. This ownership
   decision does not itself remove any current operational checks or change active thresholds.

## Implementation and remaining scope

The operator subsequently authorized implementation (“alright - let's do it!”). One central view now
hosts the daily editor and a shared typed editor for the other three families. The
[implementation review](../reviews/central-settings-implementation-20260907.md) inventories supported
controls, assignment fencing, factual-state preservation, causal floor conversion and failure behavior.
Live manual assignments pause automatic research promotion; a mixed-policy history cannot be promoted
as an untouched comparison. Existing Shadow lens/slot definitions and reviewed evidence contracts remain
fixed in this initial release. Separate account-cap overrides remain deferred.

The operator separately authorized the backup, migration and updated-host rollout on 2026-09-07.
Those steps completed with preservation checks; no real target was assigned, model trained or portfolio
activated. Future assignments remain explicit operator actions. The [system map](../concepts/settings-system-map.md)
also records a proposed refinement from the broad research pause to scoped, prospective comparison
restarts. That proposal does not change this release's research guard.

## Review questions

1. Why does one Settings destination not imply one shared set of trading rules?
2. Which settings belong to a strategy, and which belong to a particular account?
