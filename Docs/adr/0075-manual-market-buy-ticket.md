---
cssclasses: [executive-brief]
---
# ADR-0075: Shared manual market-buy ticket and recoverable orders

- **Status:** Accepted UI direction; implemented and verified offline. Native live submission remains unverified.
- **Date:** 2026-09-10
- **Domains:** architecture, user-interface, risk-management
- **Related:** ADR-0072, ADR-0073, ADR-0074

> [!summary] Decision and consequence
> A reusable Buy action opens one WPF ticket. The operator enters the exact whole-share quantity and
> explicitly submits a real market order to the verified self-directed TFSA. Protected order records
> preserve recovery and actual cumulative fills. Manual buying does not activate a portfolio or assign
> automatic selling.

## Context

The immediate problem was a Delphi form that recorded a fill already made at a broker. The parent
problem is connecting the captured market-buy client to a consistent desktop workflow. The root goal
is deliberate, understandable real-account execution, with portfolio automation added separately.

The operator approved the shared-ticket concept, clarified that manual share quantities must be
editable while portfolios choose their own quantities, and requested implementation. Compact dialog
placement and manual ownership follow the recommended initial version. No live order was requested
as part of validation.

## Decision

`MarketBuyButton` carries immutable stock/source context through `ManualBuyCommands` to the window's
`ManualBuyCoordinator`. Both Delphi lenses share one **Manual buy…** action for the selected stock;
broker holdings offer one secondary **Buy more…** action for the selected holding. There are no Buy
columns. The operator requested this follow-up to reduce repeated controls and distinguish adding to
a holding from choosing a new stock. Clearing selection or refreshing holdings clears the action's
target. Selecting a row does not submit. Holding launches also pin the broker security ID, preventing a Canadian ticker match
from substituting a different instrument. Existing-fill recording and Ghost entry remain separate.

The ticket starts with a blank quantity. Any positive whole quantity is permitted within the checks;
the optional ±10 buttons do not impose ten-share lots or portfolio sizing. The final button spells out
quantity, symbol and market execution. No generic second confirmation duplicates this review. A Hold
remains visible as context for a manual departure from the recommendation; it does not change gates
or acquire strategy attribution. The operator manages the resulting holding and any later sale.

The existing connection owner serializes authentication, context reads and the explicit submission.
The Core workflow obtains a unique Canadian CAD equity, current ask/bid and regular-session boundaries
from the captured `security.quoteV2` / `marketMetadata` contracts, plus broker cash, buying power,
holdings and all pending orders. It never treats a saved Delphi price or entered actual-fill price as
a current quote.

Initial execution rules are conservative implementation defaults:

- Require an OPEN quote within the broker's regular session and no more than two minutes old. Account,
  pending-order and quote retrieval checks expire after thirty seconds; future timestamps fail closed.
- Use the lower of current CAD cash and CAD buying power. The requested shares' estimated ask cost
  must fit. No sale proceeds, portfolio sizing or inferred margin are used. This estimate is not a
  guaranteed spending cap: the submitted order has quantity and no limit price.
- Block on any outstanding broker order, unresolved local order, unavailable storage/checks, or
  applicable existing global/Trading pause, current TFSA daily-loss hold or capital-review hold.
- Recheck instrument, account, cash, pending orders and financial holds on the final click. An increased
  reference ask updates the ticket and requires another deliberate Buy click. A changed identity,
  account or instrument requires a new review. A review is consumed at most once, including rejection.
- The SELECT-only safety repository retains the existing shared engine fence and serialized local
  TFSA account fence through dispatch. Paper gate profiles do not size or authorize manual orders.

Before transmission, flush the complete reviewed intent and stable request ID to a Windows CurrentUser
encrypted journal at `%LOCALAPPDATA%\TraderVI\Wealthsimple\ManualOrders`. Versioned records use atomic
replacement, immutable intent checks and a process-exclusive lease. Authentication secrets are absent.
The existing hash-only `OrderAttempts` marker remains an independent no-retransmission guard.

Recovery re-discovers the owned account and verifies the persisted identity/account/security and
quantity. It never re-resolves a ticker into a different security or submits on restart. Ambiguity,
timeouts and null/delayed reads retain the original intent; no replacement/retry button is offered.
An interrupted status save leaves the original intent available. Corrupt committed storage blocks buys.

Orders displays cumulative broker observations, keeping partial quantities, price, gross/net value,
fees and tax distinct. Repeated identical observations do not add shares. Missing cost details or
conflicting observations remain unresolved. Terminal orders with partial fills retain those fills.
An acknowledgement alone never creates a holding. The app checks an acknowledged order immediately;
the operator can use Check order status again, including after restart. Closing a ticket is not cancel.

## Boundaries and alternatives

Direct one-click grid submission was rejected because it obscures account and quantity review.
The later operator-approved [ADR-0077](0077-manual-holdings-market-sell.md) makes a sell-only exception
for the entire broker holding displayed on the row; this buy ticket continues to require quantity review.
Keeping actual-fill price and Ghost/Real mode in the broker ticket was rejected because those describe
different workflows. A side panel remains a possible presentation variant of the same service.

This journal covers TraderVI manual orders. It is not a complete account transaction ledger, deposit
history or strategy ownership system. No automatic `ActivePosition`/`TradeLog` insertion occurs;
the broker panel refreshes account facts after the ticket closes. Full accounting reconciliation,
portfolio execution, sales, limit/cancel/replace, cross-device recovery and an operator workflow for
resolving contradictory broker records remain deferred. Broker duplicate-ID semantics and native live
acceptance are still unverified. No strategy limit-order preference was replaced.

## Verification

Focused offline tests cover the protocol, connection wrapper, exact manual quantity, preflight checks,
consumed reviews, storage failure, timeout/restart recovery and cumulative fills. An isolated WPF build
and synthetic renders cover normal/narrow layouts, input/action bindings, selection-context preservation,
existing-fill separation and actual Windows encryption. Evidence is in ignored
`artifacts/manual-market-buy-ui-20260910/`. The operational app, SQL data, broker, models and complete
solution/SSDT build were not exercised. See project status for the final validation count and warnings.

## Review questions

1. Who chooses a manual order's quantity, and why does opening its ticket not submit it?
2. Why must both the full reviewed intent and the transmission marker survive a restart?
3. Why can a confirmed partial fill coexist with an unresolved order and no automatic sell policy?
