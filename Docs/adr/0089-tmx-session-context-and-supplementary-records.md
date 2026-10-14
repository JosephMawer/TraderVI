# ADR-0089: Stable TMX session context and explicit supplementary records

- **Status:** Accepted; implemented/tested and installed, scheduled recovery verification pending
- **Date:** 2026-10-14
- **Domains:** data-pipeline, data-sources, time-series
- **Related:** ADR-0028, ADR-0030, ADR-0045

## Problem and goal

Narrow five-minute TMX requests repeatedly return off-grid timestamps, and whole-response rejection
discards otherwise usable prices. The parent problem is reliable held-stock valuation and monitoring.
The root goal is honest intraday paper evidence with unchanged risk, freshness and trading rules.
The operator previously approved retaining independently valid bars while flagging unusable records,
and now requests the correction. Earlier narrow comparisons were inconclusive; they did not qualify it.

## Evidence and decision

The October 14 authorized six-request comparison used yesterday's QTRH/XIU session, both request
fields exposed by TMX and one-minute data. Sixty-nine complete five-minute bars exactly matched all
five minute components in OHLCV, with zero mismatches. Wider requests supplied aligned aggregates;
XIU also supplied a trailing off-grid record. That record's meaning is not assumed or used for prices.
Detailed private receipts remain under `artifacts/live-collection-fix-20261014/`.

1. Expand multi-minute intraday requests starting inside regular trading hours to the same Eastern
   date's 09:30 source context. Retain the logical requested window separately and return only its
   bars. One-minute requests remain unchanged. Existing non-overlapping five-day chunking remains.
   This is source formatting, not a substitute for the reviewed TSX calendar or evidence availability.
2. Preserve the strict UTC, minute precision, OHLCV, duplicate, requested-window and daily-fallback
   guards. Within each date, quarantine supplementary off-grid minute records only after the last
   aligned bar and before that bar's interval ends. Interior or later-bucket anomalies still reject.
   If there is no aligned bar, return no eligible evidence. Never round timestamps, aggregate sparse
   minutes, fill missing prices, or use excluded records for valuation, confirmation or fills.
3. Retain excluded timestamps and actual source-window metadata in new Live receipt JSON/hashes.
   Ordinary intraday polls expose excluded records through an informational audit code and actual
   source counts/window. Independent accepted bars can remain Valid; empty, stale, late and conflicting
   evidence retains its existing blocking disposition. Historical observations are not rewritten.
4. Replace per-cycle collection warnings after each fresh collection; retain session interruption,
   coverage and source-adoption warnings. Durable failed receipts remain the historical authority.
5. The October 14 opening trace separately identifies SQL receipt-clock rejection. Twenty native
   SELECT-only measurements confirm .NET can lead the same host's SQL clock by up to about 13 ms
   in this sample, with zero parameter-binding loss. Before receipt/evaluation writes, wait outside the
   transaction for the actual store clock to reach the original observed time. Bound this to eight
   short waits, reject discrepancies over 250 ms, and preserve cancellation and every SQL causal/
   durability deadline. Never round observations, add tolerance to SQL guards or replay a write.
6. Preserve monitor recovery and record private failure identity, SQL error numbers and bounded Core
   method locations when a desktop tick fails. Never log arbitrary exception messages, provider bodies,
   SQL text, credentials or account values. Diagnostic failure cannot replace the original exception.

The direct aligned bar contract, interval resolution, collector/policy identities and hard guards do
not change. This is a correction to request context and source validation, with optional JSON metadata;
no migration, model training, gate change, broker execution or retrospective gap repair is required.

## Limits

The bounded aggregate comparison does not prove all histories, continuous live capacity or fresh
account valuation. Native installation/resumption is a separate step. The opening trace establishes
one whole-monitor cause; it does not explain every earlier failure. Remaining failures retain bounded
diagnostic records rather than being presumed cured.

## Review questions

1. Why does a supplementary timestamp never become an aligned price bar?
2. Why must an interior off-grid point still reject the response?
3. Why does correcting future collection leave historical coverage gaps unchanged?
