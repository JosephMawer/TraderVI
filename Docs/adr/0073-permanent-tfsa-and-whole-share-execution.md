---
cssclasses: [executive-brief]
---
# ADR-0073: Permanent TFSA, primary strategy authority and whole-share execution

- **Status:** Accepted design decisions; transition preview and whole-share intent validation implemented. Broker execution and activation remain unfinished.
- **Date:** 2026-09-10 (Toronto)
- **Domains:** architecture, risk-management, market-microstructure
- **Related:** ADR-0055, ADR-0060, ADR-0062, ADR-0072

> [!summary] Decision and consequence
> TraderVI's self-directed TFSA is the enduring real account. A human-approved primary promotion assigns a complete strategy version to trade that account, while its source paper portfolio continues independently. Reconciled broker facts are required, and TraderVI-originated orders use whole shares only. A preference for ten-share purchase increments is not a liquidity guarantee or permission to exceed risk limits.

## Context

The immediate problem is defining promotion and order sizing after direct TFSA reads became available.
The parent problem is connecting paper-strategy selection to reliable broker execution. The root goal
is a permanent real account traded under the explicitly selected portfolio's rules, with understandable
financial history and independent paper evidence.

The operator accepted the permanent TFSA and reconciliation model, clarified that primary promotion
must eventually allow real trades under that portfolio's rules, and made no fractional shares an
invariant for this version. The operator prefers whole-share purchases in multiples of ten. They first
leaned toward disposing of existing positions, then explicitly clarified that overlapping holdings
should be retained and managed by the incoming strategy. The unconditional cash-start proposal is
superseded; transition timing and automatic liquidation mechanics remain to be designed.

## Accepted decisions

1. Bind the real role to the actual self-directed TFSA account identity. Its cash, holdings and history
   persist across strategy assignments. The managed TFSA remains outside this role.
2. The selected paper portfolio continues with its own cash, positions, fills and evidence. Promotion
   assigns its exact reviewed strategy version; it never copies simulated balances, fills or holdings.
3. In the future broker-enabled workflow, explicit primary promotion grants that strategy authority
   to place real TFSA orders once the agreed activation conditions hold. It means more than the
   current recommendation-only assignment. It does not grant another paper strategy or a future
   unreviewed strategy version authority over the account. Existing pauses and financial risk holds
   remain meaningful. The order/transition contract is still to be completed.
4. Reconcile broker holdings, cash, open orders and completed trades with TraderVI records before
   using those facts for account-level strategy decisions. Unknown or conflicting facts cannot be
   silently presented as a fully synchronized account.
5. TraderVI-originated buy and sell order quantities must be positive whole shares. Fractional orders
   are outside the initial execution version. `WholeShareLimitOrder` now validates this at intent
   construction; there is no enabled broker submission path yet.
6. Prefer purchases in multiples of ten as a soft sizing preference. The implementation caps shares
   by both cash at the proposed limit and the strategy's share limit, then rounds down to tens when
   at least ten fit. Below ten it permits the affordable whole quantity. This cannot increase a risk-sized order
   or leaving an otherwise sellable whole-share residual stranded.

7. Retain existing TFSA holdings that overlap with the incoming strategy's intended holdings. Do not
   sell a position merely to buy the same holding back for promotion. At the agreed handover boundary,
   the incoming strategy assumes management of the actual inherited holding. Its real purchase
   history and cost basis remain intact; record the new strategy's effective time separately.
   The paper account's quantity, cash and simulated fills are not copied into the TFSA.

## September 10 clarification: overlap retention

The operator explicitly requested keeping overlapping holdings and letting the incoming strategy
manage what it takes over. Promotion therefore reconciles the existing TFSA with the intended
incoming holdings rather than requiring every position to become cash. Selling non-overlapping
positions remains the intended transition direction; submission timing and incomplete exits remain
open. A later strategy-driven reduction or exit is distinct from an unnecessary promotion sell/rebuy.

The recommended overlap definition is a matching broker security that the incoming strategy currently
intends to hold, with its paper holdings as review evidence. Merely appearing on a candidate list does
not establish that the strategy wants a position. The exact evaluation point, stale paper state,
quantity adjustments and initialization of incoming exit/protection rules still require definition.
Do not impose a fresh-entry trigger solely to retain an already wanted position; an existing holding
can legitimately remain after its original buy signal has passed.

## Why ten is a preference

TMX defines standard trading units (board lots) using the previous closing price: 100 shares at $1
and above, 500 shares from $0.10 to below $1, and 1,000 shares below $0.10. Ten shares is not a special
board lot. Multiples of ten do not establish that an order will fill completely. Order price, available
liquidity and execution rules still matter. A partial fill of a twenty-share order may leave seventeen
shares; the whole-share contract allows selling all seventeen.

Source: [TMX order types and functionality guide, standard trading units](https://www.tsx.com/ebooks/en/order-types-guide/44/).

## Implementation implications and unresolved design

### September 10 implementation boundary

A later September 10 step, [ADR-0074](0074-wealthsimple-market-buy-protocol.md), adds the captured
market-buy client request, separate fill-status read and local duplicate-attempt guard. The complete
real execution/promotion workflow remains unfinished; the following describes the earlier preview.

The operator requested implementation after the design discussion. A read-only main-account review
now ranks Delphi Live sources using `PortfolioDailyPerformance` and shows complete strategy identity.
It keeps the fixed-gate comparison restriction: that admission profile is not represented by the
existing real recommendation contract. An equal-return tie prefers the supported operational champion.
Missing or partial day results stay explicit; source paper quantities never become real orders.

The preview matches retained instruments by broker security ID, preserving actual quantities and cost
basis. It blocks unresolved identities, fractional/negative quantities, stale/future broker snapshots,
unverified pending-order reads and any outstanding order. Two minutes is a conservative preview
freshness bound, not a guarantee of atomic reconciliation or an order-submission price policy.
The user enters a purchase ceiling (initially zero); available purchase cash is the minimum of that
ceiling, observed CAD cash and observed CAD buying power. Pending sale proceeds are excluded.

The pending-order feed is paginated across the verified identity and filtered to the owned TFSA.
Submitted quantity includes any partial fills; cancellation-pending remains outstanding. This read
and a unique Canadian security match for an existing holding were live-verified. Populated-order
shapes and additional security-match cases have synthetic tests.
An empty feed does not establish completed-fill coverage. No broker mutation, primary activation,
durable order journal or live execution loop is implemented by this step. Existing recommendation-only
approval gates remain intact. The next capture must establish submit/cancel payloads and outcomes;
complete real-fill reconciliation and durable recovery must precede enabling those mutations.

Keep decimal precision when observing broker holdings. A fractional amount introduced externally or
by a corporate action must be visible during reconciliation; never round it away to satisfy the order
invariant. Its resolution and effect on account activation need a stated policy.

The operator's initial preference is limit buys and sells. Day expiry, protective-exit repricing,
partial fills, cancellation confirmation, ambiguous submission recovery and transition liquidation
still need decisions. Retaining overlap is now accepted; liquidating everything first is superseded.
The revised draft must define which holdings transfer, which exit, who protects each during transition,
and when new entries may begin. Current runtime empty-account checks still need a reviewed amendment
for this future workflow; no existing checks have been removed.

See the [continuing integration draft](../concepts/strategy-promotion-and-execution.md#tfsa-integration-draft--september-10-2026)
for accepted, proposed and open details. Existing ADR-0062 recommendation-only code and ADR-0060's
pause/empty checks remain unchanged. This discussion is not a command to submit trades, liquidate
holdings, change SQL or implement an unfinished order lifecycle.

## Review questions

1. What changes when a paper strategy is promoted, and what account state must remain independent?
2. Why can whole-share execution coexist with precise observation of a fractional broker residual?
3. Why does a ten-share sizing preference not guarantee a complete exit?
