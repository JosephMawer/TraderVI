# TraderVI Project Status

**Snapshot date:** 2026-08-22
**Purpose:** Fast orientation to what is implemented, operational, and currently blocked. Update this document after major milestones or when the daily workflow changes.

## Executive summary

TraderVI is an advisory-mode TSX momentum-rotation system. The data collector (Hermes) is operational again and current market data is present through 2026-08-21. Delphi completed its first full restart evaluation on 2026-08-22, producing and persisting both ranking lenses, Continuation dossiers, and Granville diagnostics. The next priority is to resume paper recommendations and establish an outcome-measurement baseline before tuning the strategy.

The repository contains a coherent 30-commit June/August development sequence. It adds multi-lens ranking, more Granville indicators, relative-strength ranking, ghost-mode trade logging, historical sector data, per-symbol On-Balance Volume (OBV), and market-wide Climax (CLX) reporting.

## Verified development baseline

- Active branch: `master`, with upstream `origin/master` configured.
- SDK: .NET 10 (`10.0.400` verified on 2026-08-18).
- Complete solution build: successful with Visual Studio 2026 Insiders MSBuild 18.10 and SSDT; `TraderDB.dacpac` was produced.
- Core tests: 22 passed, 0 failed, 0 skipped.
- Known dependency advisories remain; see build output before updating packages.
- Local database engine: SQL Server 2019 Developer RTM (`15.0.2000.5`); the project now targets `Sql150` and blocks database deployment.
- Database recovery: `TraderDB` uses SIMPLE recovery with page checksums. `DBCC CHECKDB` completed without errors on 2026-08-22.
- Backup baseline: a 31.00 MB compressed checksum full backup completed and passed `RESTORE VERIFYONLY` on 2026-08-22. Its staging and OneDrive copies have matching SHA-256 hashes.
- Automatic backup validation: the first full post-Hermes path completed on 2026-08-22; both 31.09 MB copies independently matched SHA-256 `48B655E9BC1D402E85CA6F8698F548115FC074F7ED0B74AD40C9699E61482CB6`.
- A/D integrity: the incremental lookback double-counting defect was fixed, and all 262 stored rows were repaired and re-audited with zero plurality, cumulative, or step mismatches. The final 2026-08-21 cumulative is `7,307`.

## Programs and responsibility

| Program | Current responsibility | Side effects |
|---|---|---|
| Hermes | Daily OHLCV ingestion and derived market-data maintenance; verified post-success database backup | External TMX/Yahoo reads; writes multiple SQL tables and a local/OneDrive backup pair |
| Hercules (`ML.Train`) | Trains enabled profit models and records experiments/models | CPU-intensive training; writes model artifacts and SQL registry rows |
| Delphi | Evaluates the universe through Continuation and Breakout lenses and emits reports | Reads models/data; rewrites daily picks, dossiers, narratives, and Granville logs for the evaluation date |
| TraderVI | Manual trade/position CLI in ghost mode | Writes simulated positions and trade logs; does not place live orders |
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
- No active ghost-mode positions are open.
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

## Known gaps and risks

1. **Historical relative-strength features are empty.** This blocks the documented RS-to-Hercules training path and Z-score backfill analysis.
2. **Backup operations still need repeated observation and restore testing.** The first automatic post-success Hermes backup/copy completed successfully, but retention is not automated, OneDrive cloud-sync completion remains user-observed, and no test restore has been performed yet.
3. **Database deployment is intentionally manual.** `TraderDB.sqlproj` is a `Sql150` build/reference artifact and blocks Deploy targets. Live changes use dated scripts under `TraderDB/Migrations`; unresolved project/database drift must be reconciled without broad DACPAC publish.
4. **Model registry hygiene.** Retired experiments remain enabled in SQL even though runtime filtering prevents them from loading.
5. **Universe hygiene requires continued monitoring.** The reviewed full-universe audit is clean, but upstream metadata can classify newly listed ETFs as stocks. Run DataAudit regularly after ingestion changes. Delphi now independently excludes any symbol history that does not match its canonical XIU session before scoring.
6. **No CI baseline.** Builds and tests are local only.
7. **Outcome feedback loop is not operational yet.** ADR-0020 through ADR-0022 define the evidence, outcome, execution, and promotion contracts. The additive schema is present in TraderDB and matched the project during the 2026-08-23 audit; a fresh post-migration backup and hash-matched OneDrive copy were created. Delphi has not yet captured an official calibration run and Athena has not written an outcome. Track progress in `Docs/calibration-implementation-checklist.md`.

## Immediate direction

Stabilize the existing daily advisory loop before adding indicators or automation:

1. Resume deliberate daily Delphi recommendations without live execution.
2. Define the observation window and realized-outcome measures for Continuation versus Breakout picks.
3. Observe additional Hermes backups and perform a test restore.
4. Add CI, then resume feature/backfill work from `Docs/roadmap.md`.
