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
