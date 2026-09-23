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
readiness or order authority. Any portfolio must be promotable, with ongoing management until pause
or switch. Manual buys stay outside by default, with explicit purchase-time or later handover into
exit management alongside the strategy's normal automatic positions
([ADR-0085](adr/0085-portfolio-wide-ongoing-tfsa-management.md)); ungated Breakout Top 3 is an
initial candidate. The [September 23 activation review](reviews/supervised-session-20260923.md) says NO-GO.

## Now — restore a dependable advisory loop

1. **Complete the real-account foundation and execution path in dependency order.** Percentage-preserving
   cash adjustments and the broker projection/synchronization/protection/durable-session source path
   are implemented/tested. Next qualify native cash-event coverage/effective timing and boundary books,
   and native orders/strategy evidence. Storage migrations 035/039/040 are installed; operational setup
   and SQL runtime recovery still need verification. Finish ongoing-promotion design, including manual
   holding handover constraints, shared regular broker refresh and known in-app order reconciliation, then extend both
   families and replacement/restart transitions, then verify the host before actual execution approval. Follow the
   [current checklist](strategy-alignment-implementation-checklist.md#left-to-do--development-order).
   A dedicated setup screen is not a prerequisite for developing the continuation logic.
2. **Resolve current collection/valuation blockers.** Diagnose the held Live account's missing opening
   value and repeated tracked 15-minute conflict. Preserve immutable evidence and coverage flags.
   Verify current input adoption, overnight completion and uninterrupted regular-session Live/Shadow
   operation, including full-watchlist/provider capacity and SQL durability. Dated engineering
   activations are not full-session qualification or reusable exceptions.
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

- Confirm OneDrive cloud synchronization and perform a documented restore into a separate test database.
  Retention is already installed; retain the latest two completed backups per location.
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
  [open questions](reviews/open-questions.md); this is separate from installed database-backup retention.

## Completed work and history

The September 22 documentation cleanup preserves the full prior
[roadmap](roadmap-history-20260922.md), [status](project-status-history-20260922.md) and
[alignment checklist](strategy-alignment-history-20260922.md). Those snapshots include completed
milestones and historical authority, not fresh instructions. Current installed/verified facts live
only in [project status](project-status.md); this page keeps priorities and links. No operational
verification date was advanced by the cleanup.
