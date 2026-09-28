# ADR-0088: Source symbol aliases for monitoring

- **Status:** Accepted; implementation prepared, installation and runtime verification pending.
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

Focused tests cover canonical names, aliases, ownership preservation, wrong sources, missing targets,
conflicts and rejection of alias chains. Build/test results and installation status belong to project
status. This does not qualify AI valuation readers or enable AI execution.
