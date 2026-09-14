---
cssclasses: [executive-brief]
---
# ADR-0072: Read-only Wealthsimple TFSA landing page

- **Status:** Implemented; live account discovery, balances, returns and holdings verified.
- **Date:** 2026-09-09 (Toronto; September 10 UTC)
- **Domains:** architecture

> [!summary] Decision and consequence
> Load the open self-directed TFSA through the authenticated native client and display a broker snapshot on the main Trading page. Keep local trading records and recommendation authority separate until reconciliation is designed.

## Context

The immediate problem is replacing unavailable account totals with actual TFSA data. The parent
problem is direct broker integration after native sign-in. The root goal is an account-aware trading
application with separately designed and authorized order execution. The operator confirmed native
login and requested account data on the landing page. Their broker groups self-directed and managed
TFSAs together; selecting the group total would mix unrelated accounts.

## Decision

Use fixed GraphQL queries at `https://my.wealthsimple.com/graphql`. GraphQL is the provider's query
interface for selecting account fields. The token-info response supplies the identity; tokens stay
inside the existing client. Serialize reads with authentication and refresh. Do not expose arbitrary
URLs, query text, tokens or account IDs through the desktop interface.

Discover all account pages and admit only open, unarchived, unclosed accounts with type `tfsa`, branch
`TR`, and unified type `SELF_DIRECTED_TFSA`. Auto-select exactly one match; multiple eligible accounts
require a choice. The account object must belong to this client's verified identity and catalog.

Read combined CAD value, net deposits, provider return and reference date, CAD cash and buying power,
and all pages of positions scoped to the selected account. Validate identity/account IDs, position
membership, unique cursors/positions, OK status and complete stable total count. GraphQL errors,
including errors alongside HTTP 200 partial data, fail the refresh. Never fabricate zero for missing
financial values. Quantities are decimal; each holding amount retains its returned currency. No FX
conversion is inferred or local cost basis manufactured. Non-stock instruments remain visible with a
type label if stock metadata is absent.

The main page loads once after sign-in and on explicit Refresh. It labels retrieval time separately
from provider return time, and labels retained snapshots after failures or five minutes of age.
Disconnect/replacement login clears financial data; a late result cannot repopulate another session.
HTTP 401 or standardized GraphQL authentication errors retire the session and its saved copy. A 403
is an account-read refusal, not proof that the token itself is invalid. There is no automatic retry
loop for account failures or rate limits.

The existing operator-reported account, recorded holdings and active strategy controls remain in an
expander below the broker snapshot. SQL report inputs, recorded fills, strategy assignment and
recommendation evaluation do not consume this new data. Reading balances is not reconciliation:
reconciliation must establish how broker holdings, fills, cash movements and pending orders map to
TraderVI records before they can influence risk or execution.

## Validation and limitations

An isolated process used the operator's Windows-encrypted session with exclusive ownership. Live
account discovery found one eligible self-directed TFSA, and financials, cash/buying power, returns
and paginated-position parsing succeeded. The modified query documents work without copied browser
operation hashes, cookies or a visible browser. No private account values or tokens were printed.
The operator independently confirmed native sign-in. Live refresh rotation remains separately to verify.

Focused tests cover the protocol, account separation, decimal/currency handling, pagination failures,
missing values, session expiry, error sanitization and display races. WPF builds and synthetic renders
verify the UI without launching the operational dashboard. See project status for final counts.

These private API selections can change. Separate queries are not an atomic valuation or executable
quote. No contribution-room calculation, pending-order reconciliation, SQL write, trade submission,
model/market collector, commit, deployment or complete solution/SSDT validation is part of this step.

References: [native sign-in](0071-native-wealthsimple-login.md),
[protocol and usage notes](../concepts/wealthsimple-authentication.md).
