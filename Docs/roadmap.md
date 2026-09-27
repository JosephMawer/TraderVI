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

September 26 requested direction: add an AI-managed momentum portfolio and use it for real trading,
targeting September 28. AI Explorer is deferred to a future session; the first pass is AI Momentum
in paper and intended live use. [ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md) accepts
discretion, enforced boundaries, reducing risk as losses deepen and no loosening stops to prolong
losing trades. The first API client, shared spending journal and portfolio-proposal contract are
implemented offline under the operator's initial below-CAD-100 monthly ceiling. Codex subscription
access remains an alternative. Next define eligible portfolios and evaluated learning, remaining
rule classification, authoritative packet adapters and production budget/storage configuration.
Learning consultation is source-integrated into the supervisor and execution-preparation coordinator;
one post-session learning review is now agreed alongside intraday saved-lesson consultation.
Next connect qualified outcome evaluation and host scheduling under ADR-0087, leaving immature
or inconclusive cases pending and using the same monthly allowance.
Follow the [AI Momentum delivery sequence](strategy-alignment-implementation-checklist.md#ai-momentum-delivery-sequence):
Step 1 is complete offline; Step 2 is in progress. Existing engine daily templates use 7%.
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
review hooks are agreed; shared timing/budget checks also apply to later hooks. Implementation
remains pending. Budget pacing is settled: no daily allocations or future-consult reserves; use the
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
Starting-capital capture and upfront initial thresholds are confirmed: later rounds use actual
reconciled opening capital, with any earlier previews labelled estimates (ADR-0087).
Per-stock protection reuses engine settings: AI may tighten stops or sell earlier but never loosen
or remove established stops. Record blocked alternatives for evaluated learning; enforce boundaries
in code. Initial switching behavior is agreed: retain compatible holdings/protection, reconcile
required exits before replacements and preserve round/risk history. Eligibility and Step 5 integration
remain pending. Stop/action enforcement remains Step 4 and outcome comparison Step 6 work. Keep the MVP focus and
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
