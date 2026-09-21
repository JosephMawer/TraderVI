# ADR-0080: Reviewed broker history import

**Date:** 2026-09-16  
**Status:** Implemented; migration and native linked refresh verified September 16. First-link review is the
provisional initial workflow, not a separately requested permanent requirement.  
**Tags:** architecture, user-interface, risk-management

## Context

The immediate task is a concrete import preview and repeatable SQL updates. This supports connecting
portfolio promotion to real orders, with the root goal of an explicitly selected strategy managing the
permanent TFSA while preserving independent paper evidence and continuous real history.

ADR-0079 reconstructs holdings from broker order history and saves protected local observations. Those
observations are separate from the manually recorded SQL account used by the recommendation engine.
Existing TradeLog records lack broker order identities. The captured broker response reports cumulative
order totals and a last-fill timestamp, not every individual fill. Blindly appending those totals as
individual trades can duplicate purchases and manufacture execution timing. Rewriting ActivePosition
can also lose position identity and protection history.

The operator authorized continued background implementation on September 16. Use isolated output
directories while the operational WPF instance collects data. September 15 relaxed the earlier
no-restart constraint: a restart can be coordinated when tested changes are useful. Neither that nor
the background implementation request authorizes a database migration or a live broker trade.

## Decision and implemented scope

Add **Import account history…** to the main Wealthsimple account panel. It runs the existing owned-account
reconciliation, reads the legacy TFSA ledger and previews proposed matches and new broker records.
The first save explicitly links this broker identity/account to TraderVI's permanent `TFSA` label.
An unlinked account never writes merely because its window opens. Once linked, opening the import or
using **Refresh and update saved history** imports supported observations after fresh checks. This is
operator-triggered synchronization; there is no unattended polling schedule in this milestone.

Use two new append-only tables, BrokerAccountImportBinding and BrokerAccountImport. The binding pins
the broker identity/account hash and legacy ledger fingerprint. Observations preserve complete order
aggregates, holdings, cash, buying power, account value, source times, reviewed legacy references and
content fingerprints. A request ID makes uncertain responses recoverable. Repeated observations retain
each external order ID once within an account snapshot; consumers must select an observation and must
never add cumulative values across observations.

The source-controlled migration is `20260916_034_AddBrokerAccountImport.sql`. It creates no account,
binding, position, fill, strategy assignment or report. Canonical schema files, immutable triggers and
SQL project definitions accompany it. A missing schema allows preview but disables saving. A reviewed
manual migration and verified backup are required before actual installation.

The operator subsequently authorized migration 034 during collection. It was installed and verified
at 10:08 Toronto on September 16 after a new checksum-verified full backup and a SHA-256-matched copy
to the existing local OneDrive backup folder. Remote sync is not verified. Both new tables are empty;
existing schema and real-account financial hashes/row counts are unchanged, and the same WPF process
remained running/responding. This schema verification does not exercise actual broker imports or prove
uninterrupted market-data collection. Operational receipts are under the September 16 artifact directory.

This is the SQL history foundation, not the strategy-input cutover. Existing ActivePosition, TradeLog,
TradingAccountReport and protection memory are untouched. In particular, `CanImport` does not mean
`CanTrade`, complete lifetime history, trustworthy risk baselines, or automatic execution eligibility.
The main strategy engine still consumes its existing reported account until the next milestone.

## Match and refresh rules

- Match legacy trades only when side, verified symbol identity, whole filled quantity, gross, known
  fees, net and Toronto trading date agree. Exact symbols ignoring case remain supported. September 16's
  native preview diagnosis adds a narrow listing-backed alias: the same broker security must supply
  a unique CAD XTSE/XTSX listing matching its activity symbol and security type. Only the listing's
  `.TO`/`.V` spelling difference can match the legacy symbol. Missing evidence, mismatched venue,
  currency/type or colliding security identities do not qualify. Preserve/display both spellings;
  listing evidence participates in the review fingerprint and is rechecked before save. Legacy records have no tax field,
  so nonzero broker withholding cannot be silently matched to them. If an explicit reported fill time
  exists, it must lie within the order's creation/last-fill interval; otherwise both endpoints must be
  on the recorded trading date. These are proposed matches for first review, not proven execution IDs.
- Each legacy trade needs one candidate and each broker order can reference at most one legacy trade.
  Split manual fills, absent purchase history, repeated matches and uncertainty block the import.
  There is no force-match or corrective rewrite UI in this version.
- When one supported candidate matches instrument, side, quantity and time but differs financially,
  show recorded and broker gross/fees/tax/net side by side. Do not select it as a match, widen the
  amount tolerance or replace either source. Multiple candidates remain ambiguous; do not choose the
  nearest amount. The comparison is bound to the reviewed plan's fingerprint.
- Preserve referenced legacy position IDs. Validate their purchase date, instrument consistency, net
  remaining shares and average-cost book value against their complete recorded trades and then their
  complete matched broker trades. An unmatched subset cannot establish a position mismatch. Unknown
  within-day sequence cannot establish a cost disagreement; unresolved matches still block. Closed positions
  must net to zero. A single tracked position cannot span multiple complete close/reopen cycles.
  Unrecorded later broker trades may be added without pretending the legacy position already changed.
- After linking, retain existing reviewed references; new similar orders cannot steal an old match.
  Any change to the pinned legacy financial ledger blocks refresh for explicit resolution. Changes to
  market marks alone do not change its fingerprint. Risk reports and protection records remain independent.
- Require complete CAD observations no more than two minutes old, matching broker holdings and resolved
  whole-share orders. Recheck aggregate reconstruction; unsupported fractions, missing amounts, overlapping
  orders and reconciliation conflicts block saving. No rounding hides a fractional observation.
- Historical `EXCHANGE_TRADED_FUND` orders are supported alongside EQUITY with the same whole-share,
  currency, amount and timestamp checks. Preserve the raw type and reject type changes within one
  security's history. Completed ETF round trips do not grant ETF strategy or manual-order eligibility;
  current unsupported holdings still block handover. Historical accounting does not change strategy
  gates or universe selection.
- September 16's native diagnostics identify empty `EXPIRED`, then `CANCELLED` orders. Retain either
  status, including the supported `CANCELED` spelling, with zero accounting
  quantity only when quantity, average price, gross, net, fees, tax and last-fill timestamp are all
  explicitly null, and no activity reports a fill. Persist `FillQuantityWasNull` and explain the blank
  source quantity in the preview. This is a narrow compatibility interpretation, not an explicit broker
  zero or a general missing-data default. Previously observed fills cannot disappear; partial terminal
  fills require their actual quantity and complete amounts. Missing fields or contradictory execution
  evidence block. No order is omitted, and no fill time, cash or proceeds are invented.
- Existing imported orders may not disappear or change, including terminal corrections. Retain prior
  history and require a later designed correction path. An older observation cannot replace a newer one.
  The September 16 account-activity capture establishes a completed historical order with `POSTED`
  detail status. The importer accepts this with the same complete full-fill checks as `FILLED`,
  preserving the source status. A status-only `FILLED` → `POSTED` advance is permitted when every other
  fact is identical; it appends an observation and never rewrites the previous one. Reverse status
  changes or any accompanying amount, quantity, identity or timestamp change remain blocked.
- Reconcile again before first save or linked refresh, and compare the reviewed content fingerprint.
  Changed financial facts, activity, legacy state, binding or import predecessor require a new preview.
  New read timestamps alone do not invalidate otherwise identical reviewed facts.
- Save under the existing strategy/control/account transaction fences. Rebuild the plan inside the SQL
  transaction; atomically append the binding (if needed) and observation. Unique predecessor and request
  constraints prevent competing histories. Repeated request IDs recover their original receipt only
  when request content and reviewer agree. Cancellation before commit rolls back both writes.
- Preserve full timestamp precision at the SQL parameter boundary with explicit `DateTime2` types.
  The first native import committed successfully but its default Dapper `datetime` parameter rounded
  the snapshot timestamp before `datetime2` storage, causing strict receipt readback to reject it.
  Read compatibility accepts exactly the old SQL encoding (1/300 second converted to `datetime2(7)`),
  not a general time tolerance. The original full-precision JSON remains authoritative and financial
  fingerprints/account identity checks remain exact. Existing immutable imports are not rewritten.

The WPF coordinator checks the selected account and login revision and shares the existing reconciliation
and manual-order coordination. Broker calls finish before the SQL transaction. This is an observed account
snapshot, not a promise that no external broker activity can occur after its timestamp.

## Validation and remaining boundaries

Offline tests cover first matching, additions, cancelled/partly filled orders, complete round trips,
repeat observations, stale or incomplete facts, changed account/legacy/broker history, ambiguous matches,
review invalidation, cancellation and response recovery. Isolated WPF and SSDT builds, synthetic renders
and static migration/schema parity checks are recorded in `artifacts/broker-import-20260916/`.
No SQL transaction rehearsal or native broker call is implied by those checks.

The subsequent [capture review](../concepts/wealthsimple-account-history-contracts.md) records useful
account-chart, deposit/withdrawal, realised-return and broker-validation queries. They are inputs to
future implementation, not proof of cash-settlement completeness or risk-baseline readiness. The
empty-expiry/cancellation compatibility fix passes 1,339 Core tests and 24 synthetic WPF renders, including saved
null provenance and protected-history recovery. The native successful retry remains outstanding;
the subsequent retry identified CANCELLED with absent average price as well as other execution fields.
The same narrow handling now covers cancellations and tests preserve raw status, partial fills and
null provenance through import and protected storage. No cancellation/expiry test order is required
or authorized to collect evidence.

The later 32-order native preview and read-only diagnosis motivated historical ETF support, listing
identity evidence and financial comparisons. These pass 1,367 Core tests and 28 isolated WPF renders.
The recorded-sale discrepancy was subsequently resolved by an explicitly approved single-row ledger
correction with original values retained in its notes. The operator then saved the first SQL import.
Read-only inspection confirmed the binding and all 32 orders; updated repository reads and request
receipt recovery passed against that saved import after the timestamp compatibility fix above. New
timestamp parameter precision was independently verified using SELECTs. No import rewrite or schema
migration was required. The operator's subsequent rebuilt WPF screenshot at 22:06 Toronto confirms
successful saved-history refresh: zero proposed matches/new records and 32 already-saved orders.

Next: resolve full cash/settlement and account/protection baselines, then project supported broker facts
into strategy inputs with explicit source ownership, freshness and incomplete-history guards. After
that, implement durable strategy activation/execution and verify native broker operations under the
appropriate operator authority. Preserve ADR-0079's regular-session market orders, retained overlaps,
and confirmed non-overlap exits before new buys.

Open: corrections/relinking, split-fill matching, imported individual execution timestamps, historical
cash/valuation coverage, projection ownership, refresh cadence and retention. The initial review remains
provisional; the preview provides a concrete basis to reconsider it without silently weakening matching.
The operator subsequently asked whether to start from a clean slate. The proposed next design is a
verified current-account baseline with forward synchronization and optional older history import.
This is not implemented or an accepted reset of account/position protection. The starting quantities,
costs, pending orders, account risk holds and protection ownership must be established explicitly;
existing records remain intact and unknown historical entry dates must remain unknown.

## Review questions

1. Why must an order's cumulative total not be added once per imported observation?
2. Why does saving broker history leave strategy-input and protection work outstanding?
3. What makes an uncertain import response recoverable without rewriting prior history?
