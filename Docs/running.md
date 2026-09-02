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

After a successful Hermes data update, Hermes automatically creates and verifies a full database backup, then copies it to the approved OneDrive destination with SHA-256 verification. `TraderDB/Operations/Backup-TraderDB.sql` remains the manual fallback and the pre-migration backup tool.

## DataAudit — read-only local data-quality scan

**Project:** `DataAudit`
**Typical schedule:** after a successful Hermes run, weekly or whenever universe quality is in question

DataAudit scans every local symbol and the core `DailyBars` / `StockSectorMap` / `SectorIndices` relationships. It detects stale active symbols using completed XIU sessions, missing mappings, likely stock/fund misclassifications, malformed or duplicate bars, and orphaned data.

It performs no external calls and no database writes. Findings about current listings or security type are candidates for official-source review, not automatic corrections. See `Docs/data-audit.md` for the checks, thresholds, and exit codes.

```powershell
dotnet run --project DataAudit
```

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
10. After the data-update stages return successfully, creates and verifies a compressed checksum backup in `C:\ProgramData\TraderVI\Backups`, copies it to OneDrive, and compares SHA-256 hashes.

Running Hermes performs external HTTP requests and writes multiple SQL tables. Obtain explicit authorization and review schema/data prerequisites first.

The backup behavior is part of Hermes itself, so it applies whether Hermes starts from Visual Studio or `dotnet run`. By default, the destination resolves to `$env:OneDrive\Joseph\Tradervi\backups`. Override the existing directories with `TRADERVI_BACKUP_STAGING_DIRECTORY` and `TRADERVI_BACKUP_DESTINATION_DIRECTORY` when needed. Hermes never creates or cleans these directories and never overwrites a backup generation.

If the data update completes but backup creation, verification, or copying fails, Hermes prints a prominent warning and exits with code `2`. The updated database remains intact, and any completed staging backup is preserved for diagnosis or manual copying. A successful copy still requires the OneDrive client to finish cloud synchronization.

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
**Shared workflow:** `Core/Runtime/DelphiWorkflow.cs`
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

Delphi is advisory—it does not place a broker order—but it is not read-only. Do not use it as a harmless smoke test; use focused builds and tests for routine validation. A no-write mode can be added later if an operational reporting need emerges.

Delphi records `DailyPick.PickDate` and Granville `EvalDate` using the recommendation run date so their date-scoped records remain linked. Its reports separately show the latest completed TSX session as the market-data-as-of date. A weekend run can therefore produce a weekend recommendation date based on Friday's completed market data; this is intentional audit semantics, not a claim that Saturday was a trading session.

```powershell
dotnet run --project Delphi
```

## TraderVI — ghost execution and headless paper monitor

**Project:** `TraderVI`
**Entry point:** `TraderVI/Program.cs`

TraderVI is a CLI for simulated trade and position bookkeeping. Ghost mode records trades and positions but does not submit live broker orders. Its `paper-monitor` command uses the same durable monitor as the WPF dashboard: each cycle records TMX poll receipts and completed evidence, evaluates the 15-minute policy, and records an authorized policy exit at a separately observed delayed price. Pass `--advisory-only` to suppress automatic ghost exits.

For each completed policy bar, the monitor also reads the existing immutable calibration ledger and selects the newest valid `OfficialPaper` run that both started after the position entry and was durably created before that bar began. Only a same-run published Breakout with probability at least 60%, direction edge at least 10%, and down probability below 35% can qualify the paper-only 10% loss exception. Missing/unpublished evidence or a read failure defaults to the ordinary 10% exit, and no Delphi evidence can bypass the absolute 20% exit.

```powershell
dotnet run --project TraderVI -- list
dotnet run --project TraderVI -- pnl
dotnet run --project TraderVI -- buy SYMBOL SHARES PRICE "notes"
dotnet run --project TraderVI -- sell SYMBOL PRICE "notes"
dotnet run --project TraderVI -- scan
dotnet run --project TraderVI -- paper-monitor
dotnet run --project TraderVI -- paper-monitor watch
dotnet run --project TraderVI -- paper-monitor watch --advisory-only
dotnet run --project TraderVI -- paper-add EDR Continuation 5 15.34
```

Even in ghost mode, `buy`, `sell`, and `paper-monitor` mutate SQL records. `paper-monitor` also calls TMX. `scan` loads market data and models. Obtain explicit authorization before using mutating commands.

## TraderVI.WPF — combined Ghost/Real trading dashboard

**Project:** `TraderVI.WPF`
**Startup window:** `TraderVI.WPF/PaperDashboardWindow.xaml`

The WPF app is the tabbed interactive TraderVI shell. Its Tracked positions area shows open Delphi-linked positions only; completed lifecycles remain available in Trade history. The tab also shows separate Ghost/Real realized and unrealized P/L plus durable poll receipts. Rows use both an icon and a `GHOST`/`REAL` label. It refreshes SQL history every thirty seconds. During the Toronto regular monitoring window it runs once on startup and then on the 15-minute schedule after each completed policy bar. Outside that window it is history-only and makes no TMX request.

The Data Audit tab calls the same host-neutral `MarketDataAuditWorkflow` as the retained DataAudit console application. It runs only when its clearly labelled button is pressed, uses local SQL reads only, and makes no correction or external call.

The Delphi tab calls the same host-neutral `DelphiWorkflow` as the retained Delphi console application. Opening or refreshing the tab only reads the latest saved Continuation and Breakout picks and their matching saved presentation evidence. Its inner views are Overview, Picks, Market, Granville, Diagnostics, and Full Report. New official runs reopen from a typed immutable snapshot stored inside the existing calibration run context. Runs from before ADR-0035 show a clearly labelled, date-aligned reconstruction; missing facts remain unavailable rather than being replaced with newer values.

The Picks view can create a monitored Ghost position or record a Real position
from a selected saved Continuation or Breakout recommendation. The operator
enters positive whole shares and the actual fill, chooses the mode, supplies an
account label for Real, and confirms the lens, rank, recommendation date, and
book cost. `Real` means the operator says that fill already occurred; TraderVI
does not submit or verify it. The shared `PaperTradeEntryWorkflow` preserves the
exact `PickId`, rejects duplicate active symbols, and never calls a broker or
invents a fill. Breakout selections are explicitly labelled exploratory.

Migration `20260827_013_AddTrackedExecutionMode.sql` was manually applied and
verified on 2026-08-28 after a checksum-verified full backup and hash-matched
secondary copy. All legacy rows were deliberately classified Ghost. An active
Ghost row can be marked Real only through the confirmed reconciliation control,
which writes an immutable audit event. The five-share EDR Ghost mirror is no
longer active: it was automatically paper-sold at $15.62 under
`Policy TrailingProfit`. If the broker holding is still open, create a separate
operator-confirmed `REAL / TFSA` entry using the actual five-share, $15.34 fill;
do not convert or reopen the completed Ghost lifecycle.

The monitor evaluates both modes. Automatic exits are hard-guarded to Ghost.
A Real exit alert remains a manual-action signal until the operator records the
actual all-shares broker sell fill; that control changes only TraderVI's ledger
and never sends an order.

The Scorecards tab is a read-only view of the advanced official Delphi report:
coverage/readiness, model probability metrics, reliability, deciles,
Continuation/Breakout rank performance, and diagnostic slices. It uses the
same official evidence query and pure calculator as Athena, writes nothing, and
does not require CSV export. Refreshing it cannot mature outcomes or change
Delphi. Migration `20260828_014_SeedCalibrationOutcomeDefinitions.sql` was
applied on 2026-08-28 to initialize four contracts, and migration
`20260901_015_AddDelayedIntradayOutcomeDefinition.sql` was operator-applied and
verified active on 2026-09-01 to add the fifth. The latest verified Athena run
wrote 112 valid three-session mark outcomes and 112 valid excursion outcomes;
the two prediction definitions and delayed-intraday definition still have zero
outcomes, so their performance sections remain correctly unavailable.

The Project Docs tab discovers Markdown throughout the repository except `.git`, `.vs`, `bin`, `obj`, `packages`, and `node_modules`. It groups documents by folder, searches title/path/content, opens `Docs/project-status.md` by default, and reloads external edits with Refresh. Relative Markdown links and heading fragments navigate inside the tab only after safe repository resolution. Clicking an HTTP(S) link opens the system browser; merely loading, searching, or refreshing documentation never opens a web page. The reader does not write files or access SQL, models, or market services.

`Run official Delphi` first shows a confirmation describing the operation: it reads local market data and registered model files, appends immutable calibration evidence, and replaces same-date operational picks and supporting records. It does not place a broker order or create a paper position. Do not confirm it merely to test the interface; use focused builds and tests instead.

```powershell
dotnet run --project TraderVI.WPF
```

Keep the app open for future polling. Closing it, signing out, sleeping, or restarting the computer stops the in-process schedule. Version 1 does not install a Windows service or background task. The app has manual ledger-reconciliation controls but no broker connection and cannot place a real order; automatic actions are database-only Ghost exits.

## Athena — calibration outcome evaluation

**Project:** `Athena`
**Entry point:** `Athena/Program.cs`

Athena reads immutable official calibration candidates and local `DailyBars`, reproduces the enabled production labelers, and idempotently writes matured 10-session labels, 20-session price paths, three-session marks/excursions, and delayed-intraday outcomes. It prints coverage first: distinct completed-market-session cohorts, official run and candidate counts, valid/degraded/invalid/pending outcomes, completion coverage, usable coverage, and whether the 95% reporting floor permits a primary descriptive score. ADR-0038 adds official-only probability calibration, eligible-lens rank quality, and descriptive technical/market slices. All metrics use nested candidate/run/market-session weighting so a deliberate Delphi rerun cannot inflate independent evidence. The report never changes a model, weight, gate, lens, or trading policy.

For `DelayedIntradaySwing`, Athena replays only a continuous first-received 15-minute path. A later bar that proves a missing bar/session, a receipt-order conflict, or a later five-minute bar that proves the exact symbol/XIU fill bar is missing produces an audited invalid outcome. If no later evidence yet proves a gap, the candidate stays pending. An alert during the session uses the exact next five-minute boundary; an after-close alert waits for the next observed regular-session open. Athena never substitutes a later convenient price.

The 2026-09-01 verified run wrote 112 valid `SwingMarkToMarket3` and 112 valid `SwingExcursion3` outcomes. It wrote zero `PredictionLabels10`, `PredictionPath20`, or `DelayedIntradaySwing` outcomes. Durable SGY/XIU evidence still ended on 2026-08-28, so launching WPF without a newer receipt does not make delayed outcomes ready.

Athena makes no external requests. It is still a database writer because it creates missing definitions and matured outcomes: apply the reviewed calibration migration manually first and obtain explicit authorization before running it. The optional CSV switch writes five export-schema-v1 artifacts and refuses to overwrite an existing filename.

```powershell
dotnet run --project Athena
dotnet run --project Athena -- --scorecard-csv C:\path\to\an-empty-export-directory
```

Athena is manual in the initial release and is not launched by Hermes or Delphi.

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
