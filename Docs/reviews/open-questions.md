# Open Questions

Things we punted on and need to revisit. Cleared as decisions are made
(those then become ADRs).

## Active

### AI-managed portfolio and proposed live adoption — September 26, 2026

- **Cycle-loss schedule accepted (September 27, ADR-0087):** 5% / 3% / 1% replaces the earlier
  7% AI pause; stop new buys for the day after three failed portfolio rounds while protective exits
  continue. Failed-round closure is agreed: close remaining AI-managed holdings and confirm/reconcile
  exits and commitments before a new permitted round. Fresh-entry qualification is agreed: a new
  qualifying signal from the selected engine after closure, no extra fixed delay, and AI choice to
  take it or wait. Proportional sizing within rounds is agreed in ADR-0087: allowance can recover
  before the round limit; hitting it commits to closure. The 10% decline from highest recorded
  completed-session closing value now requires operator approval before new buys resume, regardless
  of attempts remaining; protective exits continue. Threshold proximity must be explicit in the UI
  (ADR-0087, checklist Step 8). A fresh sequence after three failed, closed/reconciled attempts is
  now eligible next trading session with fresh entry evidence and AI review; carried round limits
  and the 10% approval hold persist. UI must clearly distinguish those states and unmet restart
  conditions. Round limits and buying allowance now trail the highest verified round value,
  initialized at its opening value; the overall 10% closing-peak guard retains its history. Next
  specify qualified valuation/cash-flow treatment, successful-round handling and counter
  treatment after fewer failures or a carried round completes. Preserve original capital/peak records and independent holds; day/round changes cannot
  clear the cumulative review. Existing engine templates remain unchanged.
  Stop execution can exceed a trigger price:
  [FINRA stop-order guidance](https://www.finra.org/investors/insights/stop-orders-factors-consider-during-volatile-markets).

- **Round continuity and rule consultation (September 27, ADR-0087):** unfinished rounds retain
  holdings, loss references, round limits and protection across nights/weekends under existing exit
  rules. New sessions require review of qualified fresh entry-engine guidance. The operator wants
  session-start, mid-session and pre-close consults alongside regular Delphi Live intraday checks
  and AI receipt of their data. One-to-fifteen-minute AI review intervals during market hours are now
  accepted, subject to the shared budget. Set exact checkpoint times, relevant rules and whether the AI controls only paid
  review scheduling or any discretionary collection cadence. The recommended MVP uses AI-proposed
  next-review intervals validated by the host, with defined local events requesting earlier reviews;
  it cannot weaken protection or change five-minute bar semantics. Initial events are now agreed:
  new qualifying entry signals, confirmed holding closures and risk-limit changes to permitted actions.
  Combine events arriving together, respect the one-minute minimum and shared budget, and allow
  later hooks through the same path. Event identity/duplicates and checkpoint priority remain
  implementation details. Monthly spending is now agreed without daily allocations or future-consult
  reserves. Provider/budget fallback is now agreed: pause new buys, additions and switches,
  continue monitoring/stops/engine exits, and require a successful fresh review plus all other limits
  before resuming. Define missed/stale review timing, retries and in-flight order/handover recovery
  without blocking protective handling or clearing independent holds. Next-session restart after a
  fully failed/reconciled sequence is agreed; other daily counter interactions remain unresolved.
  No scheduler is implemented.

- **Step 2 / MVP discussion in progress:** the AI gradual allocation design follows continuous
  losses/recovery across days without a calendar reset (ADR-0087, September 27 amendment). This
  supersedes its original daily framing and session-minimum lock; the later cycle schedule above
  adds a daily retry cap and replaces the fixed 7% AI pause. Both starting capital and recorded portfolio
  peak are selected references. The round reference now trails its highest verified value, initialized
  at the opening value, and drives both its trigger and allowance. Define cash-flow adjustments and
  qualified valuation cadence/source; retain the independent overall closing-peak guard.
  The operator wants continued monitoring and automatic pickup after recovery while in cash; the
  fresh engine signal and round-relative sizing are agreed. Market recovery alone does not restore an
  all-cash portfolio's value, so this requires an explicit recovery transition at zero allowance.
  Resolve interaction with existing daily/capital-review holds before integration. Existing engine
  7% daily templates, frozen V1 and saved assignments are unchanged. AI enforcement remains pending.

- **Requested direction, foundation only:** an AI-managed portfolio with a mandate, discretionary
  decisions and enforced boundaries. The operator supports this framework and logging decisions,
  supporting evidence, reasons for rule overrides and conditions for reconsideration.
- **Requested live scope:** short-term momentum, with the AI portfolio also managing the real TFSA
  in real time by Monday, September 28. This is a launch target; native risk/order qualification,
  host readiness and an exact supported strategy remain prerequisites under the existing contracts.
- **Accepted framework:** the AI may use or ignore discretionary trading rules when it records why;
  separately enforced hard boundaries cover cash, exposure, emergency protection and account/data/order
  validity. Exact limits and rule classification remain unresolved; no existing protection contract is
  superseded by this discussion.
- **Deferred by the operator:** AI Explorer, the separate paper experiment with freedom over investing
  style, is outside the first pass and may be revisited in a future session. Current scope is AI Momentum
  in paper and intended live use.
- **Accepted protection direction:** reduce permitted risk as portfolio losses deepen. Reuse the
  selected engine's existing per-stock hard-stop/trailing-profit settings; AI may tighten or sell
  earlier, never loosen/remove an established stop. Application code must enforce this and recheck
  current protection at execution. Qualified valuation/cash-flow treatment and remaining recovery
  details remain open. Round-level trailing protection is agreed; the gradual allocation
  scale is agreed but not implemented. See
  [ADR-0087](../adr/0087-ai-momentum-portfolio-boundaries.md). Triggers do not guarantee fill prices.
- **Budget pacing settled (September 27, ADR-0087):** use the shared monthly allowance as needed,
  without daily allocations or reserves for later consults; retain per-call reservations, token limits
  and combined event requests. At exhaustion pause, unless the operator explicitly extends the
  allowance/provides funding as needed. No specific increase or purchase is authorized now. The
  current validator rejects CAD 100 or more and the journal retains the month's lowest ceiling;
  implement the manual extension path without resetting
  spend, automatic top-ups or bypassing fresh-review recovery. Provider funds alone do not change
  TraderVI's cap. Production accounting/qualification remain pending.
- **Client/budget:** GPT-6 Astra; operator allows API usage and sets an initial incremental ceiling
  below CAD 100 monthly. Recommendation is API first using Oracle's abstraction; subscription access
  remains an alternative if measured savings justify integration. All roles and learning share the cap;
  reserve call costs including reasoning/retries and currency/tax headroom. The library implements
  reservations and a default CAD 90 working ceiling; production FX/tax bounds, endpoint billing,
  journal protection/recovery and cadence remain unqualified. Protection must operate independently.
  Official documentation supports programmatic Codex and saved subscription authentication, but no
  TraderVI connection, account/model access, latency or quota qualification has run. API-key use is a
  separate usage-based option. The operator explicitly authorizes adapting the existing Oracle client;
  proposal-only version-1 output and narration-rule scope are implemented under ADR-0087.
  Historical Phase 2 narration exists, but the September 26 bounded process/schedule inspection found
  no running Oracle or directly named scheduled action; current nightly configuration excludes Oracle.
- **Return aspiration:** the operator asked for at least 1% daily gains. This is unproven, cannot be
  guaranteed, and is not an accepted trading obligation or qualification criterion. A meaningful
  evaluation objective and period remain to be agreed.
- **Expanded requested role:** select an existing portfolio, operate within its strategy context,
  monitor alternatives and switch in real time. Initial handover is agreed: retain mutually permitted
  holdings/stops, sell other AI-managed holdings and confirm/reconcile required exits before replacements.
  Preserve round reference/peak/limit, attempts and independent holds, including all-cash transitions;
  log why the AI switched. Define the eligible set and compatibility mapping, and implement durable
  handover/recovery before activation. Separate agents/conversations are a hypothesis, not yet chosen.
- **Learning mandate:** self-improvement is integral; define recorded lessons, evaluation and versioned
  changes without permitting risk-boundary or budget overrides. Model training and self-editing are not
  implicitly authorized. See ADR-0087 for the proposed boundaries and unresolved scope.
- **Blocked discretion learning (September 27):** record desired stop loosening/removal as a
  non-executable hypothesis with the requested alternative, evidence, blocking rule and actual
  outcome. Compare later with clearly labelled simulated alternatives, evaluating gain/loss magnitude
  as well as frequency. Define comparison horizons/assumptions and lesson qualification/expiry;
  missing evidence must remain unevaluable. No learned result can automatically change a hard
  boundary. Stock-level records and outcome comparisons are not implemented.
- **Accepted learning consultation:** both supervisor and execution coordinator must consult evaluated
  lessons periodically (ADR-0087). Journal, version-2 assessments and execution-preparation rechecks
  are implemented in source. One post-session learning review is now agreed for completed trades
  and blocked alternatives with enough follow-up evidence; inconclusive cases remain pending and
  evaluated lessons inform subsequent decisions. Intraday saved-lesson consultation continues.
  Set exact post-close/refresh timing and missed-review handling, implement lesson qualification and
  expiry policy, and qualify host handling of stale/unavailable learning. Preparation fails closed;
  local consultation needs no model call and has no connection to protective handling.
- **Next discussion, one topic at a time:** portfolio selection/switching, then learning; subsequently
  finish remaining rule/lifecycle and learning qualification decisions,
  production data access, missing-data/model-outage behavior and durable proposal dispositions.
  ADR-0087 records the implemented client/audit/proposal foundation and remaining dependencies.
  No real model calls, broker operations or activation have run.

### Portfolio-wide ongoing TFSA management

- **Accepted September 22:** any portfolio must be promotable; keep the exact approved strategy
  managing across days until pause or switch, subject to each day's account/risk checks (ADR-0085).
  Breakout Top 3 is an initial candidate, not the product scope.
- **Accepted September 23:** manual purchases through TraderVI stay outside the active strategy by
  default. Add a later holding handover action and an optional purchase-dialog strategy-management choice.
  Regular account synchronization with manual refresh is requested; the first source implementation
  uses five minutes. Native rollout remains pending.
- **Accepted direction:** manual handover may exceed the normal position count (Breakout 3 can manage
  four). Apply existing sell/protection rules and preserve explicit manual attribution; no forced exit
  solely to restore the count. This replaces the assistant's proposed wait-for-a-slot restriction.
- **Accepted clarification:** the strategy continues its own three automatic positions while manual
  extras receive sell/protection management only. Extras do not occupy automatic slots; selling one
  creates no replacement authority. This rejects counting extras against the normal automatic target.
  Actual cash, account-wide valuation/risk and existing sizing rules still apply.
- **Deferred September 23:** same-stock manual additions and split ownership. The operator will not
  manually add to a strategy holding and asked to stop the edge-case design pass. Preserve conflict
  checks; resume the main implementation without building a special ownership mechanism.
- **Open:** direct-Wealthsimple trade ownership, manual holdings at promotion/switching, shared
  capital/limits, transfer timing/protection and re-entry after a manual sale.
- **Polling implemented/tested, not installed:** shared five-minute account/history refresh while
  connected, manual/post-trade refresh coalescing, cancellation and bounded backoff. Only reviewed
  existing links update; original-order recovery and fresh final preflight remain separate requirements.
- **Revised proposal:** reconcile known in-app orders and continue automatically when ownership,
  account and risk checks pass. Pending/uncertain orders retain reservations; human review addresses
  unresolved facts or authority. The earlier blanket manual-fill review recommendation was too broad.
  Current first-session source still invalidates any changed fill outside its own session order list,
  including manual in-app orders; this is a source finding, not an operational test or accepted new rule.
- **Accepted September 23:** stopping prevents new strategy purchases while ordinary exits continue.
  Retain the existing Market sell all row action for manual exits. Proposed labels: Stop new buys /
  Resume buying; status: New buys stopped · exits active. Core now persists a separate purchase control;
  the existing full execution pause still blocks exits too.
- **Accepted September 23:** restore the same approved strategy and saved buying mode after app restart
  once fresh account/order checks pass. Stopped buying stays stopped; failed checks preserve state
  without authorizing orders. Core now supports explicitly approved ongoing assignments across checked
  days; TFSA saved-mode controls restore after a linked refresh. No execution host starts from these controls.
- **Open:** replacement/cancellation and recovery of invalidated reviews.
  Preserve family rules, account holds, causal protection and original order IDs.
- **Implementation gap:** native risk/order qualification, Delphi Live protection, manual ownership,
  switching and execution-host startup remain. Existing single-session approvals still expire; the
  new ongoing scope cannot silently extend them or replenish their purchase ceilings. Follow the
  [continuing draft](../concepts/strategy-promotion-and-execution.md#portfolio-wide-ongoing-management--september-22).
### External broker trade synchronization extensions

- ADR-0084 implements resolved whole-share order projection and signed-in desktop polling. Pending
  partial executions, overlapping order sequences, broker corrections, nonzero withholding, sub-cent
  amounts and fractional positions remain blocked pending explicit representation/recovery contracts.
- Native installation/transaction acceptance, the reported external exit and repeat-refresh duplicate
  prevention were verified September 24. Complete broker-backed cash/risk recommendation inputs remain
  separate; preserve risk references.
  [ADR-0084](../adr/0084-external-broker-trade-synchronization.md).

### Morning account values and the future standalone engine

- **Accepted September 21:** prefer dependable pre-session engine startup and shared Core reference
  handling; investigate actual recovery data before adding a blanket late-start buy pause. A future
  Windows scheduled task will host the extracted engine, not a separate WPF recovery workflow.
- **Accepted September 22:** confirmed deposits/withdrawals preserve the measured loss percentage and
  existing holds. That behavior is implemented with matched books, complete event-interval contracts
  and durable duplicate checks. The later full suite passes 1,640 tests; migrations 035/039/040 are
  installed, with native schema/preservation checks at 23:34 Toronto. Operational setup remains absent.
- **Open source qualification:** the supplied activity capture shows completed/pending events and
  occurrence times but does not establish effective cash timing or matching account-value books.
  Qualify a complete evidence producer before using these calculations on the real account.
- **Next:** verify that cash source plus native morning publication timing and broker value scope.
  Delphi Live retains first-bar Open/exact final-bar Close; Shadow retains its saved-price book
  definition. A graph point alone is not source qualification.
- Shared broker orchestration, projection, setup UI and durable sessions are source/tested under
  [ADR-0086](../adr/0086-supervised-broker-strategy-session.md). Native risk evidence production,
  SQL runtime recovery verification, supervised host rollout and order authority remain unfinished. No new
  blanket morning-pause rule is approved. See the [no-go review](supervised-session-20260923.md).
  [ADR-0083](../adr/0083-opening-account-values-and-shared-engine-hosting.md).

### Active-listing maintenance and partial sector refreshes

- September 17 recovery applied migration 036 to deactivate verified acquired/delisted ARX and SES.
  Existing active-universe filtering now excludes them; historical rows are preserved.
- Decide how to surface listing/corporate-action review before retired symbols block nightly collection,
  and how weekly sector refresh should track unsuccessful rows independently of its global timestamp.
  No automatic deactivation from provider errors or relaxed data/trading gates is approved.

### Backup capacity and retention

- September 17 recovery hit a full C: drive during the post-Hermes backup. The latest compressed
  backup was about 15 GiB, with both staging and local OneDrive copies consuming space.
- Resolved September 17: the user selected **two completed backups per location**, after new-backup
  and copy verification. ADR-0082 implements retention and compact Live storage. Migrations
  037/038 and lossless conversion are installed and verified. The full backup fell from 16.27 GB to
  535 MB; every historical/current snapshot reconstructed and WPF resumed compact writes.
- Resolved September 23, 23:55: the user separately authorized reclaiming unused SQL capacity while
  preserving all stock/application data. The one-time shrink is complete with a verified backup,
  growth headroom and preservation checks; see [current evidence](../project-status.md#latest-operational-evidence).
  No automatic shrink is approved, and no existing backups were deleted. An independent backup
  download and documented restore drill remain outstanding; no application performance benchmark ran.

### Broker history import and strategy-input cutover

- **Implemented in source September 16:** ADR-0080 adds a concrete initial review, fixed TFSA binding,
  append-only SQL observations, duplicate-safe cumulative order reads, preserved legacy references and
  fresh/transactional rechecks. Migration 034, the linked 32-order import and the September 16 22:06
  Toronto native refresh are verified. The alias/ETF compatibility, approved legacy correction and
  timestamp readback follow-ups are complete; do not replay the migration/correction or relink history.
- **Provisional:** review the first link, then update supported history on explicit import refresh.
  This is not a background broker polling schedule or authority to activate a strategy.
- **Accepted in ADR-0081:** first missing risk references can use verified current account value during
  setup, recording the actual starting boundary and preserving known baselines/holds. Use the lower of
  fresh broker CAD cash/buying power, constrained by sizing/reservations; unfinished sales supply no funds.
  Current protection ownership remains until activation. Carry established protective levels; initialize
  missing protection from actual cost/fresh evidence, never paper state or invented historical highs.
  Confirmed deposits/withdrawals adjust loss references; unexplained cash movements pause new buys.
- **Initial setup implemented in source:** pure risk-reference preparation and reviewed SQL save/recovery,
  with source identities, explicit unknown-history blocking and no reinitialization of an established
  baseline. Migration 035 is prepared/uninstalled. Operator setup UI and broker projection are now
  implemented in the isolated source branch; no operational baseline or installed cutover exists yet.
- **Next (September 22 evening):** qualify complete cash-event evidence/effective timing and native
  opening values for the implemented continuation caller. Rehearse/install 035/039/040 separately.
  Do not wait for a dedicated review screen. Minimal setup review and separately verified storage
  remain prerequisites for operational use. Then complete position-cycle mapping and causal
  protection handover, followed by source-aware strategy inputs and ongoing synchronization. See the
  [continuing draft](../concepts/strategy-promotion-and-execution.md#account-baselines-and-protection-ownership--september-16-continuation).
- **Native read follow-up:** the September 16 activity capture verifies POSTED completed-order details
  and supplies account-chart and cash-flow-total read contracts. The subsequent screenshot identifies
  an empty EXPIRED response, then a CANCELLED response on retry. A narrow compatibility fix retains
  either order (including the CANCELED spelling) with explicit null provenance
  and zero accounting quantity, subject to absent execution evidence and unchanged prior fills. Offline
  tests cover partial fills and conflicting execution evidence; the later native import/refresh succeeded.
  See the [contract review](../concepts/wealthsimple-account-history-contracts.md).
- **Proposed simplification, September 16:** the operator asked whether full historical import is
  necessary. Recommend an explicitly verified current-account baseline with forward synchronization
  and optional older-history import. Optional history remains unimplemented; ADR-0081 accepts initial
  reference rules while preserving history/protection. It does not bypass the existing importer.
- **Native preview now reached, September 16:** a read-only HAR/protected-preview/SQL comparison finds
  completed ETF history rejected by EQUITY-only validation, a symbol-only `.TO` candidate mismatch and
  a recorded-sale gross/net disagreement beyond rounding. Historical ETF accounting, verified listing
  aliases and side-by-side amount comparisons are now implemented. Strategy eligibility remains unchanged;
  missing listing evidence and amount differences still block. Matched-only subsets no longer produce
  a false position warning. The financial disagreement was subsequently resolved by the separately
  approved, audited single-row correction; the account was imported and its native refresh verified.
- **Deferred:** ambiguous/split-fill matching, broker corrections/relinking, individual execution
  timestamps, retention and refresh cadence. No force-match or automatic legacy rewrite exists.

### Remaining manual-sell integration

- **Protocol implemented September 11:** [ADR-0076](../adr/0076-wealthsimple-market-sell-protocol.md)
  adds captured whole-share market sell and scoped fill reading, validates market buy, and shares
  compatible attempt fences across sides. Native sell acceptance is still unverified.
- **Manual workflow resolved September 13:** [ADR-0077](../adr/0077-manual-holdings-market-sell.md)
  implements the operator-chosen immediate sell-all row action, protected intent/recovery, exact
  holdings/pending-order checks, cross-side reservations and remaining-holdings verification.
- **Still deferred:** verified limit-sell placement (the dialog is only a preview), native live mutation
  acceptance, cancel/replace, cross-device order coordination, full SQL position/cash reconciliation,
  and ownership of manual versus strategy exits. No automatic sell engine is enabled.

### Fixed-gate paper comparison follow-up

- **Design resolved September 9:** [ADR-0067](../adr/0067-fixed-gate-paper-comparison-pairs.md) records
  five fresh matched pairs: the four Shadow strategies plus Delphi Live, CAD 1,000 per account,
  published selection limits, and lifetime-fixed Normal/Portfolio rules only profiles. Existing
  accounts continue separately. Migration 033 and all ten accounts are now active, with a common
  September 9 13:25 Toronto source boundary. Initial collection and both engines' processing are verified.
- **Follow-up evidence:** observe paired closes over aligned sessions, preserving missing valuations
  and today's partial coverage. No gate-performance conclusion exists on the activation day. The
  accounts' pinned daily strategy must not be silently changed by global strategy selection.
- **Deferred:** breadth-only variants, arbitrary per-gate editors and profile changes within existing
  accounts. Provider conflicts and nightly Hermes mapping failures remain separate work.

### Nightly reliability after source-adoption recovery

- **Source/account recovery resolved September 9:** ADR-0066 automatic publication adoption and ADR-0067
  fixed-gate pairs are implemented and active. Preserve the historical observations below; their proposed
  late-start/account decisions are superseded. Hermes mapping failures and provider bar conflicts remain
  independent operational follow-up work.

- **September 9 revised user direction:** operational paper portfolios should automatically adopt a
  newer valid Delphi publication, including the corrected current run, rather than permanently reject
  publications after 09:30. Gate-profile/account details are being aligned in the
  [source adoption and comparison draft](../concepts/paper-source-refresh-and-gate-profiles-draft.md).
  A read-only evaluation of today's saved run found that breadth-only bypass still admits zero of 207
  candidates per lens because Granville blocks next. Portfolio-rules-only comparison accounts are now
  the recommended first experiment. No runtime adoption or new profile has been applied by this draft.

- **September 8 bounded exception applied:** the operator subsequently requested forced Top 3/Top 5
  paper candidates for engineering observation. [ADR-0064](../adr/0064-one-session-shadow-engineering-override.md)
  and migration 030 record the 11:02 adoption, fresh 11:05 bar boundary, preserved financial/official
  evidence, and visible test labels. This one-session exception does not implement a reusable late-start
  control. Raw portfolio charts include the test and any carried effects.
- **Observed September 8:** concurrent source edits stopped the nightly preflight before execution.
  A user-authorized 09:52 Delphi run successfully published today's report from current completed-session
  inputs, but Shadow's 09:30 availability cutoff and already-frozen `NoValidDelphiRun` sessions still block
  ordinary entries. The operator challenged losing the rest of the day to this rule.
- **Proposed direction, awaiting a decision:** an explicit, recorded late start may adopt the new daily
  source prospectively, require fresh observations after that boundary, retain holdings/pending protective
  exits/risk holds, and label the session incomplete for comparable performance evidence. Do not backdate
  picks, replay missed fills, erase the original blocked session, or silently relax the ordinary cutoff.
- **Resolve:** how to record the source transition within a frozen session, which account scopes can
  resume, and whether late-start evidence is excluded or shown separately in comparison/research.
- **Related retry risk resolved September 9:** authorized recovery exposed the unfinished-day US-index
  issue. Hermes now requests before the local run date and refreshes its last stored observation;
  Delphi bounds US diagnostic context to the completed TSX date. The corrected full pipeline passed.
  See the daytime-recovery addendum to ADR-0058; this correction does not implement source adoption.

### Historical practice environment — follow-up milestones

- **Accepted:** shared Core runner, separate saved historical dataset, and eventual WPF Practice display;
  see [ADR-0063](../adr/0063-historical-practice-environment-direction.md) and the
  [running draft](../concepts/historical-practice-environment-draft.md).
- **Verified:** bounded TMX probe returned full five-/fifteen-minute XIU and RY sessions on March 4,
  August 4 and September 4, 2026; all 156 five-to-fifteen aggregation comparisons matched exactly.
- **Accepted first model mode:** exploratory runs with today's preserved model set, pinned per run and
  visibly labelled; later-trained/unverified models do not establish honest historical forecasts.
- **Accepted starter universe:** today's eligible stock list and sector classifications, explicitly
  exploratory and retaining their actual capture date. Historical membership remains unverified;
  prices and indicator histories still obey the historical cutoff.
- **Accepted first milestone:** historical daily Delphi plus all four independent Shadow portfolios,
  carrying accounts across sessions; Delphi Live follows afterward.
- **Accepted starter trial:** latest 20 consecutive completed TSX sessions, including source gaps. The
  reviewed calendar resolves the current target to August 10–September 4, 2026; prior indicator history
  is additional. This defines a starter test rather than a strategy-performance evidence threshold.
- **Accepted capital:** each of the four accounts starts with CAD 1,000 cash, zero holdings/pending orders,
  and no deposits/withdrawals during the trial; all accounts remain independent.
- **Accepted missingness:** missing historical data does not block or abort the ordinary trial. Keep
  the timeline, use production no-data/recovery behavior and record effects/failures; completeness is
  not a prerequisite. Artificial failures have separate scenario identities. Unknown marks stay explicit.
- **Implemented in source:** bounded historical daily inputs, the shared Shadow controller with isolated
  storage/time, pinned packages, 20-session scheduling, per-account CAD 1,000, gap diagnostics and WPF
  recorded playback. [Contracts and commands](../concepts/historical-practice-implementation.md).
- **Starter trial completed and deployed:** 964 successful TMX requests supplied 44,792 bars, retaining
  5,336 missing slots. Daily Delphi published on all 20 sessions; the four accounts produced 255 fills.
  Two actual-data runs are byte-identical and the independent audit passed. WPF playback is open;
  all 76 operational table counts and selected state hashes are unchanged. Exact historical membership
  remains deferred, not backdated. See ADR-0063 for outcomes and assumptions.
- **Deferred:** automatic checkpoint resume, real isolated SQL/time/lease integration, deliberate
  provider-failure injection, throughput testing, live runner controls in WPF and Delphi Live replay.
- **Deferred:** broader acquisition, historical quote/corporate-action verification and walk-forward training.
  The first source implementation establishes neither strategy edge nor clean forward cohorts.

### Central settings — strategy version adoption

- **Accepted and implemented in source:** pause new buys, close holdings separately, then edit/save/assign
  while paused and empty; explicitly resume afterward. One shared draft per strategy, retained navigation,
  per-setting help and system banners are implemented. See [agreed workflow](../concepts/global-settings-design-draft.md).
- **Initial scope:** family-wide locks; shared Daily Delphi changes require all dependent families paused
  and empty. Old frozen daily picks cannot initiate buys under a changed daily selection.
- **Rollout:** migration 027 is installed. Migration 028 and new host rollout are tracked in project status.
- **Retained:** account history and risk holds; separate Save and Assign; no duplicate account-cap layer.
- **Deferred:** a prospective research restart after operator intervention. Empty accounts remove carried
  trades but do not establish equal capital, periods, inputs or clean controls. The broad promotion guard
  remains in force. Narrowing it requires explicit study ownership, restart authority and eligibility.
- **Deferred:** partial Real fills/commissions in the new dialog, durable drafts across app restarts,
  editable Shadow lens/slot definitions, account capital flows/caps, provider/schedule editors and broker routing.
### Delphi settings — candidate assignment and first operational selection

- The desktop settings page selects complete preserved strategy/model sets. A candidate not yet assigned
  to a reviewed strategy is not selectable, and individual model mixing remains deferred pending a
  complete input-compatibility and evidence-review flow (ADR-0057/0059).
- Perform the first intentional settings selection and later rollback with a concrete reviewed target;
  verify the active identity and immutable event. Source validation deliberately did not change the
  active daily strategy. Cross-family performance promotion remains separate; independent supported account policies now have
  their own sections in central Settings.

### Corrected daily model inputs — first scheduled evidence

- **Accepted:** exclude only stocks with incomplete required dated inputs; unusable shared XIU inputs
  stop the new daily recommendation set. Existing model reuse requires separately authorized proof of
  compatibility; retrain candidates only when needed and authorized. Corrected behavior needs a new identity.
- **Resolved:** the operator authorized inspection, replacement training when proof was unavailable,
  and a new corrected daily selection. [ADR-0057](../adr/0057-preserved-model-sets-and-explicit-selection.md)
  now governs candidate-only training and preserved assignments. The later ADR-0058 benchmark repair
  selected `v3.4-observed-benchmarks` with the same four models; its actual bootstrap and observed
  benchmarks verify. Previous strategies remain available for reviewed rollback.
- **Q:** Does the first normal scheduled corrected cohort complete with the expected input coverage and
  evidence identity? This requires observation of a real scheduled result, not another design approval.
- **Status:** the separately authorized September 6 standalone v3.4 run completed at 22:43 Eastern with
  valid provenance, 209 evaluated stocks, four matching model identities and no qualifying trade. All 50
  published rows are Hold; seven input exclusions and unavailable leadership-movers evidence are explicit.
  Earlier evidence/account snapshots are preserved. Scheduled pipeline completion remains pending; no
  new monitor was launched. The
  later readiness findings were repaired under separate operator authority: every-day scheduling,
  Friday/SPY data refresh and required observed benchmark inputs now pass. See the
  [cutover review](model-input-cutover-20260906.md). Continue ordinary dialogue
  one concrete step at a time; the operator asked to stop using timed question cards.
  [Design draft](../concepts/strategy-alignment-stabilization-draft.md);
  [implementation checklist](../strategy-alignment-implementation-checklist.md).
- **Tags:** architecture, machine-learning, data-pipeline, decision-engine

### Common strategy comparison, recommendation promotion and broker execution
- **Resolved/implemented in source:** immutable complete strategy identity, account-specific human approval,
  auditable history, immediate committed boundary, paused/empty switching and Real recommendation routing.
  [ADR-0062](../adr/0062-complete-strategy-recommendation-promotion.md). Migration 029 was applied with
  verified backup/preservation checks on September 7; Debug and Release desktop builds are ready.
- **Operator decision:** performance evidence is advisory in manual recommendation mode. A human may
  accept incomplete evidence; this does not certify that ADR-0022 or Live research standards were met.
  Live's internal untouched/research rules and Athena evidence are unchanged.
- **Deferred to Wealthsimple live trading/automation:** decide which evidence tiers and family requirements
  become enforced activation gates, including capital/cash-flow normalization, paired periods, costs/fill
  assumptions, coverage, downside criteria and untouched validation. No universal winner score is agreed.
- **Deferred:** reviewed Wealthsimple access/authentication, approved accounts, order authorization,
  idempotency, partial fills, fees, reconciliation, cancellation, stale inputs, stopping and recovery.
- **Installed read paths verified:** all four Shadow reviews, Real account/history/latest reads, the
  unassigned workflow's no-quote/no-publication behavior and reviewed calendar loading. Live has no initialized
  paper ledger to review. All existing data and the five empty promotion tables remain unchanged.
- **Operational validation outstanding:** Real account reconciliation, existing paused/empty requirements,
  explicit strategy approval and actual publication/source/quote capacity checks. Approval-write/concurrency
  integration and an isolated migration rehearsal were not run; applying 029 successfully is not a rehearsal.
  Do not execute operational activation as unit validation.
- **Tags:** architecture, decision-engine, risk-management, market-microstructure

### Delphi Live short sessions and calendar extension
- **Q:** How should the frozen full-session V1 collection, protective exits, marks, daily baselines and
  forward outcomes handle an official early close?
- The reviewed calendar installed on 2026-09-06 ends at 2026-12-23. TMX lists a 13:00 close on December 24;
  treating it as a full session or omitting it as though the exchange were closed would misstate evidence.
- **Status:** open — resolve before 2026-12-24 and before extending the snapshot, including protection
  for positions carried into the short session. [Calendar record](../../Operations/Calendars/README.md).
- **Tags:** data-pipeline, risk-management, decision-engine

### Risk-threshold ownership and persisted defaults
- **Q:** Which values are the authoritative policy for new tracked positions and Delphi's downside veto,
  and which are merely legacy constructor/repository defaults?
- `StrategyConfig.MaxDownProb` and the design rules use `0.35`, while the repository insertion default and
  `DownProbabilityGate` constructor fallback use `0.20`. Delphi currently supplies the strategy value, but
  another caller can receive a materially different veto without an explicit policy choice.
- `StrategyConfig.WarningPercent` and the design rules use −5%, while accepted ADR-0015 and `TradeManager`
  create tracked positions with a −8% warning. The −10% stop agrees, but the warning contract does not.
- Before changing code or stored values, classify each value as a versioned strategy setting, a tracked-position
  lifecycle policy, a per-run override, or a compatibility default. Decide whether entry and exit policies need
  separate versioned snapshots, and preserve existing position/audit history.
- **Tags:** risk-management, decision-engine, database, architecture
- **Status:** open — identified by the 2026-09-01 static changeability audit. Do not normalize these values as
  mechanical cleanup; resolve them through an explicit decision and strategy/policy version boundary.

### On-Balance Volume soft tilt — first live-read calibration (ADR-0016)
- **Q:** The first live Delphi OBV read (2026-06-06, 233 symbols) validated the wiring
  (`0 indeterminate`; the ±0.1 tilt verifiably reordered ESI below BTE) but surfaced two
  numbers that should be calibrated before the soft tilt is trusted long-term.
- **Finding 1 — is `ObvSignalWeight = 0.10` too strong vs. the *gate-passer* RScomp spread?**
  ADR-0016 sized the weight against the full-universe `RScomp` range (~±0.2). But the names
  that actually clear every gate and compete for the executable #1 slot cluster near
  `RScomp` ≈ ±0.1 (e.g. FM +0.097, ORE −0.009, DPM +0.022) — the high-RS names (MATR +0.91,
  ATH +0.77) all fail gates. So among gate-passers a ±0.1 tilt is the same magnitude as the
  entire RS spread and can act as the deciding vote rather than a gentle nudge. Resolve via a
  paper-trade IC/agreement window; candidate fix is to scale the tilt to gate-passer RS
  dispersion or lower the constant. Do **not** retune by guess.
- **Finding 2 — is the classifier's "≥2 pivots per side" rule inflating `Doubtful`?** First
  read was 140/233 (60%) `Doubtful`. Part of that is structural, not market chop:
  `ObvFieldTrendCalculator.Classify` only returns `Rising`/`Falling` when it has ≥2 UP *and*
  ≥2 DOWN pivots; once any opposing pivot exists with fewer than two per side it falls into
  the "too few pivots" `Doubtful` branch. With a 20-session breakout window over capped
  6-month retention, pivots are sparse, so 1–3-pivot symbols are forced `Doubtful`. This is
  conservative/safe for a soft tilt, but (a) keeps the signal quiet and (b) directly
  throttles the upcoming **Climax** indicator, which aggregates the same UP/DOWN
  designations — a thin pivot supply means a thin breadth tally. Decide whether to relax the
  pivot rule (e.g. allow a single-pivot-per-side higher-high/higher-low call) or accept the
  conservatism.
- **Tags:** decision-engine, technical-indicators, time-series, math-statistics
- **Status:** parked — wiring verified live (ADR-0016). Both numbers to be calibrated against
  a paper-trade window; Finding 2 is also an explicit input to the Climax ADR.

### Sector index history backfill (`dbo.SectorIndices` thin coverage)
- **Q:** The 2026-05-23 Delphi run exposed `RScomp`/`CompZ`/`RS10d` displaying `null` for the top-14 passing candidates while a few lower-ranked rows (SOFY, RDDY) showed real values. Root cause: `dbo.SectorIndices` currently holds only **7–8 trading days** of history per `^TTxx` symbol (range 2026-04-16 → 2026-05-22). `RelativeStrengthCalculator.Compute(...)` clamps `n = min(stockBars, sectorBars, marketBars)` to ~8, so `ReturnDiff(horizon=10)` returns `null`, `ComputeRsZ(horizon=10, zWindow=20)` requires `n ≥ 30` and also returns `null`, and the switch expression collapses `CompositeScore` and `CompositeScoreZ` to `null` for every sector-mapped symbol. The 5 "fallback to XIU" symbols show non-null values because XIU has full history, but the sector dimension is mathematically degenerate (StockVsSector ≈ StockVsMarket, SectorVsMarket ≈ 0) — those numbers carry no real sector information.
- **2026-05-23 partial fix (diagnostic, not data):** Delphi now emits an explicit RS-coverage block in both console and diagnostic report (min/max sector bars, fallback count, null-composite count) and a `⚠` warning when sector history < 80 bars. The bad data still produces null composites, but at least Delphi tells the truth about why instead of silently rendering empty cells.
- **Real fix needed:** backfill TraderDB sector indices to ≥ 80 trading days (max RS horizon 60d + Z window 20d). Open sub-questions:
  - Owner: should this live in Hermes (the daily TMX importer) as a one-shot backfill subcommand, or in a separate `Tools/Backfill` console app? Hermes-side keeps the data path centralized; standalone keeps a one-off concern out of the daily flow.
  - Source: TMX Money historical query (matches Hermes's existing transport) vs Yahoo (`^GSPTSE` siblings — but TSX sector indices are not all on Yahoo). Probably TMX.
  - Cadence: one-shot backfill + rely on Hermes daily appends afterward, or recurring "fill any gap to N bars on startup" guard. The latter is more robust to symbol-add events.
  - Retention floor: do we always need ≥ 80 bars, or do we want a longer buffer (e.g., 120) so Hercules can backtest RS feature lift on the same data Delphi sees live?
  - After backfill: re-validate Z-window choice (currently `zWindow = 20`) against actual sector volatility — 20 may be too short for sector indices that move smoothly.
- **Tags:** data-pipeline, technical-indicators, decision-engine
- **Status:** parked — diagnostic visibility shipped. Data backfill is the next ADR-tracked piece of work (candidate ADR-0011).
- **2026-09-01 correctness update (ADR-0041):** the calculator no longer clips undated arrays to
  `min(count)`. It aligns exact stock/sector endpoints to canonical XIU sessions and reports interior or
  stale-tail gaps. The complete current feature set requires 61 sessions, not the earlier diagnostic's
  `60 + 20 = 80`; retaining 80–120 sector sessions is still useful operating/backfill headroom. The
  source, owner, cadence, and historical-backfill questions remain open.

### Relative Strength composite scale — raw return diffs vs Z-score normalization
- **Q:** The 2026-05-23 Delphi run showed `RScomp = +0.000` (3-decimal display) for the entire top-14 passing list. Investigation in [`RelativeStrengthCalculator.cs`](../../Core/RelativeStrength/RelativeStrengthCalculator.cs) confirmed the values are **real, not a bug**: the composite is a weighted blend of raw 10-day return differences (`0.5×svm10 + 0.3×svs10 + 0.2×secvm10`), which for typical TSX stocks lives in `±0.005` (±0.5%) on a 10-day window. The leaderboard now displays 4 decimals + `RS10d` (raw stock-vs-XIU 10d) for visibility, but this is a *cosmetic* fix.
- The underlying issue is **scale**: as a tiebreaker, RS composite has almost no resolving power when the top candidates cluster within ±0.05% of XIU. The `RelativeStrengthRow` already carries `RS_Z_StockVsMarket` / `RS_Z_StockVsSector` / `RS_Z_SectorVsMarket` (Z-score normalization — today's RS expressed in std-devs of its own 20-day history), which would map the same data into a `~±2` range with real separation.
- **Update 2026-05-23 — partially resolved by ADR-0010.** A new `CompositeScoreZ = 0.5·Z_svm + 0.3·Z_svs + 0.2·Z_secvm` is now computed, persisted (`dbo.RelativeStrengthFeatures.CompositeScoreZ`), and displayed in both Delphi console and diagnostic alongside the raw composite. v1 is additive only — no ranking change. The remaining open sub-questions:
  - Historical Hermes RS rows do not yet have `CompositeScoreZ` populated; a full RS backfill is required before any IC measurement is meaningful.
  - Should `CompositeScoreZ` eventually *replace* `CompositeScore` as the persisted primary RS scalar, or coexist permanently?
  - Are the same `0.5 / 0.3 / 0.2` weights right for the Z composite, or does volatility normalization change which horizon/dimension matters most?
- **Tags:** technical-indicators, decision-engine, math-statistics
- **Status:** partially resolved — additive Z composite shipped (ADR-0010). Promotion to ranking is tracked in the separate "DirectionEdge as primary ranking key" question below. Backfill + weight-tuning questions remain parked.

### DirectionEdge as primary ranking key — re-evaluation
- **Q:** Delphi currently ranks by `DirectionEdge = P(up10) − P(down10)` (LightGBM probabilities for the next-10-day binary up/down models). This was chosen because (a) it's directly aligned with the trade horizon (~10 days), (b) it fuses two independently-trained models, partially cancelling shared bias, (c) it's bounded `[-1, +1]` and behaves like a calibrated conviction score, and (d) BreakoutEnhanced is already used as a setup *filter*, so the ranker needed an orthogonal directional axis.
- Is this still the right primary key? Concerns surfaced 2026-05-23:
  - P(up) and P(down) are both LightGBM outputs on overlapping feature sets — "independent" is aspirational, not measured.
  - Top picks routinely cluster within `±0.02` of each other on Edge, so the ranking is effectively determined by `CompositeScore` anyway in practice.
  - Relative strength — arguably the single best-validated momentum predictor in the public literature — is currently *not* in the ranking key at all.
- Candidate alternatives to evaluate:
  - Keep Edge primary; add `RS_Z_StockVsMarket` as an explicit tiebreaker before `CompositeScore`.
  - Promote a blended key: `0.6×Edge + 0.4×RS_Z` (after Z-normalization is in place).
  - Two-stage rank: Edge > threshold filters, then RS_Z ranks within the survivors.
- Measurement needed before deciding: out-of-sample IC (information coefficient) of Edge vs RS_Z vs blend against next-10-day return on held-out 2024–2025 data.
- **Tags:** decision-engine, ml-models, math-statistics
- **Status:** parked — no ranking change until Z-score composite lands and IC comparison is run.

### Leveraged/inverse ETP classification
- **Q:** ADR-0009 introduced `dbo.Symbols.IsLeveragedOrInverseEtp` as the authoritative gate that excludes products like NRGU/BetaPro/MegaLong/SavvyLong/LFG-Daily-2× from Delphi's ranking universe. The flag was set by a one-shot curated UPDATE against the current 62 known leveraged/inverse rows. The runtime `IsLeveragedOrInverseByName` guard in `Delphi/Program.cs` is a defense-in-depth net, not a primary classifier — it relies on a hand-maintained keyword marker list and is therefore brittle to new naming conventions.
- How do we keep the flag accurate as Hermes imports new symbols over time? Options:
  - Manual review queue (Hermes flags any new `SecurityType = 'Stock'` row whose `ShortName` matches the marker list and writes it to a `SymbolsPendingClassification` table for human approval).
  - Hermes-side classifier (run `IsLeveragedOrInverseByName` at import and write `IsLeveragedOrInverseEtp = 1` directly when it fires; rely on a periodic audit to catch misses).
  - Periodic audit query (cron-style: re-run the marker scan weekly against `dbo.Symbols WHERE IsLeveragedOrInverseEtp = 0` and surface unflagged candidates in a report).
- Residual risk to characterize: a brand-new BetaPro/Horizons product imported with a never-before-seen naming convention will pass both gates silently. What blast-radius bound should we accept (e.g., "at most 1 day in the leaderboard before the periodic audit flags it") before we commit to a heavier solution?
- **Tags:** decision-engine, data-pipeline, risk-management
- **Status:** parked — current curated flagging covers all known cases. Revisit when Hermes next imports a batch of new symbols or when an unflagged leveraged ETP appears in `TopPicks`.

### Granville cross-family overlap audit (#04 Plurality / #10 Leadership / #14 Most Active)
- **Q:** On 2026-06-?? these three indicators all fired `StrongBullish` simultaneously. Hypothesis: they report different *facets* of the same underlying evidence ("breadth was positive AND XIU rose"), which is acceptable ensemble behaviour. But before we tune Granville point weights we should deliberately audit cross-family correlation:
  - Compute pairwise daily co-firing rates across all 28 active Granville indicators over the available history.
  - Flag any pair with co-firing rate ≥ 0.85 and identical sign — these are candidates for either (a) deduplication via reduced point weight, or (b) explicit redundancy acknowledgement in the ensemble.
  - Specifically inspect: #04 Plurality vs #10 Leadership vs #14 Most Active (the trigger for this question); #15/#16 Weighting vs #04 Plurality; #17/#18/#19/#20 within Genuity.
- **Tags:** technical-indicators, decision-engine, math-statistics
- **Status:** parked — no indicator code changes until audit run. Revisit before next Granville point-weight tuning pass.

### Genuity #19 — fractional GranvillePoints follow-up
- **Q:** ADR-0008 introduced a ±5% tolerance buffer on the magnitude-ratio band by abstaining (Neutral/0) inside the buffer, because `GranvilleResult.GranvillePoints` is an `int` in v1 and "half-point damping" isn't representable. Should we promote `GranvillePoints` from `int` to `double`/`decimal` system-wide so borderline cases can contribute fractional conviction (e.g. ±0.5) rather than abstain entirely? Scope: every `IGranvilleIndicatorGroup` implementation, the composite adjustment math, the `GranvilleIndicatorLog` schema, and any DB-backed analytics that assume integer points.
- **Tags:** technical-indicators, decision-engine, math-statistics, architecture

### Oracle (LLM layer) — follow-ups deferred during Phase 2 pause
- **Q:** Should narrative quality have an automated lint pass
  (e.g., regex that flags adjectives without an adjacent `(Field=value)`
  citation)? Currently Rule #8 of the system prompt asks the model to
  quantify, but we don't enforce it. Cheap Phase-3 add.
- **Q:** Pre-compute deterministic qualitative buckets in C#
  (`RelativeStrength.CompositeScoreBucket = weak|neutral|strong` from
  empirical quantiles) and have prompts parrot them rather than coining
  freeform adjectives?
- **Q:** Add a real `ClosestGate` field to `DecisionDossier` (the gate with
  the narrowest pass margin) so the model can answer "what nearly killed
  this pick?" without guessing. The previous freeform version of the
  question was dropped because the model always answered "Granville".
- **Q:** Phase 3 debate-loop schema: should `[LlmConversation]` reference
  `DossierId` directly, or go through a new `[OracleSession]` parent so a
  single session can span multiple dossiers (e.g., "why these 7 picks and
  not those 7?")? Decide before writing the table.
- **Q:** Per-dossier token-budget enforcement (Rule R4) is not yet
  implemented — currently the only guard is the implicit prompt size. Add
  before Phase 4 (news context) since that's where prompts will balloon.
- **Q:** Regression eval set (Rule R6) — we have no canned dossiers +
  expected key points yet. Build before Phase 3 ships so prompt edits can
  be evaluated objectively.

### Weighting category (Granville #15–#16) — follow-ups after ADR-0003
- **Q:** Re-validate `ScoreB ≥ 0.50` / `ScoreC ≥ 0.60` thresholds after
  N ≥ 30 live triggers or 12 months in production (whichever first).
  Current v1 calibration sample is N = 13.
- **Q:** Should we eventually upgrade the price-weighted contribution proxy
  to *true* cap weights (float-adjusted shares × price)? Defer until we
  have a concrete divergence between proxy and cap-weighted views worth
  investigating.
- **Q:** Should narrow *down-day* behaviour become its own future
  indicator? Backtest showed mean-reversion bounce, not continuation —
  potentially a *bullish* short-horizon signal. Park until ADR-0003 has
  live data.
- **Q:** Promote `Core/Config/Xiu60Constituents.cs` to a DB-backed
  `XiuConstituentMembership` table with effective-dated rows? Needed if
  we move to point-in-time-correct backtests for the Weighting group.
- **Q:** Hermes-side validation that all 60 constituents are present in
  the universe — still deferred; runtime ≥ 50/60 graceful-degradation
  guard remains the contract.

### Granville Dullness (#21, #22) — deferred per ADR-0005
- **Q:** When (not if) we backfill XIU to 2001, rerun `dullness-calibrate`
  and check whether #21 / #22 hit-rates clear 55% at h ∈ {5, 10} with
  n ≥ 25 per bucket. If so, supersede ADR-0005 with an implementation ADR.
- **Q:** Is there a different universe (sector indices, individual stocks,
  or a non-TSX benchmark) where post-decline dull days are frequent enough
  to make #22 testable *now*, without waiting for a TSX backfill?
- **Q:** The positive mean forward return after #21 fires suggests Dullness
  may act as a *continuation* signal in a momentum regime. Should we
  re-derive Dullness as a continuation indicator under a new ADR rather
  than revive Granville's literal bearish framing?

### Delphi opening confirmation and swing exits (ADR-0023)

- **Q:** Which separately timestamped source should support the still-experimental opening-confirmation comparison, especially bid/ask spreads and opening gaps that ADR-0028's delayed OHLCV monitor does not establish?
- **Q:** Does a 5-, 15-, or 30-minute observation checkpoint add the best net value after missed winners, avoided losses, spreads, and slippage?
- **Tags:** architecture, data-sources, decision-engine, market-microstructure, risk-management
- **Status:** partially resolved — ADR-0028 accepts fifteen-minute delayed management and completed five-minute evidence; ADR-0030's durable ledger, migration, collector, and first market-hours cycle are complete. Opening confirmation remains an unactivated challenger.

### Automatic Delphi-to-paper entry and rotation policy (ADR-0034, ADR-0037)

- **Q:** Should a separately scored shadow controller automatically open Delphi's selected Top pick at the first completed fifteen-minute checkpoint, or should all operational ghost entries continue to require operator confirmation?
- **Q:** If automatic shadow entry is tested, which lens/rank, capital allocation, vacancy-filling, duplicate-symbol, and rotation rules define it, and how will it remain separate from operator-timed positions and official Athena outcomes?
- **Tags:** architecture, decision-engine, market-microstructure, risk-management
- **Status:** resolved for System-selected Ghost portfolios by ADR-0051. Shadow V1 freezes the valid 09:30 OfficialPaper Top 3/Top 5 Continuation and Breakout candidates, applies its versioned capital/risk/execution policy, and remains separate from operator Ghost/Real records and Athena. AI selection and real execution remain future decisions.

### Delphi Live — future research after ADR-0053

- **Q:** May a future evidence-backed version add an explicit named lens-specific requirement, such as requiring Volume Support for a Breakout action?
- **Q:** Which causal calculations and categorical boundaries should govern the explanatory multi-horizon volatility profile after V1?
- **Q:** What volatility-adjusted rule should group raw observations into one derived price-move event without erasing the immutable five-minute evidence?
- **Q:** What structured input/output, model-and-prompt versioning, reproducibility, latency, failure, and evaluation contract must a future AI Selector satisfy before Shadow comparison?
- **Tags:** architecture, decision-engine, machine-learning, technical-indicators
- **Status:** deferred, non-blocking — ADR-0053 accepts the frozen V1 behavior and leaves these as prospective successor decisions. Source implementation does not authorize migration, activation, external collection, model training, deployment, or broker work.

### Delphi Live historical replay follow-ups

- **Q:** How should the pure frozen-source selector validate published-but-ineligible rows consistently with the SQL path, which filters eligibility before selection?
- **Q:** Which explicit spread, slippage, commission and quote-path assumptions should a later research replay compare before performance interpretation?
- **Tags:** architecture, data-pipeline, market-microstructure
- **Status:** deferred after ADR-0054. The September 4 exploratory replay preserves the existing live eligibility filter and claims no live equivalence. Additional days, cost sensitivity and monitoring without an activated portfolio remain separate future decisions.

### Always-on hosting for the paper monitor

- **Q:** After the WPF-hosted monitor is proven, should the shared Core monitor
  also run under Windows Task Scheduler or a Windows Service so closing the UI
  or sleeping the interactive session cannot miss polls?
- **Tags:** architecture, data-pipeline, risk-management
- **Status:** parked — version 1 is explicitly process-local under ADR-0032.
  Revisit after several live sessions establish restart, duplicate-instance,
  and failure behavior.

### Broker reconciliation and possible future order routing

- **Q:** Should TraderVI eventually import broker positions and fills, or should Real reconciliation remain operator-entered?
- **Q:** If order routing is ever proposed, which independently authenticated broker adapter, account allowlist, cash/position limits, order preview, partial-fill state machine, kill switch, and reconciliation proof are required?
- **Q:** Should version 2 support partial Real exits and commissions before any broker integration is considered?
- **Current safety boundary (2026-09-03):** ADR-0048 keeps version 1 as one all-shares,
  zero-commission fill and requires the operator to affirm those facts. The workflow must not be used for a
  partial fill or any fee-bearing transaction. A version-2 decision must define remaining-share state,
  cost-basis and commission allocation, multi-fill retry identity, final-close rules, and correction handling;
  adding shares/commission fields alone is insufficient.
- **Tags:** architecture, data-pipeline, market-microstructure, risk-management
- **Status:** open design decision, implementation blocked — ADR-0039 resolves the combined display and
  manual-fill boundary and ADR-0048 makes the current full exit atomic/retry-safe. Neither authorizes partial
  fills, commissions, a broker connection, or order routing.

### Main-account overview — ADR-0068 follow-through

- **Deferred: complete-account earnings periods.** The accepted UI direction is implemented and
  verified offline, with total gain explicitly unavailable. The current useful measures are recorded
  sale gains over the last 30 Toronto calendar days and saved open-position gains; neither is complete
  broker earnings. Selectable full-account return periods and today's change await sufficient history.
- **Open: complete earnings evidence.** Define supported account history, net contributions,
  withdrawals/transfers, fees, income, open-position valuation and freshness before displaying a
  real-account earnings total. Account reports alone do not establish complete cash-flow returns.
  Distinguish closed-trade realized gains from unrealized changes in open holdings. Define strategy
  attribution separately so a newly selected strategy does not inherit credit for all account history.
- **Deferred behavior decisions:** changing strategy with holdings/orders still open, or making fixed
  gate-comparison accounts promotable. Quick access to review is the accepted UI direction;
  ADR-0062's paused/empty rule and ADR-0067's fixed-pair promotion exclusion remain unchanged.
- **Deferred integration:** Wealthsimple connection, account reconciliation and autonomous execution
  remain future work under ADR-0055. The UI must show its actual account-data and execution authority.

See [the connected workspace draft](../concepts/trading-workspace-redesign-draft.md).

## Resolved

- **Missing Friday data and SPY confirmation (S8/S9)** — resolved under ADR-0058 on 2026-09-06.
  Weekday-only midnight scheduling omitted Saturday's collection of Friday's close; execution and
  read-only morning supervision now run every calendar day. Friday and actual SPY history were refreshed
  with verified backups. The selected v3.4 strategy requires current observed XIU/SPY inputs and reuses the
  same four models. No substitute benchmark, threshold retuning or historical evidence rewrite occurred.
  Independent historical US-calendar gap detection remains outside this repair; a missing SPY endpoint
  on a US-only closure stops new publication explicitly. The first official corrected run subsequently
  completed with valid provenance and no qualifying trade; normal nightly completion remains open.

- **Unified Ghost/Real trading dashboard** — resolved by ADR-0039 and operational migration 013 on 2026-08-28. Durable mode/account fields, an immutable Ghost-to-Real audit, separate P/L, manual Real entry/exit reconciliation, and a hard Ghost-only automatic-exit guard are implemented and verified. All legacy rows remain Ghost. The five-share EDR Ghost mirror was automatically closed at $15.62. On 2026-08-28 the operator declined to add a separate Real row because EDR is no longer in the current Delphi picks; the historical Ghost lifecycle remains unchanged.

- **Intraday evidence resolution and durable ledger** — resolved 2026-08-26. Persist completed five-minute bars in version 1, consume and retain direct completed fifteen-minute bars for policy decisions, and use exact three-component aggregation as a consistency check when available. One-minute bars remain a possible research source rather than a storage requirement. Poll attempts and first-received bars are separate immutable records, and conflicts never overwrite first evidence. Recorded in ADR-0028 through ADR-0030.

- **Switch Continuations lens ranking from raw `RScomp` to Z-scored `CompZ`**
  — logged 2026-05-24. The user has agreed this should happen "soon" (ADR-0014
  deferred it as option (d)). Before switching: confirm `CompZ` distribution is
  healthy post-backfill (min/max sector bars ≥ 80 so Z-window is valid); measure
  whether `CompZ` produces meaningfully different rank-orderings than raw `RScomp`
  over a 2–3 week paper-trade window; if yes, cut ADR updating ADR-0014.
  - **Prerequisite:** full `RelativeStrengthFeatures.CompositeScoreZ` backfill
    (Hermes historical RS rows need to be re-computed with Z columns populated).
  - **Tags:** decision-engine, technical-indicators, math-statistics
  - **Status:** deferred — needs RS backfill + outcome measurement first.

## Resolved

- **ScoreB/ScoreC threshold defaults (`ScoreB ≥ 0.50`, `ScoreC ≥ 0.60`,
  `K = 3`)** — closed by **ADR-0003**. Empirically calibrated from
  distribution + forward-return analysis on 2020-01-02 → 2026-05-06.
- **AND-gate vs. weighted-average for ScoreB/ScoreC** — closed by
  **ADR-0003**. AND-gate at strict thresholds is the only configuration
  with measurable 1d edge surviving sub-period split. Weighted-average
  diluted both signals.
- **Price-weighted Dow-style proxy for v1** — closed by **ADR-0003** and
  the supporting `concepts/price-weighted-contribution.md`. True cap
  weights deferred (see follow-ups above).
- **Static C# constituent list for v1** — closed by **ADR-0003**. v1
  uses `Core/Config/Xiu60Constituents.cs` (reviewed 2026-05-07).
- **40% placeholder breadth threshold** — superseded by empirical
  `ScoreC ≥ 0.60` in **ADR-0003**.
- **Hermes / TMX dot-suffixed symbol handling** — resolved during
  calibration: TMX accepts `TECK.B`, `BBD.B`, `RCI.B`, `GIB.A`, `REI.UN`
  in canonical dot form; missing coverage was a seeding gap, not a
  symbol-format limitation. Backfilled via `Tools/Backfill.MissingXiu`.

### Portfolios comparison — follow-through after ADR-0061

- **September 9 operational follow-up:** recovery published after 09:30, so normal Shadow and Live
  source freezing correctly rejected it; all published rows also failed Breadth. The existing September 8
  exceptions do not apply. A tracked holding's multi-day 15-minute request now reports immutable-bar
  conflicts, blocking that holding's automatic policy evaluation. Provider correction/reconciliation
  requires review; do not overwrite first-received evidence to clear a status. ARX/SES still have no
  new TMX bars/detail responses; verify listing/universe treatment before the next weekly map refresh.

- **September 8 operational follow-up:** ADR-0065 applies a specifically authorized, dated Live
  engineering admission. A reusable late-start facility, UI test label and clean restart of the
  resulting comparison history remain design work; the one-date SQL exception is not that facility.

- **Accepted/implemented:** Compare as default, Performance, and one-challenger Strategy review; shared selection, period and account scope. Existing operational tools remain accessible in a collapsed section.
- **Provisional display:** aligned saved closing-date returns, normalized charts and maximum observed closing decline. Account histories can span earlier rule assignments; observed dates do not prove complete TSX session coverage.
- **Implemented under ADR-0062:** complete-strategy identity, supporting paper-account mapping and durable human-approved Trading routing. Prospective statistically comparable evidence remains deferred to automated Wealthsimple activation; lifetime paper returns remain descriptive.
- **Deferred:** historical daily Shadow generations, arbitrary date selection, benchmark overlay, period trade/fill statistics and reconciled Real cash-flow returns. These need explicit evidence contracts, not invented values.


**September 9, 11:10 update:** source adoption is deployed under [ADR-0066](../adr/0066-operational-paper-daily-source-adoption.md); four Shadow sessions and Live use the corrected current run. Earlier cutoff blocks above are historical. Separate normal/portfolio-rules-only comparisons are the next accepted milestone, with concrete account family/count and capital at activation. Gates remain unchanged. The tracked bar conflict, provider gaps and nightly Hermes map-failure handling remain separate follow-ups.

### Direct Wealthsimple integration — ADR-0069 through ADR-0072

- Native sign-in is operator-confirmed. Read-only self-directed TFSA discovery, balances, returns and
  holdings are live-verified under ADR-0072. Managed TFSA accounts remain excluded.
- Verify refresh rotation after actual expiry, reconnect behavior and any provider DPoP rollout.
  Browser key handoff remains unsupported; protected bearer storage remains opt-in.
- Design broker-to-local reconciliation of fractional holdings, security identities, fill history,
  deposits/withdrawals, income, pending orders and valuation currency before feeding strategy inputs.
- Design order submission/status/cancellation, duplicate prevention, account authority and reviewed
  execution limits separately. The new account display has no order endpoint or SQL write path.

### Permanent TFSA and primary execution — ADR-0073

- **September 14 / ADR-0079:** regular-session market orders and promotion-initiated non-overlap exits
  before new buys are accepted for the first version. Account-history reconstruction and protected
  observations now gate the transition preview. Still open: broker-to-SQL mapping of existing Real
  records, full cash/settlement coverage and risk baselines, correction of disputed history, individual
  fill sequencing for overlapping orders, observation-journal retention, inherited protection, exact
  intended-holdings evidence and automation qualification. Native validation and execution activation
  remain separate operations. The earlier limit preference below is historical.

- **Manual market-buy UI, September 10:** [ADR-0075](../adr/0075-manual-market-buy-ticket.md) implements
  the accepted editable whole-share ticket, compact dialog and operator-managed buys. The captured
  broker quote/session contract, protected full-intent recovery and cumulative observations are wired
  and tested offline. Native live acceptance, account-wide accounting reconciliation, conflict recovery
  tooling, cross-device history and less restrictive pending-order reservations remain unfinished.
  Portfolio automation, selling and automatic tracked-position/exit ownership remain deferred.

- ADR-0074 supplies the market-buy client contract and cumulative fill-status read from an operator
  capture. ADR-0075 adds protected reviewed intents, cumulative observations and recovery UI for manual
  buys. Native live mutation acceptance and broker duplicate-ID behavior remain unverified; strategy
  execution/account reconciliation remain unfinished. Preserve the strategy limit-order preference;
  manual market buying does not authorize automatic market fallback or strategy activation.

- Accepted: permanent self-directed TFSA, independent paper portfolios, primary strategy version as
  future real-order authority, reconciliation and no TraderVI-originated fractional-share orders.
- The ten-share buy increment is a preference, not a board-lot or guaranteed-fill rule. The implemented
  sizing helper rounds down within risk/cash limits and permits smaller whole quantities below ten.
  Its use in a real execution workflow remains unfinished; exits allow any positive whole quantity.
- Accepted clarification: retain overlapping holdings and let the incoming strategy manage them;
  do not sell solely to repurchase on promotion. An unconditional cash-start transition is superseded.
- Define the incoming intended-holdings snapshot, treatment of stale paper state, quantity adjustments,
  protection initialization, required non-overlap exits and activation timing. Preserve actual cost
  basis/entry history and distinguish strategy handover from a new purchase.
- Confirm limit pricing, unfilled-order behavior, cancellation and protection ownership throughout
  transition. Retained holdings change the future activation condition from unconditional emptiness.
- Continue the [integration draft](../concepts/strategy-promotion-and-execution.md#tfsa-integration-draft--september-10-2026).
  Reconciliation, complete order lifecycle, evidence eligibility and operational activation remain work.

## Nightly build retention (ADR-0078, 2026-09-14)

Choose retention limits for ignored captured-source/build directories and the dedicated NuGet cache. Cleanup is manual until this policy is reviewed; operational logs and source identities must remain attributable.

The September 23 user-authorized [one-time cleanup](../project-status.md#latest-operational-evidence)
removed reviewed old compiled outputs while retaining source/evidence, models, the latest two nightly
builds, package cache, recent work and the separate worktree. This was not a recurring-retention decision
or installation. Future policy must preserve attributable evidence and active work, and distinguish
rebuildable binaries from models and private captures; older pruned builds require rebuilding before reuse.
