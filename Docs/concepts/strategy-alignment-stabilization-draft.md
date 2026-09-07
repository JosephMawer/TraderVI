# Strategy alignment stabilization — design draft

- **Status:** Running design record. Accepted F2 rules are in
  [ADR-0056](../adr/0056-dated-profit-model-input-contract.md); preserved model sets and explicit
  selection are in [ADR-0057](../adr/0057-preserved-model-sets-and-explicit-selection.md). The operator
  subsequently authorized model inspection, separate replacement training, registration and cutover;
  these steps are complete. Later policy and common-comparison decisions remain open.
- **Date:** 2026-09-06
- **Domains:** architecture, machine-learning, data-pipeline, decision-engine
- **References:** [audit](../reviews/strategy-direction-alignment-20260906.md),
  [ADR-0055](../adr/0055-independent-strategies-to-approved-live-execution.md),
  [design rules](../design-rules.md), [frozen Delphi Live V1](delphi-live.md),
  [implementation checklist](../strategy-alignment-implementation-checklist.md).

## Frame

The immediate problem is correcting feature inputs, loaded-model identity and post-fill measurement.
The parent problem is attributing trustworthy evidence to a complete identifiable strategy. The root goal
is dependable human-approved real-account recommendations, then separately authorized broker execution.

The audit confirmed that training supplied XIU while prediction omitted it. Embedded relative-performance
values could silently become zero, and market-window filtering did not guarantee matching stock/benchmark
session endpoints. These existing features are distinct from planned database RS integration. The source
correction now shares one dated input builder. A later authorized review could not establish the saved
models' original training contract, so separate replacements were trained and selected.

## Accepted decisions

1. **Preserve existing product boundaries.** ADR-0055's independent accounts, complete-strategy adoption,
   human review and separate broker authority stand. Frozen Delphi Live V1 formulas, thresholds, timing,
   portfolio ownership and family-specific evidence requirements remain unchanged.
2. **Contain missing stock inputs.** The user accepted excluding only an affected stock when its required
   dated input history is incomplete; otherwise complete stocks may still be evaluated. Exclusions must
   retain explicit reasons and counts. Never substitute zero for an unavailable required model feature.
3. **Treat shared input failure as shared failure.** Unusable required XIU inputs prevent the new official
   daily recommendation set. Existing position protection retains its own rules and authority.
4. **Require proven compatibility before model reuse.** The user accepted permitting an existing artifact
   only after a separately authorized review proves compatibility with the corrected input contract.
   Otherwise train separate candidates under explicit authority. The operator subsequently authorized
   that review and fallback, which have been completed without overwriting the original artifacts.
5. **Separate corrected evidence.** Corrected feature behavior needs a new explicit strategy/feature
   identity. Preserve original cohorts and their definitions; no retrospective reruns or relabelling.
6. **Restore settled producer/consumer contracts independently.** A saved post-fill mark must contain
   exactly the marked holdings used in its NAV. Model provenance must describe the bytes actually loaded
   by the engine. Each failed CI command must fail its step. These repairs have acceptance criteria without
   changing selection thresholds or promotion rules; implementation status is in the checklist.

## Source staging and completed operational transition

The guarded nightly runner builds the current working tree. Changing default feature behavior can
therefore affect its next run without a separate deployment. A new implementation must not silently use
corrected inputs with old unidentified model/strategy assumptions.

Initially the corrected path was implemented behind an explicit strategy/model compatibility binding;
the initial source-only phase made no operational switch. The operator then authorized technical review,
replacement training if necessary, a new strategy version and nightly adoption. ADR-0057 now preserves
exact model assignments and separates training from reviewed selection. The initial v3.3 cutover was
followed by the authorized [ADR-0058](../adr/0058-daily-ingestion-and-observed-benchmark-confirmation.md)
schedule/benchmark repair. Current `v3.4-observed-benchmarks` reuses those four model files and requires
current observed XIU/SPY inputs. Actual bootstrap, input validation and preservation checks passed.

The nightly CLI and WPF resolve the stored assignment automatically. The predecessor remains available
for deliberate rollback; both directions passed rolled-back SQL rehearsals. Existing evidence and
thresholds are preserved. No full nightly pipeline was launched, so first scheduled-cohort observation
remains pending. Details and validation are in the [cutover review](../reviews/model-input-cutover-20260906.md).

The user requested a slower ordinary conversation after finding the timed question cards confusing.
Explain the concrete remaining operational step before asking for it; do not repeat the previous rapid
question sequence or infer an answer from elapsed time.

## Acceptance criteria for F2 source

- Training-window generation and prediction consume one explicit, dated input contract with identical
  feature order, dimensionality and calculations for the same observations.
- Required stock and XIU endpoints match exact canonical sessions. Missing, duplicate, invalid or future
  input cannot silently become a shorter return window, a nearby date or a substitute numeric value.
- Training reports rejected windows; prediction reports excluded symbols. The exact required history and
  canonical-session source must be specified in the implementation boundary, with existing history rules preserved.
- Corrected model input behavior cannot run under an old strategy identity merely because vector length
  matches. Unknown compatibility remains unknown and fails the corrected-path compatibility check.
- Reports expose input-contract identity, omissions and reasons. Operational activation requires
  reviewed strategy/model registration and explicit authorization; the initial cutover met that boundary.
- Tests use synthetic dated observations and model fixtures; no training or private artifacts are needed
  to prove feature parity, no-future-input behavior, stock-only exclusions or shared-input failure handling.

## Deferred and not implementation-ready

- Historical label-definition dispatch, daily Shadow policy dispatch and risk-default ownership: later phases.
- Fair comparison across strategy families and real-account recommendation routing: phase 4 reviewed design.
- Broker access and execution: separate future design and authorization.

The dated contract is wired through actual Hercules training and Delphi prediction/reporting, with
isolated tests of their shared input methods. Candidate-only default training, immutable manifests,
reviewed selection and retained rollback identity are implemented for the supported daily workflow.
Broader strategy/policy dispatch and legacy entry-path retirement remain tracked independently.

## Review questions

1. Why does a missing stock endpoint affect one candidate while a missing shared XIU input affects the set?
2. Why can a correct feature-vector length fail to prove that an existing model is compatible?
3. Why does a source change need an explicit rollout boundary when a nightly task builds current source?
