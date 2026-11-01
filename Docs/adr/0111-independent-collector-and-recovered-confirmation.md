# ADR-0111: Independent collector and recovered confirmation

- **Status:** Accepted direction; implementation and source qualification pending
- **Date:** 2026-10-27
- **Domains:** architecture, data-pipeline, decision-engine, risk-management
- **Related:** ADR-0053, ADR-0045, ADR-0066, ADR-0090, ADR-0103, ADR-0104, ADR-0107, ADR-0109

## Context

Restarting the trading engine currently discards operational indicator continuity even when useful
prices remain saved. The parent problem is missing or late decisions while collection and execution
share a lifetime. The root goal is prompt execution of a deliberately approved strategy with durable
history, independent protection and unchanged financial limits.

Joseph explicitly accepts a separate collector, restored indicator history and two consecutive
qualifying observations reconstructed from validated backfill. No additional two-observation wait is
required merely because either process restarted. This supersedes the restart reset and fresh-only
recovery requirements of ADR-0053 for the prospective successor only. It does not rewrite V1 policies,
earlier missed-cycle evidence, original orders or the installed strategy.

## Decision

### Owners and data flow

Use one separately supervised intraday collector process. It alone requests shared stock/benchmark
bars, detects gaps and requests bounded recovery history. Persist evidence in SQL; the trading engine
and independent Paper consumers read local evidence. Neither dashboard closure nor trading-engine
restart stops collection. Enforce one durable writer lease with a fencing generation: a process that
loses the lease cannot commit, even if an earlier request later succeeds. A second collector stays idle.

The collector has no broker credentials, account permissions, order adapter or financial activation
interface. The execution engine remains the sole broker/order owner, including account reconciliation,
fresh executable quotes and protective bids. Shared market judgments never import Paper confirmation,
capital or lifecycle into Real. Hermes nightly daily collection remains separate; overlapping intraday
consumers must route through the collector or an explicit compatibility path, not competing pollers.

The subscription union includes selected daily candidates, XIU, held securities and pending exits.
Keep held symbols subscribed across source changes and entry pauses. The trader persists subscription
changes independently of the collector; unavailable candidate input cannot silently drop held symbols.
Collection remains independent of Paper research backlog. Process separation on one laptop does not
survive laptop sleep/power loss and does not provide protective execution while the trader is stopped.

### Durable evidence and recovery

Persist canonical symbol, provider/venue/interval contract, session, bar start/end, raw response hash,
request/receipt/validation times, immutable first completed observation, completion state, revisions,
missing intervals and collector fencing generation. Save request failures and empty responses explicitly.
Do not log private prices/account data to tracked files. UTC event time and actual receipt time are
distinct; receiving a backfilled bar now never makes it an on-time observation in historical evidence.

Only fully completed, valid OHLCV rows are eligible. A read begun before bar completion cannot become
complete by waiting. Repeated rows neither advance confirmation nor renew expiry. Reject ambiguous
duplicates, impossible timestamps/values and source changes; preserve conflicting completed revisions
and block affected evidence pending a reviewed resolution. No synthetic carry-forward bars or inference
that an absent minute means no trades. Missing source rows remain missing unless the source contract
explicitly qualifies an alternative. Backfill work is bounded by session/horizon, request and retry
budgets; fresh collection has priority and failure must not create a tight retry loop.

Consumers reconstruct from a coherent committed evidence boundary. Validate identity, integrity,
complete required windows and actual availability by the present decision time. Do not overwrite
original decisions when later evidence arrives. A recovered judgment records both its logical bar
endpoint and present calculation time, input IDs/hash, policy/source identity and recovery provenance.

### Confirmation and present-time action

Keep three supportive families out of four and two consecutive qualifying observation endpoints.
For the proposed minute successor, endpoints are one minute apart; reading one endpoint twice cannot
satisfy persistence. Both judgments must be rebuilt under the same approved policy/source/session and
each must satisfy its own maturity, validity, support and safety rules using data through that endpoint.
Never use a later bar to improve an earlier endpoint's indicators.

If the collector stayed current, restore the qualifying sequence across trader restarts. If the
collector also stopped, validated recovered history may establish both qualifying observations.
No additional fresh pair is required. The newest endpoint must still be the current completed endpoint
within the explicitly configured source/strategy freshness deadline at decision and submission.
Recovery does not extend that deadline. A fresh broker quote cannot make stale indicator history current.

The existing offline minute evidence deadline contract requires a deadline after bar end and before
the next minute. Retain that bound for the successor; exact within-minute scheduling/deadline settings
remain part of source qualification and the operator's updated-strategy review. Historical context may
be late; present entry evidence cannot be expired. A prior recovered endpoint can satisfy persistence
after its original deadline without being relabelled on time. The current endpoint must be available
and unexpired. Distinguish this operational reconstruction from historical on-time research scoring.

Only a new present-time decision can request an order. Reconcile held quantities and pending/uncertain
orders first; preserve existing duplicate-order identities and idempotency. Do not replay missed buys.
Preserve session entry windows/cutoff, cash/sizing/cumulative purchase ceilings, no-transfer agreement,
risk holds and the approved 0.25% fixed-ask movement / 0.50% spread limits. Deliberate pause persists.
Changes to policy/source/model require the normal explicit review; a process restart alone does not.

### Indicators, protection and adoption

Keep 20/60/120/180-minute indicator horizons. Minute confirmation does not turn four original five-minute
legs into four one-minute legs. Keep complete-path session VWAP and its unavailable semantics unchanged.
Implementation preserves VWAP's original opening-anchored completed five-minute typical-price/volume
buckets. Between those boundaries, use the last completed five-minute VWAP reference, while requiring
the complete minute path from open through the current endpoint. Publish the reference's actual through
time. This avoids silently substituting minute-weighted VWAP for the accepted indicator.
Historical context recovery must re-evaluate both stock and benchmark coverage. No automatic lowering
of the three-of-four rule. Opening-entry/top-three buying is deferred.

Protection retains existing floors, high-water marks, pending exit identities and separate fresh broker
bid checks. New minute entry cadence does not change the five-minute closing-high protection contract.
Recovered historical highs cannot silently loosen a stop or retrospectively create an executed exit.
Existing signal-exit and protection recovery rules continue; quote-based protection does not wait for
entry history to mature. If collector ownership/history is unhealthy, explain affected entry readiness
while preserving whatever independently valid protection can run.

Use an explicit successor identity for cadence and recovery semantics. No silent migration of an
approved five-minute assignment or fixed comparison pair. Build review and strategy adoption are
separate where the definition changes. Joseph retains final financial activation in the app.
Until source qualification and reviewed adoption pass, keep the approved runtime coherent and avoid
installing a build that strands its account awaiting an unattended approval.

## Verification and implementation order

Legacy compatibility at cutover uses a local same-user relay for the frozen five-/fifteen-minute source
requests. The collector performs those network reads and returns the original bounded receipts; the
existing consumer retains its lease, audit/publication and Paper lifecycle. This avoids representing
minute aggregates as original legacy TMX observations or transferring Paper financial writes to the
collector. New one-minute decisions read durable SQL books directly. The explicit routing package has
no intraday HTTP fallback; broker execution/protection quotes remain separate. Combined legacy/minute
transport capacity and native process ownership must be qualified before cutover.

The owning implementation checklist contains progress. Implement in coherent reviewed commits:

1. Recovery-aware context and confirmation core, offline tests, unchanged legacy behavior.
2. Durable collector schema/repository and fenced lease, additive reviewed migration and isolated tests.
3. Separate collector host, bounded source adapter/backfill, startup/health/stop diagnostics and supervision.
4. Local-data publication and independent Real consumption, exact successor identity and review display.
5. End-to-end synthetic restart/outage/conflict/order/protection tests, release build and install manifest.
6. Controlled cutover only when coherent and qualified; otherwise stage a reviewable package and retain
   the working runtime. Apply no unreviewed migration or schema deployment. Native activation stays human.

Acceptance includes trader restart with collector current; collector outage with valid reconstructed
pair and immediate current decision; stale latest evidence; a weak second judgment; missing XIU/session
VWAP; duplicates, clock errors, conflicting revisions; lease takeover/stale writer; crash before/after
commit; source/policy change; paused account; uncertain orders; holdings/protection continuity; next-day
calendar/source changes; and preservation of original financial/history records.

## Alternatives and remaining evidence

Resetting everything after every restart is rejected: process lifetime is not market continuity.
Context-only backfill plus two extra observations is rejected by Joseph: a validated current pair should
be actionable. A recovery-only single-observation override is rejected: persistence remains two.
Buying the top daily picks immediately and weakening VWAP are deferred, not part of this implementation.

TMX showed fresh minutes but also gaps on October 27. October 28 checks must quantify timely first
arrivals, missing intervals and later revisions separately from retrospective coverage. Three symbols
cannot qualify full-universe capacity or venue completeness. Do not infer source fitness from successful
HTTP reads. Replacement provider, paid subscription and revised missing-minute rules need a separate
decision if TMX is insufficient. No overnight test can prove tomorrow's live coverage in advance.

## November 1 — Receipt storage and historical corrections

The collector needs to preserve the first value it saw and detect a later contradiction. The
full-session request also recovers missed minutes after downtime. Rechecking the same response
once per returned bar and storing the expanded JSON on disk are avoidable costs.

Compare each validated response with the saved minute book in one batch. Preserve original
observation identities, first-arrival times and the existing rule that any completed-bar conflict
blocks new observations for that book. Store the exact serialized receipt in losslessly compressed
form. Keep the original request ID, content hash, timestamps and observation/conflict references.
Migration 056 exposes the original JSON to readers for both old and new rows. Convert historical
receipts only by matching the existing fingerprint and verifying the exact decoded bytes in bounded
transactions. File reclamation and evidence expiry have separate owners.

The initial 056 writer retained the full-session request. A three-minute routine request plus a full
check every 15 minutes would miss transient historical contradictions outside the shorter window.
An actual October 28 response contained 140 such contradictions. Changing this acquisition policy
requires explicit acceptance of that coverage loss and new source qualification. The underlying
provider's response to narrower requests has not been measured.
[Poll replay and measurements](../reviews/sql-cleanup-inventory-20261031.md#collector-implementation-and-poll-window-comparison)

### Superseding decision — latest completed minute and rule-driven history

The operator chose latest-minute polling on November 1 after reviewing the transient-correction
tradeoff above. Each routine request covers one completed minute of one monitored symbol. Use
the first observations already in SQL for history. Only an active rule with missing required
minutes may request those gaps through the collector. Restart, subscription and elapsed time alone
do not request history. Nightly backfill is deferred until there is a concrete consumer.

This supersedes the full-session acquisition choice above. Older transient corrections can go
unobserved. Preserve conflicts actually seen, the time recovered evidence became available and
the original deadlines. Existing trading decisions and earlier receipts retain their meaning.
The change requires fresh source qualification before minute-strategy adoption.

Rule evaluations publish only missing SQL minute keys. Pending requests expire in two minutes
or at the close. SQL rechecks observations and conflicts, then claims one contiguous gap of at
most 15 minutes in the same transaction as the next live request reservation. This bounds both
provider work and repeated attempts for unavailable minutes. The collector asks for the gap and
the live minute as separate fields of one HTTP request. A historical field failure alone cannot
discard a valid live minute. Preserve the actual query bounds in the losslessly packed receipt.

## Review questions

1. Which operations remain with the trading engine when intraday collection is extracted?
2. How can backfill establish current confirmation without rewriting historical on-time evidence?
3. What prevents repeated/expired observations or an uncertain order from becoming a new buy?
4. Which source and financial-review checks still separate tested code from enabled Real execution?
