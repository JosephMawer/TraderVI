# ADR-0074: Wealthsimple market-buy protocol and attempt recovery

- **Status:** Client implementation with offline tests; connected to the separate manual WPF ticket by ADR-0075. Native live submission remains unverified; portfolio execution remains unfinished.
- **Date:** 2026-09-10
- **Domains:** architecture, risk-management, market-microstructure
- **Related:** ADR-0069, ADR-0072, ADR-0073, [ADR-0075](0075-manual-market-buy-ticket.md)

## Context

The immediate problem is implementing a market buy from an operator-supplied successful browser
capture. The parent problem is reliable broker execution and reconciliation. The root goal remains
an independently funded, permanent real TFSA using an explicitly promoted strategy's rules.

The operator supplied a completed whole-share equity market buy and requested that GraphQL responses
be retained. The original capture remains unchanged outside the repository. Ignored local inspection
copies preserve financial values while redacting credentials and identifiers. Checked-in tests use
synthetic identities and financial values; no captured order is replayed.

## Decision

Use the observed `SoOrdersOrderCreate` mutation and `SoOrders_CreateOrderInput`, with
`executionType=MARKET`, `orderType=BUY_QUANTITY`, an integer `quantity`, `timeInForce=DAY`, the owned
self-directed `canonicalAccountId`, a resolved Canadian `securityId`, and a new `order-{uuid}`
`externalId`. Do not copy a captured order ID into a new order. There is no limit price in this contract.
A market order does not promise a maximum fill price or total cost.

The creation response contains a different broker `orderId` and `createdAt`. It acknowledges creation;
it does not prove execution. Read `orderServiceExtendedOrderByExternalId(branchId: "TR", externalId: …)`
separately and validate identity, account, security, side, order type and submitted quantity. Keep
cumulative filled quantity, average price, actual value/net value, fees/tax and timestamps as separate
observations. Missing fill quantity remains unknown. Repeated reads must never be applied as new
incremental fills. Complete broker-to-local reconciliation remains a separate workflow.

Before transmission, create and flush an exclusive, write-once attempt marker under
`%LOCALAPPDATA%\TraderVI\Wealthsimple\OrderAttempts`. It contains only SHA-256 hashes, not credentials,
account IDs, quantities or financial values. The marker key covers the verified identity and external
request ID, so changing quantity/account under the same request ID cannot bypass it. Existing markers,
including empty markers left by a crash, prevent resubmission. Storage failure prevents transmission.
This is a local duplicate-transmission guard, not a complete order journal or proof of broker idempotency.
The caller must durably retain the original reviewed intent and request ID for recovery.

Do not automatically retry a mutation after a timeout, cancellation, ambiguous HTTP/GraphQL response,
or DPoP nonce challenge. After transmission becomes possible, uncertain outcomes require a read by
external ID and reconciliation. A null lookup or delayed feed is not permission to submit a replacement.
Successful read/auth nonce handling remains unchanged.

Resolve ticker suffixes only inside a verified CAD Canadian venue: `.TO` for XTSE and `.V` for XTSX.
Recheck current buyability and market-order support before attempting submission. Keep fractional
orders rejected at construction. Do not replace the preferred limit-order strategy policy with market
orders because a market-buy demonstration is now available.

## Consequences and remaining work

The public client method is an explicit brokerage mutation. It is not called by account refresh,
promotion review, WPF timers or Delphi Live. The caller remains responsible for reviewed trading
authority, fresh account/cash/risk checks, persisted intent and operational activation. No UI path is
enabled by this change. Limit buy/sell, cancel/replace, complete fill/cash reconciliation, durable
strategy authority and real-account monitoring remain unfinished.

Native authenticated mutation acceptance and broker duplicate-ID behavior are unverified. The
operator's browser capture establishes the successful wire contract; offline tests establish request
construction, scoping, parsing and local attempt handling. Response headers/cookies from the HAR are
not authentication inputs to this implementation.

## Review questions

1. Why is an acknowledged order different from a confirmed fill?
2. Why must a timeout retain its original request ID without automatic resubmission?
3. Why does a market-buy implementation not replace the strategy's limit-order preference?
