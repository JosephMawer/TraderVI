# ADR-0084: External broker trade synchronization

- **Status:** Implemented in source; installation and native acceptance pending.
- **Date:** 2026-09-24
- **Domains:** architecture, data-sources, risk-management, user-interface

## Problem and authority

The immediate problem is an external broker sale absent from TraderVI's saved ledger, leaving a
closed holding displayed as active. This belongs to real-account synchronization and supports the root
goal of accurate account state before deliberate strategy execution. The operator explicitly requested
automatic recording and propagation of trades noticed in Wealthsimple. This authorizes development of
the synchronization path, not new broker orders, strategy activation or a financial-history reset.

## Decision

Use the existing complete, owned-account reconciliation and reviewed account binding. While the desktop
is running and signed in, refresh every two minutes and on the existing Refresh action. No detached
scheduler is installed. Automatic continuation of an already reviewed binding saves the latest complete
reconciliation, which checks orders against holdings and a final activity checkpoint. It does not repeat
the manual approval workflow's second read: changing quoted market values can otherwise invalidate
identical trade facts. The exact observation is still revalidated against the binding, prior history and
ledger inside the saving transaction. Initial/manual approval retains its second-read requirement;
unchanged financial observations with a committed synchronization receipt need no further SQL snapshot.

After reviewed migration 040 is installed, append the import, missing real trade records, changed real
positions and an immutable synchronization receipt in one serializable transaction. Existing import
request recovery returns its original result after a lost response. Stable broker order IDs produce
deterministic trade IDs, and receipts prevent repeated application. The receipt preserves the original
reviewed ledger and the complete resulting ledger fingerprint (a hash of financial facts). Later reads
must reproduce that fingerprint before using the preserved baseline for import matching. This narrowly
supersedes ADR-0080's prohibition on projecting imports into TradeLog/ActivePosition; original binding,
imports, matched legacy trades and manual-edit conflict detection remain intact.

Reconstruct each supported resolved order once. The trade row is explicitly a cumulative order
aggregate using its last-fill time, never an asserted individual execution. Store broker gross, fees and
net exactly; the displayed average price is rounded to the ledger's four decimals. Realized profit is
net sale proceeds less proportional average cost. Partial sales retain position identity and remaining
book cost; a full close followed by a new purchase creates a distinct lifecycle. Additions preserve the
existing holding's identity, pick attribution, stops, highs and protection memory. New holdings require
verified Canadian listing identity, receive no strategy attribution and invent no historical high.
Closed rows retain last-held shares/book in accordance with existing history presentation; IsActive
determines ownership. Match every resulting open holding to the reconciled broker book before commit.

The first projection also records supported previously imported orders that lack legacy trade records;
this is historical completion from verified source facts, not a new fill at synchronization time.
Matched trades remain untouched. No paper account, risk reference, strategy assignment or immutable
account report is rewritten. New order attribution is BrokerConfirmedOrder, not a strategy recommendation.

After a save, refresh the account, tracked positions, trade history and portfolios. Old account cash/value
reports are shown as unavailable after ledger changes. New recommendation batches identify the exact
ledger they evaluated; old-ledger advice is hidden until reevaluation. Protective recommendations from
a fresh evaluation remain displayable even when cash/risk reports still need reconciliation. Existing
readiness checks continue to prevent new risk from stale account inputs.

## Boundaries and alternatives

Do not infer a sale from a disappeared holding: require complete order facts and matching holdings.
Do not mutate the original import binding or force-match a changed manual ledger. Reject overlapping
orders without known fill ordering, corrections, missing history, unresolved orders, fractions,
nonzero withholding and sub-cent amounts that the current ledger cannot faithfully represent.
Terminal cancelled/expired partial fills use their actual filled quantity. Still-working partially
filled orders remain pending until terminal reconciliation; incremental execution support is deferred.
Any blocked check leaves financial records unchanged and displays the reason. Authentication expiry,
offline hosts and broker publication lag prevent an immediate synchronization guarantee.

Broker cash and buying power remain visible in the broker panel. This change does not manufacture
session-opening values, reset loss holds or complete the broader broker-backed recommendation input
cutover. Background refresh is serialized with local manual order work and uses the existing exclusive
protected-journal lease. Independent app instances still serialize financial updates through SQL.

Migration 040 adds one immutable receipt table and trigger with foreign keys to imports and prior
receipts; it writes no trades by itself. It does not require or install migrations 035/039. Installation
requires separate explicit authority and a verified full backup under TraderDB/AGENTS.md. Native
broker confirmation of the operator's reported sale remains outstanding until that rollout.

## Validation

Focused offline import, projection and account-view tests pass. Isolated Release desktop and SSDT
schema builds pass. Detailed final counts and limits are in project status and private
`artifacts/broker-sync-20260924/`. Source tests do not establish installation or live database behavior.

## Review questions

1. Why is a missing holding insufficient evidence of a sale?
2. How do original-ledger and post-synchronization fingerprints prevent both duplicates and silent edits?
3. Why can a confirmed sale update positions without establishing new account risk references?
