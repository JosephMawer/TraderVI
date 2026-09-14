# ADR-0076: Wealthsimple market-sell protocol and shared attempt protection

- **Status:** Implemented with offline tests; connected to the manual holdings workflow by [ADR-0077](0077-manual-holdings-market-sell.md). Native submission remains unverified.
- **Date:** 2026-09-11
- **Domains:** architecture, risk-management, market-microstructure
- **Related:** [ADR-0074](0074-wealthsimple-market-buy-protocol.md), [ADR-0075](0075-manual-market-buy-ticket.md)

## Context

The immediate problem is adding a market sell from the operator's FEC buy/sell capture and checking
the existing buy request. The parent problem is reliable broker execution and reconciliation. The
root goal remains an independently funded real TFSA managed through deliberately approved rules.

The supplied HAR contains two successful `SoOrdersOrderCreate` mutations and separate filled-order
details. A HAR is recorded network evidence, not trading authority or instructions to replay requests.
The original stays unchanged in Downloads. Test fixtures retain the public schema and use synthetic
identifiers, quantities, financial values and timestamps; no credentials or private account data are copied.

## Decision

Add `WealthsimpleMarketSell`, `SubmitMarketSellAsync` and `ReadMarketSellOrderAsync` to the current
authenticated client. Market sells use the captured seven fields: verified `canonicalAccountId`,
caller-retained `order-{uuid}` external ID, `executionType=MARKET`, `orderType=SELL_QUANTITY`, positive
integer `quantity`, verified `securityId`, and `timeInForce=DAY`. There is no limit-price field or
guaranteed minimum proceeds. Whole shares are required, with no ten-share rounding or sell-all flag.

Share transport, response parsing and the durable attempt fence between buys and sells. An attempt
fence is a flushed local marker that prevents retransmission after a crash or uncertain response.
Keep its existing identity/external-ID filename and version compatible with prior buy markers;
include the side-specific order type in its fingerprint. Changing side, account or quantity under
the same request ID cannot bypass an existing marker, including an empty marker left by a crash.
Neither HTTP errors, cancellation, DPoP nonce challenges nor missing lookups permit mutation retries.

Recheck CAD equity identity, Canadian venue, active status, market-order support and broker eligibility
before submission. Buys require `buyable`; sells require `sellable`, independently of buyability.
These are instrument checks, not proof that the account has enough unreserved shares to sell.

Creation acknowledges an order, not its execution. Query the original external ID on branch `TR`
and validate the returned identity, account, security, side, order type, execution type, currency,
time in force and submitted quantity. Preserve actual cumulative quantity, average price, gross/net
proceeds, fees, withholding and timestamps. Partial fills may have fractional cumulative quantities
even though the submitted quantity is whole. Missing fill quantity stays unknown; repeated reads do
not add new shares sold. A `FILLED` status requires the full submitted quantity and actual price/time.

The existing buy API and manual-buy observation serialization remain unchanged through thin adapters.
Both directions use the shared protocol; synthetic fixtures independently reproduce the capture's
request/acknowledgement/detail contract and validate the previous buy implementation.

## Consequences and boundaries

This change implements the broker client piece. A sell caller must retain its reviewed intent and
request ID durably, verify current available holdings and outstanding orders, and supply explicit
trading authority. The protocol client does not reserve shares or provide a complete sell journal.
The WPF manual-buy ticket remains buy-only; sell UI, protected sell recovery workflow, automatic exits,
position/cash reconciliation and cancel/replace remain separate work. No strategy rule or database
contract changes. No native order was submitted as validation.

## Review questions

1. Why must sell eligibility be independent of whether new buys are allowed?
2. Why must changing an uncertain buy into a sell with the same request ID remain blocked?
3. Which checks still belong to a sell workflow before it calls the protocol client?
