# TraderVI Project Status

**Snapshot date:** 2026-08-27
**Purpose:** Fast orientation to what is implemented, operational, and currently blocked. Update this document after major milestones or when the daily workflow changes.

## Executive summary

TraderVI is an advisory-mode TSX momentum-rotation system with an immutable paper-calibration ledger and deterministic Continuation/Breakout scorecards. The user reported Hermes current and ran Delphi on 2026-08-26. Completed five-minute TMX bars are the accepted version-1 storage resolution; the operational policy consumes TMX's direct completed fifteen-minute bars. ADR-0029 distinguishes an operational ghost-entry pilot from official Athena outcomes. The first five one-share ghost positions—NDM, CMG, ALK, EDR, and OGI—were linked to persisted Continuation picks. CMG, EDR, and OGI have been closed as ghost trades for a combined realized -$0.01; NDM and ALK remain open. Migration 012 is applied and verified. ADR-0031/0032 provide a shared durable monitor, database-guarded automatic ghost exits, and a live WPF dashboard without broker integration. ADR-0033 through ADR-0036 evolve TraderVI into a tabbed shell with Paper Trading, Data Audit, a six-view Delphi operator workspace, and a native read-only Project Docs reader while retaining the CLIs over shared workflows. The Delphi tab reads the saved run by default and requires confirmation before an official database-writing evaluation. The next trading boundary is observing the first market-hours durable collection cycle.

The repository contains a coherent 31-commit June/August development sequence. It adds multi-lens ranking, more Granville indicators, relative-strength ranking, ghost-mode trade logging, historical sector data, per-symbol On-Balance Volume (OBV), market-wide Climax (CLX) reporting, and the read-only desktop documentation surface.

## Verified development baseline

- Active branch: `master`, with upstream `origin/master` configured.
- SDK: .NET 10 (`10.0.400` verified on 2026-08-18).
- Complete solution build: successful with Visual Studio 2026 Insiders MSBuild 18.10 and SSDT; `TraderDB.dacpac` was produced.
- Core tests: 120 passed, 0 failed, 0 skipped on 2026-08-27.
- Known dependency advisories remain; see build output before updating packages.
- Local database engine: SQL Server 2019 Developer RTM (`15.0.2000.5`); the project now targets `Sql150` and blocks database deployment.
- Database recovery: `TraderDB` uses SIMPLE recovery with page checksums. `DBCC CHECKDB` completed without errors on 2026-08-22.
- Backup baseline: a 31.00 MB compressed checksum full backup completed and passed `RESTORE VERIFYONLY` on 2026-08-22. Its staging and OneDrive copies have matching SHA-256 hashes.
- Automatic backup validation: the first full post-Hermes path completed on 2026-08-22; both 31.09 MB copies independently matched SHA-256 `48B655E9BC1D402E85CA6F8698F548115FC074F7ED0B74AD40C9699E61482CB6`.
- Calibration evidence backup: the final post-run 32.51 MB checksum backup and approved OneDrive copy matched SHA-256 `62CB244339235D555830CD93B139AD19182B160B4B4C7CBF9429B5A52CCB08BB` on 2026-08-23. Cloud-sync completion remains user-observed.
- Intraday-ledger backup: the pre-migration checksum backup and approved OneDrive copy matched SHA-256 `CBDDB1E31877CA36E7B798867B5AC924DE0C9F09373F382191F30AE815C4A5B7` on 2026-08-26; migration 012 was then applied and verified.
- A/D integrity: the incremental lookback double-counting defect was fixed, and all 262 stored rows were repaired and re-audited with zero plurality, cumulative, or step mismatches. The final 2026-08-21 cumulative is `7,307`.

## Programs and responsibility

| Program | Current responsibility | Side effects |
|---|---|---|
| Hermes | Daily OHLCV ingestion and derived market-data maintenance; verified post-success database backup | External TMX/Yahoo reads; writes multiple SQL tables and a local/OneDrive backup pair |
| Hercules (`ML.Train`) | Trains enabled profit models and records experiments/models | CPU-intensive training; writes model artifacts and SQL registry rows |
| Delphi | Evaluates the universe through Continuation and Breakout lenses and emits reports | Reads models/data; rewrites daily picks, dossiers, narratives, and Granville logs for the evaluation date |
| TraderVI | Manual ghost CLI plus shared durable paper monitor | Writes simulated positions, trade logs, and intraday evidence; does not place live orders |
| TraderVI.WPF | Tabbed desktop shell for Paper Trading, Data Audit, Delphi, and Project Docs | Paper tab refreshes SQL history, polls TMX during the regular monitor window, and can record ghost exits; Data Audit, Delphi's saved-session workspace, and Project Docs are read-only; confirmed official Delphi runs have their documented SQL effects; no broker integration |
| Oracle | Optional LLM narration over deterministic decision dossiers | May call a configured LLM service and write narrative records |
| DataAudit | Read-only full-local-universe classification, freshness, mapping, and bar-integrity diagnostics | Local SQL reads only; no external calls or writes |
| Sandbox | Manually selected probes for reconnaissance, calibration, and controlled backfills | Probe-specific; some call external services or mutate SQL |

## Decision engine

### Active trained profit models

The code registry currently enables four profit models:

1. `BinaryUp10` — upside-tail probability and direction input.
2. `BinaryDown10` — downside-tail probability and veto/penalty input.
3. `VolExpansionRelative10` — volatility-expansion confirmation.
4. `BreakoutEnhanced` — breakout/setup probability.

`RelStrengthCont10_2pct` is retained in code but disabled. Historical `ModelRegistry` rows for retired tasks remain enabled in SQL; `DelphiBootstrap` filters them out unless the task is currently enabled in `ProfitModelRegistry`.

### Active deterministic pattern signals

- `Trend10`
- `Trend30`
- `MaCrossover`

These are rule-based detectors evaluated directly in code, not trained ML models.

### Ranking lenses

- **Continuation** is the executed lens. It requires trend confirmation and ranks primarily by live relative strength plus the OBV tilt.
- **Breakout** is journaled as a comparison lens. It retains the breakout setup gate and ranks by `DirectionEdge + relative strength + OBV tilt`.
- CLX is diagnostic-only in v1; it does not gate or rank candidates.

### Active Granville groups

- Plurality (#1–#4)
- Disparity (#5–#6)
- Leadership (#7–#10)
- Most Active / Features (#11–#14)
- Weighting (#15–#16)
- Genuity (#17–#20)
- Light Volume (#25–#28)

Dullness (#21–#22) is deferred by ADR-0005. Overdueness (#23–#24) and groups after #28 remain future work.

## Local data snapshot

Read-only aggregate inspection on 2026-08-19 showed:

| Dataset | Rows | Latest date |
|---|---:|---|
| `DailyBars` | 758,870 | 2026-08-18 |
| `AdvanceDeclineLine` | 259 | 2026-08-18 |
| `SectorIndices` | 18,315 | 2026-08-18 |
| `LeadershipData` | 106 | 2026-08-18 |
| `SymbolObv` | 61,271 | 2026-08-18 |
| `MarketClimax` | 1 | 2026-08-18 |
| `RelativeStrengthFeatures` | 0 | — |
| `UsIndexBars` | 5,161 | 2026-08-19 |
| `DailyPick` | 327 | 2026-06-06 |
| `GranvilleIndicatorLog` | 76 | 2026-06-06 |

Additional state:

- 386 stock-sector mappings are present.
- The ADR-0029 pilot began with five one-share positions. CMG closed at $3.98 (+$0.03), EDR at $15.12 (-$0.04), and OGI at $1.74 ($0.00); NDM at $2.43 and ALK at $1.92 remain open. These are operational pilot positions, not official Athena outcomes.
- 27 historical task types have enabled registry rows, with no duplicate enabled row within a task type.

Post-restart Delphi verification on 2026-08-22:

- 25 Continuation picks persisted with ranks 1–25.
- 25 Breakout picks persisted with ranks 1–25.
- 25 Continuation decision dossiers persisted with ranks 1–25.
- 11 Granville diagnostic rows persisted for the recommendation date.
- The 19 RS fallbacks were audited and corrected: 14 funds were reclassified as ETFs, BITF/GLXY/NGD/NVA were marked inactive after their TSX listings ended, and active GDI was mapped to Industrials (`^TTIN`). The guarded migration updated 18 symbol rows and inserted/updated one mapping without deleting price history.
- `PickDate`/Granville `EvalDate` remain the recommendation run date; reports separately identify the latest completed TSX session used as the market-data-as-of date.

First full-local-universe DataAudit run on 2026-08-22:

- 1,592 symbol rows inspected; 496 active (375 stocks and 121 ETFs).
- 10 errors: nine severely stale active symbols plus one empty active symbol key.
- 118 warnings: one lagging symbol, one missing active-stock map, 43 unmapped active stocks, 24 stale mappings, and 49 fund-like rows classified as stocks.
- No non-positive prices, inverted high/low ranges, open-outside-range rows, negative volumes, duplicate symbol/date bars, orphan bars, invalid sector prices, or referenced sector-index coverage failures were reported.

Full-local-universe reconciliation completed on 2026-08-22:

- Final audit: 1,591 symbol rows; 485 active (321 stocks and 164 ETFs); zero errors and zero warnings.
- Ten reviewed ended or suspended listings were marked inactive without deleting their price history: BLX, ECN, FOOD, GDI, KSI, OLA, QIPT, SOY, URC, and WNDR.
- The single invalid empty-symbol metadata row was deleted only after a dependency scan found no related data and the exact deletion was authorized.
- Eighty-eight additional fund rows were reclassified from Stock to ETF; active status, leverage/inverse flags, mappings, and price history were preserved.
- The apparent active-stock mapping and stale-mapping warnings were classification errors, so no artificial sector mappings were created for those ETFs.

Shared-workflow DataAudit verification on 2026-08-26:

- Both the retained CLI and the new WPF tab use `MarketDataAuditWorkflow`.
- The read-only CLI run inspected 1,591 symbols and reported zero errors plus one warning: active RCTR's latest bar was 2026-08-21, two XIU sessions behind the 2026-08-25 market-data date.
- No database correction or external market call was performed.

Shared-workflow Delphi tab implementation on 2026-08-26:

- `DelphiWorkflow` now owns the evaluation once; the retained CLI and WPF tab are adapters over it.
- Opening or refreshing the tab only reads the latest persisted Continuation and Breakout picks plus their matching immutable Delphi presentation snapshot.
- ADR-0035 adds Overview, Picks, Market, Granville, Diagnostics, and Full Report views backed by typed report facts instead of parsed console text.
- New official runs store the versioned presentation snapshot inside the existing `CalibrationRun.RunContextJson`; older saved runs use a clearly labelled, date-aligned reconstruction and never substitute current market values.
- An official run requires a warning confirmation and explicitly states that it appends calibration evidence and replaces same-date operational records.
- Core, Delphi, and WPF focused builds succeeded; 110 core tests passed. Delphi itself was intentionally not run and no database record was changed during implementation. No database migration is required for ADR-0035.

Native Project Docs implementation on 2026-08-27:

- ADR-0036 adds a fourth WPF tab that discovers repository Markdown, groups it by folder, searches titles/paths/contents, and defaults to this project-status document.
- A native `FlowDocument` renderer presents headings, prose, emphasis, code, lists/checklists, blockquotes, tables, rules, and links without an embedded browser or new Markdown dependency.
- Core owns testable catalog discovery, exclusions, heading identifiers, and safe link resolution. Relative Markdown links stay inside the tab only when their canonical path remains under the repository root and the target is in the catalog; external HTTP(S) links open only when clicked.
- Refresh reloads files edited outside TraderVI. The feature is read-only and performs no SQL, model, market-data, or trading operation.
- 120 Core tests passed and the focused Release WPF build succeeded. The application itself was intentionally not launched because its Paper Trading tab can poll TMX and write SQL during market hours. No database migration is required for ADR-0036.

## Known gaps and risks

1. **Historical relative-strength features are empty.** This blocks the documented RS-to-Hercules training path and Z-score backfill analysis.
2. **Backup operations still need repeated observation and restore testing.** The first automatic post-success Hermes backup/copy completed successfully, but retention is not automated, OneDrive cloud-sync completion remains user-observed, and no test restore has been performed yet.
3. **Database deployment is intentionally manual.** `TraderDB.sqlproj` is a `Sql150` build/reference artifact and blocks Deploy targets. Live changes use dated scripts under `TraderDB/Migrations`; unresolved project/database drift must be reconciled without broad DACPAC publish.
4. **Model registry hygiene.** Retired experiments remain enabled in SQL even though runtime filtering prevents them from loading.
5. **Universe hygiene requires continued monitoring.** The reviewed full-universe audit is clean, but upstream metadata can classify newly listed ETFs as stocks. Run DataAudit regularly after ingestion changes. Delphi now independently excludes any symbol history that does not match its canonical XIU session before scoring.
6. **No CI baseline.** Builds and tests are local only.
7. **Outcome feedback is collecting but not mature.** ADR-0020 through ADR-0032 define the official contracts, policy separation, promotion rules, delayed intraday challenger, non-calibration pilot, durable evidence ledger, automatic ghost exits, and live dashboard. Migration 012 is applied; collector and automatic ghost-exit code are built, but their first market-hours durable cycle has not yet been observed. The pilot cannot contribute to Athena or promotion evidence. Calibration-grade delayed outcomes and the fresh post-entry Delphi exception join remain incomplete. At the last independently audited calibration count, two official validation runs and one exploratory replay covered the same 2026-08-21 market session, so they count as one independent cohort. Track progress in `Docs/calibration-implementation-checklist.md`.
8. **Compiler warning backlog.** A clean Athena/Core rebuild succeeds but reports 236 warnings, primarily nullable annotations outside a nullable context plus existing unreachable/unused code warnings. Treat this separately from the dependency-security advisories and from build failures.

## Immediate direction

Stabilize the existing daily advisory loop before adding indicators or automation:

1. Continue deliberate daily official Delphi recommendations without live execution to accumulate distinct cohorts.
2. Run Hermes on the normal schedule so future eligible sessions become available; do not run Athena merely to create still-pending outcomes.
3. Run Athena once official swing paths have matured, then verify entry timing, 1/2/3-session marks, MFE/MAE paths, separate lens scorecards, and reporting coverage; verify label-aligned 1/5/10/20-session outcomes when those longer horizons mature.
4. Keep `TraderVI.WPF` open during regular market hours, observe the first durable polling cycle, and verify the remaining NDM/ALK ghost positions are monitored and any policy exit is recorded exactly once.
5. Implement the calibration-grade delayed intraday outcome and fresh post-entry Delphi exception join without mixing pilot records into Athena evidence.
6. Observe additional Hermes backups and perform a test restore.
7. Add CI, then resume feature/backfill work from `Docs/roadmap.md`.
