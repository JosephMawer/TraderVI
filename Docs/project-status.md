# TraderVI Project Status

**Snapshot date:** 2026-08-19  
**Purpose:** Fast orientation to what is implemented, operational, and currently blocked. Update this document after major milestones or when the daily workflow changes.

## Executive summary

TraderVI is an advisory-mode TSX momentum-rotation system. The data collector (Hermes) is operational again and current market data is present through 2026-08-18. The recommendation pipeline (Delphi) has not produced a recorded daily pick since 2026-06-06 and is the next workflow to validate deliberately.

The repository contains a coherent 30-commit June/August development sequence. It adds multi-lens ranking, more Granville indicators, relative-strength ranking, ghost-mode trade logging, historical sector data, per-symbol On-Balance Volume (OBV), and market-wide Climax (CLX) reporting.

## Verified development baseline

- Active branch: `master`, with upstream `origin/master` configured.
- SDK: .NET 10 (`10.0.400` verified on 2026-08-18).
- Complete solution build: successful with Visual Studio 2026 Insiders MSBuild 18.10 and SSDT; `TraderDB.dacpac` was produced.
- Core tests: 12 passed, 0 failed, 0 skipped.
- Known dependency advisories remain; see build output before updating packages.

## Programs and responsibility

| Program | Current responsibility | Side effects |
|---|---|---|
| Hermes | Daily OHLCV ingestion and derived market-data maintenance | External TMX/Yahoo reads; writes multiple SQL tables |
| Hercules (`ML.Train`) | Trains enabled profit models and records experiments/models | CPU-intensive training; writes model artifacts and SQL registry rows |
| Delphi | Evaluates the universe through Continuation and Breakout lenses and emits reports | Reads models/data; rewrites daily picks, dossiers, narratives, and Granville logs for the evaluation date |
| TraderVI | Manual trade/position CLI in ghost mode | Writes simulated positions and trade logs; does not place live orders |
| Oracle | Optional LLM narration over deterministic decision dossiers | May call a configured LLM service and write narrative records |
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

## Known gaps and risks

1. **Delphi restart is unverified.** Its data is fresh, but its persisted output has not been refreshed since June.
2. **Historical relative-strength features are empty.** This blocks the documented RS-to-Hercules training path and Z-score backfill analysis.
3. **Database project target mismatch.** `TraderDB.sqlproj` targets SQL Server 2025 (`Sql170`) while the local database engine is SQL Server 2019.
4. **Full SSDT publish is unsafe today.** A reviewed deployment plan proposed unrelated table rebuilds, constraint/index drops, and database-option changes. Use narrowly reviewed schema scripts until drift is reconciled.
5. **Model registry hygiene.** Retired experiments remain enabled in SQL even though runtime filtering prevents them from loading.
6. **No CI baseline.** Builds and tests are local only.
7. **Outcome feedback loop is incomplete.** Picks and market forecasts exist, but routine realized-outcome evaluation is not yet established.

## Immediate direction

Stabilize the existing daily advisory loop before adding indicators or automation:

1. Reconcile instructions and documentation.
2. Establish safe schema/version management.
3. Review Delphi side effects and add or confirm a controlled dry-run path.
4. Run Delphi, inspect both reports, and resume paper-trade outcome collection.
5. Add CI and only then resume feature/backfill work from `Docs/roadmap.md`.
