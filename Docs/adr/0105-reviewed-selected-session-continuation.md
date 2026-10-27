# ADR-0105: Reviewed selected-session continuation

**Date:** October 22, 2026. **Status:** Accepted implementation under the operator's request to fix next-session approval, clear verified real holds and enter normal mode with trading disabled.
**Domains:** architecture, risk-management, user-interface.
**Related:** ADRs 0092, 0095–0104.

**Scoped supersession:** [ADR-0109](0109-explicit-ongoing-delphi-live-management.md) replaces dated
expiry and separate enable only after a new explicit ongoing no-transfer approval. Existing dated
records and authority remain unchanged; startup never converts them automatically.

The immediate problem is that the installed review stops at October 22 while the last permission
expired October 21. The parent problem is independent real-account readiness without repeated paper
research. The root goal is timely real execution with deliberate permission and preserved risk history.

Add one reusable, explicitly dated review for a later regular session of the existing cash-only
Portfolio rules only assignment. This supersedes ADR-0101's no-October-23 restriction for this new
contract only. Existing dated records, hashes and expiries stay unchanged. This does not automatically
renew anything or qualify invested-account rollover, transfers or a changed strategy.

Review on the target session's date before its regular close, including pre-open. Require the host
off, the exact prior account revision/risk state and original unchanged cash-only book, zero automatic
orders/positions/protection, no financial/order/review holds, and the operator's confirmation of no
outside changes across the entire interval, including missed days. Preserve original references,
Through, LastCompletedSession, memory, modes, all prior approvals and the unused cumulative purchase
allowance. Gaps remain gaps; an expiry observation is not a closing valuation.

Each request has one deterministic identity for its baseline/date, a canonical prior-session hash,
the immediately preceding permission's identity/hash, and one fixed original scope. Strip permission
objects from the compact risk anchor. Do not recursively embed previous generic continuations.
The authoritative append-only history retains every prior permission; current objects remain bounded.
Absent new fields stay omitted from old JSON; no migration or legacy hash rewrite is needed.

Review the real assignment's saved policy/daily/model definition against current immutable records
and its qualified real subscription. Do not load paper lifetime history or wait for a paper audit.
Historical performance evidence is disclosed as historical, never refreshed by this operation. Before
open require the target date's Valid OfficialPaper daily evidence with exact config/models and the
immediately preceding market session. During trading require current direct published inputs. Existing
execution checks still independently enforce bar freshness, confirmation, quotes and all risk limits.

The review lasts two minutes. Approval repeats storage/source/account/build checks, saves risk first,
reads it back and appends the linked session approval. Interrupted saves recover only the identical
request. Approval never enables a host or sends an order. Separate enable binds the current build,
assignment and newly dated permission. Old grants cannot transfer. The 15:45 strategy entry cutoff,
regular-close outer expiry, pre-open order prohibition and full-fill-only safeguards remain unchanged.

The dashboard shows the actual target date and explains tomorrow's fresh checks after today's close.
Normal engine mode and historical hold clearance do not constitute financial approval. The operator
explicitly authorizes the controlled cutover and exact real-only clearance; paper restrictions and the
saved paper cursor remain. Real trading stays disabled pending review of the checks. No test trade,
model workflow, historical deletion or automatic financial approval is part of this work.

The October 23 storage correction in [ADR-0106](0106-exact-selected-session-risk-receipt-storage.md)
removes duplicate persisted risk evidence while preserving this contract and its full domain hashes.

## Review questions

1. Why can a skipped day remain an honest gap while the next permission preserves original risk references?
2. Why does starting normal engine mode not enable trading?
3. How do fixed anchors and links avoid embedding every earlier renewal in the current state?
