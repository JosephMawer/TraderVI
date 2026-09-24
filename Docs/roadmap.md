# TraderVI Roadmap

This is the authoritative priority list. [Project status](project-status.md) owns dated evidence;
the [strategy alignment checklist](strategy-alignment-implementation-checklist.md) owns execution
dependencies. [Design rules](design-rules.md) and applicable accepted ADRs own behavior.

## Accepted direction — prove strategies, then adopt one deliberately

Keep independent paper strategies, deliberately approve an exact complete strategy for the permanent
TFSA, and enable real execution only after the required account, protection and broker checks.
[ADR-0055](adr/0055-independent-strategies-to-approved-live-execution.md) and
[ADR-0062](adr/0062-complete-strategy-recommendation-promotion.md) distinguish recommendation approval
from automatic trading. The operator's September 23 real-trading target is an aspiration, not verified
readiness or order authority. No strategy is selected by this roadmap.

## Now — restore a dependable advisory loop

1. **Complete the real-account foundation and execution path in dependency order.** Percentage-preserving
   cash adjustments are implemented/tested. Next qualify complete broker cash-event coverage/effective
   timing, native opening values and operational setup, then broker-input projection, synchronization,
   protection handover, durable activation/execution and qualification. Follow the
   [current checklist](strategy-alignment-implementation-checklist.md#left-to-do--development-order).
   A dedicated setup screen is not a prerequisite for developing the continuation logic.
   The September 24 [external-trade check](project-status.md#latest-operational-evidence) confirms a
   current synchronization gap; reconcile the reported exit and saved history under checklist item 4.
2. **Resolve current collection/valuation blockers.** Diagnose the held Live account's missing opening
   value and repeated tracked 15-minute conflict. Preserve immutable evidence and coverage flags.
   Verify current input adoption, overnight completion and uninterrupted regular-session Live/Shadow
   operation, including full-watchlist/provider capacity and SQL durability. Dated engineering
   activations are not full-session qualification or reusable exceptions.
   The [latest nightly check](project-status.md#latest-operational-evidence) confirms overnight
   completion; Athena's historical-input warning and full-session qualification remain separate follow-ups.
3. **Establish trustworthy comparisons.** Accumulate mature forecast/pick outcomes, verify contiguous
   leadership-mover history and compare common strategy/policy histories. Observe skipped entries,
   causal fills, Session-2 rotation and risk exits across clean Shadow sessions; exclude Sep 4 shakedown
   evidence from performance conclusions. The proposed TSX-gainers versus Delphi analysis remains
   analysis work, not authorization to retune gates. Use the
   [calibration checklist](calibration-implementation-checklist.md) and
   [Live checklist](delphi-live-implementation-checklist.md).
4. **Finish the controlled Practice dataset.** Prepare/exercise the actual 20-session dataset under
   acquisition authority, preserving missing inputs and historical membership provenance; Live and
   isolated SQL recovery/load trials follow. [Contracts](concepts/historical-practice-implementation.md).

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
