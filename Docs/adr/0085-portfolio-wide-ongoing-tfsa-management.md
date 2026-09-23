# ADR-0085: Portfolio-wide ongoing TFSA management

**Date:** September 22, 2026 Toronto (September 23 UTC)
**Status:** Scope, duration and manual-holding handover accepted; common-path implementation resumed
September 23 at the operator's request. Implementation incomplete; unusual same-security cases deferred.
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

## Manual holdings and deliberate handover — accepted September 23

A holding manually purchased through TraderVI stays outside the active trading strategy by default.
The operator requested both an **Assign to active strategy** action on an existing holding and an
optional **Let the active strategy manage this holding** choice in the purchase dialog. Keep that
purchase option off by default. Handover changes management of actual shares; it does not copy paper
state, sell/rebuy the holding, erase its manual origin or create a fictional new entry.

Show the exact active strategy in either action. Preserve existing account risk references, holds,
actual costs and known protection. The operator explicitly wants manual handover to be allowed beyond
the normal position count: Breakout 3 may manage four holdings after a manual addition. The added
holding receives the strategy's existing sell/protection rules and can leave through an ordinary exit.
Do not manufacture an immediate sale solely to restore the count. This is a recorded manual exception,
not evidence that the paper strategy itself selected a fourth stock.

The operator rejected counting manual extras against the strategy's normal positions. Keep the
strategy's own automatic positions distinct from manually added holdings managed for exits only.
Breakout 3 continues buying, selling and replacing its own three positions while an extra holding can
remain under the same applicable sell/protection rules. Selling an extra does not create a strategy
vacancy or cause an automatic replacement. The exit-only role does not authorize automatic purchases
or additions to that holding. A later independent strategy entry must qualify under its normal rules.

For example, strategy positions A/B/C plus manual extra D can become B/C/E plus D after A's confirmed,
reconciled sale and a qualifying E purchase. D's presence does not consume one of the three automatic
positions. If D sells, the remaining B/C/E continue without an extra replacement. This supersedes the
assistant's proposal to wait until the total managed count falls below three.

All actual holdings still belong to the same TFSA cash and risk accounting. Extra holdings consume
real funds and their gains/losses affect account-wide checks. This role split does not authorize
bypassing unresolved orders, stale facts, cash/sizing limits or loss holds. The operator will not manually
add shares of a stock already among the strategy's positions and asked to stop expanding this edge case.
Split ownership of one security is outside the first version; preserve existing conflict checks if an
unexpected overlap occurs. Capital and effective-transfer checks remain necessary for actual handover.

This adds explicit manual ownership inside the permanent TFSA. A holding designated manual is not
implicitly enrolled in the selected strategy. Applying ADR-0079's promotion exits to such holdings,
including during later strategy replacement, needs the scope clarification in the continuing draft;
do not silently classify older holdings or rewrite historical attribution. Cash and account-wide risk
still cover the actual TFSA. Automatic entry/sizing and account loss rules do not change implicitly;
the explicit real-account manual exception must remain visible in history and performance attribution.

The operator also requested regular account synchronization while retaining **Refresh now**. The first
source implementation uses a five-minute interval while connected and the app is open, independent of
the visible tab. The display and managed-account source reuse the same reconciliation/import workflow;
only an already reviewed account link may append saved history. Return the final reconciled read to
the display instead of fetching independent balances. Existing post-trade refresh uses this same path.
Background ticks defer while an order runs. Manual/post-trade requests during a read coalesce into one
later read; failures retain prior history and back off at 5/10/20/30 minutes. Logout or shutdown cancels
in-flight work and replaced-account/login results are discarded. Pending orders and conflicts remain
visible blocks; polling does not recover or resubmit an uncertain order, clear holds, qualify risk
inputs or replace fresh order preflight. This source change is not installed or operationally enabled.

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
Manual default ownership and the two handover entry points are accepted above. Direct-Wealthsimple
trade ownership, automatic continuation after known in-app orders and transfer boundaries remain
open/proposed in the draft; same-security split ownership is deferred. Do not silently extend the current blanket
invalidated-review behavior into the general workflow or assume a five-minute-old snapshot can authorize
an order.

Proceed with the agreed common path under the original implementation request. Express remaining
consequential transitions as acceptance criteria, including multi-day recovery, switching between
families and retained-position protection. Native cash/opening evidence, SQL runtime
recovery, broker lifecycle verification and explicit activation approval remain required.

## Alternatives and consequences

A Breakout-specific implementation would make other portfolio promotion incomplete and duplicate
broker controls. Requiring a new selection every morning contradicts the accepted ongoing assignment.
The selected approach requires reusable family adapters and durable assignment/session separation;
existing broker clients, risk continuation, reconciliation and order durability remain the foundation.

## Review questions

1. Why does promoting a paper portfolio transfer rules rather than simulated shares or cash?
2. Why can an assignment remain selected while new orders are blocked for the current day?
