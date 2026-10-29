# ADR-0088: Source symbol aliases for monitoring

- **Status:** Original and October 27 paths installed; October 29 tracked-purchase extension implemented, installation pending.
- **Date:** 2026-09-28
- **Domains:** architecture, data-sources

## Problem

A broker listing symbol can differ from the app's stock identifier. Passing it directly into Live
session membership violates the Symbols foreign key and prevents comparison-source adoption.
Restoring those monitoring inputs supports the AI Momentum integration without changing account facts.

## Decision

Keep a small `SymbolAliases` table: source, alias, canonical symbol, exchange MIC (exchange identifier),
and verification time. One source/alias can identify only one existing Symbols row. Use explicit reviewed
listing evidence; never strip suffixes automatically or silently create a second stock reference.
A separate row per alias is easier to validate than a comma-separated column and supports future names.

Resolve Real holdings through the Broker aliases when `DelphiLiveHoldingSource` reads them for session
creation and subsequent observation synchronization. Paper holdings already use app identifiers.
Preserve every owning record ID and observation/action flag. Reject unresolved or conflicting references
with a clear message; do not drop holdings or transfer trading authority. Resolution is a single lookup,
not a chain. The session foreign key stays enforced.

Original positions, trades, broker imports, synchronization fingerprints, risk state and historical
observations keep their original contents. Alias resolution is not a ledger migration. A future editor
may expose aliases, but no new UI or automatic alias-discovery service is needed for this fix.

Migration 041 creates the table and seeds the one reviewed alias after checking saved CAD/XTSE broker
listing evidence and the existing canonical stock. The target's missing exchange code is disclosed in
the script; it is not silently repaired. New aliases require their own reviewed evidence. Installation
requires a verified backup and authorization for the exact script. Restart and prospective adoption
must be verified separately; missed monitoring intervals cannot be backdated into coverage.

## Validation

### October 27 managed-purchase repair

The operator authorizes repair of collection/execution after the first managed purchase. For a managed
holding, the original reconciled purchase intent already contains its market symbol and security ID.
Use that exact identity together with the saved CAD/EQUITY/XTSE listing to recognize the broker's
`.TO` spelling. This extends the original manual-only alias boundary for owned purchases; it does not
strip arbitrary suffixes, discover securities or grant authority to outside holdings. Missing, conflicting,
wrong-security or wrong-exchange evidence remains blocked. Unmanaged legacy references still require
explicit reviewed aliases; `BrokerManaged` and `BrokerPending` now use those aliases too.

Only derived collection/market-evaluation references change. Keep broker holdings, original orders,
financial fingerprints, protection IDs, costs and historical subscriptions unchanged. Older subscriptions
can obtain the corrected view from the verified immutable current broker head, with an unchanged-revision
check; no migration or rewritten history is needed. New subscriptions publish the market reference.
Valuation, quotes, independent confirmation, duplicate-position checks and close/open risk evidence use
the same correspondence. Recommendations and owned protection return to the original broker name.
The one-minute successor and all entry thresholds remain unchanged.

### October 29 tracked-purchase observation extension

The independent broker observation already uses the verified market symbol, but `ActivePosition`
also contributes a tracked Real observation with the broker ledger's original name and a different
position ID. Those duplicate observation paths caused a new unresolved name after each new purchase.

For an unresolved tracked Real reference only, use the verified current broker head matching the
collection subscription's binding, session, revision and contents. Reuse the original reconciled BUY,
security/listing checks and the trade projector's deterministic tracked-position identity. Match both
that exact position ID and broker spelling before deriving the market reference. Do not write an alias,
change the ledger, infer arbitrary suffixes or reuse proof for another owner/position. Missing listing,
changed head, ambiguous ownership, conflicting alias or absent canonical stock still fails closed.
Legacy/unmanaged references retain the reviewed manual-alias route.

Already canonical or manually mapped holdings retain their existing lookup path; they do not read
another broker archive. The fallback reads only the current verified head, never the full revision
history. It preserves every observation owner, record ID and action flag and creates no financial
authority. Installation and prospective native verification are separate from source tests.

Focused tests cover canonical names, aliases, ownership preservation, wrong sources, missing targets,
conflicts and rejection of alias chains. Build/test results and installation status belong to project
status. Authorized installation and restart on September 28 preserved the checked account records,
resolved the tracked holding to its canonical monitoring symbol and restored adoption for all eight
Shadow comparison accounts. This does not qualify AI valuation readers or enable AI execution.
