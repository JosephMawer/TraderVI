# ADR-0087: AI Momentum mandate and enforced protection

**Date:** September 26, 2026 Toronto
**Status:** Client/learning foundation, continuous-round risk core, local journal and broker/paper valuation adapters implemented offline; production sources, engine integration, outcome evaluation and host qualification pending. Lesson criteria are operator-deferred.
**Domains:** architecture, llm, risk-management
**Related:** ADR-0055, ADR-0062, ADR-0081, ADR-0083, ADR-0085, ADR-0086; `Docs/oracle-rules.md`

## Summary and consequence

Create an AI Momentum strategy for paper evaluation and intended real-account adoption, with
short-term momentum as its mandate. The AI may use or reject discretionary trading rules with a
recorded explanation, but TraderVI enforces the approved hard boundaries. AI Explorer, a separate
paper strategy free to choose its investing style, is deferred to a future session.

The client/proposal foundation is implemented; an operating AI portfolio and Monday launch are not verified.
The requested September 28 launch still depends on the existing native risk, broker and host checks.

## Problem

The immediate question is how an AI can exercise trading judgment while respecting protection.
The parent problem is introducing an auditable complete strategy that can manage paper and actual
holdings independently. The root goal remains deliberate adoption of a strategy for the permanent
TFSA with preserved cash, execution and risk history.

## Accepted principles

- Separate mandate, trading discretion and non-overridable boundaries. Cash, exposure, emergency
  protection and account/data/order validity belong to the enforced boundary; precise parameters
  and the full classification of existing rules remain open.
- Log meaningful decisions, evidence, reasons for overrides and conditions for reconsideration.
  A persuasive explanation is not proof of predictive value or permission to breach a boundary.
- Reduce permitted risk as portfolio losses deepen using the accepted gradual allocation scale below.
  Restore allowance gradually as measured losses recover across days, without a calendar reset.
  Track starting capital and portfolio peaks. The later round-trailing decision uses the round's
  highest verified value for its limit and allowance, adjusted for confirmed capital flows as below;
  valuation and cash-event source qualification remain unresolved.
- Reuse the selected engine's existing per-stock hard-stop and trailing-profit settings. The AI may
  tighten an established stop or sell earlier, but cannot loosen or remove it. Record a desired
  relaxation as a blocked learning hypothesis under the position-protection decision below.
- Protect profit as an objective. This requires a measurable activation threshold and allowed
  giveback (decline from an observed peak), not a promise that every trade avoids losses. Those
  per-stock settings now come from the selected engine; round protection is defined below. A trigger
  cannot guarantee a fill price or realized loss bound.
- Retain independent paper/live histories and approved strategy identity. Paper outcomes or changes
  cannot silently overwrite real holdings, protection or the live assignment.

## Shared risk authority and strategy responsibilities — September 27 decision

The immediate problem is avoiding competing engine/AI risk decisions. The parent problem is
composing discretionary AI with existing portfolios and Delphi Live; the root goal remains one
auditable strategy managing its account within enforced protection. The operator accepts one
shared risk authority with an explicit approved risk configuration for each portfolio.

- The portfolio records holdings, cash, trades and risk history, separately for paper and live.
- Delphi and strategy engines supply signals, rankings and trading rules with their versions.
- The AI supervisor chooses proposed actions and records permitted discretionary exceptions.
- Shared deterministic risk controls enforce stops, exposure/cash limits, loss thresholds and holds.
- The execution coordinator rechecks permission against current state and uses the existing single
  execution path. Protective exits can proceed without waiting for AI or learning consultation.

Shared enforcement does not require identical settings or a global shared balance. The AI-managed
profile uses the accepted continuous 5% / 3% / 1% rounds in place of the selected engine's daily
buying-pause calculation. Ordinary portfolios retain their existing daily rules and saved versions.
This is explicit policy composition, not permission for the AI to waive a hard rule. The overall
10% approval hold, per-stock protection and all independent account/data/order controls still apply.
Engine switches preserve portfolio risk state and cannot select a looser profile or reset attempts.

Extract only the protection boundary needed for this MVP, reusing existing calculations, journals,
account checks and execution contracts. Keep engine-specific signal semantics and existing ordinary
portfolio behavior. Avoid creating competing risk ledgers or a second order-submission path.
Existing saved holds require their established release/transition rules; choosing an AI profile
cannot silently discard them. Test profile composition and preserved behavior before integration.

Read-only source inspection finds family-specific guard calls in `TradingRecommendationEngine`
and common checks in `BrokerManagedSessionWorkflow`. `AiPortfolioExecutionCoordinator` remains
learning-aware preparation only. Shared authority extraction, AI profile integration and order-path
connection are pending Steps 4/5 work. No runtime behavior, assignment or execution authority changes
in that documentation increment. The discretion principle is settled; map concrete engine rules
during implementation and escalate only genuine ambiguities.

## Coordinator scope clarification — September 30

The operator reconfirms adaptive AI attention within the agreed 1–15-minute limits and event
handling. The AI chooses which portfolio is active and may decide to switch; “AI's portfolio”
means that active choice, not a permanently fixed strategy. The existing switching contract below
still preserves holdings, protection and financial/risk history across the transition.

Continue Step 7 coordinator integration using the existing input builder and validity checks.
The operator prefers reviews to include explicitly dated information and missing-data details
so small data gaps need not prevent useful AI assessment. Defer changing current rejection or
eligibility behavior unless it becomes a concrete integration blocker; no relaxation is accepted
or implemented here. Keep input preparation separate from scheduling/model invocation so that
this policy can be revisited without redesigning the coordinator. The first connected reviewer
records decisions without executing trades; no separate proposal approval workflow is requested.

## Review coordinator connection — September 30 implementation

`AiPortfolioReviewCoordinator` joins the existing schedule, paper input readers, packet builder,
learning-aware reviewer and a scoped SQL review repository. Input preparation remains a separate
interface; existing qualification/eligibility rules are unchanged. The production factory uses the
budgeted Responses client and SQL catalogue/engine readers, with caller-supplied ages, configuration,
current active choice/holds and explicitly initialized histories. It does not start a timer or change
the active portfolio when an answer arrives; executing a switch remains Step 5 work.

Prepared migration 042 adds queryable append-only review evidence. Save the full learning-enriched
packet as Pending before calling the model, then save Reviewed with the validated answer or Rejected
without one. Pending is an admitted input, not proof a paid request occurred. Local journals remain
the source of call/spending/learning/scheduling recovery. SQL exact retries are idempotent; changed
scope/input/disposition is rejected. A missed terminal save can be copied from local evidence without
repeating the model call. SQL history does not restore permission or remove newer pending events.
Pre-packet failures remain visible in the existing schedule history. No separate proposal approval
system is added. SQL installation/native roundtrip and app startup/operating qualification remain
pending; no activation follows from the source increment. Validation belongs in project status.

For the first connected review, the operator subsequently selects the existing **Breakout 3 with
skipped daily gates** book (BreakoutTop3 / PortfolioRulesOnlyV1) to supply starting cash and holdings.
This is the initial review context, not a restriction on AI portfolio selection. Preserve its actual
history and the existing switching contract; no assignment or risk reset follows from this setup choice.

## Implementation checkpoint — September 27

Step 2 has enough agreed framework to proceed with code. Portfolio eligibility is now agreed below:
all current strategy portfolios, expanding as more are built and integrated. The operator now defers
lesson criteria until needed for learning implementation; keep delivery moving. Do not reopen ordinary-rule
discretion. Data/feed qualification, concrete rule mapping, storage and execution wiring are
implementation dependencies, not additional broad
design interviews. Step 4 has an offline risk core; Step 3 has broker and paper adapters below.

`Core/Trader/Risk/PortfolioRiskAssessment.cs` establishes the shared output contract and explicit
engine-daily/AI-continuous profile identifiers. It is a risk assessment, never order authority.
`AiPortfolioRiskPolicy` implements immutable state transitions for initial capital, round peaks,
5%/3%/1% limits, proportional allowance, latched failure, reconciled closure and fresh-entry restart.
Round/sequence identity carries across sessions; exhausted sequences consult the existing reviewed
market calendar. Re-entry evidence is bound to the preceding round. Independent holds and missing
fresh AI review block new allowance without suppressing a committed protective closure.

Valuations must have identity, scope, complete/current whole-portfolio evidence and a confirmed
no-capital-flow interval. Duplicate observations are idempotent; stale/conflicting observations
cannot advance the peak. Adapters must qualify these inputs and surface rejected observations as
new-risk blocks. `AiPortfolioRiskJournal` now retains every accepted command, recording time and
resulting state in a private local history scoped to account, portfolio and paper/live mode. It
wraps policy transitions; callers cannot replace the saved state or start over within the history.
Expected revisions reject stale writes; exact command retries return the current state, while
changed command/evidence contents are rejected. Recovery replays the history through the policy
and verifies saved financial/lifecycle fields, keeping original round and sequence identities.

Initialization is explicit and leaves a persistent ownership marker. A single writer flushes a
pending file before replacing committed history; missing, corrupt or pending history requires
reviewed recovery, never an automatic reset. Checksum/replay checks detect corruption and invalid
transitions; they do not establish authenticity or detect a valid old backup restored between
process lifetimes. Same-owner external history changes are rejected. This bounded MVP journal
replays/rewrites complete history (32 MB maximum); protected storage, backups, retention/performance
qualification and host failure handling remain Step 7 work. Keep the reviewed calendar's historical
coverage available for replay. Independent holds and stock stops remain in their existing owners.

`BrokerAiPortfolioRiskMonitor` now adapts the existing `BrokerRiskBoundaryBook` and
`BrokerRiskNoFlowEvidence` contracts to that journal. The live journal's account scope is its broker
binding ID; portfolio identity and paper/live mode must match. `BrokerAiExactBookMarksV1` requires
one identified exact-time price for every reconciled holding and derives value from cash and shares.
It does not substitute the saved-price opening contract or a broker graph. No-flow coverage must
span the previous valuation through the new book, including overnight; initial capture is explicit
at the opening capital instant. Actual capital movements still require the pending adjustment path.

The caller must configure a qualified maximum evidence age; no production duration or feed is chosen
here. Canonical book/flow source bytes and their hash are saved with the valuation. Absent provenance
is omitted for compatibility with old journal entries; recovery retains exact evidence and round state.
Identical retries do not create observations or refresh expiry. Rejected observations suppress new
allowance and preserve saved state/required closure. Storage exceptions propagate and must block new
risk in the host; there is no silent initialization or reset. Independent holds and AI-review freshness
remain caller inputs, and a result is an assessment rather than execution authority.

This is contract validation over producer-supplied evidence, not native source qualification. It
cannot prove the producer supplied every actual holding or complete cash-event history; those remain
explicit producer obligations under ADR-0083. Production reader composition, native collection, current-account/price
reconciliation and host composition remain pending. Tests use synthetic evidence only.

`DelphiLiveAiPortfolioRiskMonitor` and `SystemShadowAiPortfolioRiskMonitor` now share the same
persist-before-assess writer with the broker adapter. Their journals use the actual managed paper
portfolio ID for both account and portfolio scope. Candidate portfolios may supply strategy context,
but cannot substitute their balances/holdings for the managed account. Source-family changes require
the still-pending reviewed handover; changing a source never silently resets risk.

The Delphi Live adapter requires the latest complete checkpoint to match current holdings, cash and
all whole-share fills. It rejects opening-only marks, incomplete/corporate-action checkpoints,
changed revisions and trades occurring after the checkpoint time; wait for a subsequent complete
checkpoint in that case. Original capital and completed fills remain fixed across observations.
The Daily Shadow adapter requires a coherent complete generation/portfolio/session/position book,
including closed positions, with identified source bars. Stored last-price timestamps mix bar starts
and fill times, so require matching completed five-minute bar closes for every held symbol. Derive
value from actual cash/shares and reconcile fixed capital against original costs and realized results.
This is an explicit intraday input contract, not a change to either engine's opening definition.

Both retain generation/policy/gate identity, add saved engine holds as independent blocks, and reject
capital/history substitution. `PaperAiPortfolioRiskReader` now connects saved records to the checks:
Delphi Live exposes a read-only snapshot interface on its existing repository; Daily Shadow implements
a single read-only serializable transaction across account, generation, session, complete position
history, exact strategy/gate settings and existing source bars. It neither collects data nor writes
the paper ledger. Explicit assignments require their saved settings/hash; original unassigned Shadow
accounts retain frozen V1 and its established identity. Unversioned comparisons must match their pinned
V1 settings, using the same identity resolver as decision readers. Broken explicit assignments cannot
fall back; comparisons retain their fixed settings/version instead of another assignment.

The Shadow builder declines missing/mixed-time price evidence and trades after the valuation bar,
including additions whose original entry time alone would miss the later trade. Saved rows/receipts
determine observation time and content-derived identity; replaying a receipt cannot refresh stale evidence.
The September 28 cash-only refinement replaces the saved-update-only rule for current cash books:
the reader captures a verification time after reading the full book, while its serializable locks still
protect cash, positions and history. When there are no open holdings and the requested Toronto session
date is today, this is a new current cash observation (`CashVerifiedUtc`). It must reconcile fixed
capital and all closed history, and cannot predate any saved book update or fill. Historical reads
retain saved times. No account row, historical value or engine opening is rewritten.
The explicit receipt is retained in risk source evidence; replay preserves its identity/expiry, while
a subsequent complete read can produce a new observation without resetting the round, peak or holds.
Any open holding keeps the original exact-bar valuation time and requires matching price evidence;
a read receipt cannot refresh an invested book. Failed reads produce no receipt. This narrowly applies
to the fixed-capital paper ledger, not broker cash or proof of external cash-event completeness.
Invalid stored price audits or overlapping
later invalid receipts cannot supply accepted bars. Source failures propagate; missing records produce
rejected risk observations. The reader checks freshness after I/O and cancellation before journaling.

**Receipt clock correction — September 29:** native diagnostics found SQL receipt times 0.04–0.25 ms
before the caller's read start on localhost. The repository now stamps completion with the same app
UTC clock used by its consumers, after reading the complete account and before committing/releasing
serializable locks. This supersedes using SQL server time for that receipt. Keep saved update/fill
timestamps, strict caller read bounds and all age checks; add no clock tolerance, price freshness or
post-transaction restamping. Old evidence retains its original timestamps and expiry. No SQL records
or schema change, and no operating freshness limits are selected.

This is compiled/source-connected, not an installed operating loop. A September 28 SELECT-only native
check now verifies saved paper-book mapping, arithmetic and in-memory risk rejection; dated results and
limits are in project status. Transaction lock/concurrency behavior still needs qualification. No schema
change, production freshness duration, one-minute feed, AI-owned operating paper account or execution
loop is introduced.

**Faster paper-value preparation — September 28 implementation:** the accepted independent-update
contract below now has a separate `PaperAiFastValuationReader`. It combines the managed paper
account's verified cash and full holding/fill history with `TmxAiRiskPriceSource`, using existing
account reconciliation rules. Shadow exposes the coherent account independently of saved price marks;
Delphi Live supplies its existing complete immutable revision. Read the account before collection and
again afterwards; any changed source contents reject the result. Preserve settings, identity and saved
holds. Require every held stock's fresh completed price to end at the same minute, and reject trades
after that boundary. Differing latest minutes remain unavailable; no inferred no-trade price is added.
Cash-only accounts reconcile original capital and closed history without requesting market prices.
Retain both reads, actual bar/receipt times, cash, calculated holdings value and holds in hashed source
evidence. Expiry is bounded by the earliest account, market-price, receipt or session limit; no operating
age defaults are selected. This reader produces a prepared value; the subsequent source increment
below connects it to risk assessment. Existing engine marks, account records and stops are unchanged.
Native concurrency/feed qualification and scheduling remain open.

**Broker quote uncertainty and clarified fallback scope — September 30:** the operator accepts
the undocumented meaning of `quoteV2.quotedAsOf` remaining explicit during paper exploration.
The subsequent scope clarification supersedes the broader interpretation of this as a switch to
broker-based portfolio valuation: TMX remains the normal source. Wealthsimple is only a possible,
limited AI-manager fallback when a held stock's TMX input is delayed or unavailable. This is a
lower-priority, deferred item, not the default path or an immediate delivery prerequisite.
No system-wide quote replacement, bid/midpoint valuation choice, operating age limit or change to
existing minute-bar rules is approved. Existing manual broker quote checks remain separate.
Preserve broker-reported time separately from receipt time; neither a recent timestamp nor a
changing quote proves a known upstream market-data age. Keep the limitation at the parser and in
any future fallback evidence. Revisit fallback triggers, how the AI may use it and any risk-valuation
implications only when this item is resumed. Preserve current stale-input blocks, cash/holding
reconciliation, saved holds, risk history and independent protection. No fallback is implemented,
no live risk feed is qualified and no broker-support inquiry is required or sent.

**Durable faster-value integration — September 28 implementation:** `PaperAiFastRiskMonitor` rechecks
the complete retained evidence, ownership, arithmetic, source holds and explicit age bounds before
using the shared journal writer. Preserve generation, fixed capital, exact strategy and completed
position/fill history between observations. The explicit `Handover` operation permits transition from
the same paper family's saved-price contract only after those continuity checks; it observes the
existing round rather than initializing another. It does not implement strategy switching or broker
cash-flow handling. Repeated market time with an unchanged book retains the first journal value,
identity and expiry; a newer fetch cannot refresh it. Compare selected minute-bar contents against all
accepted faster-source history, including earlier rounds and after restart. A changed bar saves its
conflicting evidence alongside the risk history and exposes `RiskSourceConflictRequiresReview`.
All shared monitors and the packet builder respect that block. It prevents new valuations/rounds and
usable review packets while preserving already-required closure and allowing confirmed closure to be
recorded. Recovery cannot automatically clear it; source reconciliation and an audited release path
remain operational work. Missing/corrupt/pending storage still fails closed. The normal faster reader
composition now writes accepted values and their evidence before returning an assessment. This is
offline-tested integration, not native source qualification, a running host or execution authority.

`AiPortfolioCatalogRepository` now reads maintained account choices and exact complete definitions:
daily strategy/model identity, policy, position count, gate profile and implementation identity. Fixed
comparison rules cannot fall back to ordinary assignments. Unknown/inconsistent definitions stay
visible as unavailable; same display names do not merge accounts. Definition fingerprints exclude
display renames and include gate identity. This uses current saved catalogue rows, not a fixed list
of fifteen or a winner recommendation.

`AiPortfolioPacketBuilder` combines that catalogue with the managed journal's current accepted source
observation and explicitly dated, version-matching engine contexts. Candidate definitions contain no
account balances; actual funds/history come only from the managed journal. Rejected/stale reads,
changed journal revisions, unknown active choices and conflicting identities cannot create a packet.
Unavailable engine context keeps its choice visible but unselectable. Context rules retain their
declared discretion; the app protection boundary remains hard. Missing AI attention is the reason
for requesting a review, not an exception to any other saved hold or execution recheck.

Packets use the existing proposal/learning contracts; expiry is no later than their contributing
inputs and the reviewer freezes collections before model work. September 28 adds the Daily Shadow
saved-context reader: exact adopted publication/model/policy/gate identity, published rankings and
gate traces, current-source candidate decisions, and bounded daily paper returns. Existing gate
failures are retained separately from comparison bypass. Known daily trading gates and ordinary
entry/rotation guidance are discretionary; source validity, cash/exposure and established protection
remain hard. Unknown gates are conservatively hard and a recorded failure blocks usable context.
Stale/missing/partial/data-blocked decisions and an absence of evaluated admitted candidates cannot
supply selectable context. Rereads never renew decision timestamps. Paper history may span earlier
policies and grants neither available funds, superiority nor managed-stock entry/re-entry authority.
Delphi Live now has an equivalent saved-context reader. It retains current source/gate/policy/model
identity and process-continuity epoch, preserves invalid/conflicted latest checkpoints, and discloses
the candidate account's own lifecycle separately from shared market judgments. Neither shared nor
candidate-account confirmation transfers to managed-account entry/re-entry. Market endpoints bound
expiry; later saves or rereads cannot refresh them. Daily mark history preserves incomplete latest
values. Signal/entry guidance is discretionary; source validity, cash/exposure, existing stops and
protective exits remain hard. No signal recalculation or operating strategy changes occur here.
The native compatibility fix preserves the existing legacy Shadow contract: absence of an original
assignment means frozen `SystemShadowV1`, using the same identity as strategy promotion. Fixed
unversioned comparisons must retain pinned V1 settings; an explicit incomplete or corrupt assignment
cannot use this fallback. This does not substitute new-strategy defaults or alter saved policy.
Initial native mapping evidence is in project status. Remaining SQL/source coverage,
raw-bar/conflict requalification, prompt-size/API-cost qualification
and the operating host remain pending.
No paid model request, strategy assignment or order follows from building a packet.

This increment cannot adjust cash flows or qualify a feed. Matched cash-event
integration, overall 10% guard calculation/release, stock-stop enforcement, current-account/exposure
checks, ordinary-engine adapters, production storage/host integration and execution remain separate dependencies. Existing
holds are supplied as independent blocks; this core cannot clear them. Existing runtime engines
are unchanged and no production caller is connected. Validation evidence belongs in project status.

## Lesson criteria deferred — September 27 decision

The operator wants to continue delivery without specifying what qualifies as a learned lesson now.
Leave admission criteria open-ended and revisit them when they become relevant to Step 6. They are
not a blocker for the current operating contract or data/risk/execution work. Preserve the learning
feature and consultation hooks; no thresholds are selected and hypotheses do not automatically become
evaluated lessons through this deferral.

## Daily buying pause — September 26 decision

**Partly superseded for AI Momentum:** the [continuous-recovery amendment](#continuous-recovery--september-27-amendment)
below replaces the daily framing. The existing engine templates implemented here remain unchanged.

The operator selects **7%** as AI Momentum's non-overridable daily buying-pause threshold and
requests the same default in the existing engines. This supersedes the proposed 3% AI threshold
and the 3% default for newly created Delphi Live and System Shadow strategy templates. It does
not rewrite frozen V1 definitions or stored/assigned strategies under ADR-0053 and ADR-0060.

The settings editor now uses explicit `Default` templates with `DailyLossGuardFraction = 0.07`.
Delphi Live's template has a distinct policy identity; saving still creates an immutable version,
and assignment remains a separate operation. Legacy V1 runtime fallbacks and historical replay
retain their original settings. No active assignment or database is changed by this source update.

With the new templates, the engines pause new buys at a session return of -7% or worse against session-opening
portfolio value. Protective exits continue; the threshold does not change position stops or the
10% capital-review threshold and cannot clear an existing pause. The AI cannot waive its accepted
7% boundary by selecting a more permissive strategy. Wiring that boundary into AI execution is
Step 4 work. The amendment below now governs the unresolved AI loss-reference/recovery design.
A buying pause is not a guaranteed maximum realized loss.

## Gradual allocation scale — September 27 decision

**Numeric boundary superseded:** the [cycle schedule](#three-cycle-loss-schedule--september-27-decision)
below replaces the fixed 7% AI pause. The later within-round sizing decision maps the gradual scale
to those cycle limits. The original fixed-7% formula below is historical.

The immediate problem is reducing new buying before the 7% pause; the parent problem is bounding
AI discretion within its selected strategy. The root goal remains protected, auditable paper and
intended real-account management. The operator accepts a linear reduction in each stock's permitted
allocation: 100% of its normal strategy allowance at no measured loss, 50% at a 3.5% loss and zero at
7% or worse. For a nonnegative measured loss fraction `loss`, the multiplier is
`max(0, 1 - loss / 0.07)`, capped at 1. Gains do not enlarge the normal allowance.

This is a ceiling on the stock's combined allocation for buying, including initial purchases and
additions, not a fresh allowance for each order. Splitting purchases cannot bypass it. The AI may
choose less; cash, exposure and other hard limits can further restrict buying. Existing holdings
continue under their exit/protection rules; the reduced allowance alone does not force a sale.
Existing exposure at or above the reduced allowance leaves no room for an additional purchase.

This is an accepted AI Momentum design, not an implemented or installed allocation control, and
does not add the scale to other engines automatically. Step 4 must enforce it and account for
holdings and outstanding buy commitments. Step 2 still must settle the continuous loss reference
and recovery details under the amendment below. Existing engine holds remain authoritative until
an explicit integration contract is implemented.

### Same-session recovery — September 27 decision

**Superseded:** [continuous recovery](#continuous-recovery--september-27-amendment) replaces the
session-minimum decision below. It was recorded as design only and was never implemented.

The operator accepts retaining the session's lowest allocation multiplier as the MVP behavior.
At each valid observation, use the lower of the saved session multiplier and the newly calculated
multiplier. A rebound after a 3.5% loss therefore leaves the ceiling at 50% of the normal allowance;
deeper losses can reduce it further. Reaching 7% keeps new buying paused for the rest of that session.
This applies to the risk multiplier, not a promise of constant dollar buying power: other limits,
account value, holdings and buy commitments still govern availability.

The reason is predictable behavior and straightforward validation, not demonstrated profitability.
Revisit the choice through reviewed paper evidence. Preserve the session minimum across restart
and portfolio-context switches so neither can reset this boundary. This design is not yet implemented
or installed. Next-session reset conditions remain unresolved; retaining a same-day minimum does
not authorize clearing capital-review, account-validity or other independent holds.

### Continuous recovery — September 27 amendment

**Later refinement:** the [cycle schedule](#three-cycle-loss-schedule--september-27-decision) replaces
the 7% AI boundary. The later continuous-sequence decision makes the three attempts span sessions,
with a pause after exhaustion; it does not erase cumulative history.

The operator prefers ongoing portfolio management over daily boundaries and endorses allowance
falling with losses and gradually returning as those losses recover, across trading days. This
supersedes the same-session minimum above and the earlier daily framing of the AI scale. A new
session alone does not reset allowance. The proposed next-day reset was never accepted.

Retain the gradual 0–7% scale and combined per-stock buying ceiling, including additions. Restoration
must follow verified portfolio-value recovery, not AI confidence, a restart or switching portfolio
context. Protective exits and hard position stops continue independently. This direction better
matches the requested ongoing behavior; it is not evidence of superior trading returns.

The operator subsequently selects both starting capital and the recorded portfolio peak as loss
references; see the clarification below. Define cash-flow adjustments, qualified valuation,
combination of the two measures, interaction with existing daily/capital-review holds and resumption
at the zero-allowance boundary before implementation. No automatic rebase, clearing of independent
holds or change to the existing engines' 7% daily templates is authorized by this design.
This remains Step 2 agreement; source enforcement, tests and activation are pending.

### Both loss references and continued monitoring — September 27 clarification

The operator requires tracking losses against both starting balance and the highest recorded
portfolio value, preserving protection as profits increase. This resolves the earlier either/or
question; it does not yet specify how the two measures determine the allocation multiplier or
authorize a new portfolio-wide liquidation threshold. Peak sampling and cash-flow adjustment must
be explicit. A starting balance is not the current day's opening balance.

Source inspection confirms existing position protection to reuse: `SystemShadowPolicy.HardStopPrice`
uses acquisition cost, and `EvaluateFifteenMinuteClose` raises an armed trailing floor from completed
15-minute close highs without lowering it. `DelphiLiveSafetyPolicy` similarly raises its profit floor
using completed five-minute closes after activation. Portfolio guards are separate: System Shadow
and `DelphiLivePortfolioPolicy.EvaluateGuards` measure daily loss from opening value and drawdown
from the highest completed closing value; the latter triggers capital review. These are not already
the new AI continuous allowance or a portfolio-wide trailing sell rule. This is read-only source
verification, not a new test run or operational verification.

The operator wants monitoring to continue while in cash and automatic pickup when conditions
recover. Preserve that intent; the exact qualifying market signal and permitted restart allocation
remain open. A recovery in watched stocks can produce a new opportunity but cannot increase an
all-cash portfolio's trading value. Therefore market-based re-entry requires an explicit recovery
transition at zero allowance, without erasing actual losses, lowering retained protective stops or
bypassing independent holds. The later fresh-entry decision below selects the existing engine's
entry checks for this transition; it does not settle restart sizing or permit bypassing hard limits.

### Three-cycle loss schedule — September 27 decision

The operator accepts 5% / 3% / 1% for successive failed portfolio rounds, replacing the earlier
7% AI buying-pause boundary. The original round-start reference is superseded by the round-trailing
decision below: use the highest verified value within the round, initialized at its start.
After the third failed round, stop new buying for
the remainder of the trading day; independent protective exits continue. Monitoring may continue
without granting another attempt. The equal 5% / 5% / 5% alternative is not selected.

For three consecutive losses exactly at these triggers, with no intervening gains, cash flows or
additional execution costs, the remaining fraction is `0.95 * 0.97 * 0.99 = 0.912285`: an 8.7715%
combined loss. This is illustrative arithmetic, not a guaranteed maximum loss or performance evidence.
Cycle thresholds do not rewrite individual position stops. Existing engine daily templates remain
at 7%; integrating the AI schedule with underlying engine holds still requires an explicit contract.

The immediate purpose is to bound repeated attempts while continuing to watch for new entries;
the parent goal remains AI discretion within enforceable protection. Keep original starting balance,
portfolio peaks and actual loss history across cycles. A retry counter cannot clear independent
account/data/capital-review holds. The later cumulative-review decision below retains the existing
10% capital-review safeguard for AI Momentum.

This is an accepted Step 2 design, not implemented or activated. The failed-round transition is
accepted below, along with fresh-entry qualification and within-round sizing. Next define
qualified valuation/cash-event sources. Cash-flow percentage preservation is accepted below.
The later continuous-sequence and ordinary-sale decisions
settle attempt counting across sessions and profitable/all-cash transitions.
The later decisions govern trailing round references, carried rounds, exhausted-sequence restart
and session-start consultation.
These decisions do not authorize running liquidation or changing real trading in this session.

### Failed-round closure — September 27 decision

The operator accepts closing the remaining positions managed by the AI when the round reaches its
loss threshold. Stop new buys for that round, reconcile its pending orders and close the managed
holdings through the existing execution path. Wait for confirmed fills and reconciled holdings/cash
before establishing another round. An order acknowledgement, partial sale or uncertain outcome is
not completion; keep the failed round in its closing state until remaining exposure and commitments
are resolved. Reuse the exit-before-buy and original-order recovery contracts rather than creating
another execution mechanism. Positions outside AI management are outside this liquidation scope.

After confirmed closure, continue monitoring and require fresh entry signals before another permitted
attempt. After the third failure, monitoring/protection continue but no new buying is allowed that
day. Preserve original capital, portfolio peaks and actual losses; the next round's starting value
reflects confirmed results. This gives the failed round a definite end and prevents granting a new
loss allowance while its old positions remain open. Shrinking the gradual allowance alone still does
not force a sale: reaching the round's hard loss threshold is the accepted closure trigger.

This is design agreement only, not broker authority or an installed workflow. The later next-session
decision covers exhausted sequences; the continuous-sequence amendment below settles carried counts.

### Fresh-entry qualification — September 27 decision

The operator accepts reusing the selected engine's entry logic for another permitted round. After
the failed round's sales are confirmed and reconciled, wait for a fresh qualifying entry signal
from that engine, based on post-closure evidence. A stale signal from before the sales cannot
restart trading. Preserve the engine's existing evidence freshness and confirmation requirements;
do not add a separate fixed waiting period. The AI may take the qualified opportunity or continue
waiting; a signal is eligibility, not an instruction to buy.

This resolves the immediate restart-trigger question while reusing existing components for the
parent goal of a small, protected AI execution workflow. Re-entry still needs an available attempt,
valid account/data/order state and all hard protection checks. A signal cannot override the
third-failure daily stop or erase independent holds. Rule classification and sizing remain separate
decisions. This is accepted design only; signal adapters, enforcement and host integration remain
unfinished, with no runtime, database or broker change in this increment.

### Gradual sizing within rounds — September 27 decision

The operator accepts applying the proportional buying scale separately within each round. For a
nonnegative round loss fraction `loss`, measured from the round's highest verified value under the
later trailing decision, and that round's limit `limit` (0.05, 0.03 or 0.01), use
`max(0, min(1, 1 - loss / limit))` as the multiplier on the selected strategy's normal per-stock
allocation. A 2.5% loss in round 1, 1.5% in round 2 or 0.5% in round 3 therefore leaves 50% of the
normal allowance. Initial buys and additions share that ceiling, accounting for existing holdings
and outstanding buy commitments; splitting orders cannot create another allowance.

Before the round's hard limit is reached, a reduction in measured losses restores allowance
proportionally, up to 100% of normal. Gains never increase the multiplier above 1. The AI can choose
less, and other hard limits can reduce or block buying. Shrinking the ceiling alone does not force
a sale. Reaching the hard limit instead ends the round and starts its accepted closure workflow;
a subsequent rebound does not cancel that failed-round state or permit immediate reuse of its
allowance. A new permitted round requires confirmed closure and a fresh qualifying entry signal.

This resolves how the earlier gradual-sizing principle applies to the selected three-round schedule.
It does not erase original capital/peak history. The later trailing decision resolves the round's
loss reference; ordinary-sale handling and cross-session counting are settled below.
The formula and examples are accepted design, not implemented/tested runtime behavior.

### Round limits trail verified gains — September 27 decision

The operator accepts measuring each round's 5% / 3% / 1% limit from its highest verified portfolio
value during that round, initialized with its opening value. This supersedes the fixed round-start
reference in the original cycle schedule. Preserve the opening value as history; for a positive
round reference `H` and qualified current value `V`, update `H = max(H, V)` and calculate
`loss = max(0, (H - V) / H)`. The round exit trigger is `H * (1 - limit)`. This same loss fraction
drives the accepted buying-allowance multiplier `max(0, min(1, 1 - loss / limit))`.

For a 5% round starting at CAD 10,000, the trigger is CAD 9,500. A verified rise to CAD 11,000 raises
it to CAD 10,450; subsequent price declines do not lower it. At CAD 10,725 the drawdown is 2.5%
and the buying multiplier is 50%. Recovery before the hard limit restores allowance, while a hit
commits to the existing failed-round closure even if value later rebounds. A trigger is not a
guaranteed fill value. The no-intervening-gains three-loss illustration above remains conditional.

Carry the round's peak and trigger across nights/weekends and process restarts without a calendar
reset. A new permitted round initializes a new reference only after confirmed closure/reconciliation;
retain original capital and all prior round history. The independent 10% guard continues to use the
highest recorded completed-session closing value and retains its explicit-approval hold. Existing
position trailing stops and engine holds remain separate constraints.

Use qualified portfolio valuations, not an unverified tick, AI estimate or a high from an unrelated
period. The later independent-protection decision checks every fresh verified portfolio update.
Source qualification and collection frequency remain open; confirmed cash-flow adjustment is
accepted below under the existing accounting contract. The simple formulas above describe intervals without external cash
flows. This decision does not authorize treating deposits as profit or withdrawals as trading loss.
The Step 8 UI must show the round's opening value, current verified value, recorded round peak,
trailing exit trigger, drawdown and remaining room using enforcement values and freshness labels.
This is accepted design only: enforcement, tests and UI implementation remain pending.

### Starting capital and initial thresholds — September 27 clarification

The immediate issue is calculating the initial dollar limits, within the continuous-round contract
for protected AI management. Capture the actual capital allocated when the portfolio starts and
preserve it as original-capital history. At each permitted round's start, record its actual verified
opening capital and calculate its initial trigger as `openingCapital * (1 - roundLimit)`.
For CAD 10,000 in round 1, the 5% limit initially gives CAD 9,500.

The operator confirms that verified gains raise the active threshold under the existing trailing
formula. Rounds 2 and 3 use the actual remaining capital when they start, after required closure
and reconciliation, at 3% and 1% respectively. Any later-round thresholds shown upfront are labelled
estimates, not enforceable values or assumptions about future fills. Retain the original capital
alongside each round's opening record; no daily, ordinary-sale or portfolio-switch reset is added.

This confirms initialization and trailing behavior. The later independent-protection decision
supersedes the proposed five-minute-only threshold checks. Qualified valuation sources, collection
frequency and cash-event qualification remain open; the adjustment rule is accepted below.
Enforcement and UI remain pending.

### Confirmed capital flows preserve loss percentages — September 27 decision

The immediate issue is distinguishing deposits/withdrawals from trading performance. The parent
problem is maintaining continuous risk references; the root goal remains protected AI management
without resetting loss history. Reuse ADR-0083's accepted percentage-preserving adjustment for
confirmed external capital movements affecting the AI portfolio. A portfolio 2% below its adjusted
peak remains 2% below after a deposit or withdrawal, with prices and holdings otherwise unchanged.

For each confirmed movement, let `B` and `A` be positive portfolio values immediately before and
after its cash effect, using identical holdings and price evidence. Each applicable effective risk
reference `R` becomes `R * A / B`, without rounding the stored reference to cents. Apply this to
the active round's effective peak and the independent overall drawdown reference; derive the round
trigger and allowance from the adjusted reference. Retain original allocated capital, original
round-opening records and historical observations, alongside an auditable adjustment record.

A transfer alone changes neither measured drawdown nor the allocation multiplier. It cannot
consume/refill attempts, change round identity or percentage limits, cancel a failed-round closure,
clear a pause (including the 10% approval hold), or alter per-stock costs/stops. A withdrawal can
lower the capital-adjusted dollar reference/trigger; this is accounting normalization, not permitted
stop loosening. Price declines still cannot lower the trailing reference. Actual available cash
and all other execution checks continue to constrain buying.

Reuse matched before/after books, confirmed effective events, ordered processing and replay
protection from ADR-0083. Opposing flows cannot be replaced by their net total. Missing/conflicting
evidence or nonpositive values do not authorize invented adjustments or a reset; retain the
existing unexplained-cash restriction on new buying until reconciliation. Deposits are not gains
and withdrawals are not trading losses.

Read-only inspection confirms the existing `BrokerAccountCashFlowContinuation` scaling contract.
Its AI round integration and native cash-event source qualification remain pending in Steps 3/4/7;
no new runtime tests, database or broker operations accompany this documentation decision.

### Per-stock protection and blocked alternatives for learning — September 27 decision

The operator accepts reusing the selected portfolio engine's existing hard-stop and trailing-profit
settings for individual holdings. These are separate from the portfolio round's 5% / 3% / 1% limits;
do not copy the round percentages onto each stock. The AI may propose selling earlier or tightening
a stock's established stop, but may never loosen or remove that stop. An accepted tighter stop
becomes the retained protection floor; a later proposal cannot restore the earlier, looser level.
Other exit signals may be discretionary only where explicitly classified that way.

Enforce this boundary in TraderVI code, independently of prompts, model reasoning and learning.
The proposal validator and execution path must check the currently enforced stop and policy before
applying a change, preserve engine-required protection and reject prohibited proposals. Recheck at
execution so an older proposal cannot loosen a stop that has since tightened. The AI proposes;
it has no direct path to changing protection or submitting an unchecked action. Existing account,
data, order, quantity and execution checks still apply to early sells and tightening proposals.

The operator also wants to learn from discretion the boundary prevents. Permit a separate,
non-executable statement that the AI wanted to loosen/remove a stop, including its specific
alternative, reason and contemporaneous evidence. Log the holding/strategy identity, current stop,
requested change, blocking rule and time, and the actual enforced outcome. Explicitly label it
blocked/not executed. An executable hard-rule override remains invalid; recording the desire must
not turn it into an allowed action or bypass the validator.

Feed these records to the learning workflow for later evaluation of what would have happened under
the proposed alternative. Preserve actual outcomes separately from simulated alternatives, with
the comparison horizon/exit assumptions fixed before judging results and missing evidence marked
unevaluable. Assess frequency of improvement and magnitude of gains/losses; a majority of small
wins alone does not establish an improvement. Exact qualification/expiry criteria remain Step 2
learning work. Evaluated lessons may inform permitted future choices or an operator's later policy
review, but cannot automatically loosen stops, remove protection or rewrite hard boundaries.

Source inspection confirms `AiPortfolioReviewer` rejects overrides of `IsHardBoundary` rules and
`AiLearningAwareSupervisor` journals rejected reviews, with raw responses in the call journal.
That foundation handles portfolio proposals only. It does not implement stock-stop actions, a
structured blocked-alternative record, outcome comparisons or live enforcement of these new actions.
Those remain Steps 4/6 integration and validation work. No runtime, provider or broker action ran.

### Portfolio switching with open holdings — September 27 decision

The operator accepts the initial AI switching behavior: retain holdings permitted by both the
outgoing and incoming portfolio, preserve their established stops, and sell AI-managed holdings
the incoming portfolio does not permit. Confirm and reconcile required sales before buying
replacements; anticipated proceeds or an order acknowledgement are not available funds. Reuse
the established handover contracts in ADR-0079/0081/0085/0086, including actual position identity,
quantities, costs and history. This is not permission to copy paper holdings into the live account.

A switch does not reset the active round's opening reference, saved peak, loss limit, attempts used,
overall review hold or other risk history, even if no holdings overlap and the transition passes
through cash. A strategy change alone does not create a new round. Existing buying allowance and
independent holds still constrain the incoming strategy. Preserve tighter stops already established
by either the engine or AI; a looser incoming setting cannot replace them.

Retained holdings keep their existing protection owner during required exits and reconciliation;
transfer responsibility once at the recorded activation boundary. Preserve original order identities
and reservations through partial/uncertain outcomes, with protective handling continuing. Use the
existing execution checks and supported engine contracts; changing AI context is not activation.
Log the outgoing/incoming portfolio versions, reason for switching, retained/exited holdings and
handover outcome. The accepted unavailable-review fallback still blocks new switches.

The catalogue scope is now agreed below; rule-to-holding compatibility mapping and execution adapters
remain to be qualified. This decision does not supersede manual policy-edit pause/empty rules or broaden
management authority to unrelated manual holdings. Existing source provides handover foundations;
automatic AI strategy replacement is not implemented or activated. Integration and offline recovery
validation remain Step 5 work; no broker, database, assignment or runtime change ran here.

### Portfolio eligibility and catalogue growth — September 27 decision

The immediate question is which portfolios the AI may select. The parent problem is giving AI
Momentum strategy choices while preserving their rules and protection; the root goal remains
auditable paper management and deliberate adoption for the permanent TFSA.

The operator accepts all current strategy portfolios in the Portfolios comparison catalogue as
AI Momentum choices, including position-count and daily-gate variants. The supplied screen reports
15 paper portfolios; this is a screen observation, not a fixed limit or a complete enumeration of
their identities. Do not restrict scope to a preferred subset or the initial Breakout candidate.
Map each choice to its actual portfolio, strategy version, gate profile and engine contract;
similar display names do not make variants interchangeable.

The operator wants more portfolios developed over time to expand the AI's choices. Use the maintained
strategy catalogue rather than hard-coding today's list or count. Include new portfolios as they are
built and integrated under the same selection and protection contract. Specific new designs and
development order remain future work; the AI is not authorized to create strategies or edit rules itself.

Catalogue mapping and packet construction are now implemented offline; native and execution adapter
coverage is not yet qualified. The catalogue surfaces unsupported choices and their reasons until evidence, compatibility and execution
adapters are ready. Every selection still needs current data/account/order checks. Every switch
preserves the agreed holdings/protection handover, continuous round history and independent holds.
Paper histories remain separate from the managed account; selection does not authorize live activation.

This resolves portfolio eligibility in Step 2. Steps 3/5 own mapping and integration across all current
variants, followed by connected paper validation and native live qualification in Steps 9/10.
Evaluated-lesson admission remains deferred. Catalogue code does not change an assignment or enable execution.

### Cumulative review and visible thresholds — September 27 decision

The operator accepts retaining the existing 10% drawdown review for AI Momentum: when portfolio
value falls 10% or more below its highest recorded completed-session closing value, pause new buys
and require explicit operator approval before they may resume. Protective exits continue. This
guard takes precedence even when a sequence attempt remains. Neither a new day, a new round, a fresh
entry signal, an AI decision nor a subsequent rebound clears the review hold. Reuse the established
review/audit and reference-handling contracts; approval is not a bypass of other hard checks. The
review pause itself does not add a liquidation rule; reaching a round's loss limit still requires
the separately accepted round closure. Existing engine settings are unchanged.

The immediate UI requirement is to make distance to the enforced limits visible, so the operator
can understand the AI portfolio's available discretion and why it is paused. Include an explicit
threshold summary in the AI portfolio/live-strategy UI (Step 8), with the current measurement,
limit, remaining room, reference value and action at the boundary. Keep paper/live context clear.
At minimum cover the overall 10% review threshold, the active round's 5%/3%/1% threshold, current
buying-allowance multiplier, failed rounds and attempts remaining, and relevant per-position hard
stops/trailing floors. Show applicable cash/exposure constraints and shared monthly AI spending
against its configured cap in the appropriate portfolio/holding/budget views.

Use plain labels and explicit numbers; color alone is insufficient. Show percentage-point distance
for percentage loss comparisons and a monetary/price distance where meaningful, using the same
references and calculations as enforcement. Identify any blocking rule and its required next action,
especially "Review required — new buys paused; protective exits active" for the cumulative hold.
A recovered value must not make an uncleared hold appear ready. Expose freshness/unavailable state
instead of showing missing or stale measurements as unused risk capacity. These are requirements,
not a rendered or implemented UI; tests, installation and operational qualification remain pending.

### Continuous rounds and timed rule consultation — September 27 decision

The operator accepts carrying an unfinished round across nights, weekends and market closures with
its existing holdings, saved round peak/reference, applicable 5%/3%/1% limit and protective levels,
subject to the selected strategy's existing exit rules. A date change alone does not end a round,
reset its loss reference, loosen a trailing stop or clear a review hold. This is continuity of saved
state, not permission to trade while the market is closed or to treat old prices as current evidence.
Existing market-hours, valuation, order-reconciliation and protection contracts still apply.

At the beginning of a fresh trading session, the AI must consult the engine's current entry guidance
when qualified fresh results are available and reconsider its decisions using that information.
Use the selected portfolio's authoritative engine/source and current holdings, risk and evaluated
learning context. The review may support holding, waiting, taking an eligible opportunity or proposing
a supported change; the new session itself creates no order, forced switch or new loss allowance.
Missing/stale entry guidance cannot be presented as a fresh review, and protective handling must not
wait for a model call. No exact clock time or new stale-data exception is selected here.

The operator also requests deliberate review points at key parts of the trading day, where the AI
consults the relevant rule sets and records which apply and why. This connects continuous portfolio
management to current context without making the AI responsible for every protective tick. The
later clarification below accepts three consult checkpoints plus continuing intraday monitoring.
Exact clock times and detailed rule-to-checkpoint mapping remain to be chosen. All reviews and learning share the
existing AI spending ceiling. Budget exhaustion/provider failure cannot bypass protection or risk
holds; the accepted unavailable-review fallback is recorded below.

The exhausted-sequence restart and continuous attempt counting are agreed below. A new session
does not renew attempts or reset an active round's limit. This is Step 2
design agreement. Step 7 must implement and qualify scheduling, including restart/missed-checkpoint
handling. No scheduler, automation, paid call, runtime change or trading action is installed here.

### Next-session restart and UI explanation — September 27 decision

After all three attempts have failed and their AI-managed positions are closed with orders/fills/cash
reconciled, the operator accepts eligibility for a fresh 5% / 3% / 1% sequence at the next trading
session. Restart still requires a fresh qualifying entry signal and a successful fresh AI review,
with all other limits satisfied. This is permission to reconsider entry, not an automatic order at
the open. Use the actual next market session, including weekends/holidays, not a midnight reset.

An unfinished round carried overnight keeps its existing loss reference, applicable limit and
protective levels. The overall 10% review hold still requires explicit operator approval; a new
session or renewed attempts cannot clear it. Preserve capital/peak history and other independent
holds. The continuous-sequence amendment below settles counting after fewer failures and a carried
round failing in a later session. The ordinary-sale decision below settles profitable/all-cash transitions.

The operator explicitly requires the UI to explain this behavior. Show attempts used/remaining for
the continuous sequence, active round number and loss limit/reference, and the reason buying is
paused. Distinguish "Paused until next trading session" (three failed attempts, exits reconciled),
"Unfinished round carried forward" (same limit/reference), and "Your approval required" (10% hold).
If closure/reconciliation is incomplete, show that pending state rather than restart eligibility.
For an exhausted sequence show the actual next eligible market session and that fresh entry evidence
and AI review are still required. If multiple holds apply, show them together; the session countdown
must not imply approval or override budget/data/order/risk blocks. Display enforced state and source
freshness, not independently calculated UI permission. This is a Step 8 requirement, not built UI.
State-machine enforcement and restart validation remain pending in Steps 4/7; no runtime changed.

### Continuous attempt sequence — September 27 amendment

The immediate issue is whether opening a new session refills attempts. The parent problem is a
consistent round lifecycle for continuous AI management; the root goal is auditable paper and
intended live trading within enforced protection. The operator accepts carrying the entire
5% / 3% / 1% sequence across sessions, without daily renumbering or a fresh daily allowance.
This supersedes earlier daily-counter wording; the proposed morning renumbering was not implemented.

Round 2 carried overnight remains round 2 at 3%, with the same opening reference, saved peak,
trigger, holdings and protection. If it fails in a later session, confirmed/reconciled closure and
a fresh qualifying signal may permit round 3 at 1%, subject to all other checks. The sequence can
span several trading days; one or two failures do not disappear at a session boundary. Session-start
consultation updates trading context without resetting round identity, limits or attempts.

After the third failure of that sequence, pause new buying for the remainder of that trading
session. Retain the accepted next-session restart conditions above: all failed-round exits and
commitments must be reconciled, with fresh entry evidence, a successful fresh AI review and all
other limits satisfied before a new 5% / 3% / 1% sequence can begin. Monitoring/protection continue.
The separate 10% approval hold and original capital/peak history persist. UI counters must describe
the sequence, not imply three new attempts every morning.

This resolves cross-session failure counting. Qualified valuation/cash-event sources remain open;
ordinary-sale handling is settled below. Enforcement, recovery tests and UI remain Steps 4/7/8 work;
this documentation decision does not change runtime or authorize orders.

### Ordinary sales preserve the round — September 27 decision

The immediate issue is whether taking profit or becoming entirely cash starts a new risk round.
The parent problem is a consistent continuous lifecycle; the root goal remains auditable AI
management within enforced protection. The operator accepts keeping the same round and saved peak
through ordinary stock sales, including profitable exits and selling all holdings. A completed
stock trade is not a completed portfolio risk round.

Retain round identity, opening reference, peak, loss limit, trailing trigger and attempt count.
Buying again uses the same round and its current allowance, subject to existing entry, account,
order and other hard checks. Selling/rebuying cannot reset protection or refill attempts. Only
reaching the round's hard loss limit consumes an attempt; a hit remains a failed round even if
the portfolio is already in cash or subsequently recovers. Confirm/reconcile remaining orders,
holdings and cash before the next permitted round under the existing closure contract.

This resolves the previously open successful-round handling: there is no separate success reset.
Qualified gains can still raise the peak and recovery before a limit hit can restore allowance
under the existing formula. Valuation/cash-event qualification remains open. Enforcement and tests are
Step 4 work; no runtime or trading action changed in this documentation increment.

### Scheduled consults and adaptive intraday attention — September 27 clarification

The operator accepts the three proposed consults: session start with fresh daily entry guidance,
mid-session reassessment of holdings/opportunities, and a pre-close review of exits/overnight
holdings. These are additional rule consultations, not a replacement for ordinary intraday Delphi
Live checks. Keep the regular intraday monitor running and feed its dated observations and signals
to the AI throughout the session. Exact checkpoint times remain open; provider/budget fallback is
agreed below.

The operator requests AI input into intraday frequency: increasing attention when activity warrants
it and reducing it when conditions are quiet. On September 27, the operator confirms one minute as
the fastest permitted AI review interval and fifteen minutes as the slowest during market hours,
subject to the shared budget. These replace the earlier illustrative intervals with accepted bounds
for scheduled AI reviews. They do not guarantee a completed review every fifteen minutes when budget,
provider or data availability prevents one. The provider/budget fallback is agreed below; event and
stale-data timing details remain open.

Separate market collection, deterministic monitoring/protection and paid AI review scheduling.
Source inspection finds a 30-second host tick in `DelphiLiveDesktopService` composition, while the
versioned Delphi Live signal policy uses completed five-minute bars and a two-minute collection
offset. A faster scheduler tick is not a fresh one-minute signal bar, and a one-minute AI review
cannot manufacture unavailable evidence. Do not silently change the policy's bar interval, data
source contract or protective cadence under an AI frequency request.

The recommended MVP mechanism is for each AI review to propose its next review interval and reason,
with the host enforcing the accepted one-to-fifteen-minute bounds, fresh-data availability, concurrency and the shared
monthly budget. The independent-protection decision below settles ownership: AI review timing cannot
control or slow required protection or its data collection. Because the model is not reasoning
between calls, the local monitor must be able to request an earlier review when defined material
events occur; the initial triggers and unpaced shared-budget policy are agreed below. Checkpoint
priority remains an implementation detail. No promise of one
paid call every minute, guaranteed response latency or full-session minute-level coverage is made.

Log requested and applied intervals, reasons, evidence times and any budget/data constraint that
changes or rejects a request. Show the actual review cadence and next scheduled review in the AI
status UI. All intraday reviews, scheduled consults, learning and any retries use the same spending
ledger and initial below-CAD-100 ceiling, subject only to an explicit operator increase as described
below. Independent protection continues if no paid review can run. This
is design/source inspection only: no monitor, timer, provider request or runtime configuration changed.

### Independent protection on fresh portfolio updates — September 27 decision

The immediate issue is whether loss checks wait for AI polling. The parent problem is timely
protection during adaptive AI attention; the root goal remains continuous management within
application-enforced boundaries. The operator accepts an independent deterministic check of the
round loss threshold whenever fresh, verified portfolio data produces a qualified valuation.
This supersedes the proposed restriction to completed five-minute Delphi Live valuations.

Include every qualified one-minute update when such data is available. The check requires no AI
call and does not wait for the next paid review. Slower AI reviews, provider failure or exhausted
AI budget cannot slow required protection or its collection. Existing per-stock stops and engine
exit checks retain their own contracts; this decision does not change their bar semantics.

Run checks on new qualified portfolio evidence, not a busy loop over unchanged prices. A poll
attempt, failed read, repeated cached value or isolated holding quote is not by itself a new
verified whole-portfolio valuation. Data freshness and coverage must remain explicit; stale or
incomplete evidence cannot manufacture a new peak or imply fresh protection coverage.

The current five-minute Delphi Live bars do not supply fresh one-minute portfolio evidence.
Steps 3/7 must qualify a suitable price source, whole-portfolio valuation coverage, collection
frequency and recovery before claiming minute-level protection. Faster AI reviews alone do not
satisfy this dependency or authorize external collection. Preserve versioned signal-bar behavior.
The AI controls its review attention within accepted bounds; the app owns independent protection.
This is accepted design only; source/feed integration, enforcement tests and UI remain pending.

### Initial intraday review triggers — September 27 decision

The operator accepts three initial events that can bring an AI review forward:

- A new qualifying entry signal appears from the selected engine.
- A holding closes, freeing cash or a portfolio slot; use confirmed/reconciled closure evidence.
- A risk limit changes what the portfolio is allowed to do.

Combine events arriving together into a review request, respect the one-minute minimum review
interval and check the shared budget. These events request reconsideration, not an automatic buy,
switch or override. Protective actions continue immediately under existing execution contracts
without waiting for the AI. Record the triggering evidence with the review and any deferral reason.

The operator expects more hooks later. Keep this initial list extensible through the same review
request path and shared timing/budget checks; the MVP does not require a separate framework or
independent AI caller for each event. No additional trigger is selected here. Exact event identity,
duplicate handling, concurrency and checkpoint priority remain host implementation/qualification
details. This is agreed Step 2 behavior; runtime integration and tests remain pending in Step 7.

### Review scheduling source increment — September 28

The operator authorizes starting Step 7 independently while Step 3's native fresh-price check awaits
a regular session. `AiReviewScheduler` implements one reviewed session in memory. Exact checkpoint
times remain open and must be supplied; no operating defaults are selected. Requested intervals and
reasons are retained with the applied 1–15-minute bound. This is an explicit input hook; the model's
structured next-review proposal remains to be integrated. One active request combines due checkpoints
and identified initial events. Exact duplicates do not repeat work; changed event identity or bounded
event-history overflow requires attention. Late checkpoints share one current review, not historical
catch-up calls. Events arriving during a call remain pending. At least one minute separates a completed
attempt from the next admission, including slow preparation, failure and cancellation.

`AiScheduledPortfolioReviewer` invokes the existing learning-aware supervisor on explicit ticks.
Trigger evidence/reasons and interval choices join the saved private input packet. The supervisor
also returns its saved enriched evidence so review freshness cannot outlast a consulted lesson.
Spending stays in the existing shared call journal; there is no daily pacing, separate allowance or
spending reset. Failed/stale/cancelled reviews keep a review block; a fresh successful result is needed
to clear that block. Pending newer events suppress returning the older proposal. This status does not
clear independent account/risk holds or grant execution authority. Collection/protection has no dependency
on this paid-review adapter.

The follow-on September 28 increment adds `AiReviewScheduleJournal`: account/portfolio/mode-scoped
history with explicit initialization, one writer, checked transition replay and save-before-admission.
Persist event requests/conflicts, interval changes, admitted review IDs, results, recovery and explicit
session advance. Recovery records an interrupted attempt under its old identity and preserves pending
evidence, timing and fallback blocks; it never resubmits that ID or changes its spending reservation.
A new attempt needs a new fresh packet and normal shared-budget admission. Reopening after success
also requires fresh review, preserving the saved next-review timing. Session advance accepts today's
reviewed session only, carries unfinished evidence/blocks and cadence, and does not replay missed days.
Missing/corrupt/pending history or failed saves block scheduling. Do not recreate an in-memory scheduler
or initialize another directory to bypass history. Checked files are bounded at 32 MB; hashes do not
authenticate malicious edits or prevent inter-process-lifetime rollback.

This remains an offline source increment. Operational storage/recovery qualification, actual managed-
account/input binding, current-source rechecks, automatic independent protection, production composition
and execution enforcement remain Step 7/4/5 work. Existing
spending/learning journal recovery is unchanged. No external API, database, restart or trading action
is authorized by this source implementation. Validation is recorded in project status.

September 29 connects the model's timing request through a version-3 proposal: `next_review` contains
whole minutes from 1 through 15 and a nonblank reason capped at 1,000 characters. Application validation
rejects malformed or out-of-range timing; version-1/2 proposals cannot introduce this field. Scheduled
learning-aware calls opt into v3; earlier callers keep their existing contracts. The accepted interval
and reason are saved atomically with successful fresh completion and replay on recovery. Expired or
superseded reviews cannot alter cadence. Earlier checkpoints/events can still advance the due time;
data expiry, budget and provider availability can defer it. No freshness extension, change to protective
checks, operating checkpoint defaults or API activation follows. After this increment, the operator
directs work back to Step 3's prepared market-hours connected value check.

### AI unavailable or budget exhausted — September 27 decision

The operator accepts the MVP fallback when the AI cannot review because the provider is unavailable
or the shared AI budget is exhausted: pause new buys, additions and portfolio switches. Continue
intraday monitoring, stop-loss handling and the selected engine's existing exit rules independently
of the AI. The outage itself does not introduce a sell-all instruction or relax any existing hold.

Resume discretionary decisions only after a successful fresh AI review and only when every other
applicable limit permits them. Provider recovery or budget renewal alone is insufficient; an old
decision cannot authorize resumption. This review does not clear operator-controlled risk holds,
reset loss references or bypass the spending ceiling. Show the blocking reason clearly in the UI
and journal the fallback and recovery decisions under the existing audit requirement.

Detailed timeout/retry timing, freshness qualification and treatment of already-submitted orders or
partially completed handovers remain integration work. Preserve existing order reconciliation and
protection contracts; this decision does not authorize cancelling or resubmitting uncertain orders.
This is accepted Step 2 behavior, pending enforcement, host integration, UI and offline validation.
No application, provider call, database change or trading action accompanies this documentation.

### Simple monthly spending policy — September 27 decision

The operator rejects daily spending allocations, spreading usage across remaining trading days,
and setting aside budget for later consults. For the MVP, use the shared monthly budget as needed
for useful scheduled/event reviews and learning until it is exhausted. There is no guarantee that
it lasts the month. Retain the agreed review intervals, combined event requests, bounded input/output
and per-call cost reservations; these prevent excessive or unaccounted calls without daily rationing.
Per-call reservations cover calls already admitted, not money withheld for future checkpoints.

When the budget is exhausted, apply the accepted unavailable-review fallback. The operator may stop
paid AI activity for the rest of the month or manually provide additional funding/authorize a higher
TraderVI allowance as needed. Provider funding and the app's spending cap are distinct: funding alone
does not raise the app cap. The AI cannot increase its allowance or purchase credits. No extra amount,
purchase or actual cap change is authorized now. Any explicit later cap increase must retain the
month's recorded spend; it is not a reset of usage. Resumption still requires a successful fresh
review and satisfaction of all other limits.

The initial below-CAD-100 budget remains, with the existing CAD 90 working default and per-call
token limits unchanged. Source inspection shows `AiSpendPolicy.Validate` currently rejects a monthly
limit of CAD 100 or more, and `AiCallJournal` retains the lowest ceiling used in the month. A manual
extension therefore needs an explicit policy/journal update path, preserving spend and outstanding
reservations; changing settings alone cannot refill the allowance. This decision does not claim that path or the runtime
scheduler exists. Keep the existing currency/tax cost accounting and shared journal. Implementation,
UI and qualification remain pending; no paid call, funding transaction or runtime change ran here.

## Client direction and current evidence

The operator prefers GPT-6 Astra, is open to API billing and sets an initial incremental AI budget
below CAD 100 per month. With that ceiling, the recommended first implementation adapts Oracle for
API access; the earlier subscription-first exploration is an alternative if measured costs justify it.
A subscription-backed local Codex integration remains a candidate, not a verified TraderVI connection.
Official documentation checked September 26 describes ChatGPT subscription sign-in, programmatic
Codex integrations and reuse of saved CLI authentication; API-key use is a separate usage-based path.
Sources: [authentication](https://learn.chatgpt.com/docs/auth),
[SDK](https://learn.chatgpt.com/docs/codex-sdk),
[automation](https://learn.chatgpt.com/docs/non-interactive-mode),
[requested model](https://developers.openai.com/api/docs/models/gpt-6-astra).
Account/model access, quotas, latency, unattended recovery and data handling remain unqualified.

Source inspection found `Core/Oracle/Llm/ILlmClient.cs` and `OpenAiLlmClient.cs`. The latter uses
Chat Completions and older model-family parameter handling; it has not been validated for Astra.
The operator explicitly authorizes reusing and adapting Oracle's client for AI Momentum. Reuse its
useful client/prompt/usage components while defining the new structured decision and transport contracts.
Oracle's existing rules forbid LLM influence on decisions and constrain historical prompt inputs.
The new proposal-only contract below is separately scoped in `Docs/oracle-rules.md`.
This ADR does not change Oracle's narration behavior
or authorize unrestricted raw history, tools, credentials or broker access for a model.

`Docs/oracle-phases.md` records a successful manual Phase 2 narration run in May; this is historical
documentation, not fresh database verification. September 26 at 21:07 Toronto, read-only process
inspection found no Oracle executable or matching dotnet-hosted Oracle process. Scheduled-task
inspection found no task name/action directly mentioning Oracle; the matching TraderVI Nightly task
does not mention it, and `Operations/nightly-runner.json` contains Hermes, Delphi and Athena only.
This bounded check does not establish all past runs or rule out arbitrary indirect launchers.

Architecture: persist a dated decision packet, request a structured proposal and validate it in
TraderVI. The implemented reviewer has no execution interface. Eventual submission must use the
established execution path. Protective monitoring must operate independently of model availability;
host integration and outage behavior remain to be designed.

## First source increment — September 26

`AiPortfolioReviewer` accepts a version-1 packet of dated evidence, eligible portfolio versions,
classified rules and evaluated lessons. It freezes the packet before requesting strict JSON and
checks freshness again after the response. Allowed proposals are wait, keep, select and switch;
unknown references, changed versions, unsupported actions and hard-rule overrides are rejected.
Each discretionary exception needs its own reason. New learning observations are hypotheses only.
The host still must build authoritative packets, classify rules, evaluate lessons and recheck current
assignment, evidence and risk before acting. A cited evidence ID does not prove the model's rationale.

`OpenAiResponsesLlmClient` reuses `ILlmClient` and Oracle's API-key configuration through an explicit,
disabled-by-default factory entry point. Existing Oracle composition is unchanged. The new path uses
GPT-6 Astra, standard reasoning/billing, strict structured output, no tools, no server conversation,
`store=false` and disabled truncation. These request settings are not a zero-retention guarantee.
It counts the matching input before reserving the bounded response cost, including reasoning tokens.
No automatic retries, model fallback or response-generated tools are allowed. Authentication, model
access, provider billing and live latency are still unverified. Injected HTTP transports must preserve
single-transmission behavior and must not redirect credentials or add retries.

`AiCallJournal` persists the exact request and hash before generation, then the raw response and
conservative usage estimate. A stable call ID cannot be reused. One process owns the journal and
concurrent reservations share one UTC calendar-month budget. Unknown outcomes retain their full
reservation across restart/month boundaries; settlements spanning months count conservatively in
both months. Current source prevents raising a lower ceiling within its starting month; the accepted
manual-extension design above is pending implementation. Missing/corrupt storage or
an interrupted write blocks further calls; usage overruns or unexpected billing identity block them
durably. Recovery/retention tooling remains host work; the learning increment below adds domain review dispositions.
The private JSON is not encrypted: production needs a protected directory outside the checkout,
one stable shared journal for every AI Momentum role, backups and reviewed recovery. Do not create
another journal to reset spending. The SHA-256 is an integrity check, not tamper-proof attestation.

Current source defaults to CAD 90 and requires policy ceilings below CAD 100; the manual-extension
decision above changes the planned operator contract, not this implementation evidence.
Currency/tax/fee upper bounds and their expiration are mandatory explicit inputs; none are activated.
Default per-call limits are 32,000 input and 4,096 total output tokens. Input is conservatively charged
at USD 12.50/million and output at USD 50/million, with no anticipated cache savings. Input above
272,000 tokens is forbidden to avoid the higher price bracket. Rates, conversion and other charges
must be requalified before activation; this local estimate is not a provider invoice or an account-wide
cap on unrelated clients. Sources checked September 26:
[model pricing](https://developers.openai.com/api/docs/models/gpt-6-astra),
[exact token counting](https://developers.openai.com/api/docs/guides/token-counting),
[structured output](https://developers.openai.com/api/docs/guides/structured-outputs).

Synthetic transport/packet tests cover budget exhaustion, concurrency, restarts, month rollover,
cancellation, interrupted saves, response failures and unsafe proposals. See project status for the
validation result. No real API requests, production journal, database changes or broker operations
are part of this increment. It does not run a portfolio or implement continuous learning.

## Open work and validation

### Spending and expanded discretionary role

The budget covers all AI Momentum model roles, reviews, learning calls and retries together, separate
from the operator's existing Pro subscription. The library now reserves bounded input/output cost
and reconciles returned usage; production conversion, tax treatment and cadence still need review.
A dashboard alert alone is insufficient.
At the limit, stop discretionary model calls while independent deterministic protection continues.
The operator accepts independent protection while AI reviews run less often. No cost control is
operationally installed yet. Profits may justify a later reviewed increase, not an automatic spending escalation.

The operator now requests an AI supervisor that selects an existing portfolio as its active strategy
context, exercises discretion inside that context, monitors alternatives and may switch in real time.
This changes the proposed role from only picking stocks to managing strategy selection. A portfolio
instance, its rule set and its execution engine are related but distinct existing contracts; do not
treat switching as merely changing a prompt. The open-holdings switching decision above now defines
the initial handover behavior. The catalogue decision above admits all current strategy portfolios
and future integrated additions; adapter qualification remains pending under ADR-0085/0086.

Self-improvement is a requested integral mandate. Define persistent lessons, outcome evaluation and
versioned strategy changes; no model-weight training, self-editing code, risk-boundary changes or
unlimited learning calls are implied. Separate agents/chat histories are a user hypothesis to assess,
not a selected implementation or an instruction to launch subagents. All would share the same budget.
Discuss portfolio selection/switching and then learning separately; the operator requests short,
single-topic responses. AI Explorer remains deferred.

### Required learning consultation — September 26 clarification

The operator requires both the AI supervisor and execution coordinator to consult the learning
module periodically. This connects evaluated outcomes to future judgment and execution while
preserving the root goal of protected, auditable account management. The source increment below
implements consultation and its audit. Production scheduling and outcome evaluation remain unfinished.

Implement consultation through a shared, versioned snapshot of evaluated lessons, separate from
unproven hypotheses. The supervisor includes relevant lessons in each decision review, with periodic
reviews governed by the shared budget. The coordinator refreshes its lesson snapshot periodically
and checks relevant execution lessons before a portfolio switch or a new discretionary trading action.
These coordinator checks read saved lessons; they do not require a paid model call each time or grant
the coordinator independent AI discretion. Record the consulted version, relevant lesson IDs and
whether each was applied or disregarded, with reasons. Advisory lessons cannot rewrite approved
execution policy, hard boundaries or budget limits.

Protective exits and emergency handling must never wait for learning refresh or model availability.
Exact intervals, lesson qualification/expiry and stale/unavailable-learning behavior for discretionary
actions require host qualification. No timer or automation is created by this ADR.

### End-of-session learning review — September 27 decision

The operator accepts one learning review after each trading session for the MVP. Review completed
trades and blocked alternatives with sufficient follow-up evidence under their declared comparison
horizons. Record what helped, what hurt and the supporting evidence; retain inconclusive or immature
cases as pending for a later session review. Session close alone does not complete a comparison
horizon or prove a hypothesis. Preserve actual and simulated outcomes as distinct evidence.

Feed evaluated lessons into subsequent AI decisions through the shared learning journal. The
supervisor and execution coordinator still consult saved lessons throughout the trading day as
already agreed; creating/evaluating lessons after the session does not replace those consultations.
An AI-written observation remains a hypothesis until it meets the lesson admission criteria.
The review cannot change hard boundaries, edit code or train model weights.

All paid learning work uses the same monthly allowance, with no reserved learning budget. If budget,
provider or required evidence is unavailable, record the deferred/incomplete review and retain pending
cases; do not fabricate lessons or block independent protection. Exact post-close timing, restart/
missed-review handling and lesson qualification/expiry remain implementation/design work. This is
one logical session review, not a new scheduled task or implemented evaluator. Steps 6/7 must wire
and validate it, including avoiding duplicate processing after restart. No runtime or paid call ran.

### Learning source increment — September 26

`AiLearningJournal` now stores immutable evaluated-lesson snapshots, consumer consultations,
supervisor review state and execution-preparation checks. Lesson scope pins portfolio ID, strategy
version and paper/live mode. Publication requires evaluation results and outcome references, rejects
future/backdated publication and changed content under an existing lesson ID, and advances snapshot
versions. An explicitly published empty snapshot supports the initial no-lessons state. These checks
validate provenance fields and scope, not truth or statistical merit: a trusted outcome evaluator and
its qualification criteria still need implementation. Model-generated hypotheses cannot publish lessons.

`AiLearningAwareSupervisor` consults the journal before every model review, persists a pending review
before calling the client, and records Reviewed or Rejected afterward. Pending/rejected IDs cannot be
retried automatically, including after restart. Version-2 packets carry dated, scoped lessons; structured
responses must assess every lesson exactly once as apply, disregard or not_applicable with a reason.
Apply means the lesson informed the proposal, not that execution policy changed. Version-1 reviewer
callers remain compatible but are not the learning-aware integration path.

`AiPortfolioExecutionCoordinator` reads the saved review rather than caller-supplied output and
consults learning afresh. Changed/expired learning or decision evidence yields ReviewRequired with
no prepared proposal. A Current check is recorded only after an atomic journal recheck of the snapshot;
it is a dated learning observation, not trading authority. Mode/assignment mismatches and missing or
unavailable learning fail preparation. Both consumers expose `RefreshLearningIfDue` with explicit
interval configuration; last consultation survives restart, while a new snapshot or lesson expiry
causes an earlier refresh. These calls read local saved lessons and do not invoke the model.

The coordinator is decision preparation only. It has no order transport, broker-session composition,
assignment mutation or protective-exit dependency. A future host must recheck current source/account/
policy authority through the established execution workflow before acting; a saved Current check
cannot authorize a later order. No learning failure can block existing protection through this increment.

The private journal uses exclusive ownership, an initialization marker, write-through replacement and
an integrity checksum. Missing/corrupt/interrupted storage fails closed; it must not be silently recreated.
It is not encrypted or tamper-proof. Protect its directory, backups and retention like the API call
journal. No production files or lessons have been installed. See project status for synthetic validation.

Define numerical protection, profit giveback, decision cadence, source adapters and production
composition. Complete audit dispositions, outcome evaluation and execution failure/restart behavior.
See [open questions](../reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026)
and the [implementation checklist](../strategy-alignment-implementation-checklist.md#left-to-do--development-order).
Runtime library code exists; no model calls, database changes or broker operations have been performed.

## Initial host timing — September 30 clarification

The operator selects an initial five-minute review interval, with checkpoints at the reviewed
session open, three hours after opening and fifteen minutes before closing (09:30, 12:30 and
15:45 Toronto on a normal session). This resolves the earlier open clock-time choice. The existing
AI-requested 1–15-minute interval and earlier event/checkpoint behavior remain. These settings do
not authorize an execution path or bypass unavailable inputs. Operational activation remains separate.

For the first review, the operator also accepts the existing CAD 90 monthly ceiling, a CAD 1.60
per USD accounting ceiling and a 1.20 tax/fee multiplier, valid through October 31 Toronto
(expires November 1 at 04:00 UTC). These are conservative accounting bounds, not exchange-rate
predictions, provider charges or a new budget. Existing shared spend and outstanding reservations
must be preserved. Automatic operation still follows bounded connected qualification.

## First connected review age — October 1 clarification

The operator accepts a ten-minute maximum input age for the first review-only connected test,
covering its saved portfolio valuation, catalogue and engine context configuration. Five-minute
saved bars motivate the test allowance; timestamps, expiry and existing input/eligibility checks
remain unchanged. This neither qualifies continuous operating ages nor changes trading protections.
Keep automatic reviews disabled while qualifying the first result. Dated installation, native
results and remaining input gaps belong in project status and the implementation checklist.

## Concise review view — October 1 implementation

Following the operator's request to shorten the oversized review input, render a separate, declared
model-facing view while retaining the complete frozen source packet in the existing review records.
This is presentation work: do not change engine eligibility, ages, protection, spending or the response
contract. Validate the answer against the full original packet, and save the exact transmitted request
through the existing call journal. Small packets can retain their original presentation.

The large-packet view keeps all choices, exact decision versions, hard/discretionary rule IDs, current
managed holdings/stops, risk assessment, source times, missingness and performance context. It shares
identical values and uses named-column tables. Live stock rows show current lens ranks/admission,
momentum/family judgments, eleven named current measurements and data-quality reasons, alongside every
loaded checkpoint's original bar time, continuity epoch, validity, maturity, confidence and momentum state.
Numeric enum codes carry their names. No price rounding or missing-to-zero conversion is permitted.

The view explicitly omits detailed/older calculation bodies, detailed daily gate traces, vote totals,
lifecycle bookkeeping and internal source IDs/hashes; closed managed positions become a count. Those
details remain in the saved packet. Required decision/account/evidence IDs remain visible. Omitted
facts are unknown to this review, never assumed passes or reconstructed by the model. Unknown Live
checkpoint shapes fall back to complete evidence. Format selection does not guarantee capacity: exact
token admission remains mandatory, and further growth can still block a review. Connected model
interpretation and operational results belong in project status, not this representation contract.

## Review questions

1. Which trading decisions may the AI override, and who enforces hard boundaries?
2. Why must a losing position not receive a progressively looser protective stop?
3. Does ChatGPT subscription access establish a working unattended TraderVI client or API credit?
