# ADR-0100: Live entry and order decision traces

**Date:** October 21, 2026. **Status:** Accepted within the operator's explicit diagnostics request.
**Domains:** architecture, decision-engine, risk-management.
**Related:** ADR-0079, ADR-0081, ADR-0086, ADR-0097–0099.

The immediate problem is confirmed signals without a durable explanation for no order. The parent
problem is distinguishing ordinary strategy waiting from latency, pricing or execution failure.
The root goal is reliable, reviewable execution with the exact existing risk and permission controls.

Add optional diagnostic properties to the existing private append-only broker session snapshots.
Older snapshots omit the absent properties when serialized; their approval/order/risk hashes and
financial contracts remain. No new table, migration, permission or financial owner is added.

The recommendation engine records the first existing failed predicate for each fully confirmed
signal: session/freshness, account/risk readiness, current daily/run, quote availability/causality/
contract/window, policy entry start/cutoff, re-entry rules, whole-share sizing or a recommendation.
Maturity alone and observation-only momentum confirmations are not full buy eligibility. A result
record does not change any condition, comparison, threshold, ordering or recommended quantity.

Quote evidence distinguishes no request (readiness, stale signal or elapsed attempt window), a
returned observation and an exception. Retain request/receipt times and exception type, never raw
provider/SQL/exception messages or credentials. Quote prices remain excluded except for the explicitly
authorized rejected-buy comparison amendment below. Existing held-position quote priority
and protection remain independent of entry readiness. Use the original 60-second observation phase,
seven-minute signal age and existing quote contracts; do not refresh timestamps to create eligibility.

Before quotes, save a pending trace with the original protection/memory preparation. Final evaluation
records entry reasons and quote attempts alongside the existing StrategyEvaluated append. The same
single-order selection identifies protective-exit/earlier-recommendation deferral. Later records
distinguish duplicate intent, cumulative purchase ceiling/cash, security resolution, preflight,
reservation, expiry before/after reservation, acknowledgement and uncertainty. Preflight tags keep
the existing exception types/messages and expose fixed local checks, including an ask above the
original reference, changed account/risk, stale two-sided quotes and changed login. Only known local
validation explanations are persisted. Submission acknowledgement is never a confirmed fill.

Unexpected quote/replay/evaluation/risk-save/preflight failures attempt one diagnostic append while
preserving the original exception. A failed diagnostic append cannot retry a financial action or
erase a saved intent. Pending/missing traces remain incomplete evidence; they cannot be interpreted
as passing checks. Selection deferrals add no reservation. Trace data is never read to grant risk,
permission or order authority. Ordinary immutable state/size checks continue to apply.

Fixed privacy-safe timing stages cover paper admission, source/account/input reads, preparation,
quotes, evaluation, session save, security lookup, preflight and submission. Existing bounded local
timing logs contain counts/times/stage names only. Per-symbol details stay in private session SQL;
the reviewed SELECT-only summary groups counts/reasons and reports no account amounts or symbols.
The host shows a compact last-decision explanation while orders retain their own fill status.

This records future observed outcomes. It cannot retrospectively recover October21's missing quote
outcomes or establish native capacity. Installation, normal startup, exact build/source review,
separate enable and any newly dated permission remain distinct operator-controlled steps.

## October 23 amendment: rejected-buy price comparison

Joseph explicitly requests capturing both prices and available timestamps on the next rejected buy,
testing/installing the diagnostic and retaining the existing price guard. This supersedes only the
quote-price exclusion for an AskAboveDecisionReference preflight rejection. The parent problem is
distinguishing feed timing or ask/last-price differences from an unexplained execution blocker; the
root goal remains reviewable trading under unchanged permission and risk controls.

Attach the exact compared reference and broker ask to the existing private failure trace. Retain
broker quotedAsOf, its local receipt and comparison-observation time. Enrich that trace with the
same in-memory TMX quote selected by the recommendation engine: source contract, Ask/Price field,
request and receipt times. Leave the unavailable TMX source timestamp explicitly null. Broker
quotedAsOf is not a verified exchange-update timestamp. No new quote read, source substitution,
threshold/tolerance, sizing, retry, reservation, approval or order action is added. No account amounts,
credentials or raw provider messages are copied. Price values stay out of public timing logs, fixed
exception messages and ordinary human summaries; inspect them through existing private evidence.

The optional trace field is omitted when absent, preserving old serialized histories/fingerprints.
Capture occurs only after the existing comparison rejects; financial order identities and journal
records are unchanged. Failure to append diagnostics preserves the original rejection and cannot
trigger a replacement attempt. Tests cover the exact equality boundary, price-field fallback,
timestamps, shared-archive/restart round trips and failed diagnostic persistence. Installation requires
normal drain/restart; ADR-0107 operator build review and separate host enable remain unchanged.

## Review questions

1. Why does a confirmed signal still need a current quote, sizing and broker preflight?
2. Why is a pending or missing trace not proof that every trading check passed?
3. Why must an uncertain submission retain its original intent instead of retrying for diagnostics?
4. Why does a quote received one second ago still need an explicit unknown source-time label?
