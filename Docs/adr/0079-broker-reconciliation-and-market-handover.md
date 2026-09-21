---
cssclasses: [executive-brief]
---
# ADR-0079: Broker reconciliation and first market-order handover

- **Status:** Accepted first-version order and transition direction; account reconciliation and transition review implemented in source. SQL integration and automatic execution remain unfinished.
- **Date:** 2026-09-14 (Toronto)
- **Domains:** architecture, risk-management, market-microstructure, user-interface
- **Related:** ADR-0062, ADR-0073, ADR-0075, ADR-0076, ADR-0077

## Problem and direction

The immediate problem is joining the existing broker buy/sell functions to portfolio promotion.
This belongs to the real-account execution boundary and serves the root goal of one explicitly
selected strategy managing the permanent TFSA while paper accounts retain independent evidence.

The operator requested an implementation pass before the market open and accepted two bounded choices:

1. The first portfolio execution version uses market orders during regular market hours. Whole-share
   validation and existing risk checks remain. Limit orders are deferred, replacing the earlier
   unfinalized limit preference for this first version. Market orders have no guaranteed execution price.
2. Promotion will automatically submit the required non-overlap sells and wait for their confirmed
   fills and reconciliation before enabling new buys. Overlapping holdings retain their actual shares
   and book value. This is the accepted future execution contract, not an enabled order path today.

## Implemented reconciliation boundary

The account screen has an explicit **Reconcile account…** action. It reads the owned, selected TFSA's
activity feed to exhaustion and follows each discovered order's external ID to its cumulative fill
details. These read contracts are derived from the supplied buy/sell HAR. Order reads check identity,
account, instrument and side. Duplicate activity IDs, cursor loops, mismatched identities and a
200-page cap fail the whole read; they never produce a successful truncated history.

The reconstruction uses each order once. Confirmed buy net cost increases book value; partial sells
remove proportional average cost and preserve remaining shares; net sale proceeds less that cost
produce the displayed accounting result. A cancelled or expired partially filled order still has
real shares and costs. Repeated reads rebuild from cumulative totals rather than adding them again.
Completed round trips remain in history even when no shares remain.

Compare reconstructed quantities and book values with the fresh broker holdings by security ID.
Book-value comparisons allow at most one CAD cent for representation/rounding; quantities must agree
exactly. Fractional observations remain visible and block this whole-share handover. Missing purchases,
unsupported instruments/currencies or position-affecting activities, missing costs, overlapping orders
whose individual fill sequence is unknown, pending broker orders and unresolved local orders block a
matching result. Monetary inconsistencies and invalid/future timestamps also block it.

History, holdings and pending-order observations must be at most two minutes old; holdings are read
after history. This is a conservative review freshness policy, not an atomic broker snapshot or order
price guarantee. The workflow pins the connection revision and shares the manual-order semaphore and
exclusive journal directory lease. It cannot overlap a manual submission in this app or another app
instance using that directory. A replacement login invalidates the result.

After holdings and pending orders, reread the latest activity page and compare its identity/status/
quantity fingerprint with the first history page. A changed checkpoint fails without saving. This
detects reported activity changes during the read; it cannot guarantee atomicity or eliminate broker
publication lag and is not a replacement for final execution preflight.

Earlier saved orders and known manual-order observations cannot silently disappear, regress or change
financial facts. Conflicts remain recorded across later refreshes; there is no automatic correction
or reset path. A failed read saves no partial reconciliation result.

Completed observations are appended as Windows CurrentUser-protected files in the existing local
ManualOrders directory. Hashed identity/account prefixes avoid identifiers in filenames. Each write
flushes an encrypted temporary file before an atomic rename; earlier observations remain intact.
This is an observation journal, not the canonical SQL trading ledger.

## Integration and practical limits

**Reconcile account and prepare review** now runs the same reconciliation before showing the TFSA
transition. It rechecks broker holdings/cash and the source portfolio revision after the read; changed
inputs require a new review. The preview explains the accepted market-sell / reconcile / new-buy order.
It still cannot approve a broker strategy, place orders or change SQL positions.

`HoldingsMatch` describes an observed holdings comparison. It is not `CanTrade`, complete account
reconciliation, statistical strategy qualification, a protection baseline or execution authority.
An exhausted activity feed does not prove that the broker retains all lifetime history. Initial
missing zero-net historical round trips cannot be inferred from current holdings. Known local history
is cross-checked to catch omissions where evidence exists.

Cash, buying power and account value are fresh broker observations. This version does not reconstruct
all cash flows, settlement, income, capital adjustments, session-opening value or historical high-water
value. A pure cash transfer does not pretend to be a stock transfer. Unsupported stock events remain
explicit limitations. Historical order totals do not identify every individual fill: the saved
`FirstBuyCompletedAt` is the last-fill time of the first buy order in the current position cycle, not
an invented first-fill timestamp or a new entry at promotion.

No existing SQL tracked position or manual fill is overwritten. ADR-0062 recommendation approval's
pause/empty checks remain unchanged. Native order submission, the new history query and account
matching still require an explicitly authorized live validation. No HAR is replayed against a broker.

## Remaining implementation sequence

1. Establish an audited broker-to-SQL import and account binding, including mapping or resolving
   existing manually recorded Real history. Preserve fees, partial exits, real entry history and prior
   account risk holds. Agree correction/recovery and cash/settlement coverage before automatic use.
2. Define inherited-position protection ownership and initialization, exact intended-holdings evidence,
   account valuation baselines and automation evidence eligibility. Preserve the independent internal
   paper research rules; recommendation-only human approval does not silently become automation approval.
3. Implement a durable promotion transition and execution coordinator. Bind the exact approved strategy,
   reserve cash/shares, submit once, recover uncertain outcomes, exit non-overlaps before new buys and
   preserve management/protection of retained positions. No queued after-hours market submissions.
4. Verify native broker reads and explicitly authorized small orders, then review operational activation.

## Acceptance and validation

Focused offline tests cover reconstruction, fee-bearing partial exits, cancelled partial fills,
round trips, cumulative replay, historical conflicts, identity/pagination failures, stale observations,
fractional inventory, manual-order coordination and encrypted restart recovery. WPF builds and a
synthetic renderer check the matching, blocked, empty and failed screens at ordinary and narrow widths.
Current counts and operational limits are recorded in project-status.md and ignored evidence under
`artifacts/broker-reconciliation-20260914/`.

## Review questions

1. Why is a cumulative order total applied once rather than once per status check?
2. Why can matching broker holdings still be insufficient to activate a strategy?
3. Which existing holdings transfer at promotion, and when can new buys begin?
