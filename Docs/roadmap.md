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
- [ ] Observe several manual post-Hermes backups before automating backup-and-copy behavior.
- [ ] Confirm the retention count against actual OneDrive quota and measured backup growth.
- [ ] Perform and document a test restore into a differently named temporary database.
- [ ] Reconcile remaining project/deployed index, default, foreign-key, and database-option drift without broad DACPAC publish.

### 3. Delphi restart

- [ ] Inventory Delphi's database writes, artifact writes, and optional external calls.
- [ ] Add or confirm a no-write/dry-run mode suitable for safe daily report validation.
- [ ] Run one controlled Delphi evaluation against current data.
- [ ] Review Continuation and Breakout outputs, gate traces, OBV coverage, and CLX interpretation.
- [ ] Confirm persisted picks, dossiers, and Granville logs match the console report when writes are enabled.

### 4. Observation baseline

- [ ] Resume daily paper recommendations without live execution.
- [ ] Define the minimum observation window and outcome measures before tuning thresholds.
- [ ] Record forecast/pick outcomes so changes can be compared against a stable baseline.

### 5. Development hygiene

- [ ] Add GitHub Actions for .NET build and `TraderVI.Core.Tests`.
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

- Sentinel intraday monitoring and enforced stop-loss/rotation workflows.
- Oracle Phase 3 debate loop and evaluation harness.
- Granville Overdueness (#23–#24), Heavy Volume, and later groups.
- Broader walk-forward strategy simulation and version comparison.
- Automated order execution only after paper-trade validation, risk controls, and broker integration are independently verified.

## Explicitly deferred

- Dullness (#21–#22) remains deferred under ADR-0005 pending longer history or a better universe.
- Promoting CLX from diagnostics to a gate/ranking input requires evidence and a new ADR.
- Promoting relative-strength Z-score to the executed ranking requires the backfill and comparison described above.
- New ML features, thresholds, or models require design-rule review and an ADR when they change decision behavior.
