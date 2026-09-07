# Dated model inputs — compatibility and cutover review

**Date:** 2026-09-06. **State:** selection completed and verified through Delphi's actual bootstrap.
**Latest state:** the first authorized official v3.4 run completed at 22:43 Eastern with valid provenance
and no qualifying trade. It used the same four model files and observed September 4 XIU/SPY inputs.
The sections below retain the initial v3.3 cutover, readiness findings, repair and first run as separate events.
**Authority:** the operator explicitly authorized compatibility inspection, separate replacement training
when compatibility cannot be proven, a new corrected strategy version and switching the nightly runner.
See [ADR-0056](../adr/0056-dated-profit-model-input-contract.md) and
[ADR-0057](../adr/0057-preserved-model-sets-and-explicit-selection.md).

## Compatibility finding

The enabled registry contains 27 profit rows, but current code permits four tasks. Those four artifacts
load successfully in ML.NET and have the expected vector dimensions. They lack named feature slots and
embedded input-contract manifests. Their registry records omit training-date ranges; 43 associated task
experiment records contain no artifact hash/source-contract identity linking them to the current files.
Matching dimensions therefore cannot prove the historical training-input semantics.

The existing models are not claimed corrupt or necessarily incompatible. Compatibility could not be
established from retained evidence, so the operator-authorized fallback trained four separate candidates.
The four original byte snapshots were copied and hash-verified; none was overwritten. Private metadata,
inspection logs and initial snapshots are retained under `artifacts/model-lifecycle-20260906-2333`.
Operational model storage is outside Git under the Windows user's local TraderVI Models directory.

## Completed prerequisites

- 44 focused feature/binding/preservation tests passed before the source-archive addition.
- All four original artifacts load and match expected vector dimensions; this is not semantic approval.
- The SQL project builds with Visual Studio MSBuild plus SSDT.
- A fresh compressed checksum backup passed `RESTORE VERIFYONLY WITH CHECKSUM`; the local OneDrive
  backup copy has the same SHA-256. Remote synchronization was not independently verified.
- Migration 026 added two tables; all 64 existing tables retained their row counts. It activated nothing.
- All four replacement models completed using stored SQL history. Their rows remain disabled and
  predecessor signal thresholds are preserved. The private candidate manifest is in model set
  `46957ff9-826b-4bc7-8f7f-fed06cd426d7`.
- The exact 288 source files used by that run were archived and their combined identity matched its
  completed manifest. A separate immutable receipt links that archive to the existing manifest without
  rewriting it. Subsequent Hercules runs archive their source automatically before training.

## Completed selection and validation

- Sole active strategy: `v3.3-dated-profit-inputs`, ID `9EB26788-76C3-4064-9595-2373ECF6C26A`,
  decision `ADR-0056`. The predecessor remains registered and its exact files are retained.
- Both previous and corrected assignments passed hash validation, actual ML.NET loading and finite
  synthetic predictions. The forward SQL transition passed a rolled-back rehearsal, then committed.
- Delphi's actual bootstrap resolved all four selected hashes with `ProfitInputs.XiuDatedV1`. Nightly
  CLI and WPF share that stored assignment; no per-host file or scheduler reinstall is needed.
- A rollback rehearsal passed without committing. A subsequent bootstrap check confirmed v3.3 remained
  selected. The two immutable assignments and one committed selection event remain in SQL.
- All 228 pre-existing model registry rows are unchanged field by field. The four new rows remain
  disabled globally because the strategy's stored assignment selects them explicitly.
- Thirty-eight historical evidence/account tables retained matching row counts and aggregate checksums
  across selection. These checks complement the migration's existing-table row-count verification.
- All 655 Core tests passed, with no failed or skipped tests. The complete Release solution built with
  Visual Studio MSBuild plus SSDT, including `TraderDB.sqlproj` and the new ModelLifecycle project.
  Existing nullable/unused-variable compiler warnings remain. Separately, dependency-security warnings
  include System.Drawing.Common, System.Data.SqlClient, System.DirectoryServices.Protocols and
  System.Security.Cryptography.Xml; this phase did not update those dependencies.

The current training run finished before automatic source archiving was added. Its unchanged completed
manifest is linked to the verified exact-source archive by a separate receipt. The corrected strategy's
runtime source has an explicitly prefixed working-tree SHA-256 identity; no commit was fabricated or
created. The retained training source and runtime source are recorded separately as ADR-0057 requires.

## Interpretation and remaining work

This is a repaired paper-advisory baseline. Training metrics do not establish strategy superiority or
approve real-account recommendations/broker execution. Preserve original cohorts and family-specific
promotion requirements. Historical-label and daily Shadow dispatch remain separate audit findings.

The next successful normal nightly run will publish under the corrected identity. A first scheduled
cohort has not yet been observed; bootstrap validation does not claim end-to-end nightly completion.
Observed XIU dates remain the canonical session source, so independently detecting a missing/stale
benchmark session remains a documented data-quality limitation. Policy dispatch, historical outcome
definitions and common strategy comparison remain open in the audit checklist.

No full nightly pipeline, market collection, new recommendation cohort, paper monitor or broker workflow
was launched. No commit, push or deployment was performed. Authorized writes were replacement training,
the additive migration and reviewed strategy/model registration and selection.

## Read-only readiness follow-up — 2026-09-06, 21:19–21:29 Eastern

The operator authorized the listed pre-run checks, reserving the actual Delphi run for a later step.
Only local source/calendar inspection, read-only SQL, process/task inspection and model loading were
performed. The query and private aggregate results are retained under
`artifacts/delphi-readiness-20260906-2119`. An initial summary query had a SQL formatting error and was
corrected before the successful SELECT; no database write occurred.

| Check | Result |
|---|---|
| Selected strategy/models | Pass: v3.3 remains the sole active strategy, with a valid stored checksum, four assigned models and four associations. Actual Delphi bootstrap loaded the exact four files. |
| Source/build identity | Pass: the current 288 Core/Hercules source files match the selected working-tree identity. Core assemblies in Delphi and the verification tool match. The prior 655-test/full-solution validation was not rerun for these read-only checks. |
| Concurrent execution | No Delphi, Hermes, Athena, trainer, WPF or matching nightly process found. Task Scheduler reports Ready; next configured start is September 7 at 00:30:30 Eastern. This is a point-in-time check, not a lock reservation. |
| Latest completed session | The installed reviewed calendar's checksum matches. September 4 is the latest completed TSX session; the next session is September 8. |
| Daily data freshness | **Needs refresh:** XIU and 320 of 321 eligible stock records stop at September 3; one stops at September 2. No eligible stock has September 4 data. A/D, leadership, OBV, climax, sector and stored US-index data also stop at September 3. |
| XIU calendar coverage | September 4 is the only missing observed XIU session from January 1 through September 4 against the reviewed calendar; there are no unexpected dates. Stored 2026 XIU OHLCV checks found no malformed bars. Runtime's observed-date convention does not independently reject this stale shared endpoint. |
| Same-date publication | September 6 has zero picks, dossiers, narratives, Granville logs or linked positions. A normal run on this date would not replace September 4's 50 published picks. Those September 4 picks also have no position references. |
| Historical links | No orphaned position/pick links. Existing links belong to earlier dates and remain untouched. Four completed Shadow sessions retain their immutable calibration-run links; there are no Delphi Live sessions. Relevant SQL foreign keys are enabled and trusted. |
| SPY confirmation | **Separate contract gap:** `DailyBars` contains no SPY history. `QuoteRepository.GetDailyBarsAsync("SPY")` reads that table directly. `ComputeRegime` defaults both SPY trend flags to true when fewer than 200 bars are supplied, so reported positive confirmation is not backed by SPY observations. |

Recommendation: refresh the stored daily inputs before a new official publication, and resolve the SPY
source/missing-data contract before describing the run as fully checked. The SPY issue belongs to the
existing deterministic regime filter, not the corrected ML feature contract; no replacement benchmark,
gate behavior or strategy-version change was introduced. It is tracked as follow-up S9, not retroactively
attributed to the original audit. The regular Hermes source path inspected here collects TSX symbols and
separate `^GSPC`/`^NYA` index history; that does not prove a refresh will populate the SPY table consumed by
Delphi. No Hermes/Delphi run, market request, selection change or scheduler change was made. The nightly
task remains enabled, and these findings do not install a new runtime block.

## Authorized schedule and SPY repair — completed 2026-09-06

The operator subsequently asked to fix both findings and explain why Friday was missing. The installed
task and repository configuration both used Monday–Friday at 00:30. The saved Friday run shows Hermes
succeeded, collecting the previous completed day; the absent Saturday trigger explains the missing
Friday close. Its Attention status was from Delphi/Athena evidence diagnostics, not a Hermes crash.

The configuration and installed trigger now cover all seven days. Export comparison confirmed that the
task actions, principal and other settings were preserved. Every reviewed 2026 session has a trigger on
the following calendar day. The existing 07:00 read-only supervisor was also updated for weekends and
to avoid repeated unchanged alerts. The pipeline was not manually started.

The authorized standalone Hermes refresh added 481 daily bars, with zero symbol-download failures.
XIU, 320 of 321 stocks, breadth, leadership, OBV, climax and sectors now reach September 4. The new SPY
maintenance path appended 503 actual SPY daily observations and verified the required endpoint and
history. Its response identity and OHLCV are checked; existing SPY rows are not overwritten, and SPY is
not added to the TSX universe or substituted with a Genuity index. Both refreshes completed checksum
backups and hash-matched local OneDrive copies. Remote synchronization was not independently verified.

One stock remains stale, four current stocks have missing required bars and two have invalid bars in
the 55-session source check. The accepted stock-only exclusions apply. The 314 complete source windows
are not a claim that all pass liquidity, relative-strength or trading gates; no recommendation workflow ran.

`v3.4-observed-benchmarks` (`48864449-92F4-4E33-99CF-40039210786E`) is the sole active daily strategy,
with `DecisionRef=ADR-0058`. It reuses model set `46957ff9-826b-4bc7-8f7f-fed06cd426d7` unchanged. The
runtime policy requires observed/current XIU and SPY inputs before evaluation, retaining all existing
complete-data calculations and thresholds. The reviewed calendar prevents an absent shared latest day
from silently becoming the current date. Source archive and working-tree identity
`B01C325C20B36F5E3D658F25979EA03CC6897BA3014F554C277DDF588F8353C7` are retained in the review artifacts.

- All 676 Core tests passed; the new fixtures cover missing/stale/invalid/future inputs, unchanged complete
  calculations, source identity, append-only ingestion, policy dispatch and both reports.
- The complete Release solution built with Visual Studio MSBuild plus SSDT, including TraderDB.
- Actual XIU/SPY prerequisite validation and Delphi bootstrap loading passed for the active strategy.
- Forward and rollback rehearsals passed with rollback; only the forward registration was committed.
- All 232 model registry rows are identical. Thirty-eight evidence/account tables retain matching counts
  and aggregate checksums against both the pre-selection and pre-repair snapshots. SQL has three preserved
  assignments and two committed selection events; v3.3 and the older baseline remain available.
- Existing compiler warnings and dependency-security advisories remain. No new training, schema migration,
  Delphi/Athena run, paper monitor, broker action, commit, push or deployment was performed in this repair.

Private logs, reviewed SQL, source archive, schedule exports and preservation checks are under
`artifacts/nightly-spy-repair-20260906`. First official corrected-run observation remains pending.
ADR-0058 explicitly records the US-only holiday and historical US-calendar continuity limits; no hidden
staleness tolerance was added. F6's broader source-completion contract and the remaining strategy-policy,
historical-outcome and common-comparison audit phases remain unfinished.

## First authorized official v3.4 run — 2026-09-06, 22:43 Eastern

The operator explicitly requested the Delphi run after the readiness repairs. The standalone Release
CLI completed once with exit code 0 at 22:43:23 Eastern. Its recommendation date is September 6 and
market-data-as-of date is September 4; this does not assign a market session to Sunday.

Preflight held the existing nightly single-instance lock, found no conflicting TraderVI application,
confirmed no same-date publication or position links, built the affected Delphi project without restore,
matched the current source to the v3.4 reviewed hash, and loaded all four assigned models through the
actual bootstrap. The build passed with zero emitted warnings/errors; this incremental build does not
supersede the previously recorded full-solution compiler warnings or dependency-security advisories.

| Verification | Saved result |
|---|---|
| Run | `7cd0ab32-07c1-4efb-b5c3-808f99b75887`, `OfficialPaper`, `audit=Valid` |
| Strategy | `v3.4-observed-benchmarks`, `48864449-92F4-4E33-99CF-40039210786E` |
| Inputs | `ProfitInputs.XiuDatedV1`; model set `46957ff9-826b-4bc7-8f7f-fed06cd426d7`; all four captured model IDs/task types/hashes match the immutable assignment |
| Benchmarks | `DailyBenchmarks.XiuSpyObservedV1`; reviewed calendar; XIU 1,677 and SPY 503 observations through September 4 |
| Evaluation | 321 discovered, 209 model-evaluated; seven required-input exclusions, 71 above the price ceiling, 12 below the price floor and 22 below the volume floor |
| Immutable evidence | 209 candidates, 418 lens rows and the saved presentation snapshot |
| Operational publication | 25 Continuation and 25 Breakout entries, all Hold; 25 dossiers and 10 Granville logs, published atomically |
| Trade result | Zero eligible in either lens; **no qualifying trade** and no position linked to the new picks |
| Preservation | All 38 prior evidence/account snapshots retain matching counts/checksums after excluding only this new cohort/publication; model registry remains 232 rows and selection events remain two |

The run records the real Git commit with a Dirty working tree; the separate preflight/source record
matches the reviewed v3.4 working-tree hash before and after execution. No clean-tree identity was invented.
The audit's Valid state verifies provenance, not complete indicator coverage or investment performance.
Leadership movers have 0/12 required contiguous observations, so Leadership #7–#10 and Light Volume
#25–#28 remain explicitly unavailable under the existing missingness contract. No substitute observations
or threshold changes were made. Publication despite zero eligible candidates also leaves audit S2 open;
published rows must not be treated as approved buys.

Private logs, the one-run wrapper, read-only verification SQL and aggregate snapshots are retained under
`artifacts/delphi-official-20260906-2241`. No Hermes refresh, further training, selection change, Athena
evaluation, paper monitor, broker operation, migration, commit, push or deployment occurred. This verifies
the deliberate daily workflow; the first normal scheduled pipeline completion and forward outcomes remain
unobserved. The next implementation phase remains policy/version dispatch and historical outcome contracts.
