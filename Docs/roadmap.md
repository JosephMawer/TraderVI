# TraderVI Roadmap

This is the authoritative priority list. Keep detailed rationale in ADRs and deferred design questions in `Docs/reviews/open-questions.md`.

## Now — restore a dependable advisory loop

### 1. Repository orientation and working agreements

- [x] Make root `AGENTS.md` the repository-wide Codex instruction source.
- [x] Move Sandbox-specific guidance to `Sandbox/AGENTS.md`.
- [x] Replace stale status/TODO material with `Docs/project-status.md` and this roadmap.
- [ ] Review the June/August feature sequence as a stabilization release boundary and decide whether it needs a tag or release notes.

### 2. Database deployment safety

- [x] Align the SQL project with the deployed SQL Server 2019 provider and disable project deployment.
- [x] Establish dated manual migrations with additive-by-default, no-unapproved-deletion rules.
- [x] Add checksum full-backup, integrity-check, and guarded SIMPLE-recovery operational scripts.
- [x] Create and verify the first full backup, copy it to the approved OneDrive folder, and measure its compressed size (31.00 MB).
- [x] Run the first integrity check and change the live database to SIMPLE recovery.
- [x] Integrate verified backup-and-copy behavior as an internal post-success Hermes stage, independent of its launcher.
- [ ] Observe several automatic post-Hermes backups and confirm OneDrive synchronization before relying on the routine unattended (first successful run confirmed 2026-08-22).
- [ ] Confirm the retention count against actual OneDrive quota and measured backup growth.
- [ ] Perform and document a test restore into a differently named temporary database.
- [ ] Reconcile remaining project/deployed index, default, foreign-key, and database-option drift without broad DACPAC publish.

### 3. Delphi restart

- [x] Inventory Delphi's database writes, artifact writes, and optional external calls.
- [x] Confirm ordinary operation is a deliberate persisted run; use builds/tests for harmless validation rather than launching Delphi as a smoke test.
- [x] Run one controlled Delphi evaluation against current data.
- [x] Review Continuation and Breakout outputs, gate traces, OBV coverage, and CLX interpretation.
- [x] Confirm persisted picks, dossiers, and Granville logs match the console report when writes are enabled.
- [x] Correct diagnostic ambiguity around lens ranking, model-signal versus strategy-gate thresholds, RS fallbacks, and recommendation-date versus market-data-date semantics.

### 4. Observation baseline

Design background: [`Docs/concepts/paper-calibration-and-outcome-feedback.md`](concepts/paper-calibration-and-outcome-feedback.md).
Implementation tracker: [`Docs/calibration-implementation-checklist.md`](calibration-implementation-checklist.md).

- [x] Resume daily paper recommendations without live execution; the 2026-08-26 Delphi run supplied the first ADR-0029 ghost pilot cohort.
- [x] Define the evidence, outcomes, primary swing direction, initial three-session marks/excursions, separate lens scorecards, cohort aggregation, coverage contract, promotion tiers, delayed intraday swing-management challenger, non-calibration ghost-entry pilot, durable intraday ledger, automatic ghost exits, and live dashboard (ADR-0020 through ADR-0032).
- [x] Repair and revalidate the TMX intraday request: current short-window XIU calls return clean 15-minute bars; wide responses are capped and require bounded chunks.
- [x] Run the prepared `tmx-xiu-market-hours` sequence and lower-resolution comparison: completed bars advanced reliably, forming bars were mutable, five-minute evidence stayed gap-free, and one-minute evidence showed occasional gaps.
- [x] Select completed five-minute bars as version-1 evidence and add the replay-only ADR-0029 ghost-entry/advisory-monitor bridge.
- [x] Add and source-validate the ADR-0030 canonical intraday schema, additive migration, transactional repository, and conflict/idempotence tests.
- [x] Back up and apply migration 012 with explicit authorization, verify the objects, and replace the pilot replay path with the shared durable collector-backed monitor.
- [x] Add a live WPF paper dashboard and database-guarded automatic ghost exits that cannot place broker orders.
- [x] Begin the ADR-0033 tabbed desktop shell with Paper Trading and read-only Data Audit tabs; preserve the DataAudit CLI over the same shared workflow and reduce display refresh to thirty seconds.
- [x] Add ADR-0034's Delphi tab over a shared `DelphiWorkflow`; safely display persisted lenses by default and require confirmation before an official database-writing run.
- [x] Add ADR-0035's six-view Delphi operator workspace and immutable typed presentation snapshot, with labelled date-aligned reconstruction for older saved runs.
- [x] Add ADR-0036's native read-only Project Docs tab with repository-wide discovery, search, folder navigation, safe links, and refresh.
- [x] Observe the first market-hours durable dashboard cycle: the 2026-08-27 09:47 Toronto cycle persisted valid receipts and completed evidence for NDM/ALK; no exit rule triggered.
- [x] Add ADR-0038's advanced official prediction scorecards with cohort-weighted model calibration, lens rank quality, diagnostic slices, integrity checks, and versioned CSV artifacts.
- [x] Add ADR-0039's read-only WPF Scorecards workspace and explicit Ghost/Real source model with separate P/L, account labels, manual fills, audited reconciliation, and Ghost-only automation.
- [x] Accept ADR-0040's reconstructible delayed-intraday fill contract, add the pure calculator, and make future WPF monitor cycles collect aligned XIU five-/fifteen-minute evidence once per cycle.
- [x] Complete ADR-0040 in source with immutable-evidence queries, Athena persistence, separate raw/sensitivity lens reports, and guarded migration 015; leave migration application and Athena execution as explicit operational steps.
- [x] Add ADR-0040's continuity guard so proven missing policy sessions/bars, receipt-order conflicts, and missing exact fill bars are audited invalid rather than silently bridged; preserve pending for an unproven tail.
- [x] Apply and verify migration 015's fifth active definition, then run Athena: 112 valid three-session marks and 112 valid excursions matured; prediction and delayed-intraday outcomes remain at zero.
- [x] Back up, apply, and verify migration 014 so the read-only Scorecards workspace can load the canonical outcome definitions without running Athena prematurely.
- [x] Review, back up, manually apply, and verify migration 013 with explicit authorization; preserve all legacy rows as Ghost and verify the empty immutable execution audit.
- [x] Close the EDR reconciliation decision without changing its audited Ghost history: on 2026-08-28 the operator chose not to add a separate `REAL / TFSA` row because EDR is no longer in the current Delphi picks. The broker holding remains outside TraderVI unless deliberately re-entered from a future saved pick.
- [x] Verify the five-share EDR Ghost position joins a valid scheduled durable cycle and observe its automatic $15.62 `Policy TrailingProfit` exit end to end.
- [ ] Record enough forecast/pick outcomes to compare changes against a stable baseline. Collection has begun with 112 valid three-session marks and 112 valid excursions across a ledger of 7 official runs/5 market-data cohorts; longer prediction and delayed-intraday outcomes are still unavailable.
- [x] Review and correct the 19 fallback symbols: 14 funds reclassified as ETFs, four obsolete TSX listings made inactive, and GDI mapped to Industrials.
- [x] Add a reusable, read-only full-local-universe audit with session-based freshness and structural integrity checks.
- [x] Triage the full-audit candidates using official sources and apply only reviewed manual corrections; final audit passed with zero findings on 2026-08-22.
- [x] Add a Delphi pre-scoring eligibility gate that excludes symbol histories not matching the canonical XIU market-data session.

### 5. Correctness and change-safety stabilization

The 2026-09-01 static changeability audit found correctness and integrity risks inside the existing
advisory loop. This tranche preserves the observation-first strategy: it corrects measurement and
publication contracts without tuning weights, thresholds, ranking theses, or automatic execution.

- [ ] Keep new official Delphi publication/evidence cohorts paused through the separately authorized
  migration-016 activation and verification. Normal Hermes ingestion and single-instance WPF evidence
  collection remain separate operational workflows.
- [x] Replace undated, minimum-length relative-strength inputs with explicit alignment to canonical XIU
  sessions; surface missing/degraded coverage and add source-level regression fixtures for unequal
  histories, exact depth boundaries, and interior gaps (ADR-0041; source completed 2026-09-01).
- [x] Validate ADR-0041 under the separately authorized build/test boundary: 20 focused tests and all
  170 Core tests passed; Core, Delphi, WPF, and Hermes focused builds succeeded; and the complete Release
  solution built with Visual Studio 18.10 MSBuild plus SSDT on 2026-09-01.
- [x] Record ADR-0042's `v3.1-rs-date-aligned` strategy/code identity and immutable legacy-evidence
  treatment; implement publication guards and scope comparative reports/exports to the explicitly
  identified active strategy. No threshold, model, gate, ranking, or policy was tuned.
- [x] Validate ADR-0042 source: all 174 Core tests passed in Debug and Release; affected-project builds
  succeeded; and the complete Release solution built with Visual Studio 18.10 MSBuild plus SSDT on
  2026-09-02 without database deployment or launching an operational application.
- [ ] After a fresh verified backup and separate authorization, apply and verify manual migration 016
  so the identified successor becomes active. Keep official Delphi publication paused until then.
- [ ] Preserve leadership-source missingness so an unavailable movers layer cannot be persisted or scored
  as genuine zero/falling breadth.
- [ ] Restore ADR-0013's score-once contract and add direct decision-engine/lens characterization tests
  before adding another lens.
- [ ] Reconcile canonical SQL definitions with repository writes before any broad database work. Continue
  to use reviewed manual migrations only; do not use DACPAC deployment.
- [ ] Record and implement explicit transaction boundaries for same-day Delphi operational publication and
  tracked-position opening. A successful zero-result rerun must replace stale same-date operational picks,
  and retry behavior must not expose empty or partially replaced projections. Keep append-only calibration
  evidence semantically distinct.
- [ ] Establish CI and static contract checks after the bounded correctness fixes and before large workflow
  extraction. Decide the SSDT job boundary separately.
- [ ] Introduce immutable Delphi evaluation facts and typed load/evaluate/evidence/publish stages while
  preserving ADR-0035 presentation compatibility.
- [ ] Separate Hermes completion semantics and Hercules training/evaluation/artifact-promotion responsibilities.
- [ ] Phase in clearer module boundaries, nullable/analyzer enforcement, dormant-code removal, and public-surface
  reduction after the consequential seams above are protected.

### 6. Development hygiene

- [ ] Add GitHub Actions for .NET build and `TraderVI.Core.Tests` as ordered in the stabilization tranche.
- [ ] Decide whether the SSDT build belongs in the same Windows workflow or a separate job.
- [ ] Address dependency-security advisories separately from compiler cleanup.

## Next — improve data and model evidence

- [ ] Backfill `RelativeStrengthFeatures`, including `CompositeScoreZ`, with coverage diagnostics.
- [ ] Compare raw relative-strength and Z-score rank orderings before changing the executed lens.
- [ ] Integrate historical RS features into Hercules only after the backfill is validated.
- [ ] Audit enabled historical `ModelRegistry` rows and model artifacts; retire obsolete entries deliberately.
- [ ] Calibrate `ObvSignalWeight` from paper outcomes rather than intuition.
- [ ] Accumulate enough CLX history to assess confirmation/divergence usefulness.
- [ ] Run the Granville cross-family overlap audit before changing point weights.

## Later — expand system capability

- Always-on paper-monitor hosting through a Windows service or scheduled process; v1 requires the WPF app or console watch to remain running.
- Extend the Scorecards workspace with charts only after enough matured official cohorts make them useful; retain Athena and its optional CSV export.
- Consider Hermes status/run and System Log tabs as shared-workflow operator surfaces, with consequential runs explicitly confirmed and the existing CLIs preserved.
- Consider broker import/reconciliation only after the manual ADR-0039 Real workflow is operated and audited; do not infer that `Real` means broker-verified.
- Any future live-broker Sentinel stop-loss and rotation workflows require a new decision, independent authentication, limits, previews, partial-fill handling, reconciliation proof, and a kill switch.
- Oracle Phase 3 debate loop and evaluation harness.
- Granville Overdueness (#23–#24), Heavy Volume, and later groups.
- Broader walk-forward strategy simulation and version comparison.
- Automated order execution only after paper-trade validation, risk controls, and broker integration are independently verified.

## Explicitly deferred

- Dullness (#21–#22) remains deferred under ADR-0005 pending longer history or a better universe.
- Promoting CLX from diagnostics to a gate/ranking input requires evidence and a new ADR.
- Promoting relative-strength Z-score to the executed ranking requires the backfill and comparison described above.
- New ML features, thresholds, or models require design-rule review and an ADR when they change decision behavior.
