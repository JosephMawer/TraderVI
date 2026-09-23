# Synthetic market-order protocol fixtures

The September 11 operator-supplied FEC buy/sell HAR establishes these request shapes.
Zero-based HAR entries 16/36 contain buy creation/details; 173/195 contain sell creation/details.
Both use the same mutation, seven input fields and acknowledgement structure. Separate details
report `BUY`/`BUY_QUANTITY` or `SELL`/`SELL_QUANTITY`, `MARKET`, `DAY`, CAD and cumulative fills.

Only the public query, schema fields, enum values and nullability are retained. All identifiers,
quantities, prices, financial values and timestamps are synthetic. Request/response headers,
cookies, credentials and other account data are omitted. Fixtures contain the detail fields the
client consumes, not a full activity response. They are served exclusively by an in-memory handler.
The original HAR remains unchanged in Downloads and is never replayed or needed to run tests.

`history-posted-sell.json` is a synthetic regression derived from the September 16 TFSA activity
capture: completed/FILLED activity corresponds to POSTED, fully filled STOP_LIMIT order details.
The fixture retains schema and status distinctions, not the operator's data. Its securityType
field represents the history client's selection of the captured Security schema. See
`Docs/concepts/wealthsimple-account-history-contracts.md` for other reusable contracts and limits.

`history-expired-empty.json` is an independent synthetic case for the later native screenshot's
EXPIRED / quantity missing / amounts absent / timestamp absent diagnostic. No expanded empty expired
response was present in the HAR. The fixture additionally sets the captured `averageFillPrice` field
to null, which the new reader requires; this condition still needs native verification. Its activity
quantity deliberately differs from executed quantity. Tests retain the empty order and null-source
flag, preserve partial fills, and reject contradictory or missing execution fields. It contains no
operator identities, amounts, timestamps, credentials or response headers.

The next native retry reported CANCELLED with null quantity and absent amounts, timestamp and average
price. Tests derive synthetic CANCELLED and CANCELED variants from this fixture, preserve those raw
statuses and apply the same contradictory/missing-field checks. The latter spelling is an existing
supported status alias, not a separately captured native response. Partial-fill, saved-import and
prior-history regressions cover all three statuses. A complete native import remains unverified.
