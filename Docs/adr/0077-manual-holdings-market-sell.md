# ADR-0077: Immediate manual market sell from broker holdings

- **Status:** Implemented and verified offline; native submission remains unverified. Limit ticket is a non-submitting preview.
- **Date:** 2026-09-13
- **Domains:** architecture, user-interface, risk-management, market-microstructure
- **Related:** [ADR-0075](0075-manual-market-buy-ticket.md), [ADR-0076](0076-wealthsimple-market-sell-protocol.md)

## Context

The immediate problem is connecting the market-sell client to the main account's Wealthsimple holdings
grid. The parent problem is durable manual broker execution and recovery. The root goal remains
deliberate real-account trading with a clear boundary between manual orders and automatic strategies.

The operator explicitly chose **sell the entire holding immediately** on one click, without a quantity
ticket. They also requested a limit-sell dialog and recognized that its broker protocol might be missing.
The supplied HAR verifies market buy/sell only; a limit mutation must not be inferred from it.

## Decision

Add **Market sell all** and **Limit sell…** to each supported CAD equity row, beside its share count.
Market sell all is the explicit submission action. The row, tooltip and nearby explanation make the
entire displayed quantity and market execution clear; there is no second confirmation or quantity
dialog. This supersedes ADR-0075's rejection of one-click submission for this sell-only workflow.
Buy continues to use its quantity ticket. Selecting, refreshing or restoring a row never submits.

Pin the account, broker security ID, exact displayed quantity and connection revision at the click.
Require fresh, matching holdings, an independently sellable Canadian security, no pending account
orders, and a current two-sided CAD quote during the regular session. A changed quantity stops the
attempt and requires a fresh explicit click after refresh. Closed-market clicks never queue orders.
No whole-share rounding occurs: fractional holdings must be sold through Wealthsimple. Cash limits,
buying pauses and new-risk financial holds are buy controls; the sell workflow does not acquire the
buy-only SQL safety lease or restrict exits based on available cash.

Market execution has no guaranteed price or minimum proceeds. The current bid is checked as evidence,
not used as a limit. The broker subsequently reports actual execution price, cumulative filled shares,
gross/net proceeds, fees and withholding. Acknowledgement is never displayed as a completed sale.

Before transmission, atomically save the complete intent and request ID in Windows CurrentUser
encrypted storage. Sell files use `.sell` envelopes in the existing `ManualOrders` directory; `.order`
buy files and their version-1 format remain unchanged. Both share the process-exclusive file lease and
one submission semaphore (an asynchronous lock). Both workflows block on unresolved buys or sells.
The existing independent `OrderAttempts` marker continues to prevent retransmission by request ID.

An unresolved sell reserves the holding locally by blocking another manual order. After a terminal
broker result, retain that reservation until a fresh owned-account read confirms exactly the expected
remaining quantity and no pending orders. This includes partially filled cancellations/expiries.
A changed terminal result or regressing cumulative quantity becomes a persistent conflict, never a
new authorization. Preserve fill observations even when the following holdings read fails.
These local protections do not reserve shares at the broker or coordinate orders entered on another device.

Open the results window from the explicit command and check an acknowledged order once. **Manual sells…**
reopens protected history after restart; **Check sell status** only reads and records observations.
Opening/loading the window never transmits. Refresh the main account after submission/status checks.
During submission, disable other manual actions and prevent closing the order/app window. Closing an
idle results window does not cancel an order. No automatic SQL tracked-position exit is recorded.

**Limit sell…** opens an editable quantity/minimum-CAD-price preview with estimated proceeds and a
clearly disabled submission button. It has no broker mutation path. A successful operator-supplied
limit-sell capture and reviewed order lifecycle are required before enabling placement.

## Verification and remaining work

293 focused tests pass, including existing buy/protocol/transition tests and new sell workflow,
identity, inventory, storage, stale-row and conflict cases. The isolated WPF build succeeds. Sixteen
synthetic renders verify normal/narrow holdings, empty/filled/partial/uncertain/closed results and the
limit preview; command routing, bindings and actual Windows-encrypted sell recovery also pass.
Evidence is under ignored `artifacts/manual-market-sell-ui-20260913/`.

Existing compiler warnings and cached dependency advisories remain separate. No fresh vulnerability
audit, full solution/SSDT build, live broker call, SQL operation, operational application start/restart,
strategy change or commit ran. Native sell acceptance, limit placement, cancel/replace, cross-device
coordination and complete position/cash/strategy reconciliation remain unfinished.

## Review questions

1. What exactly does one Market sell all click authorize when the live holding quantity has changed?
2. Why can a filled sell remain locally reserved until the holdings snapshot catches up?
3. Why is the limit ticket editable while its submission action remains disabled?
