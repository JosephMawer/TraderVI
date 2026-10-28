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

## Review questions

1. Which operations remain with the trading engine when intraday collection is extracted?
2. How can backfill establish current confirmation without rewriting historical on-time evidence?
3. What prevents repeated/expired observations or an uncertain order from becoming a new buy?
4. Which source and financial-review checks still separate tested code from enabled Real execution?
