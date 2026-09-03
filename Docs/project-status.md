# TraderVI Project Status

**Snapshot date:** 2026-09-02
**Purpose:** Fast orientation to what is implemented, operational, and currently blocked. Update this document after major milestones or when the daily workflow changes.

## Executive summary

TraderVI is an advisory-mode TSX momentum-rotation system with an immutable paper-calibration ledger and deterministic Continuation/Breakout scorecards. ADR-0040's delayed-intraday outcome is complete in source, including a continuity guard: Athena rejects proven missing 15-minute bars/sessions, receipt-order conflicts, and missing exact symbol/XIU fill bars instead of silently replaying across them; an unproven end-of-data tail remains pending. Migration 015 is applied and its fifth definition is active. Athena has produced the first 112 valid three-session marks and 112 valid excursion outcomes; the longer prediction and delayed-intraday definitions still have zero outcomes. Operational Real exits remain manually reported Wealthsimple fills and are never substituted into official outcomes. The Trading tab keeps open Delphi-linked positions and unlinked operator-reported Real holdings in Tracked positions while retaining closed lifecycles in Trade history. There is no broker integration.

ADR-0042 now defines and implements the post-ADR-0041 strategy/code boundary in source. Comparative
reports and export-schema-v2 artifacts are scoped to one explicitly identified active strategy, while
earlier runs remain immutable and are counted as excluded. Manual migration 016 was backed up,
authorized, applied, and verified on 2026-09-02. `v3.1-rs-date-aligned` became the sole active strategy at
that boundary, with zero included official runs and seven earlier-identity runs excluded.

ADR-0043 now preserves leadership-source missingness in source. Movers-derived breadth requires an exact
same-date XIU anchor and a complete 50-symbol observation, while scoring uses a contiguous canonical XIU
session suffix and reports unavailable inputs as neutral/no-data rather than zero or falling breadth. The
fixed successor is `v3.2-leadership-missingness` at behavioral commit `fad4b96`. Manual migration 017 is
applied and verified: v3.2 is the sole active strategy, the nullable leadership contract is trusted, and
all protected evidence counts and references remain unchanged. The first post-migration Hermes ingestion
completed on 2026-09-02: its 2026-09-01 observation stored 11 new highs, 18 new lows, and 467 eligible
issues while preserving unavailable movers breadth and same-session price anchors as null. Athena does not
collect or repair leadership inputs. The first deliberate official v3.2 Delphi run is valid for recommendation
date 2026-09-02 using market data through 2026-09-01; it added 219 candidates, 438 lens rows, and 50
published picks. The official ledger now contains 8 runs across 6 distinct market-data cohorts and 1,729
candidates.

The repository retains a coherent June/August feature foundation plus the September correctness and
identity stabilization sequence. It includes multi-lens ranking, Granville indicators, relative-strength
ranking, ghost/real trade logging, historical sector data, per-symbol On-Balance Volume (OBV), market-wide
Climax (CLX) reporting, and read-only desktop documentation and scorecard surfaces.

ADR-0046 adds the unattended operations boundary: a Windows task builds the current source and then runs
the resulting hash-verified Release artifacts for Hermes, Delphi, and Athena at 00:30 Toronto/Eastern time
on weekdays, with source-stability verification, exclusive locking, same-date suppression, stage timeouts,
atomic status, and no automatic retry. A separate Codex heartbeat
reads only that durable status and stays quiet unless a run is missing, stale, long-running, failed, or
needs attention. The pipeline does not include Hercules, migrations, WPF monitoring, Oracle, Sandbox, or
broker activity. The Windows task and weekday 07:00 Codex heartbeat were installed and verified active on
2026-09-02; no operational pipeline run was started during setup, so the first scheduled result is pending.

## Verified development baseline

- Active branch: `master`, with upstream `origin/master` configured.
- Nightly operations: Windows task `TraderVI Nightly` is Ready for weekday 00:30 execution under the
  signed-in user, with wake/start-when-available enabled and no automatic retry. Codex automation
  `TraderVI Nightly Watch` is active for weekday 07:00 read-only supervision.
- SDK: .NET 10 (`10.0.400` verified on 2026-09-02).
- Complete Release solution build: successful after the ADR-0013 score-once restoration using Visual Studio 2026 Insiders MSBuild 18.10 and SSDT; `TraderDB.dacpac` was produced without deployment on 2026-09-02.
- Core tests: 213 passed, 0 failed, 0 skipped in both Debug and Release validation on 2026-09-02. The test project is explicitly marked as a test project so .NET 10 discovery cannot silently report success with zero executed tests.
- Migration 017 parses with the SQL Server 2019 (`TSql150`) offline parser with zero errors. The final canonical SQL project and complete Release solution both build successfully after the strengthened identity constraint; neither build deployed the DACPAC.
- Known dependency advisories remain; see build output before updating packages.
- Local database engine: SQL Server 2019 Developer RTM (`15.0.2000.5`); the project now targets `Sql150` and blocks database deployment.
- Database recovery: `TraderDB` uses SIMPLE recovery with page checksums. `DBCC CHECKDB` completed without errors on 2026-08-22.
- Backup baseline: a 31.00 MB compressed checksum full backup completed and passed `RESTORE VERIFYONLY` on 2026-08-22. Its staging and OneDrive copies have matching SHA-256 hashes.
- Automatic backup validation: the first full post-Hermes path completed on 2026-08-22; both 31.09 MB copies independently matched SHA-256 `48B655E9BC1D402E85CA6F8698F548115FC074F7ED0B74AD40C9699E61482CB6`.
- Calibration evidence backup: the final post-run 32.51 MB checksum backup and approved OneDrive copy matched SHA-256 `62CB244339235D555830CD93B139AD19182B160B4B4C7CBF9429B5A52CCB08BB` on 2026-08-23. Cloud-sync completion remains user-observed.
- Intraday-ledger backup: the pre-migration checksum backup and approved OneDrive copy matched SHA-256 `CBDDB1E31877CA36E7B798867B5AC924DE0C9F09373F382191F30AE815C4A5B7` on 2026-08-26; migration 012 was then applied and verified.
- Outcome-definition backup: the pre-migration 34.98 MB checksum backup and approved OneDrive copy matched SHA-256 `A01FFCECAD236C967D8BA68AA4DCC8387BFB49DFA9C5B22F533A69C995D48F27` on 2026-08-28; migration 014 was then applied and verified without creating outcomes.
- Strategy-identity backup: `TraderDB_FULL_20260902_002015_223.bak` passed `RESTORE VERIFYONLY WITH CHECKSUM`; its 37,508,096-byte staging and OneDrive copies matched SHA-256 `33C5A08493BE4A2941341CC22EFECAB773BD854AA908C289DB6D6F5E15573EFF`, and the operator confirmed synchronization before migration 016.
- Leadership-missingness backup: `TraderDB_FULL_20260902_012154_689.bak` passed `RESTORE VERIFYONLY WITH CHECKSUM`; its 37,497,344-byte staging and OneDrive copies matched SHA-256 `D74FF2F3F3B18AED0C8C72BCCB99D6214497B7BFA3738B6E9420B7FA5EACF658`, and the operator confirmed synchronization before migration 017.
- First post-migration Hermes backup: `TraderDB_FULL_20260902_013547_367.bak` passed Hermes' SQL checksum verification; its 37,552,640-byte staging and OneDrive copies independently matched SHA-256 `B4C7126D851B20704C37B3A8D4EE43497AFE9AD3A1B8B9E20F779F3EB51AE625`. OneDrive cloud-sync completion remains user-observed.
- A/D integrity: the incremental lookback double-counting defect was fixed, and all 262 stored rows were repaired and re-audited with zero plurality, cumulative, or step mismatches. The final 2026-08-21 cumulative is `7,307`.

## Programs and responsibility

| Program | Current responsibility | Side effects |
|---|---|---|
| Hermes | Daily OHLCV ingestion and derived market-data maintenance; verified post-success database backup | External TMX/Yahoo reads; writes multiple SQL tables and a local/OneDrive backup pair |
| Hercules (`ML.Train`) | Trains enabled profit models and records experiments/models | CPU-intensive training; writes model artifacts and SQL registry rows |
| Delphi | Evaluates the universe through Continuation and Breakout lenses and emits reports | Reads models/data; rewrites daily picks, dossiers, narratives, and Granville logs for the evaluation date |
| TraderVI | Manual ghost CLI plus shared durable paper monitor | Writes simulated positions, trade logs, and intraday evidence; does not place live orders |
| TraderVI.WPF | Tabbed desktop shell for Trading, Data Audit, Delphi, official Scorecards, and Project Docs | Trading refreshes SQL history, polls TMX during the regular monitor window, auto-closes Ghost positions only, and records confirmed operator-reported Real fills after migration 013; Scorecards, Data Audit, saved Delphi sessions, and Project Docs are read-only; confirmed official Delphi runs have their documented SQL effects; no broker integration |
| Oracle | Optional LLM narration over deterministic decision dossiers | May call a configured LLM service and write narrative records |
| DataAudit | Read-only full-local-universe classification, freshness, mapping, and bar-integrity diagnostics | Local SQL reads only; no external calls or writes |
| Sandbox | Manually selected probes for reconnaissance, calibration, and controlled backfills | Probe-specific; some call external services or mutate SQL |
| Nightly runner | Builds current source, orders the resulting Hermes, Delphi, and Athena artifacts, and records durable local status for supervision | Inherits only those three programs' documented effects; never restores dependencies, retries automatically, or places broker orders |

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
- Delphi now scores every symbol once and applies both lens gate/rank definitions to those exact shared facts. Profit-model results are also reused for composite calculation and reporting instead of invoking each model twice inside one score.
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
- All six Ghost lifecycles are closed. The Delphi-linked Real lifecycle is also closed after an
  operator-reported all-shares broker exit on 2026-09-02. One historical Real holding without an
  original-pick link remains open. These operational records are not official Athena outcomes.
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

Advanced official prediction scorecard implementation on 2026-08-27:

- ADR-0038 adds a pure calculator over `PredictionLabels10` version 1 plus a read-only official-evidence query used by Athena.
- The four model reports include cohort-weighted Brier score, supported AUC, fixed reliability buckets, expected calibration error, and probability-decile event lift.
- Separate eligible Continuation and Breakout reports include Spearman rank IC and Top-1/Top-3/Top-5/top-decile ten-session return lift.
- Diagnostic slices cover OBV, regime, sector, first lens-gate result, published lens, observation dollar volume, and observation range. They are descriptive and cannot change weights.
- Five export-schema-v2 CSV artifacts are available through an explicit non-overwriting Athena option; every artifact carries the selected strategy ID/name, initial code commit, and decision reference.
- 138 Core tests and the focused Release Athena build passed. Athena itself was intentionally not run, so no outcome row or export file was written.
- Migration 014 was applied on 2026-08-28 to seed the four canonical definition contracts that were previously initialized only by Athena. Post-migration inspection found 6 official runs across 4 market-data cohorts, 1,292 official candidates, 2,584 correctly paired lens rows, no duplicate run/lens ranks, and zero outcomes. The WPF query can therefore load coverage while remaining read-only; Athena was not run merely to initialize rows.

Verified operational calibration state on 2026-09-01:

- Migration 015 was operator-applied and read-only inspection verified all five active definitions, including `DelayedIntradaySwing` version 1. A migration-015 backup was not independently verified in this inspection.
- The latest official Delphi run, `B46479FF-23CE-4E25-9842-C0609AE29362`, is valid for recommendation date 2026-09-01 using market data through 2026-08-31. It persisted 218 candidates and 436 lens rows. The ledger now contains 7 official runs across 5 distinct market-data cohorts and 1,510 candidates.
- Both lenses published 25 picks. The first five Breakout symbols were MATR, GTE, BTE, ATH, and ESI; the first five Continuation symbols were MATR, BTE, ESI, GTE, and ATH.
- Athena wrote 112 valid matured `SwingMarkToMarket3` outcomes and 112 valid matured `SwingExcursion3` outcomes. `PredictionLabels10`, `PredictionPath20`, and `DelayedIntradaySwing` remain at zero outcomes.
- The operator launched WPF, but the durable ledger still ended on 2026-08-28: SGY had 76 five-minute bars and 26 fifteen-minute bars, XIU had 78 and 26, and no later receipt was present. The launch therefore does not yet verify a new market-hours collection cycle.

Strategy/code identity activation on 2026-09-02:

- Migration 016 activated fixed identity `99D52317-8D16-4F2A-8B97-AE9698972F55` / `v3.1-rs-date-aligned`, initial behavioral commit `c51c0849fd1311b3797cc664a19988e553bbe122`, decision `ADR-0041`. The prior `v3.0` row remains immutable and inactive.
- The first execution exposed a SQL Server same-batch column-resolution defect before activation. `XACT_ABORT` rolled the schema transaction back completely; all captured schema and row counts were unchanged. The migration was split into two transactional dynamic batches, rebuilt with SSDT, independently reviewed, and committed at `d391db8` before the successful execution.
- Postflight found both identity columns with the expected nullable `nvarchar` shapes, an enabled/trusted `CK_StrategyVersion_CodeIdentity`, no constraint violations or partial identities, one exact active successor, and zero bidirectional threshold or model-mapping differences from `v3.0`.
- Preservation counts remained 8 calibration runs, 1,723 candidates, 3,446 lens evaluations, 5 outcome definitions, and 224 outcomes. No historical run references the successor; the active report scope is 0 included and 7 excluded legacy official runs.
- No TraderVI application, market service, training, outcome, publication, or model-artifact workflow ran during activation.

Leadership-source missingness stabilization on 2026-09-02:

- ADR-0043 makes `ActiveAdvancers`, `ActiveDecliners`, and `ActiveN` an all-null-or-valid observation. A genuine tied basket remains numeric zero; an unavailable, stale, partial, malformed, or unanchored movers response remains null.
- Hermes requires a current-date computed row, exact current-date XIU anchor, and exactly 50 distinct nonblank symbols with explicit price changes before assigning movers data. It does not attribute undated responses to another session or overwrite a valid same-date observation with missingness.
- Granville leadership and light-volume scoring uses a 12-session contiguous canonical XIU suffix. Missing sessions break the suffix, post-calendar rows are ignored, and unavailable or insufficient mover coverage is neutral and explicitly reported.
- The fixed successor identity is `2BD1A7D0-D144-4A7B-9FA4-49606AB7E963` / `v3.2-leadership-missingness`, initial behavioral commit `fad4b968244d57a6224cb7ab137774a9bff4b645`, decision `ADR-0043`. Existing evidence remains immutable under its original identity.
- All 202 Core tests passed in Debug and Release; focused Core, Delphi, Hermes, and WPF Release builds and the complete SSDT-inclusive Release solution build succeeded. Migration 017 also passed offline `TSql150` parsing and independent static review. Existing dependency advisories and compiler-warning backlog remain separate.
- Migration 017 was separately authorized, applied, and independently verified after the synchronized backup. It converted exactly 101 legacy `0/0/0` sentinels to null while preserving all 115 leadership rows; both checks are enabled, trusted, and not-for-replication; v3.2 is solely active; threshold/model differences are zero; and 8 runs, 1,723 candidates, 3,446 lens rows, 5 definitions, and 224 outcomes remain unchanged. No application, external call, training, outcome, publication, or artifact workflow ran.

Unified Trading and WPF scorecard implementation on 2026-08-28:

- ADR-0039 adds a read-only Scorecards tab over ADR-0038's exact official query and pure calculator; it displays coverage, model, reliability, decile, lens, and slice reports without requiring CSV.
- Migration 013 and synchronized canonical schema add `ExecutionMode`, `AccountLabel`, and immutable `PositionExecutionAudit`. Existing rows are deliberately backfilled as Ghost.
- The Trading tab labels every row as `GHOST` or `REAL`, shows accounts, and separates Ghost/Real open counts plus realized/unrealized P/L.
- Saved Delphi picks can be tracked as Ghost or as an operator-reported Real fill. Existing Ghost positions can be confirmed as Real, and actual all-shares Real exits can be recorded manually.
- The shared monitor evaluates both modes, but its tested execution guard permits automatic closure only for Ghost. Real alerts remain manual-action signals; no broker client or order action was added.
- Migration 013 was applied manually on 2026-08-28 after a checksum-verified full backup and hash-matched approved secondary copy. Verification found all expected columns, defaults, enabled/trusted checks, primary/foreign keys, an empty execution audit, and preserved row counts; all 6 existing position rows and 11 trade rows remain Ghost.
- 143 Core tests and the complete Release solution build passed with Visual Studio 18.10 MSBuild, including the SSDT database project. The WPF application, Athena, Delphi, and TMX were intentionally not run during the migration rollout; migration 013 was applied and verified separately as recorded above.

Historical Real-position monitoring correction on 2026-09-02:

- ADR-0044 includes active Real holdings in the Trading dashboard and shared monitor even when they have no Delphi pick link; unlinked Ghost rows remain excluded.
- Monitoring begins at the durable enrollment record rather than pretending earlier intraday evidence exists. Unlinked Real holdings use ordinary conservative loss handling and cannot receive ADR-0028's fresh-Delphi loss exception.
- The existing hard execution boundary remains unchanged: Real alerts require manual broker action and an operator-reported fill; no broker call or automatic Real closure is possible.
- All 213 Core tests passed in Debug and Release, and the focused WPF Release build succeeded. The corrected Release binary was built separately while TraderVI.WPF was closed; no broker operation was used for source validation.

First v3.2 operating day on 2026-09-02:

- The first deliberate official `v3.2-leadership-missingness` run is valid for recommendation date
  2026-09-02 using market data through 2026-09-01. It persisted 219 candidates, 438 lens rows, and 50
  published picks. The official ledger now contains 8 runs across 6 distinct market-data cohorts and 1,729
  candidates; all earlier identities remain immutable and outside the active scorecard scope.
- The 09:47 and 10:02 Toronto WPF cycles produced valid five- and fifteen-minute receipts for XIU and the
  unlinked historical Real holding, but SGY failed both times with `Tmx15FetchFailed`. A read-only probe of
  the exact production window exposed conflicting values for the shared 09:30 boundary bar returned by two
  overlapping five-day requests; the position record and symbol were not the cause.
- ADR-0045 separates a five-minute collection schedule from the unchanged completed 15-minute policy bars,
  records new receipts as `IntradayEvidenceCollectorV2`, and makes adjacent wide requests minute-contiguous
  without overlap. The corrected SGY production-window probe succeeded with 82 returned / 81 completed
  15-minute bars.
- The rebuilt WPF app started visibly at 10:21. Its 10:22 startup cycle and 10:27 scheduled cycle were both
  fully valid: each stored six valid receipts for XIU and the two Real holdings. The cycles began 4 minutes
  59 seconds apart, verifying the five-minute runtime cadence. The first corrected SGY cycle added 55
  completed 15-minute bars and 164 completed five-minute bars; the next cycle added only the newly completed
  five-minute bar. TraderVI sent no broker operation or automatic Real exit.
- After the 10:22 cycle raised an SGY Real sell signal, the operator reported an all-shares broker fill.
  TraderVI stored one attached Real SELL and closed the tracked SGY lifecycle atomically; preflight and
  postflight confirmed one BUY, one SELL, and no active SGY row. The visibly restarted WPF app's 10:35
  cycle then stored four valid collector-v2 receipts for XIU and the remaining Real holding only.

## Known gaps and risks

1. **Historical relative-strength features are empty.** This blocks the documented RS-to-Hercules training path and Z-score backfill analysis.
2. **Backup operations still need repeated observation and restore testing.** The first automatic post-success Hermes backup/copy completed successfully, but retention is not automated, OneDrive cloud-sync completion remains user-observed, and no test restore has been performed yet.
3. **Database deployment is intentionally manual.** `TraderDB.sqlproj` is a `Sql150` build/reference artifact and blocks Deploy targets. Live changes use dated scripts under `TraderDB/Migrations`; unresolved project/database drift must be reconciled without broad DACPAC publish.
4. **Model registry hygiene.** Retired experiments remain enabled in SQL even though runtime filtering prevents them from loading.
5. **Universe hygiene requires continued monitoring.** The reviewed full-universe audit is clean, but upstream metadata can classify newly listed ETFs as stocks. Run DataAudit regularly after ingestion changes. Delphi now independently excludes any symbol history that does not match its canonical XIU session before scoring.
6. **No CI baseline.** Builds and tests are local only.
7. **Outcome feedback has started but is not broadly mature.** Eight official runs across six market-data
   cohorts now contain 1,729 candidates. Athena has written 112 valid three-session marks and 112 valid
   excursion outcomes, but 10-session labels, 20-session paths, and delayed-intraday outcomes remain at
   zero. The delayed evaluator now converts proven evidence gaps into audited invalid outcomes while leaving
   an unproven tail pending. Fresh valid SGY/XIU five- and fifteen-minute evidence now exists after the
   ADR-0045 correction, but Athena has not been rerun against it. Track progress in
   `Docs/calibration-implementation-checklist.md`.
8. **Compiler warning backlog.** A clean Athena/Core rebuild succeeds but reports 236 warnings, primarily nullable annotations outside a nullable context plus existing unreachable/unused code warnings. Treat this separately from the dependency-security advisories and from build failures.
9. **Ghost/Real source support is rolled out, but Real records remain operator-reported only.** Migration 013 is applied and verified. The EDR Ghost mirror completed its paper exit at $15.62 and remains immutable; the operator declined a separate Real entry on 2026-08-28 because EDR is no longer in the current Delphi picks. There is no broker verification, balance, partial-fill, commission, or order integration. No policy signal may be interpreted as a completed real sale.
10. **Broader leadership SQL/repository integrity remains deferred.** The first post-migration Hermes observation passed: the 2026-09-01 row retained an all-null active-breadth triple, zero sentinel and partial triples remain absent, and the constraint remains enabled/trusted. ADR-0043 intentionally does not resolve every older `LeadershipData` integrity rule; broader canonical SQL/repository reconciliation remains a separate roadmap item.

## Immediate direction

Stabilize the existing daily advisory loop before adding indicators or automation:

1. Treat the valid 2026-09-02 Delphi run as the first official `v3.2-leadership-missingness` cohort; run
   Delphi again only when another deliberate cohort is wanted, and do not count repeated runs over one
   market-data date as independent evidence.
2. Keep exactly one `TraderVI.WPF` instance open during regular market hours only when resuming the separate
   intraday evidence workflow. It now collects every five minutes while policy decisions remain based on
   completed 15-minute bars; observe source health without starting a second monitor.
3. Monitor the remaining open historical Real position, but record a sale only from the operator's actual
   broker fill. A Real alert cannot auto-close it.
4. Run Athena only after new eligible daily or intraday evidence exists. It does not initialize or repair
   leadership data. Confirm that new valid outcomes mature, incomplete tails stay pending, and any proven
   continuity gap is stored as invalid rather than bridged.
5. Add new tracked positions only from current saved Delphi Buy picks with operator-confirmed shares and fill
   prices; do not reopen closed Ghost lifecycles.
6. Observe additional Hermes backups and perform a test restore.
7. Continue the stabilization order with canonical SQL/repository drift, then transaction boundaries and CI
   before larger workflow extraction.
