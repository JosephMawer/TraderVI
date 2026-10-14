# ADR-0089: Stable TMX session context and explicit supplementary records

- **Status:** Accepted; implemented/tested/installed, first refined scheduled cycle and current portfolio marks verified
- **Follow-up:** Observation-time end context implemented; installation/operational qualification pending
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
   aligned bar, including after missing later buckets. Interior off-grid anomalies still reject.
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

The separately authorized four-request October 14 follow-up compares DXT/WPK one-/five-minute
responses through 10:10. Their trailing off-grid records exactly match the same-minute OHLCV,
while the five-minute series omits later aligned buckets. There are no complete five-minute minute
components in these sparse samples; no missing minute is assumed to mean no trade. This corrects
the initial quarantine's unnecessarily restrictive last-bucket boundary: excluded records remain
unusable, absent exact bars remain absent, and independently aligned values are not modified.

### Missing-bar follow-up: observed end context

The operator subsequently requests the nine missing bars investigated and corrected where possible.
The authorized 27-request October 14 comparison covers those nine stocks at intervals one/five,
with five-minute request ends at 10:25 and 10:30. ARE's exact 10:20 bar appears only in the wider
response; the other eight exact bars remain absent. Six stocks report no minute row in that bucket;
CVO/PSI each report one. That does not prove no trades, and the sparse data cannot qualify a synthetic
aggregate. All shared earlier aligned bars agree; the one changed WPK row starts at 10:25 and belongs
to the following interval, outside the missing 10:20 bar. A retrospective read cannot prove receipt
before the original deadline or repair that slot. Private evidence: `artifacts/live-missing-bars-20261014/`.

Opt Delphi Live's exact-five-minute adapter into source context through the actual fetch-start minute
when it is still inside the following interval, on the same regular-session date. Never request beyond
observed fetch time or the regular close. Keep history, one-minute and ordinary diagnostic end bounds
unchanged; older windows do not expand. Return only the original logical bounds, select only the exact
requested completed bar, and retain every existing receipt/durability deadline, conflict and confidence
check. No extra request or automatic retry is added. Following-interval bars cannot supply the earlier
decision's price. Persist actual source end and latest aligned source timestamp as optional receipt
metadata; source timestamps diagnose staleness but never substitute prices. Earlier aligned coverage
is labelled StaleNoNewBar instead of generic NoCompletedBar when the exact bar is absent.

This can address end-boundary omissions when subsequent source activity is already observable.
It cannot create data for empty/unreported intervals or qualify a different feed or price fallback.

## Limits

The bounded aggregate comparison does not prove all histories, continuous live capacity or fresh
account valuation. Installation/resumption and one refined cycle are verified October 14; continued
capacity and clean-session coverage remain separate. The opening trace establishes
one whole-monitor cause; it does not explain every earlier failure. Remaining failures retain bounded
diagnostic records rather than being presumed cured.

## Review questions

1. Why does a supplementary timestamp never become an aligned price bar?
2. Why must an interior off-grid point still reject the response?
3. Why does correcting future collection leave historical coverage gaps unchanged?
