# Wealthsimple account-history capture contracts

- **Domains:** architecture, data-pipeline, risk-management
- **Related ADRs:** ADR-0079, ADR-0080
- **Evidence:** operator-supplied `my.wealthsimple_tfsa_activity.com.har`, inspected locally September 16, 2026.

## What this capture establishes

**September 21 follow-up:** [ADR-0083](../adr/0083-opening-account-values-and-shared-engine-hosting.md)
records a local check of the ONE_DAY/REGULAR responses: 79 CAD points at five-minute intervals from
09:30 to 16:00 Toronto, with offsets preserved. The shared client now implements the daily-value read
and exact timestamp selection, tested offline. Native morning availability and compatibility with the
chosen strategy's valuation still need verification.

**October 18 display follow-up:** two captured `ONE_DAY` / `REGULAR` responses request
`includeSimpleReturns=true`; each has 79 native amount/rate/referenceDateTime results, all CAD and
referenced to previous close. `ReadTfsaDailyPerformanceAsync` now requests this fixed subset for
Trading's performance card through the existing desktop connection/refresh owner. It preserves the
latest point's timestamp, rejects ambiguous scope/references/times/currency and never falls back to
an older complete return. It does not calculate earnings from net deposits or alter risk baselines.
Current service availability and operator comparison remain unverified.

The capture contains the account activity feed, one expanded historical order, current account
financials, several account-chart ranges and supporting security/account queries. It contains no
order-submission mutation. It is evidence of these responses, not a complete broker API specification
or proof that the captured account history is exhaustive. Some unfiltered activity pages have
`hasNextPage: true`; other captured pages have date, type, security or status filters.

Only schema fields, operation names, enums and structural facts are recorded here. Credentials,
headers, account/order/security identifiers, actual amounts and dates stay in the original HAR.
The independent test fixture uses invented identities, quantities, amounts and timestamps.

## Confirmed client correction

An activity entry identifies a completed sell as `status: FILLED`, `unifiedStatus: COMPLETED`.
Its expanded `orderServiceExtendedOrderByExternalId` response uses `status: POSTED` and reports
the full submitted quantity filled, complete cumulative financial amounts and a last-fill timestamp.
The captured execution type is `STOP_LIMIT`: historical imports must include existing order types
even though new portfolio execution is planned to use market orders.

The history model now accepts `POSTED` as a completed order status, with the same full-quantity and
complete-financial-fact checks as `FILLED`. It preserves the actual returned status. A later observation
may advance `FILLED` to `POSTED` only when every other saved order fact is identical. Earlier observations
remain unchanged; regression, altered amounts, changed identities or changed timestamps still block.
This also permits the history cross-check to match a posted broker order against a filled manual journal.
It does not change order-submission code or enable new limit/stop-limit placement.

## Reusable read contracts

| Captured operation | Useful fields or behavior | Client use and remaining limits |
|---|---|---|
| `FetchActivityFeedItems` | Account-scoped feed, cursor/pageInfo, type/subType, nullable status, unifiedStatus, order references, quantity, currency, occurredAt; filters include accountIds, types, dates and unifiedStatuses. | Existing history discovery. Activity quantity is not proof of executed quantity; cancellation/expiry must not be filtered out to make reconciliation pass. Exhaust account history without a completed-only filter. |
| `FetchOrderServiceExtendedOrderActivityDetails` | Owned order identity, side/type, submitted and filled quantities, average price, gross/net/fees/tax, timestamps and status. | Existing order-detail reader; `POSTED` support is implemented. Totals are cumulative, not individual fills. The capture does not contain an expanded null-quantity order. |
| `FetchOrderFinancials` | `orderFinancials(orderId)` with realizedReturn in a requested currency. | Candidate for comparing broker-reported realised performance. It is not a substitute for quantity, costs, fees or fill timestamps. |
| `FetchAccountCurrentFinancials` | `account.financials.current`: netLiquidationValueV2, netDepositsV2, totalDepositsV2, totalWithdrawalsV2, simpleReturns and reference/as-of fields. | Candidate for cash-flow/account-baseline validation. Aggregated totals do not establish all transaction or settlement dates. |
| `FetchAccountGraphData` | Previous close and dated account-value/net-deposit points under currentCombined.graphData; optional simpleReturns. | Candidate for account baseline evidence. ONE_DAY includes previousClose in this capture; broader ranges do not. Validate account, session/date, currency and coverage before using any point. |
| `FetchAccountCombinedFinancials`, `FetchAccountCombinedNlv`, `FetchAccountCombinedReturns` | Combined value and return views. | Corroborates current-account reads. Do not mix combined/current series without defining their scope. |
| `FetchTradingBalanceBuyingPower` | Currency-specific cash and buying power; optional security-specific buying power. | Existing cash/buying-power reader. This response does not establish a complete settlement schedule. |
| `FetchAccountHasPendingDeposits` | Account-scoped deposit count with a status filter. | Candidate for showing pending funding. A pending deposit is not automatically available trading cash. |
| `FetchIdentityPositions` | Account-filtered positions with instrument identity and financials. | Corroborates existing holding discovery. Keep explicit account ownership and complete pagination checks. |
| `FetchMarketStatus`, `FetchSecurityQuoteV2`, `FetchSecurityMarketData` | Instrument-specific quote and market-state fields. | Supports quote/session validation; freshness and instrument matching remain necessary. |
| `FetchOrderValidationConfigs`, `FetchMarketBuffer` | Broker validation categories/configuration and a market-buffer query. | Future ticket/eligibility work. Configuration is not order acceptance or authority to weaken TraderVI risk limits. |
| `FetchLatestExchangeRate`, `FetchSecurityDividendDetails` | Exchange-rate bid/ask/mid; dividend dates, frequency and yield fields. | Future display and cash-event support; no automatic FX conversion or new strategy inputs were added. |

Account-chart ranges observed: ONE_DAY, ONE_WEEK, ONE_MONTH, THREE_MONTHS, SIX_MONTHS, YEAR_TO_DATE,
ONE_YEAR and ALL, with REGULAR session requests. An ALL chart can be sampled; its maximum is not
automatically the lifetime closing high-water value required by the strategy's risk controls. The
cash/settlement and baseline design remains open. Except for the September 21 daily graph reader
and October 18 display-only native daily-return reader,
these extra read contracts are documented for future implementation; none is an active strategy input yet.

Public query texts, variable names (no values) and response envelope fields for twelve relevant
operations are retained in ignored evidence under
`artifacts/broker-import-20260916/activity-capture/query-contracts.json`.

## Empty expired/cancelled-order follow-up

The later native diagnostic screenshot identifies `EXPIRED` with null `fillQuantity`, absent fill
amounts and no fill timestamp. The HAR itself still contains no expanded empty expired order. The
client now queries the captured `averageFillPrice` field too. The next retry identified CANCELLED with
that field absent too. The narrow compatibility rule now covers EXPIRED, CANCELLED and the already
supported CANCELED spelling: all selected execution fields must exist and be JSON null, and no activity for that
order may report FILLED, POSTED or PARTIALLY_FILLED.

For this representation the saved order has zero accounting quantity and `FillQuantityWasNull: true`.
The preview says no fills were reported and that the broker quantity was blank. This preserves the
source distinction instead of claiming the broker explicitly returned zero. The order stays in history
and contributes no shares, cash or proceeds. Actual partial fills remain actual fills. Missing fields,
non-null execution evidence, other statuses or contradictions with previously observed fills still
block. Holdings and book values must still reconcile before the import can be saved.

This is a compatibility interpretation of the empty response, not proof that arbitrary missing data
means no execution. The cancellation screenshot reports absent average price but the expanded raw
response was not captured. The independent fixture and status variants are synthetic. Unit tests and
offline WPF rendering verify the boundary and provenance; a rebuilt native preview must still verify
complete import success. No cancelled/expired test order is needed.

## Why history and a starting baseline are different

The current importer reconstructs holdings from past orders and matches the existing recorded ledger.
That supports continuity and review but is not the only possible way to initialize live trading.
A proposed simpler first version starts at an explicitly verified current-account baseline: real
holdings and costs, cash/buying power, resolved outstanding orders and preserved risk/protection state.
Subsequent orders, fills and external account changes still need synchronization. Older history can
remain optional reporting evidence; a starting snapshot cannot manufacture missing entry dates or
reset existing account loss holds. This alternative is not implemented yet and its baseline rules
remain open. Neither importing history nor establishing a baseline alone activates a strategy.

## Native preview mismatch diagnosis

The expanded replacement HAR supplied later September 16 contains 32 distinct order references, the
same set as the app's saved reconciliation snapshot, but only one expanded historical order response.
Matching counts and references do not by themselves establish exhaustive lifetime history. The
protected snapshot supplies the detail facts that the native client already read. A read-only SQL
comparison supplies the existing recorded ledger; neither source was modified or replayed.

Three independent causes were identified. First, two completed ETF orders failed the validator's EQUITY-only
instrument rule; their fill facts otherwise pass the inspected checks. Historical accounting scope
and eligibility for new strategy trades are separate decisions. Second, an existing recorded purchase
has a unique candidate differing only by the `.TO` suffix; exact-symbol matching rejects it, and no
blanket suffix removal is justified by this one case. Third, a recorded sale disagrees with the broker
on gross and net amounts, despite matching date, quantity and fees. The disagreement exceeds one-cent
rounding and needs an explicit correction/reference decision, not a relaxed financial tolerance.

The saved positions balance with their own recorded trades. `ValidateLegacy` originally reconstructed
positions from successfully broker-matched trades, so a failed sale match can also report an apparent
remaining-share/book-value disagreement. That secondary message is not independent evidence that
the stored position is wrong. No private financial values or identifiers are retained in these notes.

The follow-up now supports historical CAD whole-share ETF fills while leaving current-holding and
new-order eligibility unchanged. Nested security listing evidence permits a unique venue-specific
Canadian suffix alias and preserves both source spellings. The supplied HAR confirms CAD/XTSE/EQUITY
listing data for the same security behind the observed suffix case. Evidence and comparisons are
included in the review fingerprint; missing identity evidence or financial differences still block.

The preview now displays recorded and broker financial values for a unique otherwise-matching order.
Position checks use the complete recorded ledger, then complete matched history; an unmatched sale
cannot create a false partial-ledger warning. These changes do not resolve the actual financial
disagreement: neither source was edited, and a native successful import remains outstanding.

The first native import later committed successfully. Its readback error exposed a separate storage
issue: an exact timestamp can lose precision in the SQL parameter before reaching a high-precision
column. Bind import timestamps as `DateTime2`. Historical receipts accept only the precise old SQL
rounding representation; the reader still verifies account identity and financial content and preserves
the full timestamp in the immutable JSON. A successful save followed by a failed read is not evidence
that the data should be deleted or imported again.

## Review questions

1. Why can one completed trade be FILLED in activity but POSTED in expanded order details?
2. Why is a null executed quantity different from an explicit zero?
3. Why is the maximum of an account chart insufficient by itself for a risk baseline?
