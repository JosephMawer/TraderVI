# Running TraderVI

## Market-order client reference

`Core/WSTrade/Orders` and `WealthsimpleAuthClient.MarketBuys.cs` implement a whole-share market-buy
request and separate fill-status read from the operator's captured browser transaction. The WPF manual
ticket is now wired through the protected workflow in [ADR-0075](adr/0075-manual-market-buy-ticket.md).
Promotion activation and automatic engines remain separate. Market buys have no limit-price cap.
Do not call submission as a connectivity probe or replay a HAR.

The client also provides `WealthsimpleMarketSell`, `SubmitMarketSellAsync` and
`ReadMarketSellOrderAsync` under [ADR-0076](adr/0076-wealthsimple-market-sell-protocol.md).
It submits whole-share `SELL_QUANTITY` / `MARKET` / `DAY` orders and reads cumulative actual fills.
Buy and sell share the durable attempt fence; never change side or generate a replacement request ID
to recover an uncertain order. Sell checks `sellable` separately from `buyable`.
The WPF holdings workflow now calls it through the protected coordinator in
[ADR-0077](adr/0077-manual-holdings-market-sell.md). Native sell acceptance remains unverified.

## Manual sells from Wealthsimple holdings

After rebuilding/restarting WPF, open **Trading → Main account → Your Wealthsimple holdings**.
Each supported CAD equity row has **Market sell all** beside its share count. **One click sells the
entire displayed whole-share holding**, following fresh broker checks; there is no quantity ticket
or second confirmation. Market execution may differ from the displayed value or checked bid.

If the quantity changed, refresh holdings and click again for the new displayed quantity. Fractional
holdings require Wealthsimple. The app requires a fresh regular-session quote and no pending broker
or unresolved local order; after-hours clicks do not queue sales. Buying pauses and cash shortages
do not prevent an otherwise eligible manual sell.

The result window shows actual fills and proceeds when available. **Manual sells…** reopens saved
history; **Check sell status** reads the original order and remaining holdings. An uncertain order or
lagging holdings update keeps new manual orders blocked. Do not remove saved records or attempt
markers to retry. Closing the results window does not cancel the order. Sell records share the
Windows-encrypted `ManualOrders` directory with buys and remain available after restart.

**Limit sell…** opens a quantity/minimum-price preview. Its submission button is disabled; limit
placement still needs a verified broker contract. Use Wealthsimple to place a limit sell today.
Manual sales do not close SQL tracked positions or activate automatic strategy exits.

## Manual market buys in WPF

After rebuilding/restarting WPF, select a stock in either **Delphi → Picks** grid and use the single
**Manual buy…** action below the grids. To add to an existing broker position, select it in
**Trading → Main account → Your Wealthsimple holdings** and use **Buy more…** below that grid.
There are no per-row Buy buttons; selection alone never submits. Sign in if necessary,
select the verified self-directed TFSA if more than one exists, and enter the exact whole-share
quantity. **Refresh checks** loads the reference ask, estimate, cash/buying power, holdings and orders.
The explicit **Buy N SYMBOL at market** button sends a real order. Quantity starts blank; ±10 is optional.

The initial version requires an open regular broker session, a two-sided quote no older than two
minutes, account checks no older than thirty seconds, sufficient estimated CAD cash/buying power,
and no pending broker or unresolved local order. Applicable global/Trading pauses and TFSA financial
holds block submission. A price increase during final checks returns an updated review for another
explicit click; estimates never guarantee the fill cost.

**Manual orders…** is available beside Delphi's selected-stock action and in Trading's broker panel.
It reopens the saved order and reads cumulative broker results with **Check order status**. After an
uncertain submission, retain that order; a missing result does not permit replacement. Closing the
window does not cancel it. Filled/partial shares and actual costs are broker observations. Manual buys
do not activate portfolios, assign automatic selling or insert a tracked position automatically.
**Record an existing fill…** and **Create Ghost position…** retain their separate legacy workflows.

Full intents/results are Windows CurrentUser encrypted under
`%LOCALAPPDATA%\TraderVI\Wealthsimple\ManualOrders`; the existing `OrderAttempts` folder holds the
transmission fences. Do not remove either to retry a trade. Corruption or contradictory observations
requires recovery/reconciliation and blocks new buys. These local records are not a cross-device or
complete-account transaction ledger. Native live submission still requires the operator's deliberate
first use; offline tests do not prove broker acceptance.

For the supplied reference, open the ignored local
`artifacts/wealthsimple-market-buy-20260910/README.md` index. Each GraphQL exchange has separate
request/response JSON files; stable redacted IDs preserve relationships. Financial values remain private.
The original HAR retains every byte. See [ADR-0074](adr/0074-wealthsimple-market-buy-protocol.md) for
acknowledgement/fill semantics, attempt markers and remaining integration work.

## TFSA strategy transition preview

After native Wealthsimple sign-in, open **Trading → Main account**. Refresh reads balances, holdings
and paginated pending orders. **Review TFSA strategy transition** ranks Delphi Live paper sources on
their latest common saved day, with partial results labelled. Choose a source, enter a purchase ceiling
and select **Refresh account and prepare review** to see the exact strategy version and proposed
retained/exit/await-entry holdings. The ceiling defaults to zero and never includes anticipated sale
proceeds. Paper-held symbols must resolve to unique CAD TSX/TSXV broker instruments before an
overlap is accepted. Fixed-gate comparison profiles remain unsupported by real strategy approval.

This screen is a preview: it does not activate a strategy, change SQL records or send orders. A green
Wealthsimple connection confirms authentication, not execution readiness. Completed-fill reconciliation,
durable order tracking, verified limit submission/cancellation and real Delphi Live monitoring remain
unfinished. Existing recorded-account recommendation approval continues to use its existing gates.

## Wealthsimple authentication prototype

The isolated `Tools/Wealthsimple.Auth` console host can verify a user-completed browser session
from a local HAR. It does not start the trading app or connect its dashboards. See
[authentication commands and limits](concepts/wealthsimple-authentication.md). A successful direct
token-info check is verified. WPF's **Wealthsimple** sidebar button now opens native email/password
inputs and prompts for 2FA when required. Browser verification is a conditional fallback, and saved
sessions use optional Windows encryption. Native login is operator-confirmed; live refresh rotation
still needs verification. Green denotes authentication only, not trading readiness.

On **Trading → Main account**, the **TFSA** page loads the self-directed broker account after sign-in.
Use **Refresh Wealthsimple** for a new snapshot. Multiple eligible self-directed TFSAs require a choice;
managed accounts are excluded. The lower expander contains TraderVI records and the active strategy.
Broker reads do not update SQL reports, recorded fills or recommendation inputs. See [ADR-0072](adr/0072-wealthsimple-tfsa-account-display.md).

## Historical Practice

The **Practice** tab opens a saved `frames.ndjson` for labelled playback with a simulated Toronto clock,
Play/Pause, Step, speed controls, daily picks and four independent accounts. Generate a run with the
dedicated `Practice` console project. Acquisition is explicit and separate; `run` is offline and never
publishes operational results. See [input preparation and commands](concepts/historical-practice-implementation.md).
The first trial defaults to August 10–September 4, 2026, CAD 1,000 per account, and accepts missing data.
No actual starter dataset acquisition/replay ran during implementation.

## Central Settings

In the rebuilt app, open the top-level **Settings** tab. Delphi, Delphi Live, Trading and Portfolios have
shortcuts to the same relevant editor. General & operations contains the local Ghost-exit preference;
system sections contain their supported strategy fields; Portfolios & accounts lists existing targets.

Choose **Pause new buys** on the system page or Settings. Use each holding's **Sell** action to request a
simulated close, or **Record sale** for an actual completed Real fill. Requests retain their audit records
and wait for eligible market evidence. Exits continue while paused. No broker order is sent.

When the affected family has zero holdings and pending orders, its strategy fields unlock. Select a saved
version or template, edit related sections, name the draft and enter a reason, then **Review and save
version**. Navigation retains the combined draft in memory. Saving changes no assignment. Select the saved
version and target, then **Review assignment**. Assignment remains paused; choose **Resume buys** separately.
Existing account risk holds remain in force. If reevaluation fails, the assignment stays active and the
failure is reported for retry.

Daily Delphi selects complete preserved four-model sets and eight gates; it does not mix arbitrary
individual candidates. Assign also starts an official local-data run. Live and Shadow use deterministic
policy editors, not the daily ML-model selector. Trading limits belong to the strategy; financial facts
belong to accounts. The initial editor does not expose fixed calendar, evidence or Shadow lens/slot
contracts as tunable controls. Shared Daily changes require all dependent families paused and empty;
Live/Shadow entries wait for compatible frozen daily picks in a fresh session after a daily source change.
Manual Live assignment or operator intervention pauses automatic research promotion.

Before operational rollout, obtain authorization, create/verify a full backup, close old hosts and apply
`TraderDB/Migrations/20260907_027_AddCentralSettings.sql` manually with SQLCMD error stopping. Old hosts
do not participate in the new settings fence. Building a DACPAC does not apply this migration. Without
028, the revised trading-rule editors and new controls are read-only. Apply
`TraderDB/Migrations/20260907_028_AddTradingControls.sql` after 027 with a fresh verified backup and SQLCMD
error stopping; close older hosts before rollout. The migration creates no pauses, requests or assignments.
Migration 028 was applied with a fresh verified backup and preservation checks on 2026-09-07; do not
reapply it. The updated Debug and Release desktop outputs are ready for the next app launch.
Migration 027 was applied with a verified backup on 2026-09-07; do not reapply it. The updated Release
desktop app is installed in the build output and was launched successfully. Settings is in the main tab
bar between Scorecards and Project Docs. No settings assignment was seeded during rollout.
See [ADR-0060](adr/0060-central-settings-and-scoped-configuration.md), the
[system map](concepts/settings-system-map.md) and the
[implementation and rollout review](reviews/central-settings-implementation-20260907.md).

TraderVI currently operates in advisory and ghost-execution modes. None of these commands should be used as routine code-change validation: several call external services, write SQL Server, train models, or create model artifacts.

Read `Docs/project-status.md` before restarting a workflow after a long pause.

[ADR-0055](adr/0055-independent-strategies-to-approved-live-execution.md) records the path from independent
paper strategies to approved real-account recommendations and future Wealthsimple execution. The desktop
now implements manual recommendation promotion under [ADR-0062](adr/0062-complete-strategy-recommendation-promotion.md),
as described below. Delphi Live's separate research promotion control changes its own paper policy only.
Hercules saves replacement model candidates without selecting them. Training still writes private artifacts
and SQL and needs explicit authorization; model selection is a separate reviewed operation under
[ADR-0057](adr/0057-preserved-model-sets-and-explicit-selection.md). Strategy-directed broker execution is not implemented; the separate manual-buy ticket does not activate a strategy.

## Manual strategy promotion to Trading

Migration `20260907_029_AddTradingPromotion.sql` was applied on September 7 after a verified backup and
preservation checks; do not reapply it. The updated Debug and Release desktop outputs are ready for the
next launch. Rollout did not launch the app or approve any strategy.

1. In **Trading → Recommendations**, load the Real account. Reconcile its actual holdings in **Tracked
   holdings**, then use **Report current account state** to confirm current CAD cash, account value,
   session opening value, highest closing value and outstanding orders. These must come from the actual
   account. Report again each session and after a fill or external cash change.
2. Satisfy the existing configuration requirements: **Pause new buys** and no holdings or pending orders
   in the affected tracked family. Promotion also requires the target Real account to have no holdings
   or outstanding orders. Record sales only when they have actually happened; approval does not close holdings.
3. In **Portfolios**, select the supporting paper account and review its complete strategy for Trading.
   Inspect the target account, exact strategy/model/policy versions, evidence limitations, previous source
   and any operational blockers. Performance evidence is advisory for this manual decision.
4. Enter your reason and choose **Approve this strategy for Trading**. The approval records you as reviewer
   and takes effect when its transaction commits. Paper holdings and histories stay independent. Return to
   **Trading → Recommendations** to see the approved source and subsequent results; **Resume buys** separately
   when ready. New entries require current reported account facts and compatible frozen daily inputs.
5. Report actual purchases and sales separately after they occur. A recommendation sends no broker order.
   To switch back, select a prior approval in the history and **Review selected strategy again…**; the same
   prerequisites apply and a new approval preserves the previous history.

At rollout, all four Shadow accounts produced valid reviews. TFSA still has reported holdings, no current
account-state report, and Trading buys are not paused. Delphi Live has no initialized paper ledger yet.
These are remaining operator prerequisites, not evidence-performance thresholds. The reviewed calendar
loads; actual approved evaluation and live source capacity still require operational observation.

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

## Guarded nightly pipeline

ADR-0046, as corrected by ADR-0058, schedules one deterministic pipeline at 00:30 Toronto/Eastern every calendar day:
Hermes, then Delphi, then Athena. Running after midnight gives Delphi the new recommendation date while it
uses the prior completed TSX session. This schedule is recurring authorization for these exact three
programs and their documented effects; it grants no authority to train models, apply migrations, deploy a
database, run Sandbox probes, start WPF monitoring, invoke Oracle, or place a broker order.

Install the Windows task once, or rerun the installer when changing its schedule or task settings:

```powershell
./Operations/Install-TraderVINightlyTask.ps1
```

The task invokes the repository runner and configuration directly. At every scheduled start, the runner
fingerprints the current tracked and untracked non-ignored source, builds the three focused projects in
Release with no restore and no incremental compilation, verifies that the source did not change during the
build, and hashes each resulting output directory. Each stage's output is verified again immediately before
execution. Therefore source and runner edits apply on the next run without reinstalling the task. A build
failure or concurrent source edit stops before Hermes, Delphi, or Athena starts. Dependency restores remain
a separate reviewed operation; run an appropriate `dotnet restore` before night when package references or
restore inputs change.

The task uses the current user's interactive token, stores no Windows password, wakes the computer, and
starts when available; the user must remain signed in.

Read the latest status without starting any operational program:

```powershell
./Operations/Get-TraderVINightlyStatus.ps1 -IncludeLogTail
```

The runner writes atomic `status.json` state and per-run logs beneath
`%LOCALAPPDATA%\TraderVI\Nightly`. `Succeeded` means the current source built and all three stages passed.
`Attention` includes Athena exit code 2 or a Delphi run degraded by an actual evidence limitation; a dirty
working tree by itself remains recorded provenance and does not change the run audit state. `Failed` includes build failures,
source changes during preflight, post-build artifact changes, timeouts, Hermes failures, invalid Delphi
provenance, or any unexpected exit code. Hermes failure stops later stages; Delphi failure
does not prevent Athena from maturing earlier evidence. Neither the runner nor Task Scheduler retries a
failed pipeline automatically. A same-date completed run is suppressed unless an operator explicitly uses
`-Force` after reviewing it.

The daily 07:00 Codex supervisor is read-only. It may inspect `status.json` and its referenced log, explain a
problem, and recommend the next safe action. It must not launch or retry a program, modify SQL, call a market
service, or repair data.

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
10. Collects actual SPY daily-chart history into `DailyBars`, validates required benchmark coverage and appends missing observations without rewriting prior SPY rows. A failed required SPY refresh stops with an error.
11. After the data-update stages return successfully, creates and verifies a compressed checksum backup in `C:\ProgramData\TraderVI\Backups`, copies it to OneDrive, and compares SHA-256 hashes.

Running Hermes performs external HTTP requests and writes multiple SQL tables. Obtain explicit authorization and review schema/data prerequisites first.

Migration 017 was applied and verified on 2026-09-02. It introduced the nullable leadership contract and
`v3.2-leadership-missingness` identity; the current v3.4 strategy preserves that contract. The first authorized post-migration Hermes run also
passed: its 2026-09-01 row preserved unavailable movers breadth as null, its leadership constraint remained
enabled/trusted, and its checksum-verified staging and OneDrive backup copies hash-matched. Later Hermes
runs still require explicit authorization. A deliberate official Delphi cohort may now start when wanted;
Athena does not collect or repair leadership data.

Migration 018 was manually applied and verified on 2026-09-03 after a fresh checksum-verified full backup
and hash-matched OneDrive copy. The two Leadership value checks and `FK_Quotes_Symbols` are enabled and
trusted, with zero invalid Leadership rows and zero Quote orphans. No DACPAC was deployed.

The backup behavior is part of Hermes itself, so it applies whether Hermes starts from Visual Studio or `dotnet run`. By default, the destination resolves to `$env:OneDrive\Joseph\Tradervi\backups`. Override the existing directories with `TRADERVI_BACKUP_STAGING_DIRECTORY` and `TRADERVI_BACKUP_DESTINATION_DIRECTORY` when needed. Hermes never creates or cleans these directories and never overwrites a backup generation.

If the data update completes but backup creation, verification, or copying fails, Hermes prints a prominent warning and exits with code `2`. The updated database remains intact, and any completed staging backup is preserved for diagnosis or manual copying. A successful copy still requires the OneDrive client to finish cloud synchronization.

```powershell
dotnet run --project Hermes
```

`dotnet run --project Hermes -- --spy-only` runs the same SPY maintenance and verified-backup path without
refreshing the TSX universe or other indicators. It still accesses the external source and writes SQL;
use only under explicit operational authority. SPY is not added to the TSX symbol universe or the Genuity
index list. A missing/stale shared benchmark is not interpreted as a positive signal.

One-time A/D, OBV, and CLX backfills are separate operations; use only the documented Sandbox probe or explicitly enabled backfill path after reviewing its scope.

## Hercules — model training

**Project:** `ML.Train`
**Entry point:** `ML.Train/Program.cs`
**Typical schedule:** deliberate weekly/on-demand retraining, not every run

Hercules:

1. Preserves the selected strategy's exact models and metadata for comparison and rollback.
2. Loads stored equity histories and XIU, retaining a checksummed private data snapshot and source archive.
3. Trains only profit tasks enabled in the code's `ProfitModelRegistry`, using the shared dated inputs.
4. Saves new files in a unique candidate directory and refuses to overwrite existing artifacts.
5. Records experiment metrics, disabled model rows and a completed manifest only after all tasks succeed.

Pattern detectors are rule-based and are not trained or stored in `ModelRegistry`.

Running Hercules is consequential: it is CPU-intensive and mutates both model artifacts and SQL state.

```powershell
dotnet run --project ML.Train
```

Do not retrain until the intended data cutoff, enabled model set, output paths, and registration behavior have been reviewed.

Candidate-only dated training is now the default; `--dated-candidates` remains an optional alias.
Storage defaults to `%LOCALAPPDATA%\TraderVI\Models`; `--output-root <directory>` can select another
reviewed private location. Signal thresholds are inherited from the predecessor; optimized suggestions
remain recorded metrics. Neither a successful training run nor a completed manifest changes selection.

## Model review and deliberate selection

**Project:** `Tools/ModelLifecycle`
**Selection script:** `Operations/Set-TraderVIModelSelection.ps1`

Under explicit operational authorization, `ModelLifecycle` can inspect a private registry export
(`inspect-registry`), verify a preserved assignment using artifact hashes and synthetic predictions
(`verify-set`), or prepare previous/corrected assignments from a completed candidate manifest
(`prepare-switch`). `verify-active` reads SQL and loads models through Delphi's actual bootstrap without
publishing recommendations. These commands do not launch the nightly pipeline or fetch market data.

The selection script defaults to `Prepare`, producing reviewable forward and rollback SQL. Its
`VerifyTransaction` and `VerifyRollbackTransaction` modes exercise the transitions and roll them back;
`Apply` commits reviewed selection and `Rollback` commits a separately authorized return to the retained
predecessor. The script verifies files and expected state and holds the nightly lock. Migration 026
provides immutable assignments and selection history. Registration changes the selected strategy, while
global model-enable flags and prior recommendations/account history remain unchanged.

The 2026-09-06 authorized [cutover review](reviews/model-input-cutover-20260906.md) records the completed
selection. Future training or selection is not authorized merely by these instructions.

ADR-0058 adds `ModelLifecycle verify-benchmarks` and `prepare-benchmark-switch <review-directory>`.
The latter requires the dated-input predecessor, validates current XIU/SPY inputs, preserves the same four
models and prepares a new source/strategy identity. The selection script's `-NewDecisionRef ADR-0058`
requires an unchanged predecessor model set; provide the reviewed `-NewVersionName` for every mode.
Default ADR-0056 selection remains supported for existing review records. None of these commands publishes
recommendations; preparation and verification do not select a strategy.

## Delphi — advisory recommendations

**Project:** `Delphi`
**Entry point:** `Delphi/Program.cs`
**Shared workflow:** `Core/Runtime/DelphiWorkflow.cs`
**Typical schedule:** before market open, using the most recently completed daily bars

Delphi resolves the active strategy's immutable model assignment from SQL. The nightly CLI and WPF use
the same shared workflow, so the selected `v3.4-observed-benchmarks` automatically receives the corrected
ADR-0056 model inputs and ADR-0058 observed XIU/SPY policy, with exact reviewed model files. The benchmark
policy validates the prior session against the reviewed TSX calendar and requires 200 valid observations
and a matching endpoint for both series before evaluation. A US-only holiday without a matching SPY row
therefore stops new publication with a reason; no stale-data tolerance or substitute index is assumed.
No per-host binding flag is needed. A corrected
strategy missing its assignment fails before evaluation. An optional `--model-input-binding` file cannot
override a stored assignment. Unbound legacy strategies retain explicitly unverified legacy inputs.

Delphi currently:

1. Loads the one active strategy version and its assigned profit models. A persisted official run
   also requires the strategy's explicit initial-code and decision identity (ADR-0042).
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

Migrations 016 and 017 introduced `v3.2-leadership-missingness` on 2026-09-02 without changing thresholds,
models, gates, ranking formulas or execution policy. On 2026-09-06 the authorized input correction selected
`v3.3-dated-profit-inputs` with four separately trained models and unchanged strategy/signal thresholds.
The later ADR-0058 repair selected v3.4 with the same four model files and thresholds, after the Friday/SPY
refresh. Earlier evidence retains its original identity. The next normal scheduled run uses v3.4; a full
pipeline was not launched to validate either switch. Delphi remains a consequential database writer and must not be
launched as routine validation.

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

### Fixed-gate comparison accounts

Migration 033 and the initial five comparison pairs are active from September 9; see
[ADR-0067](adr/0067-fixed-gate-paper-comparison-pairs.md). In **Portfolios → Compare**, select
**Gate comparison pairs** to view adjacent Normal and Portfolio rules only accounts for each of the
four Shadow strategies and Delphi Live. Each has independent CAD 1,000 starting cash. Select an account
to see its fixed profile, publication, funded start, fresh-evidence boundary and admitted candidate count.
The candidate monitor explains the remaining entry conditions; missing data remains a block.

Keep the WPF host running for scheduled shared Live and Shadow collection. Startup reads the existing
accounts; it does not create accounts or add capital. A newer valid publication under the pinned daily
strategy is adopted automatically by both members. Changing the active daily strategy pauses incompatible
new entries; it does not mutate a pair. Global new-buy pauses in Settings continue to apply, with held
protection retained. The original Shadow generation's controls do not target comparison selections.

Profiles and paired rules are fixed, and their names identify the pair. Shared Settings and recommendation
review cannot change or promote these accounts. A future experiment requires a separately reviewed fresh
pair. The initial operator-only activation is `PaperComparisonRepository.ActivateInitialAsync`; the
explicit helper and verification records are under ignored `artifacts/gate-comparisons-20260909/`.
Activation retries return the existing ten identities without resetting cash or history. Do not replay
the immutable migration against an already installed schema. Today's 13:25 source boundary and runtime
gaps remain partial-session evidence; neither missing returns nor missing opening bars are fabricated.

### Existing accounts and monitoring

The WPF app is the tabbed interactive TraderVI shell. Its Tracked positions area shows open Delphi-linked positions plus operator-reported Real holdings that were enrolled without a Delphi pick; completed lifecycles remain available in Trade history. Unlinked Real holdings are labelled as historical, begin durable monitoring at their enrollment time, and cannot receive the fresh-Delphi loss exception. Unlinked Ghost rows remain excluded. The tab also shows separate Ghost/Real realized and unrealized P/L plus durable poll receipts. Rows use both an icon and a `GHOST`/`REAL` label. It refreshes SQL history every thirty seconds. During the Toronto regular monitoring window it runs once on startup and then collects every five minutes from the first safe 09:47 poll through 16:02. The exit policy still consumes only completed 15-minute bars; intermediate collection ticks do not create five-minute policy decisions. Outside that window it is history-only and makes no TMX request.

The Portfolios tab adds ADR-0051's separately versioned Shadow V1 ledger. Enter the total TFSA value and
available cash, then explicitly confirm Start. Each of the four System portfolios receives the total value
as its own alternative starting cash; the available-cash field describes the real comparison and does not
cap those separate what-if accounts. A same-session Start establishes an activation baseline and waits for
fresh completed five-minute evidence. Pause blocks new buys, add-ons, re-entries, and rotations while risk
exits continue. A portfolio at `CapitalReviewRequired` needs an explicit Resume after review. Recording a
later real snapshot updates only the comparison; it never overwrites Shadow cash or returns. Portfolio
display names are editable, but stable identity and history are not. Selecting a System portfolio shows a
read-only candidate monitor with its frozen rank, current state, yesterday's close, prior/latest completed
five-minute closes, distance from yesterday, plain-language decision reason, and last evaluation time. The
overview freshness also advances from candidate evaluations before any holding exists. Migration
`20260904_019_AddSystemShadowPortfolioLedger.sql` was manually applied and verified on 2026-09-04 after a
checksum-verified, hash-matched full backup. Migration 020 later corrected the exact 2026-09-04 Delphi run
and cleared only its first empty Shadow attempt. The first completed session is retained as engineering
shakedown evidence: it is operationally useful but is excluded from strategy-performance conclusions.
Migration `20260904_021_HardenSystemShadowExecutionCausality.sql` was backed up, manually applied, and
verified on 2026-09-04. It additively stores the last consumed fifteen-minute bar identity while preserving
all prior ledger rows and totals; historical identities remain unknown rather than inferred. The controller
also cancels any pending buy whose exact next-bar fill window was missed and requires current five-minute
requalification; pending sells remain protective. Shadow has no Wealthsimple or other broker connection.

The same Portfolios overview now also reads Delphi Live's independent paper accounts, including queued
activations and ended comparison accounts. The row's selector, currency and status identify its scope.
Before the main account is created, a **Not activated** placeholder explains where to review activation;
it has no fabricated cash or returns. Selecting a saved Delphi Live account shows its candidate states,
holdings, decisions and fills, retaining estimated-fill labels. Account values use the latest saved exact
checkpoint matching its holdings and cash; incomplete or superseded marks remain unavailable.
Daily Shadow start/pause/resume/rename controls cannot operate on a Delphi Live selection. Manage live
activation and capital review in **Delphi Live → Setup & advanced**. Friday's historical replay remains
under **Delphi Live → Replay account** and is not an operational portfolio in this list.

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

Keep the app open for future polling. Closing it, signing out, sleeping, or restarting the computer stops the in-process schedule. Version 1 does not install a Windows service or background task. The tracked-position monitor has manual ledger-reconciliation controls; its automatic actions remain database-only Ghost exits. The separate manual-buy ticket can submit an explicitly reviewed broker order and does not give this monitor automatic broker authority.

## Delphi Live — explicit paper-monitor activation

The frozen design is [Delphi Live V1](concepts/delphi-live.md); source and validation status are in the
[implementation checklist](delphi-live-implementation-checklist.md). The WPF Delphi Live tab reads
status separately from activation. Migrations 022, 023, 024 and 025 were separately authorized and
applied in order on 2026-09-06 after a checksum-verified full backup and a hash-matched OneDrive copy
reported synced by Windows. Schema and preservation checks passed, and `DBCC CHECKDB` reported no
errors. Delphi Live remains inactive. Building the SQL project alone does not apply a migration.

The reviewed local calendar was installed on 2026-09-06. The current Windows user's
`TRADERVI_TSX_CALENDAR_PATH` points to
`C:\src\TraderVI\Operations\Calendars\tsx-2026-through-20261223-v1.json`.
It covers 247 full sessions within 2026-01-01 through 2026-12-23; see the
[calendar installation record](../Operations/Calendars/README.md). Restart an already-open Visual Studio
or other launcher before starting WPF so its child process receives the new variable.
The JSON has these required
fields (names are case-insensitive; unknown fields are rejected):

| Field | Contents |
|---|---|
| `version` | Immutable name for the reviewed snapshot |
| `sourceReference` | Official source reference and review provenance |
| `firstCoveredDate`, `lastCoveredDate` | Inclusive `yyyy-MM-dd` coverage, including non-session dates |
| `regularSessionDates` | Distinct `yyyy-MM-dd` dates of the regular 09:30–16:00 TSX sessions inside that coverage |

Include enough prior history for the frozen daily baselines and enough future coverage for five-session
outcomes and next-session assignments. The host uses `America/Toronto`, refuses dates outside coverage,
and never guesses weekdays or fetches a calendar on demand. The installed snapshot stops before
December 24's 13:00 close because V1 assumes a full 16:00 close. Short-session handling, including
protection of carried positions, must be reviewed before extending coverage. The next listed session
after the installation date is Tuesday, September 8; Monday, September 7 is Labour Day.

The main **Current watchlist** reads the latest valid saved Delphi picks while inactive, showing the
recommendation date and combined source ranks. Friday's 25 picks per lens overlap into 28 stocks;
published rows that fail entry gates remain visible. This preview does not start collection or make
Friday's source eligible for a later live session. Once saved live observations exist, the same table
shows them. Select a stock to see its checkpoint price, data quality and explanation.

Click **Replay Fri, Sep 4** to open the saved historical simulation. Move the time slider or use
Previous/Next; **Replay account** shows cash, marked holdings and estimated trades at that point in time.
The app automatically finds the latest supported report under `artifacts/delphi-live-replays` when run
from the repository. If none is found, **Open saved replay…** opens a local `*-report.json` file.
This file viewer makes no market requests. The estimates and limitations are recorded in
[ADR-0054](adr/0054-delphi-live-preview-and-historical-replay.md) and the
[replay review](reviews/delphi-live-friday-replay-20260906.md).

After separately authorizing activation, use **Setup & advanced** and enter a positive simulation
amount, currency and operator reason. There is no default live capital, broker cash lookup, deposit or withdrawal.
Activation queues a cash-only Operational Champion for the next regular-session boundary. Start WPF
sufficiently before 09:30 Toronto time to establish successful pre-open heartbeats, with the eligible
official daily run and daily baselines already available. Keep it open through completion of the final
16:02 collection and closing persistence. Its monitor is independent of the selected tab.
Closing or interrupting the host records a coverage gap; restarting protects existing holdings and
rebuilds ordinary confirmation from fresh observations without replaying missed actions.

The experiment controls record explicit reasons and future boundaries. Discovery requires ten clean
engineering sessions and starts fresh, equal-capital champion-control and challenger runs for one
predeclared threshold family. Untouched confirmation selects one contender after thirty matured paired
discovery cohorts and starts new aligned runs. A passing thirty-cohort untouched score permits human
promotion review; it does not activate a policy automatically. The former champion then supplies a
thirty-clean-session baseline. Portfolio capital-review resumption is a separate recorded human action.

Research reports load only when requested for an explicit date range of at most 366 calendar dates and show their read time. They do not
run inside the periodic monitoring refresh. Official portfolio NAV includes every valid fill; the adjacent
fill-confidence diagnostic compares realized returns for all closed trades against the bid/ask-only closed
trade slice, with estimated-fill counts and percentages. Its trade-return denominator is labelled and is
not substituted for a portfolio NAV return. Report a known or suspected corporate action with its symbol,
affected date range and reason; the resulting audit excludes affected evidence without adjusting cash,
shares or historical prices.

Use offline Core tests and builds for source validation. Starting WPF, collecting quotes/bars, applying
migrations, scheduling an experiment and activating a portfolio are operational steps with persistent
effects; none was performed merely to validate this implementation.

The one-shot `delphi-live-closed-market-trial` Sandbox probe is the explicitly selected historical
source check documented in the [2026-09-06 trial record](reviews/delphi-live-closed-market-trial-20260906.md).
It requests nine pinned September 4 intervals from TMX and writes one unique local metadata report,
with no SQL or portfolio action. It requires external-service authorization on each run and does not
exercise current-session availability, persistence or activation.

The separately authorized `delphi-live-friday-replay` Sandbox probe produced the saved historical replay
on 2026-09-06. It reads the frozen Friday source and saved Shadow account capital from SQL, then obtains
at most 58 historical TMX batches (29 symbols including XIU, one- and five-minute intervals). Completed
symbol requests are cached. Its only writes are ignored local replay artifacts; it is not a database
backfill. Do not run it as a build check or to open an existing replay. Any additional acquisition run
requires explicit operational authorization.

## Athena — calibration outcome evaluation

**Project:** `Athena`
**Entry point:** `Athena/Program.cs`

Athena reads immutable official calibration candidates and local `DailyBars`, reproduces the enabled production labelers, and idempotently writes matured 10-session labels, 20-session price paths, three-session marks/excursions, and delayed-intraday outcomes. It prints the identified active strategy scope and excludes/counts earlier strategy identities before reporting comparative coverage: distinct completed-market-session cohorts, official run and candidate counts, valid/degraded/invalid/pending outcomes, completion coverage, usable coverage, and whether the 95% reporting floor permits a primary descriptive score. ADR-0038 adds official-only probability calibration, eligible-lens rank quality, and descriptive technical/market slices. All metrics use nested candidate/run/market-session weighting so a deliberate Delphi rerun cannot inflate independent evidence. The report never changes a model, weight, gate, lens, or trading policy.

For `DelayedIntradaySwing`, Athena replays only a continuous first-received 15-minute path. A later bar that proves a missing bar/session, a receipt-order conflict, or a later five-minute bar that proves the exact symbol/XIU fill bar is missing produces an audited invalid outcome. If no later evidence yet proves a gap, the candidate stays pending. An alert during the session uses the exact next five-minute boundary; an after-close alert waits for the next observed regular-session open. Athena never substitutes a later convenient price.

The 2026-09-01 verified run wrote 112 valid `SwingMarkToMarket3` and 112 valid `SwingExcursion3` outcomes. It wrote zero `PredictionLabels10`, `PredictionPath20`, or `DelayedIntradaySwing` outcomes. Durable SGY/XIU evidence still ended on 2026-08-28, so launching WPF without a newer receipt does not make delayed outcomes ready.

Athena makes no external requests. It is still a database writer because it creates missing definitions and matured outcomes: apply the reviewed calibration migrations manually first and obtain explicit authorization before running it. Migration 016 is applied; until a new official Delphi cohort exists, the active identity correctly reports 0 included runs and 7 excluded earlier-identity runs. The optional CSV switch writes five export-schema-v2 artifacts stamped with strategy ID/name, initial code commit, and decision reference, and refuses to overwrite an existing filename.

```powershell
dotnet run --project Athena
dotnet run --project Athena -- --scorecard-csv C:\path\to\an-empty-export-directory
```

Athena remains independently runnable, and ADR-0046 also invokes it as the final stage of the guarded
daily pipeline. Hermes and Delphi do not launch it directly.

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

## Automatic daily-source recovery (ADR-0066)

After manual migration 032 and a WPF restart, active Shadow and Delphi Live sessions adopt newer Valid OfficialPaper results for today, the prior reviewed TSX session and the assigned daily strategy. No session reset is needed after a late Delphi run. Inspect the account explanation/activity and Live source warning for the active run and adoption time. Original freezes and coverage gaps remain; old pending buys are cancelled and confirmation restarts. Daily gates still apply. WPF waits for its monitor to release the durable lease when closing.
