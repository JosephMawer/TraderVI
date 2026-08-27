# Paper calibration implementation checklist

- **Last updated:** 2026-08-26
- **Authoritative design:** ADR-0020 through ADR-0033
- **Background:** `Docs/concepts/paper-calibration-and-outcome-feedback.md`
- **Database rollout script:** `TraderDB/Migrations/20260823_011_AddCalibrationEvidenceLedger.sql`
- **Intraday rollout script:** `TraderDB/Migrations/20260826_012_AddIntradayEvidenceLedger.sql` (applied and verified 2026-08-26)

## Status legend

- `[x]` Complete and validated in source.
- `[ ]` Not complete.
- **Operational step** means it changes the local database or runs a consequential producer and requires explicit authorization.

## Current milestone

The immutable calibration ledger, prediction evaluator, coverage scorecard, three-session marks/excursions, and separate Continuation/Breakout scorecards are complete in source. ADR-0028 defines the delayed intraday swing-management challenger; the pure policy and deterministic five-to-fifteen-minute aggregation are implemented. ADR-0029 separates an operational intraday ghost-entry pilot from official ADR-0021/Athena outcomes. On 2026-08-26, the first explicitly selected five-position pilot cohort (NDM, CMG, ALK, EDR, and OGI) was linked to persisted Continuation picks and opened as one ghost share each. CMG, EDR, and OGI are now closed ghost trades; NDM and ALK remain open. ADR-0030's canonical two-table ledger is applied, and ADR-0031/0032 add database-guarded automatic ghost exits plus a live WPF dashboard over the shared monitor. The first market-hours run of the newly durable collector remains to be observed. Calibration-grade delayed outcomes and fresh post-entry Delphi exception evidence remain incomplete.

## Phase A — measurement contract and decisions

- [x] Separate prediction outcomes from tradeable outcomes.
- [x] Define official, exploratory, and legacy-reconstruction run purposes.
- [x] Define the model-evaluated candidate population and pre-scoring exclusion boundary.
- [x] Define normalized columns versus versioned JSON snapshots.
- [x] Define code commit, working-tree, strategy, and model-artifact provenance.
- [x] Define invalid versus degraded evidence and the 95% reporting-coverage floor.
- [x] Define label-aligned 1/5/10/20-session prediction outcomes.
- [x] Define next-eligible-session-open trade timing, costs, exits, and OHLC ambiguity handling.
- [x] Define Top-1, Top-3, Top-5, and rank-weighted research policies.
- [x] Define champion/challenger evidence tiers and human promotion rules.
- [x] Define the primary multi-day swing direction and keep intraday/opening confirmation as separate challengers.
- [x] Define coverage reporting and market-session cohort identity so reruns do not inflate evidence.
- [x] Define the initial three-session mark-to-market measure without claiming it is the final swing exit policy.
- [x] Define signed MFE/MAE, session-to-extreme, and same-session uncertainty as a separate immutable outcome.
- [x] Define separate lens scorecards and nested run/cohort aggregation so reruns cannot inflate evidence.
- [x] Define delayed 15-minute management of an open swing, same-day exits, trailing profit, conditional/absolute loss alerts, and five-/ten-session limits.
- [x] Accept ADR-0020 through ADR-0033 and add review cards.

## Phase B — immutable evidence capture

### Source implementation

- [x] Add canonical `CalibrationRun` schema.
- [x] Add canonical `CalibrationCandidate` schema.
- [x] Add canonical `CalibrationLensEvaluation` schema.
- [x] Add canonical `CalibrationOutcomeDefinition` schema.
- [x] Add canonical `CalibrationCandidateOutcome` schema.
- [x] Add one additive, transactional migration containing every new SQL object.
- [x] Add calibration domain records and schema-version constants.
- [x] Add transactional batch validation and persistence.
- [x] Capture the complete model-evaluated universe rather than only top picks.
- [x] Keep shared candidate facts separate from Continuation and Breakout decisions.
- [x] Capture configuration, market context, gate traces, model registry IDs, artifact hashes, and Git provenance.
- [x] Keep exploratory runs from refreshing official operational tables.
- [x] Add the new tables and migration to the SQL project.
- [x] Add Athena and the ADRs to the Visual Studio solution.

### Validation completed

- [x] Core tests pass: 102 passed, 0 failed on 2026-08-26.
- [x] Delphi focused build succeeds with no compiler warnings.
- [x] Athena focused build succeeds with no errors; a clean rebuild currently surfaces 236 repository compiler warnings, primarily the existing nullable-annotation backlog.
- [x] SQL project builds successfully with SSDT and includes all calibration objects.
- [x] Migration and canonical schema definitions compile together.

### Operational rollout

- [ ] A fresh immediately pre-migration backup was not independently observed; the newest recorded backup before the discovered schema application was the valid 2026-08-22 full backup.
- [x] Review `20260823_011_AddCalibrationEvidenceLedger.sql` against the target database.
- [x] Migration application discovered from live object creation timestamps: all five tables were created together on 2026-08-23 at 16:58:29 and contained zero rows when audited.
- [x] Verify all five tables, foreign keys, checks, unique constraints, and indexes exist and match the committed canonical definitions.
- [ ] Existing operational row-count preservation cannot be proven retrospectively because no immediate pre-migration counts were captured; the migration is additive and the five new tables were empty when inspected.
- [x] Create and verify a fresh post-migration full backup with checksums: `TraderDB_FULL_20260823_170922_281.bak` (31.23 MB).
- [x] Copy the backup to the approved OneDrive directory and verify matching SHA-256 `B1F19BDD3C2919DE5D1204BEF357F06276D174F9C46715D050D158E83EE5C2C3`.
- [ ] Confirm the OneDrive client reports cloud synchronization complete.
- [x] **Operational step:** run one controlled official Delphi evaluation: run `a8df36d1-3c4b-4d30-b062-f041d760d055`.
- [x] Verify the first run contains 213 distinct candidates and exactly 426 lens rows (Continuation and Breakout per candidate), with no duplicate candidates or lens evaluations.
- [x] Verify strategy/model/context JSON, commit `bf5be830a0a131ec05ce359c66b95f0c90e80101`, clean working-tree provenance, 2026-08-21 observation session, captured counts, and `Valid` audit state.
- [x] Rerun Delphi deliberately: run `092dc95b-9ad6-42dc-877e-db96db8c1d0e` appended another 213 candidates and 426 lens rows without overwriting the first run.
- [x] Run an explicit exploratory Delphi evaluation: run `99c0e2a3-1920-4b8d-a1e0-2cd7823bd751` captured 213 candidates and 426 lens rows as `ExploratoryReplay` while operational state remained 50 picks, 25 dossiers, and 11 Granville rows.
- [x] Create and verify a final post-run full backup, then hash-match its approved OneDrive copy: `TraderDB_FULL_20260823_173759_377.bak` (32.51 MB), SHA-256 `62CB244339235D555830CD93B139AD19182B160B4B4C7CBF9429B5A52CCB08BB`.

## Phase C — prediction outcomes

### Implemented

- [x] Add the separate local Athena console application.
- [x] Reuse enabled `ProfitModelRegistry` `ILabeler` implementations.
- [x] Compute aligned 1-, 5-, 10-, and 20-session returns.
- [x] Compute 10-session XIU return and excess return.
- [x] Refuse to substitute a later symbol bar for a missing XIU-aligned session.
- [x] Add deterministic IDs and idempotent inserts for initial outcome definitions.
- [x] Add idempotent matured prediction-outcome persistence.
- [x] Add focused maturity, session-alignment, label reuse, and evidence-integrity tests.

### Remaining

- [ ] **Operational step:** run Athena after the first official cohorts mature.
- [x] Persist explicit invalid records for missing or duplicate symbol sessions once the required XIU horizon has matured; genuinely immature horizons remain pending so late ingestion can settle.
- [x] Add coverage-first deterministic scorecards with run/cohort counts, valid/degraded/invalid/pending counts, completion and usable coverage, and a 95% primary-reporting floor.
- [ ] Add Brier score, reliability buckets, calibration error, AUC where supported, and probability-decile lift.
- [ ] Add Spearman rank information coefficient and top-1/top-3/top-5/top-decile lift.
- [ ] Add gate pass/fail, OBV-state, regime, liquidity, volatility, sector, and lens slices.
- [ ] Add versioned CSV export.
- [ ] Add integrity tests for duplicate outcomes, wrong-session joins, mixed purposes/lenses, and definition-version changes.

## Phase D — tradeable recommendation outcomes

- [x] Add a versioned tradeable-outcome definition and persistence shape.
- [x] Add separately versioned 1-, 2-, and 3-session economic measures for the short-term swing objective; retain 10/20-session model diagnostics.
- [x] Select the first eligible entry session using run time in `America/Toronto`.
- [x] Implement the three-session missing-entry-bar allowance and `NoEntry` result.
- [x] Apply raw open plus separately persisted 10 bps slippage and 15 bps half-spread per side.
- [x] Compute gross/net returns and net excess return versus XIU at the 1-, 2-, and 3-session closes.
- [x] Add raw MFE, signed MAE, first session-to-extreme, and conservative same-session ordering under an explicitly reviewed metric contract.
- [ ] Implement warning diagnostics and versioned hard-stop fills, including gap-through-stop handling.
- [ ] Flag conservative same-day path ambiguity.
- [x] Restrict tradeable outcomes to published lens recommendations.
- [x] Add recommendation-level Continuation and Breakout tradeability reports with joint coverage, no-entry rate, net/XIU-relative returns, MFE, MAE, and nested run/cohort weighting.
- [x] Resolve and version the initial delayed intraday swing profit-protection, trend-extension, loss-alert, and maximum-hold challenger rules in ADR-0028.
- [x] Add a deterministic pure policy engine for 15-minute bars, cost-aware break-even, non-decreasing trailing protection, fresh-Delphi exception qualification, data-age diagnostics, and time exits.
- [x] Add and run the authorized read-only `tmx-xiu-intraday` probe with no database writes: three bounded XIU calls completed on 2026-08-25.
- [x] Record the failed response contract honestly: the 2-, 14-, and 90-day 15-minute requests all returned the same seven daily 4:00 p.m. bars, and the two-day response included dates before its requested start.
- [x] Correct the TMX intraday request to omit `freq`, round Unix bounds to whole minutes, reject obvious daily fallback responses, and rerun the probe: the corrected 2- and 14-day requests returned clean, current, gap-free 15-minute XIU sessions.
- [x] Record the wide-window cap: the 90-day request returned the oldest 754 bars (29 sessions) only, so rolling monitoring windows are viable but historical loading requires bounded chunks and deduplication.
- [x] Harden `TmxClient` with bounded transient retries, timestamped intraday batches, strict OHLCV/timestamp/interval validation, explicit five-day chunked history, and neutral quote-freshness terminology.
- [x] Add the bounded read-only `tmx-xiu-market-hours` probe for five polls spaced fifteen minutes apart; it refuses to call TMX outside the regular Toronto market window and performs no database or file writes.
- [x] Run the TSX market-hours probe on 2026-08-26: five calls completed without a surfaced transport failure and advanced completed XIU events exactly once per poll, while every newer forming bar was revised before completion.
- [x] Verify lower resolutions with the read-only comparison probe: 1-, 5-, and 15-minute bars exist and each response includes a newer forming bar; five- and fifteen-minute sequences stayed gap-free, while the longer one-minute response developed two- and three-minute gaps.
- [x] Verify deterministic aggregation: all nine comparable completed fifteen-minute XIU bars exactly matched the OHLCV reconstructed from their three completed five-minute bars.
- [x] Accept completed five-minute bars as the version-1 evidence resolution while retaining the confirmed fifteen-minute polling cadence.
- [x] Add and test deterministic completed-five-minute to fifteen-minute aggregation that refuses incomplete three-bar groups.
- [x] Define the immutable completed-bar and per-request poll-audit ledger in ADR-0030, independent from positions and official outcomes.
- [x] Add canonical `IntradayPollObservation` and `IntradayEvidenceBar` definitions plus the single additive `20260826_012_AddIntradayEvidenceLedger.sql` migration; do not mix interval bars into `DailyBars` or legacy `Quotes`.
- [x] Validate the canonical intraday schema with Visual Studio MSBuild 18.10 plus SSDT; no DACPAC deployment or migration execution was performed.
- [x] Add transactional persistence planning/repository code for completed bars, exact-repeat idempotence, conflict invalidation, failed-poll audits, and schema presence checks.
- [x] **Operational step:** review the exact script, create and checksum-verify `TraderDB_FULL_20260826_161611_862.bak`, apply migration 012 manually, and verify both new empty tables, keys, checks, and indexes.
- [x] Copy that backup to the approved OneDrive directory and verify matching SHA-256 `CBDDB1E31877CA36E7B798867B5AC924DE0C9F09373F382191F30AE815C4A5B7`.
- [x] Add the ADR-0029 operational bridge for preflighted, one-share intraday ghost entries linked to today's persisted Continuation picks.
- [x] Add the pilot 15-minute monitor for linked positions, using direct completed policy bars with source age and position snapshots.
- [x] **Operational step:** preflight and open the first one-share ADR-0029 cohort for NDM, CMG, ALK, EDR, and OGI on 2026-08-26; verify all five linked active positions exist and the first monitor pass returned `Hold` while awaiting eligible completed bars.
- [x] Record the authorized CMG, EDR, and OGI ghost exits through TraderVI; realized pilot P/L is currently -$0.01, with NDM and ALK still active.
- [x] Replace the replay-only pilot with a shared durable collector-backed monitor that writes poll receipts/completed evidence before exposing decisions.
- [x] Add ADR-0031 database-guarded automatic ghost exits at a separately observed post-detection TMX price; this can never place a broker order.
- [x] Add the ADR-0032 live WPF paper dashboard with 15-minute scheduled market polling, positions, P/L, trade history, and durable receipt history.
- [x] Apply ADR-0033's thirty-second SQL display refresh without changing five-minute evidence collection or fifteen-minute policy decisions.
- [x] Add the first tabbed-shell vertical slice: Paper Trading plus a read-only Data Audit tab backed by the same shared workflow as the retained DataAudit CLI.
- [ ] **Operational step:** keep the WPF app open during a regular session and verify its first durable 15-minute cycle, five-minute evidence writes, and automatic ghost-exit behavior if a policy exit occurs.
- [ ] Join the latest valid post-entry OfficialPaper Breakout evidence needed by the conditional -10% exception.
- [ ] Add a separately versioned delayed-intraday paper outcome with achievable post-detection fills and lens scorecards.
- [ ] Keep opening confirmation and intraday wave execution as separately scored challengers; do not activate either without evidence and human approval.

## Phase E — shadow portfolios

- [ ] Implement normalized fractional Top-1 selection-quality portfolio.
- [ ] Implement normalized fractional Top-3 and Top-5 equal-weight portfolios.
- [ ] Implement the versioned rank-weighted formula.
- [ ] Implement capital-constrained integer-share versions using historical capital and reserve.
- [ ] Implement the accepted versioned swing holding/exit policy, vacancy filling, duplicate-symbol handling, and rotation rules.
- [ ] Add equity curves, drawdown, turnover, costs, utilization, and recovery metrics.
- [ ] Keep Continuation and Breakout portfolio results separate.

## Phase F — calibration experiments and promotion

- [ ] Add calibration experiment/proposal persistence.
- [ ] Record hypothesis, champion, challenger, attempted variants, windows, primary metric, and guardrails.
- [ ] Add one-variable replay/ablation support for OBV weight, raw versus Z-scored RS, and gate thresholds.
- [ ] Add untouched forward-validation windows and cohort-aware uncertainty estimates.
- [ ] Enforce the 10, 20–30, 60, and 120-cohort evidence tiers from ADR-0022.
- [ ] Record approval identity/time and link approved changes to strategy, model, policy, ADR, and code versions.
- [ ] Preserve the former champion for rollback comparisons.
- [ ] Confirm no code path can automatically activate a challenger.

## Phase G — optional narration

- [ ] Revisit LLM narration only after deterministic scorecards and fixtures are stable.
- [ ] Keep calculations, proposal approval, and activation outside the LLM.
- [ ] Reconcile any proposal-narration role with Oracle Rule R1.

## Promotion-readiness tracker

- [ ] 10 matured official cohorts: measurement system validated.
- [ ] 20–30 matured cohorts: provisional challengers may be formed with aligned historical evidence.
- [ ] 60 matured cohorts plus regime/forward-window requirements: ordinary promotion proposals may be reviewed.
- [ ] 120 matured cohorts plus stronger downside evidence: safety-gate or concentration changes may be reviewed.

## How to ask for status

Useful prompts include:

- “Update the paper-calibration checklist and tell me the next safe step.”
- “What is complete in Phase B, and what blocks the first official calibration run?”
- “Show me everything left before Athena can produce its first scorecard.”
- “Which operational calibration steps require a database backup or explicit authorization?”
