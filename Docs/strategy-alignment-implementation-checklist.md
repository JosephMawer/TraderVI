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

- [x] Build independent paper portfolios and manual complete-strategy selection for recommendations
  (ADR-0062); this does not submit orders.
- [x] Connect TFSA reads, link the permanent account and verify native saved-history refresh (ADR-0080).
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
  journal and gates. Build source only; no host/UI installation or activation.
- [x] Install migrations 035/039/040 under operator authority after a verified backup/copy; verify empty
  storage, trusted constraints, immutable triggers and preserved account records (Sep 22, 23:34 Toronto).
- [x] Agree portfolio-wide promotion and ongoing assignment until pause/switch (ADR-0085); design only.
- [x] Agree manual buys remain outside the active strategy by default, with optional purchase-dialog
  handover and a later holding action, including explicitly adopted extra positions managed under
  existing sell rules without consuming automatic strategy slots (Sep 23, ADR-0085). Controls and role
  separation are not implemented; shared-security, capital and transfer details remain open.

### Left to do — development order

- [ ] **1. Complete risk-tracking source qualification.** Daily and percentage-preserving cash-flow
  continuation are implemented/tested. Qualify complete broker event coverage, effective cash timing
  and matching account books, then native morning timing and each family's existing valuation definition.
  The activity capture does not establish effective cash timing; cumulative totals and chart points
  alone are insufficient. This item remains open for source qualification and integration.
  Existing holds persist. No new late-start-only buy pause is agreed.
- [ ] **2. Finish initial operational setup.** The source screen presents references and preserved
  holds. Migrations 035/039/040 are installed. Rehearse SQL runtime readback/leases/interrupted-save
  recovery separately, then verify saving/recovery of initial and daily references. The source screen
  does not establish an operational baseline.
  Verified initial setup remains mandatory before operational use.
- [ ] **3. Verify the broker-input cutover operationally.** The isolated source now projects reconciled holdings,
  actual costs, cash/buying power and orders. Preserve position identity through partial sales; distinguish
  closed/reopened holdings. Preserve linked history, installed migration 034 and existing protections.
- [ ] **4. Verify synchronization natively.** Offline tests cover manual/external changes, partial/new fills, stale or
  conflicting observations and uncertain orders. Reserve cash/shares once; unconfirmed sales supply no funds.
  Design and implement the requested shared recurring account/history refresh (proposed five minutes),
  immediate post-trade recovery and manual refresh; preserve fresh preflight and visible stale/error states.
- [ ] **5. Verify actual protection handover.** Source transfer/replay is tested. Retain the existing owner through transition; at activation
  transfer overlaps without losing established levels. Initialize missing protection only from real cost
  and fresh evidence; no invented historical highs or reset of known holds.
- [ ] **6. Verify the supervised execution host.** Source persists the exact approved strategy and transition state;
  use existing market-order functions with duplicate prevention, uncertain-response and restart recovery.
  Confirm/reconcile required non-overlap exits before new buys. Preserve independent paper history.
- [ ] **6a. Complete portfolio-wide ongoing management.** Finish the guided design for outside changes,
  separate automatic positions from extras managed for exits, same-symbol/transfer boundaries and known in-app orders,
  pause/resume, restart and replacement. The current
  session invalidates even an in-app manual fill; automatic continuation remains a proposal.
  Extend current Daily Shadow-only, single-session execution
  to both portfolio families and durable ongoing assignment; verify cross-family handover and multiple days.
- [ ] **7. Qualify and verify automation.** Establish strategy/evidence readiness, perform separately
  authorized native order/lifecycle tests, then explicitly authorize real execution. Recommendation-only
  approval under ADR-0062 retains its separate evidence policy; it does not qualify automation.

**Immediate next increment:** finish the ongoing-promotion design while qualifying the native
cash-event/boundary-book evidence producer and opening values. Storage is installed; SQL runtime
recovery rehearsal remains distinct from the completed schema/immutability checks.
The orchestration and production composition exist; they cannot manufacture those inputs.
Then complete the fresh financial activation packet and supervised host rollout. See
[ADR-0084](adr/0084-supervised-broker-strategy-session.md) and the
[concrete activation review](reviews/supervised-session-20260923.md).
The [current design draft](concepts/strategy-promotion-and-execution.md#account-baselines-and-protection-ownership--september-16-continuation)
and [ADR-0083](adr/0083-opening-account-values-and-shared-engine-hosting.md) hold detailed reasoning.
Optional older-history import remains a proposal; no historical reset is approved.

## Operational follow-through

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
| S4 | Bound growing account-history CPU/memory and establish collection/display capacity; installed UI fix is only partial coverage. |
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
