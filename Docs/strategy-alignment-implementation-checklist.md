# Strategy alignment implementation checklist

**Current scope:** independent paper strategies followed by deliberately approved real-account
execution. [Roadmap](roadmap.md) owns priorities; [project status](project-status.md) owns dated
implementation, installation and verification evidence. This checklist owns remaining dependencies.
The September 22 documentation cleanup preserves the
[original checklist and validation history](strategy-alignment-history-20260922.md).

## Paper strategy to real TFSA — current checklist

**Goal:** choose a paper portfolio and let its exact reviewed strategy manage the permanent real TFSA,
while keeping paper and real histories independent. **Automatic strategy trading is still off.**
Any portfolio must be promotable and keep managing across days until pause or switch (ADR-0085).
Ungated Breakout Top 3 is the initial September 23 review candidate. The
[current launch verdict is NO-GO](reviews/supervised-session-20260923.md).

A checked item means only the named action is complete. Agreed, implemented, tested, installed and
operationally verified are different states; the [status tables](project-status.md#implemented-versus-operational)
record them. A prepared migration or passing test never proves broker readiness.

### Done

- [x] Implement/test the AI Momentum API/proposal foundation (September 26, ADR-0087): explicit
  disabled factory, bounded Astra Responses client, shared durable spending reservations and strict
  portfolio proposals with explained discretionary exceptions. Forty synthetic cases are included
  in the Core suite; [status](project-status.md) records validation. This does not run a portfolio.

- [x] Implement/test the shared learning journal, supervisor consultation and execution-preparation recheck
  (September 26, ADR-0087). Version-2 decisions assess every consulted lesson; consumer intervals and
  review state persist across restart; 23 synthetic cases pass in the full Core suite. This source
  has no order transport or installed scheduler.

- [x] Build independent paper portfolios and manual complete-strategy selection for recommendations
  (ADR-0062); this does not submit orders.
- [x] Connect TFSA reads, link the permanent account and verify native saved-history refresh (ADR-0080).
- [x] Implement/test resolved external-order projection, atomic real-ledger receipts and signed-in
  desktop synchronization (ADR-0084).
- [x] Install migration 040 after verified backup and the corrected desktop; verify the reported
  broker-confirmed exit, historical completion, repeat-refresh duplicate prevention and current holdings.
- [x] Build manual market buy/sell clients and controls; native mutation/lifecycle verification remains.
- [x] Agree regular-session whole-share market handover: preserve overlaps and reconcile required exits
  before new buys (ADR-0079); preserve known references/holds, real costs and protection ownership (ADR-0081).
- [x] Implement/test initial risk-reference preparation, reviewed saving and uncertain-save recovery.
- [x] Implement/test dated value reading, actual boundary-book valuation and durable daily references/holds
  for positively verified intervals without deposits/withdrawals (ADR-0083). This is source completion,
  not completion of operational item 1 below.
- [x] Agree and implement percentage-preserving cash-flow adjustments using matched account books,
  preserving holds and preventing duplicate application/restart resets (ADR-0083).
  [Validation and source limits](project-status.md#verified-development-baseline).
- [x] Implement/test exact comparison-profile review, actual broker position/cash projection and
  shared synchronization/continuation orchestration; native risk source remains unqualified.
- [x] Implement/test protection transfer/replay, immutable supervised approval, exit-before-buy,
  original-order recovery and concurrency/duplicate prevention; prepare migration 040.
- [x] Add an account setup review screen and production composition reusing existing broker clients,
  journal and gates. Source integrated into the normal checkout; native setup review and activation remain.
- [x] Install migrations 035/039/040 under operator authority after a verified backup/copy; verify empty
  storage, trusted constraints, immutable triggers and preserved account records (Sep 22, 23:34 Toronto).
- [x] Agree portfolio-wide promotion and ongoing assignment until pause/switch (ADR-0085); design only.
- [x] Agree manual buys remain outside the active strategy by default, with optional purchase-dialog
  handover and a later holding action, including explicitly adopted extra positions managed under
  existing sell rules without consuming automatic strategy slots (Sep 23, ADR-0085). Controls and role
  separation are not implemented; capital and transfer checks remain. Same-stock manual additions and
  split ownership are deferred at the operator's request; continue the agreed common path.
- [x] Implement/test shared account/history synchronization, five-minute background scheduling,
  manual/post-trade refresh coalescing, cancellation and failure backoff (Sep 23). Existing link only;
  native refresh/persistence is observed September 25; automatic order recovery remains separate.
- [x] Agree stopping new automatic purchases while ordinary exits continue, retaining Market sell all
  on real holding rows (Sep 23, ADR-0085). Full execution pause remains distinct.
- [x] Agree restoring the same approved strategy and saved buying mode after fresh account/order checks
  on app restart; stopped buying stays stopped (Sep 23, ADR-0085).
- [x] Implement/test Core purchase-mode persistence and same-session recovery: 16 added cases cover
  stopped buys with active exits, failed checks, partial fills, lost saves, concurrent stop and original-ID
  recovery. Existing JSON storage needs no migration; the following increments extend this milestone.
- [x] Implement/test explicitly scoped ongoing Daily Shadow assignment, checked daily continuation,
  preserved cumulative ceiling, prior-day protection/closing-exit recovery, build-identity checks and
  rejection of unsupported families before execution approval (Sep 23). Existing one-day approvals expire.
- [x] Connect saved strategy/mode display and stop/resume to the TFSA panel; restore after linked-account
  refresh at startup and during polling. Verify stale/login/assignment changes and uncertain saves offline.
  This control path has no approval/order transport and starts no execution loop. The native panel's
  empty-assignment state is observed September 25; populated assignment recovery remains unverified.
- [x] Rehearse the actual managed-session SQL repository in a separate synthetic database: precise
  snapshot readback, exclusive ownership, stopped-buy restoration, interrupted append rollback and
  original-ID recovery after uncertain saves/submissions, and partial-exit reconciliation before buys.
  Parent tables are ID-only fixtures; this
  does not qualify baseline/continuation storage or actual broker fills.

### AI Momentum delivery sequence

Keep this same ten-step list and its plain-language subpoints current at each milestone.
**Built/tested** means source code is ready, not that the AI is operating. **Verified** names the
specific check completed; it does not imply full trading readiness. Dated evidence belongs in
[project status](project-status.md); priorities belong in the [roadmap](roadmap.md).
Detailed agreed behavior remains binding in [ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md),
[design rules](design-rules.md) and their linked execution decisions. AI Explorer remains deferred.
This checklist grants no deployment, paid API call or live-order authority.

#### Delivery milestones

These milestones group the same ten steps below. Steps 7 and 8 have an initial version for Milestone 1
and fuller versions for Milestone 2; showing a reviewer does not complete those entire steps.
All three milestones remain pending. Step 7's review scheduler, saved-state recovery, model-requested
timing and Core coordinator are built/tested; SQL review storage is installed with native roundtrip checks.
Opt-in desktop startup is built/tested. Private setup and one connected review with SQL readback and
recorded usage are verified October 1. Dependency repair and the authorized pipeline recovery restored
the October 1 publication to all 15 portfolios; two ungated Continuation choices had usable AI context at 10:57.
Other choice-specific evidence/admission blocks and Athena's Attention result remain explicit. Installed
automatic runtime verification remains pending. The follow-up review test assembled four eligible choices
but exceeded the existing input-token limit before generation. The concise view now fits that saved
packet under the same limit; fresh connected decision qualification is next. October 2 market-hours
reads find stale held-stock marks and failed collection cycles despite current daily sources in the
comparison Shadow/Live sessions. Diagnose that input blocker first. Empty lesson continuation is
built/tested but needs operational authorization alongside the bounded review and ten-minute test age.
Step 3's connected check verifies cash-only persistence/recovery;
receipt timing is fixed and verified. One invested saved-Shadow value passed under the October 1
test bound; normal held-stock coverage and faster-price qualification remain open. The possible
Wealthsimple AI fallback is deferred and lower priority; return to
the main reviewer delivery work while preserving existing stale-input blocks.

| Milestone | What you can use | Checklist work needed |
|---|---|---|
| **1 — Visible AI reviewer** | The app reads verified paper-account inputs, runs scheduled AI portfolio reviews and shows proposals, reasons, freshness and spending. It does not execute trades. | Reuse Steps **1–2** and the built risk core in **4**; finish the paper-input path in **3**, the basic review runtime and bounded API verification in **7**, and the review/status display in **8**. |
| **2 — Complete, tested AI paper trader** | The AI manages a paper account with enforced protection, simulated execution, portfolio switching, outcome learning and recovery. | Complete the paper scope across Steps **3–9**, including the remaining action/protection work in **4**, execution in **5**, learning in **6**, full runtime/controls in **7–8**, and connected validation in **9**. |
| **3 — Supervised live AI trading** | The approved AI strategy manages the real account through verified broker execution and protection. | Complete Milestone **2**, the real-account/cash-event portions of **3–5** and applicable live runtime/controls in **7–8**, then the qualification and explicit activation in **10**. |

Milestone 1 is the next visible delivery target. Lesson criteria remain deferred until Step 6 needs
them. Real-account prerequisites remain required for Milestone 3; a paper milestone does not check
off mixed paper/live items in full. Keep dependencies and individual completion marks below; independent
runtime/display development can proceed while native input qualification remains open.

#### Ten-step implementation checklist

1. **Foundation — built and tested offline.**

   - [x] Build the AI client and structured portfolio proposals.
   - [x] Track spending, reserve call costs and enforce the budget.
   - [x] Save decisions and supporting evidence in scoped journals.
   - [x] Let the supervisor and execution preparation consult saved evaluated lessons.

2. **Operating framework — agreed; sufficient to build.**

   - [x] Include all current strategy portfolios and variants; add future integrated portfolios through the catalogue.
   - [x] Agree continuous 5% / 3% / 1% rounds, trailing limits, buying allowance, the 10% approval hold and stock protection.
   - [x] Agree switching, review timing, event-triggered reviews, monthly budget behavior and outage recovery.
   - [x] Agree to preserve holdings, tighter stops, risk history and attempts across days, cash periods and portfolio switches.
   - [ ] Define useful-lesson criteria **only when Step 6 needs them**; deliberately deferred, not a coding blocker.

3. **Connect reliable data — in progress; continuous invested-price coverage pending.**

   - [x] Build the maintained portfolio catalogue and map exact strategy, model and gate versions.
   - [x] Connect saved Delphi signals, engine rules and account-specific activity to dated AI input packets.
   - [x] Connect complete paper-account readers and broker evidence adapters to shared risk checks; keep candidate balances separate.
   - [x] Verify saved cash, holdings and calculated values across all 15 installed paper portfolios.
   - [x] Repair symbol resolution/source adoption and verify current cash-only Shadow observations without changing history.
   - [x] Check saved and faster prices: delays and uneven minute coverage are measured; sampled gaps are consistent with quiet trading. [Dated evidence](project-status.md#latest-operational-evidence).
   - [x] Build/test the one-minute price adapter with separate price/check times and rejection of stale, missing or failed reads.
   - [x] Build/test combined paper values from faster prices, verified cash and every holding; retain evidence and reject changed accounts or mixed price times.
   - [x] Save combined values through the risk journal; test history preservation, explicit source handover, repeat prices, lasting conflict blocks and restart recovery.
   - [x] Verify after-hours rejection for all 15 real paper accounts and reopen isolated diagnostic journals; no prices or risk transitions accepted.
   - [x] Run the connected market-hours check; verify cash-only values save/reopen correctly and blocked reads grant no buying allowance. [Dated results and limits](project-status.md#latest-operational-evidence).
   - [x] Diagnose missing/old or differently timed held-stock bars and post-price trades; preserve their valuation blocks.
   - [x] Fix the SQL/app receipt-clock discrepancy and verify locked account-read timestamps without loosening freshness.
   - [x] Check TMX quotes as an alternative: sampled last sales can be old, and bid/ask has no verified quote timestamp. [Dated evidence](project-status.md#latest-operational-evidence).
   - [x] Inspect existing connections: Wealthsimple's manual quote reader includes bid/ask and a source-time field; portfolio-price suitability is unverified.
   - [x] Obtain approval, prepare/test the bounded broker quote diagnostic and verify its saved login; pre-open checks request no quotes.
   - [x] Run both market-hours broker samples; record complete held-stock quote coverage, declared ages and changed times/prices. [Results and limits](project-status.md#latest-operational-evidence).
   - [x] Check public timestamp documentation: real-time snapshots are described, but the exact API field meaning remains undocumented in the reviewed sources.
   - [x] Clarify scope: TMX stays normal; accepted timestamp uncertainty applies only to possible limited AI fallback exploration.
   - [ ] **Deferred / lower priority:** consider Wealthsimple only for AI review of a held stock when its TMX input is delayed/unavailable; agree fallback behavior when resumed.
   - [x] Accept and persist the selected invested Shadow book under the ten-minute first-review bound (October 1); continuous coverage is not qualified.
   - [ ] Qualify reliable held-stock price coverage; verify fresh invested values save and recover correctly.
   - [ ] Qualify freshness, continuous coverage and recovery before scheduling recurring value checks.
   - [ ] Resolve collection failures and distinguish them from quiet trading; never make old prices look fresh or silently change ordinary engine bars.
   - [ ] Verify native account-read consistency during trading; qualify real-account cash events separately with matching before/after accounts.
   - [ ] Connect recurring qualified observations through Step 7; no operating AI data loop exists yet.

4. **Trading decisions and protection — partly built.**

   - [x] Build and test continuous rounds, trailing thresholds, buying allowance, required closure and qualified re-entry.
   - [x] Save risk history and test restart recovery without resetting peaks, attempts or existing holds.
   - [ ] Add stock-level buy, add, hold and sell proposals, with permitted sizing and recorded exceptions.
   - [ ] Enforce each action against the chosen engine, account limits and existing stops; never loosen established protection.
   - [ ] Integrate the shared AI risk rules without changing ordinary portfolios or creating competing execution paths.
   - [ ] Apply qualified deposit/withdrawal adjustments while preserving loss percentages, history and protection.
   - [ ] Verify protection continues independently of AI availability and review timing, using genuinely fresh complete values.

5. **Execution and portfolio switching — pending.**

   - [ ] Send validated actions through the existing execution system, rechecking permission immediately before execution.
   - [ ] Keep compatible holdings, actual costs and existing tighter stops when switching portfolios.
   - [ ] Confirm and reconcile required sales before replacement buys; log the switch reason and exact versions.
   - [ ] Preserve risk state and protection ownership through handover, including all-cash transitions.
   - [ ] Recover uncertain orders using their original identities; keep paper and real histories separate.

6. **Outcome-based learning — pending.**

   - [ ] Review completed decisions once after each trading session; keep immature or inconclusive cases pending.
   - [ ] Compare actual outcomes with clearly labelled simulated alternatives, including blocked protection changes.
   - [ ] Agree useful-lesson criteria when needed, then save qualified lessons with evidence, versions and expiry.
   - [ ] Verify later decisions consult those lessons within the shared budget; learning cannot change hard limits.

7. **Runtime host — partly prepared.**

   - [x] Connect/test the Core review coordinator: gather existing inputs, call the existing reviewer and save input/answer evidence through a SQL repository; no execution or approval workflow.
   - [x] Prepare SQL review storage and migration 042; build the database schema project.
   - [x] Install authorized migration 042 after a verified backup; verify native SQL saves, duplicate handling and recovered-answer saves with no test rows left behind.
   - [x] Select and uniquely match Breakout 3 with skipped daily gates as the first cash/holdings source; other portfolio choices remain available to the AI.
   - [x] Prepare masked local API-key entry, presence-only checks and a query for the selected portfolio's current cash/holdings and saved reviews.
   - [x] Locate the existing Oracle user-secrets credential and confirm its suffix matches the operator screenshot (September 30, read-only; no API call).
   - [x] Load Oracle's existing credential in Core and verify requested-model metadata access (September 30); no paid generation or portfolio submission.
   - [x] Agree first-review cost accounting: CAD 90 monthly cap, 1.60 CAD/USD and 20% tax/fee headroom through October 31.
   - [x] Connect opt-in app startup/shutdown to a background review host with shared/scoped history ownership; test cancellation and failure handling.
   - [x] Build/test explicit first-time preparation and a one-review command that leaves automatic reviews off and preserves existing/interrupted history.
   - [x] Inspect the selected book during market hours (October 1); verify recent saved marks are present before initialization.
   - [x] Accept the ten-minute first-test input age and initialize protected private history with automatic reviews disabled.
   - [x] Complete one connected review, verify exact SQL/local input and answer, and record token usage and conservative cost (October 1).
   - [x] Diagnose missing signals across all 15 choices: nightly dependency restore stopped before today's daily publication was created.
   - [x] Repair the Hermes/Delphi/Athena dependency locks and pass isolated nightly restore/build validation without running operational stages.
   - [x] Run the authorized pipeline recovery and verify today's valid publication is adopted by all 15 portfolios (October 1); Athena separately reports two invalid outcomes.
   - [x] Verify restored AI context with read-only engine readers: ungated Continuation 3/5 permit proposals; remaining choices preserve their admission/evidence blocks.
   - [x] Test a review with restored signals: 15 choices/four eligible were assembled, but the input-token limit rejected generation; verify saved rejection and unchanged spending (October 1).
   - [x] Build/test the concise review view; preserve eligibility, rules, current holdings/risk, missingness and full saved source packets. Verify the failed packet fits the unchanged token cap using count-only access.
   - [x] Build/test explicit continuation of expired, always-empty learning for a later open session; preserve all history and refuse pending reviews. No automatic renewal or production write (October 2).
   - [x] Inspect the operator's collector-warning screenshot and prepare/build a bounded two-target provider diagnostic; exception type is known, cause remains unverified (October 2).
   - [ ] **Next:** authorize/run the prepared read-only TMX diagnostic to narrow the October 2 collector failure, restore and verify fresh market-hours inputs, then authorize empty learning continuation and one bounded review at the ten-minute test age. Verify the concise-view answer, SQL/local evidence and cost, including its use of tables/shared definitions.
   - [ ] Trace remaining Breakout/Live evidence blocks and Athena's invalid outcome reasons without treating publication receipt as entry permission.
   - [ ] Verify the installed desktop lifecycle and automatic scheduling; source/build checks do not establish activation.
   - [ ] Revisit dated/incomplete-input review handling only if it blocks integration; retain current checks for now and keep input preparation separate from coordination.
   - [x] Build and test local risk-journal persistence and recovery.
   - [x] Build/test the single-session review scheduler: configurable daily checkpoints, bounded 1–15-minute intervals, combined events and one review at a time.
   - [x] Connect explicit test ticks to the existing learning-aware reviewer; retain trigger evidence and test budget failures, stale results and fresh-review recovery.
   - [x] Build/test account-scoped scheduling history; preserve pending events, timing and blocks through restart and reviewed session changes.
   - [x] Record interrupted reviews without retrying their old call IDs or releasing spending reservations; require fresh review after recovery.
   - [x] Connect validated AI requests for the next review (1–15 whole minutes plus a reason); save accepted timing with the completed review.
   - [x] Agree a five-minute initial interval and checkpoints at open, open+3h and close-15m; AI-requested intervals remain 1–15 minutes.
   - [ ] Run qualified account observations, independent protection and AI reviews automatically in shared Core code.
   - [ ] Verify scheduled/event reviews in the connected host; keep protection independent of paid reviews and their failures.
   - [ ] Enforce outage/budget review blocks in the execution path; resume new risk only after fresh review and all other checks.
   - [ ] Protect storage and qualify backups, retention, performance, concurrent access and reviewed source-conflict recovery; schedule restart-safe learning reviews.
   - [ ] Support explicit budget increases without resetting spending; remove the current cap restriction only through that reviewed flow.
   - [ ] Verify API access, latency and actual costs through a bounded separately authorized call, including currency/tax headroom.

8. **UI and controls — pending.**

   - [ ] Show the selected portfolio, holdings, proposals, decisions, exceptions and lessons.
   - [ ] Show actual round opening/current/peak values, trailing limits, buying allowance, stock stops and attempts remaining.
   - [ ] Show every active hold, remaining room, data freshness, spending, review timing and operating status.
   - [ ] Provide stop-new-buys, portfolio-switching, explicit budget-extension and eventual live-activation controls.
   - [ ] Distinguish daily pauses, carried rounds, pending exits and approval holds; a countdown never promises automatic permission.

9. **Complete paper validation — pending.**

   - [ ] Test selection, stock actions, switching and learning together without real orders.
   - [ ] Test stale/missing data, outages, exhausted budgets, concurrent events and restarts.
   - [ ] Measure actual API costs and decision quality; synthetic tests alone do not establish either.
   - [ ] Resolve failures and document the remaining limits before live qualification.

10. **Live qualification and activation — pending.**

    - [ ] Verify real account values, cash events, opening/drawdown references and broker order handling.
    - [ ] Confirm protection ownership and recovery with real holdings under the existing broker prerequisites below.
    - [ ] Review concrete activation evidence and the exact account, strategy and limits.
    - [ ] Enable only the explicitly authorized supervised run and verify its outcomes.

### Left to do — development order

AI Momentum and learning are tracked once in the [milestones and ten-step list above](#ai-momentum-delivery-sequence).
The prerequisites below cover the broader real-account execution work and remain required for Milestone 3.

- [ ] **1. Complete risk-tracking source qualification.** Daily and percentage-preserving cash-flow
  continuation are implemented/tested. Qualify complete broker event coverage, effective cash timing
  and matching account books, then native morning timing and each family's existing valuation definition.
  The activity capture does not establish effective cash timing; cumulative totals and chart points
  alone are insufficient. This item remains open for source qualification and integration.
  Existing holds persist. No new late-start-only buy pause is agreed.
- [ ] **2. Finish initial operational setup.** The source screen presents references and preserved
  holds. Migrations 035/039/040 are installed. Managed-session SQL readback/leases/interrupted-save
  recovery is rehearsed; next exercise baseline/continuation repositories and verify saving/recovery of
  initial and daily references. The source screen
  does not establish an operational baseline.
  Verified initial setup remains mandatory before operational use.
- [ ] **3. Verify the broker-input cutover operationally.** The isolated source now projects reconciled holdings,
  actual costs, cash/buying power and orders. Preserve position identity through partial sales; distinguish
  closed/reopened holdings. Preserve linked history, installed migration 034 and existing protections.
- [ ] **4. Verify synchronization natively.** Offline tests cover manual/external changes, partial/new fills, stale or
  conflicting observations and uncertain orders. Reserve cash/shares once; unconfirmed sales supply no funds.
  Shared recurring account/history refresh (five minutes), manual/post-trade refresh and visible
  stale/error states are source/tested. September 25 integrates the newer resolved-trade/memory fixes
  with these controls. The operator confirms the native panel and a successful combined refresh;
  SQL shows repeated imports with trade-sync receipts about 5m30s apart. Rate limits, failures,
  longer coverage and original-order recovery remain. Preserve fresh preflight.
  September 24 resolved whole-share projection and signed-in desktop polling were verified: the reported
  external exit is recorded once, its tracked holding is closed, historical missing trades are completed
  and repeat refresh preserves trade counts. Working partial orders, corrections and unsupported financial
  representations remain blocked; incremental-fill/correction handling and broader cash/order reservation
  integration remain. Preserve pinned legacy-ledger checks.
  [Evidence](project-status.md#verified-development-baseline).
- [ ] **5. Complete protection handover.** Retain the existing owner through transition; at activation
  transfer overlaps without losing established levels. Initialize missing protection only from real cost
  and fresh evidence; no invented historical highs or reset of known holds.
- [ ] **6. Verify the supervised execution host.** Source persists the exact approved strategy and transition state;
  use existing market-order functions with duplicate prevention, uncertain-response and restart recovery.
  Confirm/reconcile required non-overlap exits before new buys. Preserve independent paper history.
- [ ] **6a. Complete portfolio-wide ongoing management.** Implement the agreed common path:
  separate automatic positions from extras managed for exits, transfer boundaries and known in-app orders,
  qualified automatic execution-host startup and replacement. Saved TFSA controls and Core ongoing
  recovery are source/tested; the panel's empty state is observed, but populated real-assignment
  recovery remains unverified. Reuse the existing manual
  row sale and verify coordination with managed orders. The current
  session invalidates even an in-app manual fill; automatic continuation remains a proposal.
  Extend the current Daily Shadow execution/protection adapter to both portfolio families; verify
  cross-family handover and native multi-day recovery. No silent upgrades of one-day approvals.
- [ ] **7. Qualify and verify automation.** Establish strategy/evidence readiness, perform separately
  authorized native order/lifecycle tests, then explicitly authorize real execution. Recommendation-only
  approval under ADR-0062 retains its separate evidence policy; it does not qualify automation.

**Immediate next increment:** qualify the native cash-event/boundary-book evidence producer and
opening values, alongside baseline/continuation SQL runtime recovery. Managed-session SQL recovery is
rehearsed; background synchronization and the TFSA panel's empty state are observed natively.
Ongoing recovery remains source/tested; next extend family adapters and qualified
execution-host startup without reopening deferred rare cases.
Storage is installed; baseline/continuation runtime recovery remains distinct from the completed
schema/immutability checks and managed-session rehearsal.
The orchestration and production composition exist; they cannot manufacture those inputs.
Then complete the fresh financial activation packet and supervised host rollout. See
[ADR-0086](adr/0086-supervised-broker-strategy-session.md) and the
[concrete activation review](reviews/supervised-session-20260923.md).
The [current design draft](concepts/strategy-promotion-and-execution.md#account-baselines-and-protection-ownership--september-16-continuation)
and [ADR-0083](adr/0083-opening-account-values-and-shared-engine-hosting.md) hold detailed reasoning.
Optional older-history import remains a proposal; no historical reset is approved.

## Operational follow-through

- [x] Verify the October 2 scheduled nightly chain and saved daily publication without rerunning it
  ([dated evidence and Attention limit](project-status.md#latest-operational-evidence)).
- [ ] Investigate the two active symbols without the expected daily endpoint; Hermes reported no data,
  not a collection exception. Neither is in today's candidates; the verified publication does not close this gap.
- [ ] Review Athena's older missing-session outcomes separately from successful daily collection;
  preserve invalid evidence and do not infer intraday or strategy-performance qualification from nightly completion.
- [x] Measure database allocation, staging backups and repository storage without changing production
  data (September 23; [evidence](project-status.md#latest-operational-evidence)).
- [x] Complete the user-authorized one-time cleanup of obsolete artifact binaries; verify retained
  evidence and model/source hashes (September 23; [evidence](project-status.md#latest-operational-evidence)).
- [ ] Review recurring build/artifact retention; the one-time cleanup did not install a policy.
- [x] Complete the separately user-authorized one-time SQL shrink after a fresh verified backup and
  integrity check; retain growth headroom and verify every table count afterward (September 23, 23:55;
  [evidence](project-status.md#latest-operational-evidence)). No automatic shrink enabled.
- [ ] Perform a documented restore into a separate test database. The pre-shrink backup is checksum/hash
  verified and OneDrive reports IN_SYNC; neither an independent download nor restore drill has run.
- [ ] Resolve the current held Live opening-value gap and tracked 15-minute conflict; verify subsequent
  complete monitoring sessions without rewriting first-received evidence or clearing historical gaps.
  [Last health evidence](project-status.md#latest-operational-evidence).
- [ ] Finish native opening-value qualification and the actual setup/continuation caller before treating
  offline valuation tests as operational account references.
- [ ] Extract shared Core into a standalone host and arrange pre-session Windows Task Scheduler startup
  with one active owner; no such host/task was installed by the valuation increment.

## Remaining audit work

These identifiers retain the [September 6 audit](reviews/strategy-direction-alignment-20260906.md)
and original dependency meanings. They do not reorder the execution steps above. Reverify the affected
source before implementation; historical phase labels are not new priorities or authorization.

| Identifier | Remaining work / boundary |
|---|---|
| F1 | Supported candidate-only training/selection is implemented; review legacy activation/mutation paths and immutable whole-strategy ownership. |
| F5 | Reverify or retire the unsupported unlinked Ghost CLI route while preserving operator Real monitoring. |
| F6 | Complete typed Hermes per-source completion semantics; required SPY failure handling alone is insufficient. |
| F8 | Bind pending Athena outcomes to immutable historical label/cohort definitions; preserve prior meanings. |
| D1 | Broker projection and durable supervised workflow source/tested; qualified native inputs and operational rollout remain. |
| D2 | Reverify saved Shadow policy dispatch, generation/pending-order loading and persisted friction; preserve V1 and prior fills. |
| D3 | Read-only native reads pass; synchronization/order recovery tested offline. Native order and SQL runtime qualification remain. |
| S1 | References, mapping and protection transfer source tested; qualify native risk producer and actual ownership before cutover. |
| S2 | Reconcile published versus eligible consumers without changing entry gates; a published Hold is not a Buy. |
| S3 | Make Oracle cache identity/replacement correct; Oracle gains no trading authority. |
| S4 | Bound growing account-history CPU/memory and establish collection/display capacity. Sep 24 direct typed reconstruction, report sharing and streaming integrity checks implemented/tested and installed at 09:30:32; loaded binaries and resumed compact writes verified. Sustained memory/capacity qualification remains. Next bound display/history loads and avoid unchanged-revision reconstruction with explicit freshness/integrity rules. Repeated reads and full lifetime collections remain; evidence in project status and private `artifacts/wpf-memory-20260924/`. |
| S5 | Improve Core boundaries incrementally after consequential contracts are protected. |
| S6 | Broaden CI host coverage beyond existing five-host checks; retain failure propagation. |
| S7 | Resolve short sessions/calendar coverage before 2026-12-24, including carried-position protection. |
| S8 | Complete source/concurrency/full-watchlist capacity qualification; daily schedule/input repair is partial evidence. |

F2 (dated inputs), F3 (loaded-byte provenance), F4 (post-fill valuation), F7 (CI failure propagation)
and S9 (observed SPY requirement) have their source/adoption evidence in the
[archived findings register](strategy-alignment-history-20260922.md#findings-and-dependencies).
That completion does not prove historical artifact semantics, independent calendar continuity,
complete session coverage or strategy superiority. F1's supported workflow and D1's recommendation
milestone are also recorded there; their remaining scope stays visible above.

## Authority and evidence

Read [design rules](design-rules.md), relevant accepted ADRs and [AGENTS.md](../AGENTS.md) before
behavioral work. The [original authority/evidence record](strategy-alignment-history-20260922.md#original-audit-phases-and-evidence)
preserves past approvals and limits; it grants no fresh database, training, market-service or order
authority. Existing positions, rule identities, historical labels and independent paper evidence remain
intact. Keep detailed new validation in linked reports and current state in project status, rather than
appending another chronology here. The earlier documentation cleanup performed no operational checks;
the later execution increment has separately dated test/native-read evidence in project status.
