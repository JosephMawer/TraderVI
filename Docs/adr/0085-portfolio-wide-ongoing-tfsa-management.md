# ADR-0085: Portfolio-wide ongoing TFSA management

**Date:** September 22, 2026 Toronto (September 23 UTC)
**Status:** Scope and duration accepted; detailed design continues, implementation incomplete.
**Domains:** architecture, risk-management, user-interface
**Related:** ADR-0062, ADR-0073, ADR-0079–0084

## Problem and decision

The immediate problem is making promotion work for any paper portfolio, rather than treating the
ungated Breakout three-position portfolio as the product's only supported destination. This belongs
to strategy assignment and handover. The root goal is one deliberately selected strategy managing
the permanent TFSA using actual broker facts while paper accounts remain independent.

The operator clarified that **any portfolio must be promotable** and selected **ongoing management
until pause or explicit switch**, with account and risk checks each trading day. Breakout Top 3 remains
an initial review candidate, not a special execution path or a limitation on the product.

Use one promotion flow for the current Daily Shadow and Delphi Live families, including their existing
portfolio variants. Preserve each portfolio's exact reviewed selection, gate profile, sizing,
protection and exit rules. Supporting a family requires executing its actual rules; an unsupported
family must not silently fall back to Shadow or Breakout behavior. Future policy types need compatible
implementation and verification before they can execute.

An approved assignment persists across days. Passing the next day's account, source and risk checks
does not require selecting the portfolio again. Persisting an assignment does not waive session hours,
freshness, existing holds, implementation identity or required evidence. A paper policy edit cannot
silently change the real account's pinned strategy. Only one strategy owns TFSA management at a time.

## Existing contracts retained

- Use the TFSA's real cash, holdings, costs, fills and risk history; never copy simulated financial state.
- Keep overlaps and their protection. Confirm and reconcile required exits before new buys.
- Preserve account loss references and holds through switching, pausing and restart.
- Preserve original order identities and reservations through uncertain submission and restart.
- Keep recommendation-only approval distinct from actual execution/automation authority.

## Relationship to the first-session implementation

ADR-0084 describes the source implementation, which expires authority after one supervised session,
does not renew or replace an existing session, and has only a Daily Shadow protection adapter. Those
limits remain enforced until the broader design is implemented and verified. This decision changes
the intended product scope and duration; it does not claim the current source already supports them
or authorize bypassing its expiry check.

Migrations 035/039/040 are shared account/session foundations, independent of the chosen portfolio.
Their September 22 installation does not create a baseline, assignment or enabled trading host.

## Open design and acceptance boundary

The [continuing draft](../concepts/strategy-promotion-and-execution.md#portfolio-wide-ongoing-management--september-22)
tracks outside/manual changes, pause/resume behavior, restart eligibility and replacement transitions.
The proposed treatment of manual trades is still awaiting the operator's answer. Do not treat it as
accepted or silently extend the current invalidated-review behavior into the general workflow.

Before implementation, express these transitions as acceptance criteria, including multi-day recovery,
switching between families and retained-position protection. Native cash/opening evidence, SQL runtime
recovery, broker lifecycle verification and explicit activation approval remain required.

## Alternatives and consequences

A Breakout-specific implementation would make other portfolio promotion incomplete and duplicate
broker controls. Requiring a new selection every morning contradicts the accepted ongoing assignment.
The selected approach requires reusable family adapters and durable assignment/session separation;
existing broker clients, risk continuation, reconciliation and order durability remain the foundation.

## Review questions

1. Why does promoting a paper portfolio transfer rules rather than simulated shares or cash?
2. Why can an assignment remain selected while new orders are blocked for the current day?
