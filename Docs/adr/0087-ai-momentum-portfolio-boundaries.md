# ADR-0087: AI Momentum mandate and enforced protection

**Date:** September 26, 2026 Toronto
**Status:** Budgeted client, portfolio proposals and learning-consultation foundation implemented offline; outcome evaluation, host integration and protection parameters pending.
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
  Track both starting capital and the recorded portfolio peak; exact valuation, combination and
  recovery details remain unresolved.
- Keep hard stop limits. Do not loosen a stop to prolong a losing trade or repeatedly grant more
  room as its price falls. Exact initial-stop selection and subsequent tightening remain to be defined.
- Protect profit as an objective. This requires a measurable activation threshold and allowed
  giveback (decline from an observed peak), not a promise that every trade avoids losses. Those
  details remain open. A trigger cannot guarantee a fill price or realized loss bound.
- Retain independent paper/live histories and approved strategy identity. Paper outcomes or changes
  cannot silently overwrite real holdings, protection or the live assignment.

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
the 7% AI boundary and introduces a daily limit on failed attempts. It does not erase cumulative history.

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
7% AI buying-pause boundary. Measure the stated cycle percentages against each round's starting
portfolio value, as presented in the comparison. After the third failed round, stop new buying for
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
peak/starting-reference interaction, successful-round handling and the daily retry-counter reset.
The later continuity decision governs carried rounds and session-start consultation.
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

This is design agreement only, not broker authority or an installed workflow. Failure recovery
across sessions and the daily counter's reset remain to be settled.

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
nonnegative round loss fraction `loss` and that round's limit `limit` (0.05, 0.03 or 0.01), use
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
It does not erase original capital/peak history or resolve how portfolio-wide trailing protection
combines with cycle-start loss, successful-round handling or daily retry
resets. The formula and examples are accepted design, not implemented/tested runtime behavior.

### Cumulative review and visible thresholds — September 27 decision

The operator accepts retaining the existing 10% drawdown review for AI Momentum: when portfolio
value falls 10% or more below its highest recorded completed-session closing value, pause new buys
and require explicit operator approval before they may resume. Protective exits continue. This
guard takes precedence even when a daily attempt remains. Neither a new day, a new round, a fresh
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
its existing holdings, round-start loss reference, applicable 5%/3%/1% limit and protective levels,
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
additional checkpoints, rule-to-checkpoint mapping and event-driven reviews remain to be chosen;
only the new-session consultation is agreed at this stage. All reviews and learning share the
existing AI spending ceiling. Budget exhaustion/provider failure cannot bypass protection or risk
holds; exact discretionary-review fallback belongs to the remaining outage contract.

The daily failed-attempt counter and its interaction with a carried round remain separate decisions:
do not silently reset an active round's limit when that counter is later renewed. This is Step 2
design agreement. Step 7 must implement and qualify scheduling, including restart/missed-checkpoint
handling. No scheduler, automation, paid call, runtime change or trading action is installed here.

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
both months. A lower ceiling cannot be raised within its starting month. Missing/corrupt storage or
an interrupted write blocks further calls; usage overruns or unexpected billing identity block them
durably. Recovery/retention tooling remains host work; the learning increment below adds domain review dispositions.
The private JSON is not encrypted: production needs a protected directory outside the checkout,
one stable shared journal for every AI Momentum role, backups and reviewed recovery. Do not create
another journal to reset spending. The SHA-256 is an integrity check, not tamper-proof attestation.

The default working ceiling is CAD 90, with any policy ceiling required to remain below CAD 100.
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
treat switching as merely changing a prompt. Eligibility, authorized strategy set, handover, retained
protection, costs and history still require a defined contract under ADR-0085/0086.

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

## Review questions

1. Which trading decisions may the AI override, and who enforces hard boundaries?
2. Why must a losing position not receive a progressively looser protective stop?
3. Does ChatGPT subscription access establish a working unattended TraderVI client or API credit?
