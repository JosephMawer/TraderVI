# ADR-0109: Explicit ongoing Delphi Live management

**Date:** October 26, 2026 Toronto. **Status:** Accepted; implemented/tested and installed October27 at01:26 Toronto. Initial ongoing approval confirmed09:58; current source readiness and native lifecycle qualification remain pending.
**Domains:** architecture, risk-management, user-interface, market-microstructure.
**Related:** ADRs 0079, 0081, 0083, 0085, 0105, 0107.

The immediate problem is repeated dated approval and enable downtime. Its parent problem is durable
execution authority with independently checked daily readiness. The root goal is one selected strategy
managing the actual TFSA across trading days until the operator pauses it.

## Accepted ongoing scope

Joseph explicitly accepts an ongoing no-transfer agreement after reviewing its limitation: the broker
adapter does not establish complete deposit/withdrawal coverage. Initial approval confirms no outside
changes since the original cash-only anchor and a commitment to pause before an outside deposit,
withdrawal or trade. Exact reconciliation checks cash, holdings and every original/owned order.
Matching balances cannot independently detect offsetting transfers. This is operator-scoped evidence,
never native proof of complete cash-flow coverage. Unexpected income or other unexplained cash
movement also blocks buying; no invented cash adjustment or hold clearance follows.

A new approval links previous session/risk hashes, pins the strategy/build and retains the original
cumulative purchase ceiling. Old permissions remain immutable history; their internal Ongoing flag
alone cannot grant this authority. Risk authorization preserves references, provenance, Through,
completed-session state and holds. Obsolete permission payloads leave only the current projection;
they remain in historical receipts. Initial conversion is limited to the unchanged cash-only trial
with no automatic orders or unresolved holds. General replacement/invested conversion is not implied.

Daily continuation reconstructs boundary cash/holdings from the anchor and exactly owned full fills.
An order spanning a boundary is ambiguous and blocks continuation. Valuation retains first five-minute
Open and exact final five-minute Close, independently of confirmation cadence. Missing/conflicting bars
cannot become restart-time values. Closes/opens advance in calendar order; only verified next-session
opening clears the previous daily hold. Overall holds and cumulative spending persist. Recovered
evidence retains its real observation time. Paper progress, balances and confirmation confer no Real authority.

Full-fill-only safeguards remain: partial, cancelled, uncertain or conflicting orders require original-ID
recovery and operator intervention. Stop new buys retains normal exits/protection; full execution pause
remains distinct. Shutdown drains admitted work and preserves explicitly approved ongoing grants.
Restart rechecks build, assignment, account, orders, calendar, source, integrity and risk. Stopped buying
stays stopped. Material changes require review.

This supersedes ADR-0105's dated expiry/separate enable only for the newly approved agreement and its
combined app action. Legacy trials/grants retain their limits. Activation remains Joseph's app action.
Additive omitted-when-absent JSON fields and append-only risk events require no schema migration.

## October 27 repair-build review with an existing holding

The operator requests repair, installation and verification after the first managed purchase. A changed
implementation still needs explicit app review: extend the existing append-only build review to the
unchanged ongoing agreement with fully reconciled owned orders and saved protection for every holding.
Require fresh matching shared inputs, model/configuration and storage checks, fresh no-transfer account
reconciliation, unchanged cash/positions/original orders and unchanged risk history. The review cannot
change strategy rules, reset references, clear holds, restore spent allowance or relax protection. A saved
pause remains paused. Bind the build review to the exact ongoing permission ID; older build reviews
cannot attach themselves to the new agreement. Approval does not enable the host or place an order.
The operator approves the repaired build in Execution setup and resumes its reviewed host separately.
Legacy dated/cash-only review restrictions remain intact for those scopes.

October28 repair: a stopped ongoing session may synchronize already-saved ordinary risk receipts
before preparing a changed-build review. Read at most64 exact receipts backwards from the fresh
account's risk revision to the saved session's revision, then replay them forwards through the existing
closing/opening/hold policy. Each resulting state must match its original receipt, and the final state
must match the fresh account. Append a separate `RiskStateSynchronized` revision retaining the proof;
preserve permission, cumulative allowance, orders, protection, memory, phase and buy controls exactly.
Require the host off, unchanged reconciled account and fresh current evidence. Missing, forked or altered
receipts fail closed. This step grants no build approval or execution permission and cannot clear an
integrity finding. The subsequent build review still requires identical risk state; it cannot itself
advance a day or reset a reference. No new schema or risk-journal event is introduced by synchronization.

## Accepted execution-price policy

Joseph approves a fresh broker ask as the fixed reference, at most 0.25% upward movement before
submission, and at most 0.50% bid/ask spread. Size/reserve against the bounded price; retain allocation,
cash, cumulative allowance and duplicate checks. Never ratchet the reference upward to chase rejection.
These are chosen limits, not empirical findings from the old failures. Existing regular-session market
orders remain; preflight bounds do not guarantee fill prices. Disclose the policy in the explicit review.

Implementation uses `BrokerAsk025Spread050V1`, included in the explicit ongoing approval. The first
fresh broker ask is durably saved for the independently identified confirmation episode before
preflight. The episode includes the Live session, symbol, daily candidate and confirmation start;
another read or another bar in that episode cannot raise its reference. A genuinely new confirmation
episode can establish a new reference. Restart and failed preflight preserve it. The current quote
must still pass the existing two-minute broker-reported quote age and 30-second retrieval/account
checks, including immediately before transmission. Spread is `(ask-bid)/ask`. Quotes from another
regular session cannot reuse the reference. Shares never exceed the recommendation, and sizing uses
the upper bounded price within the original allocation, actual cash and remaining cumulative ceiling.
The durable reservation also uses that upper price. Actual reconciled fills remain financial truth.
Legacy approvals retain their original price guard. Fixed reference/quote facts remain private.

The October26 hash-verified read found six AskAboveDecisionReference failures (October23 revisions1247–1267),
none retaining prices or quote timestamps. It cannot distinguish stale quotes, spread, feed definition
or excessive movement. The old reference uses TMX ask with last-price fallback; preflight uses broker ask.
Retrieval time is not upstream quote freshness. Private evidence: artifacts/ongoing-live-20261026/rejections.sql,
query hash1A652107105F1F7579F23ECBFCBDFD821354B650E320265C25DEA49F8303D041;
result hashC718D31706363E37427B91FE2560E3BE98DA289BF14A2A94EC1DA62148CC956E.

## Faster confirmation: qualification pending

Requested behavior is two genuinely fresh consecutive minute observations, retaining longer indicator
horizons and requiring updated-strategy review. The October23 raw TMX sample returned22 minute rows;
at13:24 its newest event was13:01. It does not qualify minute operation. Current shared collection uses
exact five-minute bars with a two-minute offset; Real owns its confirmation. Repeated reads supply no
new bars. Changing only a timer or BarInterval is insufficient: evaluator, measurement, collection,
storage and publication need consistent successor contracts and market-hours source/latency evidence.

No one-minute source or behavior is selected/installed by this increment. A successor must preserve
the 20-minute persistence/directional-volume/prior-range windows and 20-minute/one-/two-/three-hour
price horizons; converting four five-minute samples to four minute samples would change the strategy.
It needs an explicitly reviewed source contract, minute-aligned immutable identities, collection and
publication deadlines shorter than the confirmation cadence, gap/duplicate/conflict rejection, and
the same independent Real evaluator and complete diagnostic/report-builder output. SQL currently
permits only 5/15-minute bars and slots enforce a two-minute collection offset: both need reviewed
migrations before a minute collector can publish. The data-source decision is whether the existing
TMX minute feed can qualify under market-hours measurement or a different licensed real-time feed
is necessary. Neither old sparse samples nor broker quote snapshots establish complete minute OHLCV.
Updated-strategy approval remains required after qualification; the longer lookbacks are not shortened.

October27 follow-up: the authorized10:42–10:47 samples demonstrate fresh advancing TMX minute data,
while candidate coverage remains sparse. The old sample is not proof of a universal delay. No source
decision or strategy activation follows from the bounded check; see the
[qualification evidence and scheduled follow-up](../reviews/independent-engine-implementation-20261022.md#october-27-minute-source-qualification--live-samples-and-follow-up).

The first successor preparation is the pure `DelphiLiveMinuteEvidence` component. It pins symbol/source
and explicit session bounds, preserves first-arrival values/times, and requires an explicit publication
deadline strictly before the next minute. A request begun before completion cannot qualify by waiting;
late/backfilled observations cannot become timely through rereads. Identical rereads grant no new
minute, while changed completed values latch a conflict in the serializable evidence book. Consumers
must recheck the original deadline at publication/use. This implements the already required causal
input boundary; it selects no production deadline, data provider, indicator formula or assignment.
It has no I/O and is not wired into the installed collector/evaluator. Durable storage, full successor
integration and source qualification remain required; serialization tests alone are not native recovery.

The next offline component, `DelphiLiveMinuteWindows`, builds explicitly duration-labelled OHLCV windows
and four five-minute legs spanning twenty minutes at a minute endpoint. Tests preserve20/60/120/180-minute
horizons, reject missing/late/future/conflicting evidence, and never compress the opening warm-up. Past
timely observations can supply historical context without renewing the current price deadline. These
aggregates are not relabelled as V1's fixed-grid five-minute bars. The successor's indicator integration
and shifted-endpoint behavior remain subject to the explicit updated-strategy review; no installed
formula, exit cadence or source contract changes here.

## Source checkpoints — historical development sequence

Core agreement, linked risk authorization, boundary reader and automatic risk evidence adapter are
implemented.457 focused broker-session tests pass, including consent, legacy preservation, stopped
mode/restart, weekend continuation, invested exact boundary valuation, missing bars, stale/unexplained
cash, history changes and uncertain orders. Existing protection/ceiling tests remain. UI, final full
validation, installation and native multi-day/first-fill qualification remain pending. Tests grant no
operational authority.

The app review/combined approve-and-start action and persistent pause-new-buys control are now implemented. WPF Debug build passes (260 existing compiler warnings, zero errors). Financial activation and installed UI verification remain pending.

The price-policy increment and exact boundary source-contract check pass the complete offline suite:
3,159 passed, nine native/integration skips. Two existing AI-host timeout tests failed under parallel
suite load and passed in isolation; the final complete suite with test-collection parallelism disabled
passes. Isolated WPF/engine Release build passes with 260 existing compiler warnings, zero errors.
NuGet security audit was disabled; no fresh dependency-security assessment or full SSDT solution build
is claimed. Source evidence is under `artifacts/ongoing-live-20261026`. Installation remains pending.

## Installation checkpoint — October 27

Commit ae427a46 is installed with all128 runtime hashes verified, previous runtime retained and normal
engine/dashboard responsive. Execution is Off; financial records/head hash are unchanged. The laptop
restart created a Global integrity hold; Real-only verification completed at09:49 and qualified scoped
resolution leaves zero Broker/Global holds at09:52, with Paper restrictions preserved. Today's nightly
publication is still missing following WM metadata failure; initial financial approval was subsequently
saved by Joseph and confirmed09:58. ADR-0110 corrects isolated ticker failure classification; source
readiness and native lifecycle qualification remain separate.
[Current evidence and procedure](../reviews/independent-engine-implementation-20261022.md#october-27-ongoing-runtime-installation-and-review).
