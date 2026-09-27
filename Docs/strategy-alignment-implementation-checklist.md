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

This is the end-to-end sequence requested September 26. Step 1 groups the completed source increments
(`720c780a`, `07928730`); steps 2–10 remain unfinished. Existing broker prerequisites below still apply,
and this plan authorizes no deployment, paid qualification call or live order. AI Explorer stays deferred.
When the operator asks for "progress", show this same numbered list with current completion states.
Keep its numbering stable and update it at each meaningful milestone. Step 2 is now in progress.
MVP approach: reuse existing engines and contracts, resolve one necessary decision at a time, and
introduce complexity only when needed. Do not silently drop accepted discretion, learning or protection.

1. **Foundation — complete offline.** Budgeted API client, structured portfolio proposals, audit
   journals, learning consultation and execution-preparation checks. This is reusable Core source,
   not an operating AI portfolio. Dated validation is in project status.
2. **Set the operating contract — in progress.** Specify hard limits, the decreasing risk scale, profit protection,
   discretionary rule classification, portfolio eligibility/switching, review cadence and outage
   behavior. Define lesson evaluation/admission criteria. This step requires operator
   decisions on trading limits; implementation must not invent them. New Delphi Live/System Shadow
   daily templates use 7%; frozen and assigned versions are preserved. **Agreed September 27:** the AI
   gradual per-stock scale covers buys/additions without forced sales and now follows continuous
   losses/recovery across days, superseding its daily framing and session-minimum lock (ADR-0087).
   Both starting balance and recorded portfolio peak must be tracked; monitoring continues while in
   cash with intended automatic re-entry on recovery. AI enforcement integration remains Step 4.
   **Accepted:** 5% / 3% / 1% replaces the AI 7% boundary; stop new buys for the day after three failed
   rounds. Failed rounds close remaining AI-managed holdings and require confirmed/reconciled exits
   before another permitted attempt. Re-entry requires a fresh qualifying signal from the selected
   engine after closure, with no extra fixed delay; the AI can take it or wait. Proportional allowance
   within each round can recover before the hard limit, after which closure is required. Retaining
   the 10% cumulative buying pause with operator approval to resume is agreed. Unfinished rounds keep
   references, limits and protection across closures; new sessions prompt review of fresh engine entry
   guidance. Session-start, mid-session and pre-close consults complement ongoing intraday monitoring
   and AI receipt of its data. AI review intervals of one to fifteen minutes during market hours are
   agreed, subject to budget. Provider/budget fallback pauses new buys, additions and switching while
   monitoring/stops/engine exits continue; recovery requires a successful fresh AI review and all
   other limits to permit it. Initial earlier-review hooks are agreed for new qualifying entry signals,
   confirmed holding closures and risk-limit changes to permitted actions; combine nearby events
   within the one-minute minimum and shared budget. Later hooks use the same path.
   Monthly spending is agreed without daily allocations or future-consult reserves: use the shared
   allowance until exhausted, then pause or accept an explicit operator extension; no automatic top-up.
   **Next:** settle cadence ownership, daily counter renewal and reference
   combination/valuation, including
   interaction with existing engine holds. Reuse existing position trailing protection.
3. **Connect authoritative data.** Build adapters for Delphi candidates, strategy rules, historical
   summaries, current market observations and actual account/portfolio state. Produce immutable,
   dated decision packets with source/version identity, freshness and missing-data checks.
4. **Implement trading decisions and enforced protection.** Extend portfolio-only proposals to
   stock-level buy/add/hold/sell and permitted sizing/override decisions. Validate every action against
   the selected engine and approved boundaries in C#. Keep protective monitoring independent of
   model/learning availability and prevent losing-stop relaxation.
5. **Complete execution and portfolio handover.** Connect validated decisions to the existing engine
   and execution workflows. Support reviewed portfolio families, transfer overlaps/protection, reconcile
   required exits before buys and preserve original order identities through uncertain outcomes.
   Paper and live share decision logic while retaining separate accounts and histories; no activation yet.
6. **Implement outcome-based learning.** Link decisions and overrides to subsequent outcomes, evaluate
   hypotheses against the agreed criteria and publish versioned lessons with evidence and expiry.
   Verify supervisor/coordinator consultation and preserve hard limits and budget policy.
7. **Build and qualify the runtime host.** Compose shared clients/journals and the independent protection
   and discretionary-review loops. Configure intervals, restart recovery, protected storage, retention
   and the three agreed consult checkpoints alongside ordinary intraday monitoring. Implement
   adaptive AI review requests within the agreed one-to-fifteen-minute market-hours bounds and budget,
   once ownership is settled; preserve collection semantics and
   protective cadence and audit requested/applied intervals. Implement the three agreed event hooks
   through one extensible review request path, combine events arriving together, and preserve the
   one-minute minimum and shared budget. Qualify event-driven earlier reviews, duplicate/concurrent events,
   missed-review, stale-source and budget-exhaustion behavior. Enforce the agreed unavailable-review
   fallback and fresh-review recovery, including preservation of independent holds and protection.
   Configure the shared CAD budget with FX/tax headroom and per-call reservations, without daily
   pacing or future-consult reserves. Implement explicit operator allowance extensions while retaining
   month-to-date spend and outstanding reservations; current source rejects CAD 100 or more and
   retains the month's lowest ceiling. Qualify API authentication, model access, latency and cost
   through a bounded separately authorized call before model-backed operation.
8. **Add operator controls and visibility.** Display active strategy, holdings, proposals, decisions,
   exceptions, lessons, health and spending. Show exhausted budget and support an explicit operator
   allowance update without implying provider funding or clearing other holds. Provide clear controls for stopping new buys, continued
   protection, strategy switching and explicit live activation, using the existing execution contracts.
   **Required September 27:** show current measurement, limit and remaining room for the cumulative
   10% review guard, active round limit, buying allowance, attempts remaining, position stops/trailing
   floors, applicable cash/exposure constraints and AI spend cap. Show actual AI review cadence and
   next-review status and the reason for an unavailable-review pause. Use enforcement values, clear reference/freshness labels and explicit blocking
   reasons/actions; recovered values cannot hide
   a review hold that still needs operator approval. This UI is not implemented.
9. **Validate the complete paper workflow.** Rehearse selection, stock actions, switching, learning,
   budget exhaustion, stale data, outages and restart without real orders. Measure actual API costs
   and decision quality; synthetic tests alone do not establish either or a performance edge.
10. **Qualify and activate live execution.** Finish the existing native account/risk/broker prerequisites
    below, including cash events, opening/drawdown references, protection ownership, order lifecycle
    and recovery. Review the concrete activation evidence and exact account/strategy/limits, then
    enable the authorized supervised live run and verify outcomes. Completion of earlier steps alone
    does not establish live readiness or a September 28 launch.

### Left to do — development order

- [ ] **AI Momentum integration (September 26):** complete
  [ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md) by defining exact adaptive protection,
  profit giveback, portfolio eligibility/handover and evaluated learning. Build authoritative packet
  adapters, durable domain dispositions and the paper/live host contract. Qualify Astra access/billing,
  review expiring FX/tax bounds and install one protected shared spending journal for all model roles.
  The library defaults to CAD 90 and rejects ceilings at or above CAD 100; it is not operational.
  Continuous protection, outage handling and decision cadence remain host work. AI Explorer is deferred.
  The requested September 28 target still depends on the native risk/order/host requirements below.
  [Open decisions](reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026).

- [ ] **Learning operational integration (ADR-0087):** implement/qualify the outcome evaluator and
  its lesson admission/expiry policy, configure consultation intervals and connect both periodic
  hooks to the runtime host. Prepare a protected shared journal and recovery/retention procedures.
  Connect preparation to the approved execution path with fresh source/account/risk checks. Verify
  protective exits remain independent through outages; no installed coordinator or broker path yet.

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

- [x] Verify the September 24 scheduled nightly chain and saved daily publication without rerunning it
  ([dated evidence and Attention limit](project-status.md#latest-operational-evidence)).
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
