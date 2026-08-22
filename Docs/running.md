# Running TraderVI

TraderVI currently operates in advisory and ghost-execution modes. None of these commands should be used as routine code-change validation: several call external services, write SQL Server, train models, or create model artifacts.

Read `Docs/project-status.md` before restarting a workflow after a long pause.

## Safe validation commands

Core tests:

```powershell
dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --verbosity minimal
```

Focused project build after restore:

```powershell
dotnet build <project.csproj> --no-restore
```

The complete solution must be built with installed Visual Studio MSBuild plus SSDT because it contains `TraderDB/TraderDB.sqlproj`. A successful SQL-project build produces a DACPAC; it does not deploy the database.

## Database prerequisites

Runtime repositories connect to the local `TraderDB` SQL Server database. Before running a producer or evaluator:

1. Confirm required tables exist.
2. Confirm the backup requirements in `Docs/database-operations.md` are satisfied.
3. Apply only an explicitly authorized dated script from `TraderDB/Migrations`.
4. Verify the changed object and preserved data after execution.

Never publish the DACPAC. `TraderDB.sqlproj` targets SQL Server 2019 for build-time schema validation and blocks Deploy targets; it is not a live deployment mechanism.

After a successful Hermes run, create and verify a full database backup using `TraderDB/Operations/Backup-TraderDB.sql`, then copy the completed file to the approved off-machine destination. The initial workflow is manual until storage and retention have been observed.

## Hermes — market-data collection and maintenance

**Project:** `Hermes`
**Entry point:** `Hermes/Program.cs`
**Typical schedule:** after market close

Hermes currently:

1. Loads the TSX symbol universe.
2. Downloads missing historical daily bars from TMX and upserts `DailyBars`.
3. Incrementally updates `AdvanceDeclineLine`.
4. Incrementally updates and prunes per-symbol `SymbolObv` history.
5. Computes the current market-wide `MarketClimax` record.
6. Backfills/updates TSX sector-index history.
7. Refreshes stock-sector mappings when stale.
8. Updates market-leadership data.
9. Updates US index history from the configured external source.

Running Hermes performs external HTTP requests and writes multiple SQL tables. Obtain explicit authorization and review schema/data prerequisites first.

```powershell
dotnet run --project Hermes
```

One-time A/D, OBV, and CLX backfills are separate operations; use only the documented Sandbox probe or explicitly enabled backfill path after reviewing its scope.

## Hercules — model training

**Project:** `ML.Train`
**Entry point:** `ML.Train/Program.cs`
**Typical schedule:** deliberate weekly/on-demand retraining, not every run

Hercules:

1. Loads equity histories and the XIU benchmark.
2. Prints an informational presence report for deterministic pattern detectors.
3. Trains only profit models enabled in `ProfitModelRegistry`.
4. Writes model artifacts.
5. Records experiment metrics and model metadata in SQL.

Pattern detectors are rule-based and are not trained or stored in `ModelRegistry`.

Running Hercules is consequential: it is CPU-intensive and mutates both model artifacts and SQL state.

```powershell
dotnet run --project ML.Train
```

Do not retrain until the intended data cutoff, enabled model set, output paths, and registration behavior have been reviewed.

## Delphi — advisory recommendations

**Project:** `Delphi`
**Entry point:** `Delphi/Program.cs`
**Typical schedule:** before market open, using the most recently completed daily bars

Delphi currently:

1. Loads the active strategy version and currently allowed profit models.
2. Creates deterministic pattern signals from the code registry.
3. Computes XIU/SPY regime and A/D breadth.
4. Evaluates Granville groups #1–#20 and #25–#28.
5. Loads the equity universe, applies liquidity/ETP filters, and computes live relative strength.
6. Loads per-symbol OBV field trends and recent CLX history.
7. Evaluates two ranking lenses:
   - Continuation: executed recommendation lens.
   - Breakout: journaled comparison lens.
8. Writes daily picks, decision dossiers, and Granville diagnostics for later analysis.
9. Prints machine-oriented diagnostics and a human summary.

Delphi is advisory—it does not place a broker order—but it is not read-only. Review or add a dry-run/no-write path before using it as a harmless smoke test.

```powershell
dotnet run --project Delphi
```

## TraderVI — manual ghost execution

**Project:** `TraderVI`
**Entry point:** `TraderVI/Program.cs`

TraderVI is a manual CLI for simulated trade and position bookkeeping. Ghost mode records trades and positions but does not submit live broker orders.

```powershell
dotnet run --project TraderVI -- list
dotnet run --project TraderVI -- pnl
dotnet run --project TraderVI -- buy SYMBOL SHARES PRICE "notes"
dotnet run --project TraderVI -- sell SYMBOL PRICE "notes"
dotnet run --project TraderVI -- scan
```

Even in ghost mode, `buy` and `sell` mutate SQL trading records. `scan` loads market data and models. Obtain explicit authorization before using mutating commands.

## Oracle — LLM narration

**Project:** `Oracle`
**Entry point:** `Oracle/Program.cs`

Oracle consumes deterministic decision dossiers and produces optional narrative analysis. Depending on configuration, it may call an external LLM service and write `LlmNarrative` rows.

Review `Docs/oracle-rules.md` and `Docs/oracle-phases.md`, confirm provider configuration and token/data handling, and obtain explicit authorization before running it.

## Sandbox — probes and backfills

List probes without running one:

```powershell
dotnet run --project Sandbox
```

Run a selected probe:

```powershell
dotnet run --project Sandbox -- <slug>
```

Each probe has its own external and database effects. Read `Sandbox/AGENTS.md` and the selected probe's summary before execution. Backfill probes must be treated as database maintenance, not test fixtures.

## Operational troubleshooting

### Missing SQL object

A DACPAC build does not update SQL Server. Confirm the runtime database, add a narrow dated migration and matching canonical schema definition, build the project, execute only the explicitly authorized migration, and verify the object afterward.

### Multiple enabled registry rows

`ModelRegistry` should have no more than one enabled row per `TaskType`. Delphi additionally filters registry rows against the currently enabled code registry, so historical retired task types may remain enabled without loading; they are still cleanup debt.

### ML.NET schema mismatch

Feature vectors must have fixed size. Training and inference must use matching schema definitions and feature builders.

### Sector or benchmark gaps

- Check `SectorIndices` coverage before debugging relative-strength calculations.
- Verify stock-sector mappings and the `TsxSectorMap` normalization aliases.
- Verify external benchmark symbols still resolve before changing indicator logic.

### OBV or CLX unavailable

- Confirm `SymbolObv` has adequate per-symbol history.
- Run the OBV backfill only when explicitly authorized.
- Confirm `MarketClimax` exists and Hermes has written at least one row.
- CLX is diagnostic-only; missing history should degrade reporting rather than alter ranking.
