# Historical practice — first implementation

**Implemented in source, September 8, 2026.** The operator authorized implementation after accepting
daily Delphi plus four independent Shadow accounts, today's pinned models, August 10–September 4,
CAD 1,000 per account, and ordinary missing-data conditions. See [ADR-0063](../adr/0063-historical-practice-environment-direction.md).
The authorized starter trial is now completed and deployed; see the
[actual results and validation](../adr/0063-historical-practice-environment-direction.md#completed-trial-and-deployment--2026-09-08).

## What runs

`PracticeSession` advances through every regular session in the saved reviewed calendar. At 09:00 Toronto,
it calls the shared `DelphiWorkflow` with `ExploratoryReplay`, a historical recommendation date, CAD 1,000
and database saving disabled. Historical construction rejects operational purpose, saving and replacement
bindings before any database access. The normal workflow's default construction remains operational.

`DelphiHistoricalInputs` bounds all daily market inputs before recommendation day: prices, XIU/SPY,
breadth, leadership, sector histories, dated universe/sector/XIU membership, US indices, OBV and climax.
Most-active calculations use the saved bars on the requested historical date. Both published Top-25
lists retain their original ranks and eligibility; a Hold does not become an eligible Shadow candidate.
The existing benchmark and feature checks own missing-data rejection. A rejected daily input prevents
that day's publication, while the practice schedule continues. Model/artifact/code failures remain failures.

From 09:30 through 16:05, the same `SystemShadowController` polls at simulated one-minute intervals.
`ISystemShadowEnvironment` supplies time, frozen daily sources, settings and intraday observations;
`ISystemShadowStore` supplies its ledger. The operational implementation still uses SQL, settings locks,
recorded TMX receipts and current trading controls. The practice implementation has no operational
repository or provider fallback. Its in-memory ledger is checkpointed to a new local run directory.

Each Continuation Top-3/Top-5 and Breakout Top-3/Top-5 account starts with CAD 1,000 cash and no holdings
or orders. Cash, holdings, add-ons, pending protective exits, reentries and risk holds survive session
boundaries. Existing Shadow rules determine decisions, whole-share sizing and friction. The isolated
ledger mirrors SQL transaction arithmetic and decimal scale; it does **not** validate SQL concurrency,
leases, durability or host capacity. Those require a separately isolated SQL integration milestone.

## Explicit execution assumptions

- Five-/fifteen-minute bars become observable only after completion plus the package's assumed delay
  (default two minutes). Original acquisition times stay in the dataset. Simulated receipts are not
  reconstructed historical delivery times.
- Shadow orders retain the production later-five-minute-bar fill contract. Buys require the exact next
  eligible bar; missed buy windows requalify. Protective sells wait for eligible later evidence. No fills
  are fabricated for missing bars; no forced liquidation occurs at the end of the trial.
- Entry and exit friction, protective floors, rotation, reentry and guards use pinned Shadow settings.
  Preparation captures available saved portfolio assignments; the recorded fallback is built-in Shadow V1.
- Missing/invalid bars remain unavailable. Coverage is diagnostic, never a completeness prerequisite.
  No replacement sessions, automatic retries during replay, gap interpolation or operational downloads.
- Production guards still use production last-known ledger marks. Public practice NAV/returns require
  an exact currently observable mark for every holding; otherwise they are null. The summary withholds
  drawdown when valuation frames are missing and counts those frames explicitly. Observed exposure is
  descriptive, not a complete exposure estimate when observations are missing.

## Prepare and run

Build the dedicated console project with .NET 10. The SQL project is not needed for these commands.

```powershell
dotnet build Practice/Practice.csproj
```

Acquisition is separate and explicit. None of the following acquisition commands ran during implementation.
They use the existing configured SQL/TMX clients when an operator invokes them.

1. Supply a dated membership JSON file with `SourceReference`, `Limitations` and `Snapshots`. Each snapshot
   has `AsOf`, `Equities` (`Symbol`, `SecurityType`, `IsLeveragedOrInverseEtp`), `Sectors` (existing
   `StockSectorMapping` fields) and `XiuConstituents`. Use observed historical membership or leave missing
   membership unavailable. Do not label today's universe/sector mapping as a past observation. Empty
   snapshots are permitted; they produce no qualifying universe until dated membership becomes available.
2. Export stored daily histories, supporting observations, selected preserved models and available Shadow
   settings. No training, activation or database write is performed; selection is read under its existing lock.
3. Optionally acquire normalized historical intraday batches under an explicit logical-request budget.
   Each logical TMX call has a 20-second limit and up to the client's three transport attempts, with a
   one-second pause between logical calls. Partial acquisition is saved. Request-budget exhaustion and
   cancellation are recorded; neither becomes a claim of full coverage. The collector includes earlier
   membership symbols to cover positions carried into later sessions.

```powershell
dotnet run --project Practice -- export-daily artifacts/practice-membership.json Operations/Calendars/tsx-2026-through-20261223-v1.json artifacts/practice-daily.json
dotnet run --project Practice -- acquire-intraday artifacts/practice-daily.json artifacts/practice-with-intraday.json 200
```

The budget above is an example, **not** an assertion that 200 requests cover the trial. Acquisition
results and errors are saved beside the new input as `.acquisition.json`. To retain a bounded acquisition,
pass its output into a later acquisition with a new output filename; existing session/interval batches are
preserved. Empty successfully returned batches remain recorded missing data, not secretly retried.
The supplied XIU/RY feasibility-probe batches can also be imported into the `Intraday` array when relevant;
those two symbols and three sampled dates are not the full starter dataset.

`PracticeDatasetDocument` defines the input JSON. It embeds the calendar, `DelphiHistoricalInputs`, source
provenance and normalized `TmxIntradayBatch` records. Model metadata initially references absolute local
preserved ZIP paths. `pack` verifies the hashes, copies the exact bytes into the package, rewrites paths
relative to that directory, records the current Core assembly hash, and hashes the entire dataset JSON.
It never overwrites a package. Every `run` rechecks data/model hashes and the Core build identity; changing
code requires explicitly preparing a new package. Unknown adjustment/revision/training provenance stays
disclosed. Available daily closes alone do not supply every required OHLCV/benchmark input.

```powershell
dotnet run --project Practice -- pack artifacts/practice-with-intraday.json artifacts/practice-package-01
dotnet run --project Practice -- run artifacts/practice-package-01 artifacts/practice-run-01
```

Use a new output directory for every run. No actual dataset/model package or market replay was prepared
during initial implementation. The subsequent authorized rollout completed the starter trial under
`artifacts/practice-rollout-20260908-0117/`, including an exact preserved `runner/` build. To reproduce
that run after later source changes, use `dotnet <rollout>/runner/Practice.dll run <rollout>/trial-package
<new-run-directory>` without rebuilding the pinned runner. Historical membership remains unverified;
the operator accepted today's list/classifications as a disclosed exploratory assumption.

## Saved results and WPF

### Rollout preparation additions (September 8)

`capture-current-universe <new-membership.json>` reads the current eligible stock list, sector mappings
and the static XIU constituent list. It records the actual UTC capture time in an explicit
`ExploratoryCurrentUniverse` field, never a backdated historical snapshot. This opt-in mode uses current
membership/classifications while all prices and indicator histories retain their historical cutoffs.
The assumption appears in every playback frame and in dataset/run provenance. Leaving this field absent
keeps the original dated-membership behavior. The operator explicitly approved using current membership
in the starter trial; the captured list does not establish historical membership.

For a smaller acquisition, first run the daily-only package into a fresh planning directory, then use
`acquire-selected <package> <completed-run> <new-input.json> <logical-request-budget>`. It verifies that
the completed run belongs to the exact package and has one bounded daily result per session. It requests
both intervals for eligible Top-5 candidates in either lens, retaining each possible holding through the
last session, including days without daily publication. The daily stock scan is unchanged. The acquisition
report saves the complete request plan and its source package identity. Pack the acquired input and run
the resulting package into a separate directory for the actual trial.

The Release WPF executable also accepts `--practice <absolute-path-to-frames.ndjson>`. This opens only
the recorded playback window; it does not construct the operational dashboard or start its monitors.
The normal app entry point and its Practice tab remain available.

- `run.json`: Practice / Exploratory identity, fixed dates, model hashes/training dates, policy settings,
  CAD capital, code identity, execution and recovery assumptions.
- `coverage.json`: expected, present, missing and invalid intraday slots by symbol/session/interval.
- `frames.ndjson`: flushed chronological simulated frames, both Delphi lists, per-account cash/holdings,
  pending orders, candidate decisions and data warnings. Earlier frames contain no later state.
- `checkpoint.json`: atomically replaced full inspection checkpoint at session boundaries, including
  daily presentations, ledger events and fills.
- `summary.json`: completed-run per-account return/value, observed drawdown/exposure, missing valuation
  count, buy/sell fill count, turnover and estimated friction. Four account values are never combined.
- `failure.json`: unexpected failure with simulated time and partial ledger state. Failure is not converted
  into a successful missing-data session. Completed frames and the last checkpoint remain for inspection.

The new **Practice** tab in WPF opens `frames.ndjson` with Play/Pause, Step and three playback speeds.
The Practice / Exploratory banner and Toronto simulated clock remain visible above the four accounts,
holdings, daily lists, Shadow decisions, pending orders and gaps. Opening and playing this file invokes
only `PracticePlayback`; it cannot run Delphi/Shadow, write SQL or request market data. The usual WPF
host retains its other operational pages and scheduled duties. No operational host was launched to test
the Practice tab. Interactive runner control can later bind to `PracticeSession.StepAsync`.

Automatic crash resume is deliberately not implemented: rerun explicitly from the pinned dataset into
a fresh directory. This prevents a partial checkpoint from silently restarting accounts or duplicating
fills. Delphi Live, real SQL/transport/load/failure injection, historical quotes, corporate actions,
walk-forward training and forward/promotion evidence are later work.

## Validation

Synthetic tests exercise the actual shared controller over 20 sessions and all four accounts, accounting,
next-bar timing, missing exact fills/requalification, missing marks/recovery, failed daily publication with
protective exits, deterministic reruns, future-bar exclusion, unexpected failure traces and operational
write rejection. The complete daily workflow is also exercised with synthetic model outputs and saved
inputs; future prices, breadth, OBV and membership changes leave its earlier result unchanged. Package
tests verify preserved copies, checksum tampering and overwrite rejection. No model is trained in these tests.

The final full Core suite passed all 790 tests, including 16 new Practice tests. Practice CLI and WPF
focused builds pass. Offscreen synthetic Practice views were rendered at
1240×700 and 960×600 and inspected, with zero binding errors. Existing compiler warnings and separate
NuGet dependency-security advisories remain. This is not a full solution/SSDT or real SQL integration claim.
