# ADR-0085: Portfolio-wide ongoing TFSA management

**Date:** September 22, 2026 Toronto (September 23 UTC)
**Status:** Scope, duration, manual-holding handover, stopping new buys while exits continue and checked restart restoration accepted.
Implementation incomplete; unusual same-security cases deferred. Guided dialogue resumed September 23.
**Domains:** architecture, risk-management, user-interface
**Related:** ADR-0062, ADR-0073, ADR-0079–0084, ADR-0086

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
the display instead of fetching independent balances. September 25 integrates ADR-0084's atomic
trade projection and linked-account save without a second market-value read. First-link/manual
approval retains its reviewed-facts recheck. Existing post-trade refresh uses this same path.
Background ticks defer while an order runs. Manual/post-trade requests during a read coalesce into one
later read; failures retain prior history and back off at 5/10/20/30 minutes. Logout or shutdown cancels
in-flight work and replaced-account/login results are discarded. Pending orders and conflicts remain
visible blocks; polling does not recover or resubmit an uncertain order, clear holds, qualify risk
inputs or replace fresh order preflight. This source change is not installed or operationally enabled.

## Stop new buys and manual exits — accepted September 23

For the initial ongoing-management workflow, the operator chose one simple stop control: prevent
further automatic purchases while existing managed holdings continue through the strategy's normal
sell and protection rules. This includes no automatic additions or replacement purchases; stopping
does not liquidate holdings, remove their owner or alter exit rules. Manual holdings outside the
strategy remain outside its automatic management. Existing submitted orders retain their identities
and recovery requirements; pressing stop is not evidence that a broker order was cancelled.

Use **Stop new buys** as the proposed label, with **New buys stopped · exits active** as the status.
The complementary **Resume buying** action removes only this operator restriction; it cannot clear
risk holds, skip account/order checks or change the selected strategy.

The operator accepted restoring the **same approved strategy and saved buying mode after an app
restart, once fresh account/order checks pass**. Stopped buying stays stopped; enabled buying can
continue only when the existing checks allow it. A failed check preserves the selection and mode,
but cannot authorize an order. Restore protection, references, holds, reservations and original order
identities along with the selection. Restart is not a strategy switch or a reset of risk history.

The operator also reaffirmed **Market sell all** on each real holding row for an explicit immediate
exit, whether buying is enabled or stopped and whether the holding is strategy-managed or manual.
Reuse ADR-0077 and the existing `WealthsimpleAccountPanel` command: the click pins the entire displayed
quantity, performs fresh checks and uses the durable manual-sell workflow without another quantity
ticket or confirmation dialog. Keep the action visible and explain when it cannot run; this does not
bypass market hours, unresolved orders, changed quantities or unsupported fractional holdings.
The current client supports whole-share CAD equities; no broader broker contract is implied.

This accepts product behavior, not live order/activation authority. The existing supervised-session
`Paused` phase stops all new managed submissions, including exits, after synchronization/recovery.
It must not simply be relabelled as **Stop new buys**. A separate Core purchase control is now
implemented/tested in the bounded session: append-only stop/resume events save the mode in the existing
session snapshot, and a checked tick permits exits while suppressing Buy/Add. The account lease orders
stop against in-flight transmission; an already submitted order is still recovered and protected.
Lost save responses recover the saved mode without another event. Resume changes only the mode and
submits nothing; the next execution tick synchronizes before acting. Older snapshots retain their
previous buying behavior and full-pause semantics. No schema migration is required.

The TFSA panel now has saved strategy/mode display and Stop new buys / Resume buying controls in source.
Startup and recurring linked-account refresh restore that saved selection. An unlinked account, failed
read, changed login, stale display or uncertain save cannot enable the controls. The write pins the
displayed assignment identity under the same account lease; an uncertain response requires readback.
This control path has no approval or order transport and starts no execution loop. Its display explicitly
says that execution is not enabled. The running app has not been replaced.

## Ongoing assignment continuation — September 23 source increment

Execution review now records an explicit duration: **SingleSession** (the compatible default) or
**Ongoing**. Duration is immutable and part of the approved fingerprint. The existing durable session
identity represents the ongoing assignment; its dated account/risk snapshots advance separately.
Existing one-day approvals cannot silently acquire ongoing authority. No initial approval or strategy
is created by startup, account refresh or a buying-mode control.

For an explicitly approved ongoing assignment, checked Core ticks keep the original strategy,
activation/protection boundary, buying mode, order identities, reservations and risk baseline across
days. Continued risk records own daily-reference changes and clearing the prior daily hold. If those
records have not advanced, new risk remains blocked, the prior daily hold stays latched, and ordinary
protective exits can still be evaluated without misusing yesterday's opening as today's value.
Backward/future risk state or stale account facts block execution. Restart recovery reads protective
bars since the last saved position boundary, including missed prior-day bars; a breach followed by a
rebound must not disappear. Replay also applies the existing Session-2 closing-exit rule using reviewed
calendar boundaries and real position age. Missing required closing bars block further strategy orders
without advancing protection's recovery boundary. This does not manufacture missing market evidence.

The reviewed purchase ceiling remains cumulative across this assignment. A day change, sale or
restart never replenishes its unused authorization; actual committed costs and unresolved reservations
still count. Ongoing duration is not extra spending authority. The exact running implementation is
checked before any new order, including transition exits; current unsupported families are rejected
before approval can begin liquidation. The recommendation-only contract is unchanged.

Existing JSON snapshots carry the duration and buying mode; no migration or database change is needed.
Core continuation and control restoration are tested offline. Automatic host startup, native risk/order
qualification, account setup, portfolio-family adapters and switching remain distinct unfinished work.

## Relationship to the first-session implementation

ADR-0086 describes the original single-session implementation. The explicit Ongoing duration above
supersedes its expiry only for new approvals containing that scope. Older/single-session approvals
still expire. Replacement remains unsupported and the only execution/protection adapter is Daily
Shadow; Delphi Live cannot receive execution approval yet. The full execution pause and invalidated
review remain blocks. No running assignment or automation has been enabled.

Migrations 035/039/040 are shared account/session foundations, independent of the chosen portfolio.
Their September 22 installation does not create a baseline, assignment or enabled trading host.

## Open design and acceptance boundary

The [continuing draft](../concepts/strategy-promotion-and-execution.md#portfolio-wide-ongoing-management--september-22)
tracks outside/manual changes and replacement transitions. Stopping new buys and checked restart
restoration are accepted above; Core continuation and saved UI controls are tested, while execution
host integration, family coverage and replacement remain unfinished.
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
