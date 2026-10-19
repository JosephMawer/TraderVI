# TraderVI Roadmap

This is the authoritative priority list. [Project status](project-status.md) owns dated evidence;
the [strategy alignment checklist](strategy-alignment-implementation-checklist.md) owns execution
dependencies. [Design rules](design-rules.md) and applicable accepted ADRs own behavior.
The [session-work register](session-work-register.md) preserves cross-chat progress, unfinished ideas
and source review points; use its owning-record links when resuming work.

## Accepted direction — prove strategies, then adopt one deliberately

Keep independent paper strategies, deliberately approve an exact complete strategy for the permanent
TFSA, and enable real execution only after the required account, protection and broker checks.
[ADR-0055](adr/0055-independent-strategies-to-approved-live-execution.md) and
[ADR-0062](adr/0062-complete-strategy-recommendation-promotion.md) distinguish recommendation approval
from automatic trading. The operator's September 23 real-trading target is an aspiration, not verified
readiness or order authority. Any portfolio must be promotable, with ongoing management until pause
or switch. Manual buys stay outside by default, with explicit purchase-time or later handover into
exit management alongside the strategy's normal automatic positions
([ADR-0085](adr/0085-portfolio-wide-ongoing-tfsa-management.md)); ungated Breakout Top 3 is an
initial candidate. The [September 23 activation review](reviews/supervised-session-20260923.md) says NO-GO.

## Now — restore a dependable advisory loop

**October 18 UI design track:** explore a fresh, clearer layout for Trading, Portfolios, Delphi and
Delphi Live before detailed drill-downs. Workspace and Trading's main arrangement are selected;
Performance first is now selected and integrated into the Trading concept. Both Buy/Sell controls
belong in the holding panel. Portfolio choice is explicitly deferred; existing engine/family and
comparison-selection contracts remain. Today is the default with an Overall option; redundant heading/
badge removal is agreed. Next refine remaining Trading visual details and desktop dimensions before
other screens. Daily performance source/calculation qualification remains an implementation dependency.
Remaining-screen order and WPF implementation are still open.
Existing operating priorities and execution prerequisites below remain.
[Design draft](concepts/trading-workspace-redesign-draft.md#october-18-visual-redesign--draft).

September 26 requested direction: add an AI-managed momentum portfolio and use it for real trading,
targeting September 28. AI Explorer is deferred to a future session; the first pass is AI Momentum
in paper and intended live use. [ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md) accepts
discretion, enforced boundaries, reducing risk as losses deepen and no loosening stops to prolong
losing trades. The first API client, shared spending journal and portfolio-proposal contract are
implemented offline under the operator's initial below-CAD-100 monthly ceiling. Codex subscription
access remains an alternative. The operator also raised Dots on October 17; its connection feasibility
is [unresolved](reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026),
with one saved-input replay now returned and validated as a permissible historical wait. No API replacement
or delivery-prerequisite change is agreed. Proceed with implementation of the agreed framework. Portfolio scope
includes all current strategy portfolios and their variants, expanding as new portfolios are built
and integrated (ADR-0087). The operator defers lesson criteria until they are relevant to Step 6;
continue delivery without another design discussion on that topic. Concrete rule mapping,
authoritative packet adapters and production budget/storage configuration are implementation work.
Learning consultation is source-integrated into the supervisor and execution-preparation coordinator;
one post-session learning review is now agreed alongside intraday saved-lesson consultation.
Steps 6–7 will connect qualified outcome evaluation and host scheduling under ADR-0087, leaving immature
or inconclusive cases pending and using the same monthly allowance.
Follow the [AI Momentum delivery sequence](strategy-alignment-implementation-checklist.md#ai-momentum-delivery-sequence):
its [delivery milestones](strategy-alignment-implementation-checklist.md#delivery-milestones) are a
visible AI reviewer, a complete tested paper trader, then supervised live trading. The reviewer is
the next visible target; it uses initial runtime/display portions of Steps 7–8 while the ten-step
checklist retains all paper/live completion requirements. Independent Step 7 development proceeded
while Step 3 awaited regular-session data. The offline review
scheduler, durable scheduling/events/review-block recovery and explicit tick adapter are built/tested.
Structured model-proposed cadence is now built/tested too. Per the September 29 instruction, work returned
to Step 3: its connected check verifies cash-only persistence/recovery and the receipt-clock fix.
Missing/old or differently timed held-stock bars still block invested values; trades after a price boundary
must wait for newer prices. The tested TMX quote fields do not solve freshness: last sales can be old and
bid/ask has no quote timestamp. An existing Wealthsimple quote query includes a source-time field;
the September 30 authorized samples cover all current holdings, with advancing timestamps and some
changed prices. The operator clarifies that TMX remains the normal source. Wealthsimple is only a
possible limited fallback for AI review of delayed/unavailable held-stock inputs, with timestamp
uncertainty explicit. Defer this lower-priority item and the associated valuation questions; no source
switch is planned or implemented. Return to the main visible-reviewer integration work (Steps 7–8).
The Step 7 coordinator connects saved inputs, the existing reviewer and SQL decision storage in
source, preserving current input checks. Migration 042 is installed and native storage checks pass.
The operator selected the existing Breakout 3 / PortfolioRulesOnlyV1 book as the first cash/holdings source.
Local masked key entry and the starting-portfolio inspection query are prepared; the existing Oracle user-secrets key has been located (project status).
Core loads that credential; metadata access and one connected generation with recorded usage are verified.
The initial five-minute interval and open / open+3h / close-15m checkpoints are agreed.
Timing and initial cost bounds are agreed. Opt-in desktop startup/shutdown and the Core review host
are implemented/tested but not activated. Private history is initialized under the accepted ten-minute
first-test input age. The October 1 connected review saved `wait`, a two-minute next-review request and
conservative token/cost accounting; SQL/local evidence matches. Missing downstream dependency locks
caused the absent daily publication. The repair and authorized recovery restored the October 1 publication
to all 15 portfolios; two ungated Continuation choices had proposal-permitted AI context at 10:57.
Other choices retain their existing admission/evidence blocks. Athena's two invalid prediction outcomes
remain a separate follow-up; see project status for the Attention result and dated verification.
The authorized follow-up assembled four eligible choices but exceeded the unchanged input-token cap
before generation. The concise review view is now built/tested and its saved-packet token count fits
the existing cap. Full source packets and eligibility/spending rules remain unchanged. The authorized
October 9 concise-input review returned a valid wait with all choices unavailable; SQL/local evidence,
exact request rendering and cost match (project status). Empty learning was explicitly continued through
today's close with prior history preserved. This qualifies the blocked-choice wait path, not eligible
selection or continuous readiness. The later authorized desktop test verifies startup, blocked-attempt
timing, the bounded stop and disabled restart, but the last compact request exceeds the unchanged input
cap as session evidence grows. After v2 remained oversized, the operator approved removing engineering/
testing details and detailed evidence for unavailable choices. The v3 view is built/tested and its
largest saved example passes the unchanged cap; full source records and independent account/risk
context remain (ADR-0087 and project status). One fresh v3 review is now authorized, but its preflight
found stale invested-account prices; no paid attempt ran. The read-only trace confirms ongoing Live/Shadow
collection with failures and a stalled held-position mark. The authorized fixed-window diagnostic reproduces
five-minute timestamp-alignment rejection on both targets. The follow-up confirms source times are
preserved by mapping in the sample. The October 14 authorized full-session comparison qualifies the
implemented stable-context and supplementary-record correction (ADR-0089); the native opening trace
also identifies receipt-clock rejection, corrected with actual-store-time waiting. Both fixes are
tested and installed; scheduled Live cycles and current portfolio marks recover. The sparse trailing-
record refinement is installed and its first scheduled cycle has zero CollectionFailed receipts;
missing exact bars and historical/canonical conflicts remain explicit. The nine-stock follow-up
identifies an end-boundary omission plus unavailable sparse intervals; the observation-time end
correction and stale-source metadata are installed, with their first scheduled cycle/marks verified
(ADR-0089). Remaining unavailable intervals stay blocked. Next qualify
continuing collection and held-stock valuation through
the agreed input path, then use that single-review authority to qualify v3 interpretation, followed by
repeated successful desktop reviews and the basic display. Future
input growth remains guarded, not continuously qualified. Automatic reviews remain
off; further paid tests/activation require authorization. Collection health and invested coverage remain
unqualified. Timestamp-only inspection is built/tested and the two-target comparison is complete.
Further provider reads need authorization; do not round timestamps or silently discard records.
One invested saved-Shadow value passed under the test bound; continuous coverage remains unqualified.
Defer more permissive
handling of dated/incomplete review inputs unless it blocks integration (ADR-0087, September 30 clarification).
The AI chooses its active portfolio; the existing switching/history contract remains in force.
Normal held-stock coverage, faster invested-value persistence and storage/operational qualification remain
open Step 3 dependencies; preserve stale-input blocks rather than treating deferral as qualification.
Step 1 is complete offline; Step 2's framework and portfolio scope are agreed, with lesson criteria
deliberately deferred. Step 4 has begun with the tested offline continuous-round risk core, shared
assessment contract and scoped local journal/recovery. Step 3 now has tested broker and both paper-family
valuation adapters feeding the shared risk journal, now connected in source to paper saved-record readers.
The maintained portfolio catalogue and account-scoped review-packet builder are also implemented offline.
Native catalogue/decision-reader mapping and saved risk-value arithmetic are checked for all 15 installed
portfolios; source/freshness and Step 7 storage/host qualification remain pending (project status).
Both Daily Shadow and Delphi Live now supply saved signals, account-specific decision/confirmation
state, rules and daily paper performance to those packets. The missing comparison-source investigation
identified a tracked broker-symbol/canonical-symbol mismatch blocking Live session initialization.
The alias lookup and migration 041 are installed under ADR-0088; prospective source adoption and
a fresh Live cycle are verified. The Shadow risk reader now preserves the same legacy policy identities
as the decision reader. Current cash-only Shadow reads now carry checked transaction timestamps;
held prices retain their original freshness limits. Saved price timing is now measured: current
five-minute collection has multi-minute delay and gaps, so one-minute coverage remains unqualified.
Authorized one-minute samples show uneven latest-minute coverage. Raw rows and recent trades now
support quiet trading as the explanation for sampled gaps; the client did not drop those rows.
The one-minute price adapter and combined paper-account reader are built/tested. The reader reconciles
cash/history, rechecks the account after collection and requires fresh prices ending at the same minute.
Combined values now feed the risk journal and AI input packets, with tested history continuity,
explicit same-account source handover and persistent price-conflict blocks. Native after-hours checks
reconcile all 15 paper books and verify rejection/empty-journal recovery. The regular-session check now
verifies cash-only persistence/recovery. The SQL/app receipt-clock discrepancy is fixed and checked
natively without tolerance changes. Invested reads still reject incomplete/expired or differently timed
prices and post-price trades. The bounded quote alternative check leaves source qualification open.
Next identify usable timestamped prices, qualify reliable held-stock coverage, verify fresh invested
values/persistence and freshness/recovery, resolve collection gaps,
connect recurring paper risk observations, and qualify SQL concurrency and broker cash events, then connect
existing risk/execution adapters; source qualification
and activation remain separate. Existing engine daily templates use 7%.
The AI gradual scale now follows continuous losses/recovery across days, replacing its daily framing
and session-minimum lock (ADR-0087). Both starting balance and portfolio peak are selected references.
The accepted 5% / 3% / 1% cycle schedule replaces the AI 7% boundary and stops new buying after
the sequence's third failed round for the remainder of that session. The sequence and attempt count
carry across days without morning refill or renumbering. Failed-round closure and confirmed exits before another attempt are
agreed. Fresh qualifying entry signals from the selected engine permit AI-reviewed re-entry without
an added fixed delay. Proportional buying allowance within each round, with recovery before its
hard limit, is agreed. Round limits and allowance now trail the highest verified value within each
round; preserve the separate overall closing-peak history. The 10% cumulative review hold requiring operator approval and explicit UI
threshold visibility are agreed. Round continuity across closures and new-session engine consultation
are agreed. Three consults complement ongoing intraday monitoring; AI review intervals of one to
fifteen minutes during market hours are agreed, subject to budget. Provider/budget fallback and
fresh-review recovery are agreed (ADR-0087). Initial entry-signal, holding-closure and risk-limit
review hooks are agreed; shared timing/budget checks also apply to later hooks. The
scheduler now combines/deduplicates events, enforces bounded intervals and serializes reviews through
the existing learning-aware supervisor. Account-scoped scheduling now persists and replays through
restart and explicit session changes without releasing old spending reservations. Actual checkpoint
times and qualified production integration remain pending. Version-3 scheduled proposals supply
validated 1–15-minute intervals and reasons; accepted timing is saved atomically with review completion.
Budget pacing is
settled: no daily allocations or future-consult reserves; use the
shared monthly allowance until exhausted, with only explicit operator extensions (ADR-0087).
An exhausted, closed/reconciled sequence may restart next trading session with fresh entry evidence
and AI review; carried rounds and approval holds persist. Step 8 must make these states and restart
conditions explicit, including the rising round trigger. Ordinary sales, profits and all-cash periods
keep the same round/peak and count; only a round-limit hit consumes an attempt. Independent threshold
checks on every fresh verified portfolio update are agreed, including qualified one-minute data.
AI review timing cannot slow protection. Qualify valuation sources/collection for that coverage;
confirmed deposits/withdrawals now reuse percentage-preserving reference adjustment without resetting
attempts or holds. Cash-event qualification and AI integration remain pending. Reuse existing protection;
Step 8 owns the threshold display.
Shared risk authority is agreed: use an explicit portfolio risk configuration, AI continuous rounds
in place of its daily-pause calculation, and the existing single execution path. Extract only the
needed boundary in Steps 4/5 while preserving ordinary portfolios, saved holds and risk history.
Starting-capital capture and upfront initial thresholds are confirmed: later rounds use actual
reconciled opening capital, with any earlier previews labelled estimates (ADR-0087).
Per-stock protection reuses engine settings: AI may tighten stops or sell earlier but never loosen
or remove established stops. Record blocked alternatives for evaluated learning; enforce boundaries
in code. Initial switching behavior is agreed: retain compatible holdings/protection, reconcile
required exits before replacements and preserve round/risk history. Catalogue mapping and Step 5 integration
remain pending. Add further portfolio choices over time; their designs/order remain future work.
Stop/action enforcement remains Step 4 and outcome comparison Step 6 work. Keep the MVP focus and
one necessary decision at a time. The sequence includes
data/decision/execution integration, learning, host/UI, paper validation and native live qualification.
Integrate and qualify the client and independent protective host before enabling any paper/live actions.
This is not a verified launch commitment
or completed replacement of the execution work below. See the
[open scope](reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026).

1. **Complete the real-account foundation and execution path in dependency order.** Percentage-preserving
   cash adjustments and the broker projection/synchronization/protection/durable-session source path
   are implemented/tested. Next qualify native cash-event coverage/effective timing and boundary books,
   and native orders/strategy evidence. Storage migrations 035/039/040 are installed; operational setup
   and baseline/continuation SQL runtime recovery still need verification. Managed-session SQL recovery
   is rehearsed in isolation; shared regular account/history refresh is source/tested and observed natively.
   September 25 combines this work with the newer main-checkout memory and resolved-trade fixes.
   The operator confirms the integrated controls and successful account refresh in the normal checkout;
   no further restart is needed for this check. Next verify account risk setup and qualified risk inputs.
   Populated assignment recovery and broader native synchronization remain unverified.
   The operator ended the edge-case design pass and deferred same-stock
   manual additions. Complete the agreed ongoing-promotion path and known in-app order reconciliation, then extend both
   families and replacement transitions, then verify the host before actual execution approval. Follow the
   accepted stop/restart behavior: no further automatic purchases while ordinary exits continue; restore
   the same strategy and saved buying mode after fresh account/order checks. Core ongoing duration,
   daily/restart recovery and saved TFSA controls are source/tested. Execution-host startup, family
   coverage, native qualification and rollout remain.
   Retain the existing Market sell all row action and distinct full execution pause. See the
   [current checklist](strategy-alignment-implementation-checklist.md#left-to-do--development-order).
   A dedicated setup screen is not a prerequisite for developing the continuation logic.
   The September 24 [external-trade rollout](project-status.md#latest-operational-evidence) installed
   migration 040 and verified resolved-order synchronization, the reported exit and repeat-refresh
   duplicate prevention under ADR-0084. Cash/risk input qualification and working partial-order handling
   remain under checklist items 1–4.
2. **Resolve current collection/valuation blockers.** Diagnose the five-minute collection failures and
   the invested rules-only Live account's missing opening value. The opening/source-adoption check is
   complete for Live and comparison Shadow; October 8 saved records also verify original Shadow adoption.
   The historical tracked 15-minute conflict remains a separate follow-up. Preserve immutable evidence and coverage flags.
   Verify current input adoption, overnight completion and uninterrupted regular-session Live/Shadow
   operation, including full-watchlist/provider capacity and SQL durability. Dated engineering
   activations are not full-session qualification or reusable exceptions.
   The [latest nightly check](project-status.md#latest-operational-evidence) confirms on-time success
   for October 10–13 with no new Athena invalid outcomes and all active endpoints through the prior
   TSX session. October 13 source adoption and partial runtime progress are verified, but recurring
   monitor/cycle failures, incomplete held-account valuation and Blocked coverage remain. Capture the
   underlying cycle/monitor exception alongside the unresolved provider interval contract. October 13
   passive traces confirm off-grid timestamp rejection for sampled stock failures; they did not capture
   the separate whole-monitor exception or qualify a correction. Earlier
   missed-wake and historical invalid evidence remain separate from full-session qualification.
3. **Establish trustworthy comparisons.** Accumulate mature forecast/pick outcomes, verify contiguous
   leadership-mover history and compare common strategy/policy histories. Observe skipped entries,
   causal fills, Session-2 rotation and risk exits across clean Shadow sessions; exclude Sep 4 shakedown
   evidence from performance conclusions. The proposed TSX-gainers versus Delphi analysis remains
   analysis work, not authorization to retune gates. Use the
   [calibration checklist](calibration-implementation-checklist.md) and
   [Live checklist](delphi-live-implementation-checklist.md).
4. **Extend the controlled Practice environment.** The exploratory 20-session starter trial completed
   September 8; do not repeat its acquisition/execution merely because an older handoff says pending.
   Preserve missing inputs and current-universe/model limitations. Checkpoint resume, isolated SQL
   recovery/load and failure trials, Live replay and broader historical qualification remain separate
   follow-ups. [Current scope](reviews/open-questions.md#historical-practice-environment--follow-up-milestones)
   and [contracts](concepts/historical-practice-implementation.md).

### Reliability and correctness prerequisites

- Bound WPF account-history memory (S4). The September 24 process check confirms a multi-GB
  footprint. Direct typed reconstruction, shared reports and streaming validation are implemented/tested
  and installed; resumed compact writes are verified. Qualify sustained memory/capacity, then bound
  display/history payloads and avoid unchanged-revision reconstruction under explicit freshness rules,
  preserving immutable history and fingerprint validation. Repeated reads and lifetime engine
  collections remain.
  Evidence: [project status](project-status.md#latest-operational-evidence).
- Perform a documented restore into a separate test database and continue checking backup synchronization.
  OneDrive reports the September 23 pre-shrink backup IN_SYNC; no restore drill has run. Routine latest-two
  retention is installed; the additional manual recovery checkpoint and all existing backups were preserved.
- Reconcile remaining deployed/project schema drift using reviewed manual migrations, never broad DACPAC
  deployment. Continue universe/listing checks after ingestion changes, including acquired/delisted symbols.
- Resolve short-session behavior and extend reviewed calendar coverage before **2026-12-24**, including
  protection for carried holdings.
- Protect consequential boundaries before broader refactoring: immutable Delphi facts and typed
  load/evaluate/evidence/publish stages (preserve ADR-0035 presentation), explicit Hermes completion,
  separated Hercules training/evaluation/activation, and immutable strategy/policy ownership. Retain
  the [open audit register](strategy-alignment-implementation-checklist.md#remaining-audit-work).
- Address dependency-security advisories separately from compiler cleanup; broaden CI host coverage.
  Review the June/August stabilization boundary for a possible tag/release note. Phase in nullable/analyzer
  enforcement, smaller module/public surfaces and dormant-code cleanup after protected boundaries.

## Next — improve data and model evidence

- **Revisit intraday-gain retention soon** and carry it into future strategy/risk work. The operator
  preserved this important research goal October 16. October 17 establishes the individual-winner
  direction below; whole-portfolio protection remains background. Implementation is complete and
  tested and enabled for all 15 active paper portfolios October 18; original defaults stay false.
  [Research scope](reviews/open-questions.md#intraday-gain-retention-revisit-soon).
  **October 17 scope correction:** the intended unit is **individual stocks within a portfolio**.
  The [whole-account review](reviews/paper-gain-patterns-20261017.md) is background and does not answer
  that question. The operator accepted day-move and purchase-profit measures separately. The
  [stock-level review](reviews/stock-gain-paths-20261017.md) now covers ordinary holding paths, later
  purchases and later upside. The operator then confirmed **a trailing percentage gap that shrinks as
  purchase profit grows**, prioritizing protected profit over post-sale upside. [Accepted ADR-0090](adr/0090-progressive-individual-profit-trailing.md)
  records the three-band proposal and its authorized offline [saved-trade comparison](reviews/progressive-trailing-trade-study-20261017.md).
  Larger-winner entry tests now exercise +3% in ARE/AGI; +5% has conditional already-held ARE/CAS
  illustrations, with survival from entry unproven. Tightening does not always retain more profit.
  A five-schedule 10 a.m./noon comparison is now complete: mixed individual-stock profit retention
  supports keeping original bands as baseline; the operator now retains them for design and accepts
  combined-holding add-ons with updated cost, preserved stop and tightest earned gap. Morning/noon
  alternatives stay deferred research. The [bounded add-on test](reviews/progressive-trailing-trade-study-20261017.md#saved-add-on-trade-comparison)
  now preserves profit in ARE while keeping its stop/band across extra buys; it protects a smaller
  profit earlier than the actual sale. A [five/fifteen-minute update comparison](reviews/progressive-trailing-trade-study-20261017.md#five-minute-versus-fifteen-minute-stop-updates)
  now holds breach checks at five minutes: four closed histories favor faster updates, one (DSV)
  favors slower, 56 are equal. The operator subsequently chose completed five-minute raises and fresh
  observed-touch triggers for design. Scope now includes all portfolios, especially Delphi Live.
  Fresh bid, the highest completed five-minute close since purchase, and inclusion of existing holdings
  when enabled are accepted for design. Adaptive bid timing is now accepted too: five/one minutes at
  0.25% distance, new quote per check and a 60-second receipt-age limit. The current source/saved check
  does not qualify Live bid freshness; the operator defers further source investigation as a coding
  prerequisite. Daily Shadow/Delphi Live integration, prospective comparison assignments, recommendation
  paths and safe handover are implemented: 166 focused tests including Practice and the final full Core suite pass.
  October 18 rollout is installed/enabled across all 15 active paper portfolios, preserving all 37
  open holdings' protection, financial facts and comparison profiles; original entry controls are restored.
  Next observe normal regular-session polling/touch behavior when available. Quote investigation stays
  deferred and gap repairs secondary; weekend state checks and synthetic studies do not establish
  bid-trigger returns or continuous coverage. [Dated installation evidence](project-status.md#reference-map-and-maintenance).
- Backfill `RelativeStrengthFeatures`, including `CompositeScoreZ`, with coverage diagnostics; compare
  raw RS and Z-score rankings before any executed-lens change. Integrate historical RS into Hercules
  only after validated backfill.
- Audit historical enabled `ModelRegistry` rows and retire obsolete artifacts deliberately.
- Measure `ObvSignalWeight` from paper outcomes, accumulate CLX confirmation/divergence evidence, and
  run Granville cross-family overlap analysis before changing weights.
- Complete comparable capital/dates/fills/costs/coverage/downside/untouched evidence requirements before
  automatic Wealthsimple strategy execution; recommendation-only approval keeps ADR-0062's distinct rules.

## Later — expand system capability

- Extract a standalone pre-session engine using shared Core and one active owner; arrange Windows Task
  Scheduler hosting and verify operation. WPF/console dependence remains until that rollout. Shadow
  headless operation also retains its clean-session prerequisite.
- Add useful Scorecards charts after sufficient matured cohorts; preserve Athena and optional exports.
- Consider Hermes status/run and System Log surfaces over shared workflows, retaining explicit run authority.
- Complete Oracle Phase 3 debate/evaluation, Granville Overdueness (#23–#24), Heavy Volume/later groups,
  and broader walk-forward/version simulation.
- Any separate future Sentinel broker stop-loss/rotation workflow needs its own decision, authentication,
  limits, previews, partial-fill/reconciliation handling and kill switch. The TFSA execution path above
  does not implicitly authorize it.

## Explicitly deferred

- Dullness (#21–#22): longer history or better universe under ADR-0005.
- CLX as a gate/ranking input: evidence and a new ADR.
- Executed RS Z-score: validated backfill and rank comparison first.
- New ML features, thresholds or models: design-rule review and an ADR for behavioral changes.
- Nightly captured-source/build/cache retention: reviewed policy remains open in
  [open questions](reviews/open-questions.md#nightly-build-retention-adr-0078-2026-09-14); the
  [September 23 one-time compiled-output cleanup](project-status.md#latest-operational-evidence) is
  complete. The separately authorized one-time SQL shrink is also complete with preservation checks;
  recurring build retention remains undecided, and automatic SQL shrinking remains off.
  Installed database-backup retention does not cover build artifacts.

## Completed work and history

The September 22 documentation cleanup preserves the full prior
[roadmap](roadmap-history-20260922.md), [status](project-status-history-20260922.md) and
[alignment checklist](strategy-alignment-history-20260922.md). Those snapshots include completed
milestones and historical authority, not fresh instructions. Current installed/verified facts live
only in [project status](project-status.md); this page keeps priorities and links. No operational
verification date was advanced by the cleanup.
