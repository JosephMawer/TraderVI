# Nightly precompiled releases — design draft, November 1, 2026

The operator accepted precompiled releases for Hermes, DataAudit, Delphi and Athena during the
disk-space review. Release preparation, activation, scheduling, a dashboard action and retention
details remain proposals for discussion. This draft records that boundary. It is not an accepted
replacement ADR or authorization to install a release, change the scheduled task, run the pipeline,
delete files, or change SQL. The installed runner still follows ADR-0078.

## Problem and intended outcome

The immediate decision is how a completed application change reaches the next nightly run. The
parent problem is repeated compilation and retained output consuming disk space. The root outcome
is dependable daily preparation with bounded storage and little routine operator work.

Here, a release means a completed, tested set of the four applications and their dependencies,
saved locally for repeated nightly use. Publishing creates that runnable set. An ordinary build
produces development output. The Release build configuration is a compiler setting; selecting it
alone would not establish that a complete nightly package passed its checks.

The November 1 inventory measured 46.67 GiB allocated under `artifacts/nightly`. The October 31
run folder contained 1.334 GiB of file contents, including 1.321 GiB in build directories. The
reviewed older-library subset contained 10,684 files in 34 run directories, totaling 31.834 GiB
allocated. All selected files remained present during this discussion. The private inventory is
`artifacts/nightly-library-cleanup-20261101/storage-inventory.txt`.

## Current behavior

The scheduled task starts the repository PowerShell runner daily at 00:30 Toronto. The runner
captures source, restores locked dependencies and compiles four applications into a fresh run
directory. The captured source may include uncommitted changes. It then runs Hermes, DataAudit,
Delphi and Athena in order, checks executable identities and saves logs and status.

Source isolation was accepted to allow edits during preparation. Current task settings support
waking and a missed start while the Windows user remains signed in. The WPF application has a
Delphi action. A single action for the full nightly pipeline would be new.

## Decision record

- **Accepted direction.** Use precompiled releases for nightly execution. The prior requirement
  to capture and compile the latest working files each night is being reconsidered explicitly.
- **Proposed release preparation.** Completing a relevant code change includes building and
  testing a consistent package containing all four applications and their dependencies. Shared
  Core changes also count. Package completion must be explicit so ordinary in-progress build
  output cannot be mistaken for a finished release. This can be part of the development workflow.
  The operator supports that overall approach and asked whether a post-build step should prepare
  the folder. The proposed hook runs after the designated package build and validation, with
  ordinary debug/test builds continuing as development work. New package history also needs a
  bound so frequent development builds cannot recreate the accumulation.
  Subsequent discussion identified that a trigger for any shared Core change is too broad.
  The proposed preparation point is completion of a coherent, validated development increment.
  The exact trigger remains open alongside the assembly separation question below.
- **Proposed activation.** Publish a fully prepared package to a dedicated release directory,
  then change the selected-release reference in one operation. The next run reads that reference
  once, records the release identity and uses that directory throughout. A new release prepared
  during a run becomes available to a subsequent run. Reuse the package across nights.
- **Proposed operator experience.** Routine changes completed through that workflow require no
  separate nightly build/copy chore. Editing files alone does not prepare a release. A developer
  working outside the workflow needs one explicit preparation/activation command. Who may activate
  a package and which checks must pass remain open.
- **Proposed retention.** Keep the selected release and one previous release for rollback, plus
  any additional release still in use. The final pruning rules and cleanup of historical source,
  libraries, logs and package caches require their own decision.
- **Open schedule.** The operator suggested 05:30 Toronto to avoid active development hours.
  That leaves four hours before the regular 09:30 open. The October 31 run took about ten minutes,
  but current maximum stage times can exceed that window. Confirm the desired completion window
  and handling of delayed runs before changing the installed task.
- **Open dashboard action.** A full-pipeline Run now button could invoke the same runner and
  selected release. Retain the exclusive run lock and preparation-date duplicate guard. Existing
  early preparation supports tomorrow after 16:00 under completed-session checks. Decide how
  the button presents the preparation date, progress, completed warnings and a reviewed retry.
- **Deferred.** A SQL table for nightly logs was a thought explicitly held for later. Other
  artifact directories and the separately active SQL cleanup remain separate review items.

## Single-file packaging under review

The operator proposed one executable per application. .NET supports this through publishing.
A candidate package would contain Hermes.exe, DataAudit.exe, Delphi.exe and Athena.exe, plus
release identity metadata and any required external configuration. Model files retain their
assigned locations. Final package contents and runtime compatibility require validation.

The recommendation is Windows x64 publishing that uses the installed .NET 10 runtime. Platform
selection includes matching native dependencies, reducing the extra Linux, macOS and Windows x86
payloads seen in the portable build outputs. Framework-dependent publishing avoids bundling another
.NET runtime in each application. The local runtime inventory on November 1 includes .NET 10.0.12.
[Microsoft publishing documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/)

Single-file publishing can bundle native dependencies with IncludeNativeLibrariesForSelfExtract.
Those libraries are extracted to disk for execution, by default under the user's Temp/.net
directory on Windows. Extraction files must be included in the storage/retention design.
Single-file packaging changes assembly file-path APIs, so the four applications and their
dependencies need checks against their actual entry paths. Source inspection found assembly-file
hashing in other shared Core components; reachability through these four applications and bundled
ML/SQL dependency behavior remain unverified. Published sizes have not been measured. Reusing a
bounded set of releases provides the primary storage saving even with multiple files per package.
[Microsoft single-file documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

## Shared assembly separation under review

The operator suggested extracting the nightly applications' dependencies from Core so unrelated
Core edits do not require their compilation/publication. Source inspection confirms meaningful
shared consumers. The Delphi CLI uses DelphiWorkflow; WPF execution was subsequently removed below.
Athena and WPF scorecards both use CalibrationOutcomeRepository. DelphiWorkflow also depends on market data, indicators, model
loading, strategy logic and repositories. MarketDataAuditWorkflow depends on the SQL audit
repository through a snapshot-source interface.

Other callers can reference an extracted shared library and keep one implementation of each
rule. The relevant boundary follows responsibilities and dependency direction. A new nightly
library that still references the entire Core assembly retains the original coupling. Shared
types and required services would need a lower-level home, or the unrelated execution code could
move out of Core instead. The November 1 mapping below identifies the initial extraction boundaries.
The DataAudit and Hermes slices are now implemented below; Delphi and Athena extraction remains proposed.
Bounded release storage can be implemented independently of it. Any future package-size claim requires
checking which ML/native dependencies each extracted project actually retains.

The operator subsequently authorized removing both desktop daily Delphi execution paths.
[ADR-0118](../adr/0118-delphi-desktop-results-only.md) records the implemented source change:
the Run official Delphi button and automatic run after daily settings assignment are removed.
The settings code-identity reference now uses DelphiSettingsChange in the same Core module.
The UI retains the saved-result reader and presentation types. Assignment leaves buying paused
and explains that new results await the next nightly Delphi run. This change has not been
installed, and it does not yet separate the remaining shared Core dependencies. The earlier
full-pipeline Run now idea remains a separate unresolved choice.

### DataAudit extraction — implemented November 1

[ADR-0119](../adr/0119-independent-data-audit-build.md) records the first accepted extraction.
DataAudit owns MarketDataAuditWorkflow, MarketDataAuditor, MarketDataAuditModels and
MarketDataAuditRepository. The audit has its own SQL client configuration and keeps the existing
local default and explicit override. The audit does not need SQLBase simply to choose a database
connection. There is no separate audit library. The later Hermes extraction below shares actual
market repositories and their SQLBase implementation without adding them to DataAudit's dependencies.

For later extractions, share code when the remaining consumers need one implementation. A default
connection setting alone does not justify a shared SQL project or a dependency on SQLBase.

DataAudit's single shared project, TraderVI.MarketData, contains TsxSectorSymbols, DailyBenchmarkSymbols,
MarketDataFreshness and SecurityNameHeuristics, all consumed by both the audit and existing Core
code. It has no package dependencies or Core reference. Core and DataAudit reference this one
implementation. The CLI's restored dependency graph excludes Core, ML.NET, MKL, LightGBM and
the market-provider clients. DataAudit.Tests now owns the audit tests and checks this boundary.

The running WPF, collector and trading engine retain their old
binaries. Existing nightly folders remain in place. The source-capturing nightly runner can use
the new project/lock graph on its next capture; no operational audit was run during validation.

### Hermes extraction — implemented November 1

[ADR-0120](../adr/0120-independent-hermes-build.md) records the second accepted extraction.
Hermes owns its backup/retention, Yahoo source, SPY ingestion, sector classification, new-high/low
calculation and leadership acquisition code. Shared market repositories (used by Delphi, saved-result
readers and Athena as well as Hermes) and SQLBase live in TraderVI.MarketStorage. The TMX client shared
with collector/quote consumers lives in TraderVI.MarketProviders. Both reference package-free
MarketData, which now also holds shared bars, calendar, validation and deterministic calculations.
Core consumes these same implementations; none of the three market projects references Core.

DailyBar is separated from the ML prototype, and regime evaluation stays in Core rather than being
pulled into shared benchmark validation. Hermes.Tests owns the ingestion/backup fixtures and verifies
the resolved dependency graph excludes Core and ML. The isolated Release output is 17.71 MiB across
66 files, with no Core.dll, ML.NET, MKL or LightGBM. This is a normal build output, not a single-file
publish or an installed release. DataAudit still has no provider or shared-storage dependency.

The purpose is fewer dependencies and less unrelated change propagation. Changing a shared component
Hermes uses still requires rebuilding Hermes. Running a build does not itself change that component.
The installed nightly runner still captures source and builds each night; release reuse and bounded
retention are the separate changes that stop accumulating another full output on each run.

### Dependency findings — November 1, before extraction

At mapping time all four CLI projects referenced Core.csproj. Moving only DelphiWorkflow to another project
that references Core would leave every nightly build coupled to Core. The proposed dependency
direction is: each CLI uses its own application functionality plus small shared libraries;
the remaining Core, desktop, collector and trading engine can also use those shared libraries.
The extracted libraries must have no reference back to the remaining Core assembly.

| Application | Functionality that needs a home outside the broad Core assembly | Existing source locations |
|---|---|---|
| DataAudit | Audit rules, report models, snapshot-source interface and SQL snapshot reader | `Core/DataQuality/MarketDataAuditWorkflow.cs`, `MarketDataAuditor.cs`, `MarketDataAuditModels.cs`, `MarketDataFreshness.cs`, `SecurityNameHeuristics.cs`; `Core/Db/MarketDataAuditRepository.cs` |
| Hermes | Daily collection providers, ingestion/validation, indicator calculations, repositories and backup service | Relevant `Core/TMX` provider clients and models; `Core/Indicators`; `Core/Db/QuoteRepository.cs`, symbol/sector/breadth/OBV/leadership repositories and `TraderDbBackupService.cs`; `Core/Runtime/SpyBenchmarkIngestion.cs`, `DailyBenchmarkPolicy.cs`, `NightlyPreparationDate.cs` |
| Delphi | Daily orchestration, prediction from saved models, ranking/gates/sizing, reports, provenance and publication | `Core/Runtime/DelphiWorkflow*.cs`, `DelphiBootstrap.cs`, `DelphiHistoricalInputs.cs`, `DelphiReportBuilder.cs`; relevant `Core/ML/Engine/Profit`, `Core/ML/Engine/Patterns`, `Core/Trader/Gates`, `Core/RelativeStrength`; `TradeDecisionEngine.cs`, `StrategyConfig.cs`, lens/sizing classes; daily strategy/model/evidence/publication repositories; pure `Core/Oracle/DecisionDossier.cs` and `DecisionDossierBuilder.cs` |
| Athena | Outcome maturation, shared label definitions, report/scorecard calculations, evidence queries and writes | Relevant `Core/Calibration` calculators/reports; `Core/Db/CalibrationOutcomeRepository.cs`, `CalibrationEvidenceRepository.cs`, `IntradayEvidenceRepository.cs`, `QuoteRepository.cs`; `Core/Trader/DelayedIntradaySwingExitPolicy.cs`, `FreshDelphiBreakoutEvidenceResolver.cs`; pure label definitions currently under `Core/ML/Engine/Profit` |

These are responsibilities, not a proposal to move each entire directory. Hermes needs provider
clients; a SQL reader should not acquire those packages just to share a bar or sector type.
Likewise, Delphi needs prediction support, while the model trainers can remain with Hercules.
Athena uses the same outcome-label rules as training; copying those rules would risk disagreement
between training and evaluation. Move their shared definitions once and update both consumers.

The first shared layer needs these concrete separations:

1. **Market facts and calendar.** Move DailyBar out of `Core/ML/TimeSeriesUtils.cs`, where it shares
   a file with an obsolete ML training prototype. Share the bar/quote/sector types, benchmark
   symbols, freshness rules and reviewed session calendar independently of training/live hosts.
   `ITsxSessionCalendar` currently lives in `Core/Trader/DelphiLive/DelphiLiveWorkflowContracts.cs`.
   `DailyBenchmarkSnapshot.ComputeRegime()` in `DailyBenchmarkPolicy.cs` calls TradeDecisionEngine;
   separate this analysis operation from the validation methods Hermes and QuoteRepository use.
2. **SQL access and record shapes.** Extract the common SQL connection/query foundation and the
   repositories each app actually uses. Split `Core/Db/ModelExperimentInfo.cs`: it contains five
   repositories plus strategy, pick, position and trade records. A saved pick should not bring the
   active-position implementation with it. Keep result readers available to WPF.
3. **Decision and evidence contracts.** SignalResult, MarketRegime, TradeDirection, TradeDecisionResult
   and RankedPick currently share `Core/Trader/TradeDecisionEngine.cs` with the evaluator. Move these
   data shapes into shared contracts, preserving their meaning and serialization. CalibrationEvidence
   needs SignalResult, not the evaluator. Separate common gate traces from gate execution similarly.
4. **Daily analysis versus intraday helpers.** `DelphiReportBuilder.DescribeMinuteConfirmation`
   accepts DelphiLiveMinutePair from the live evaluation implementation. Move that formatting helper
   to the live reporting area or accept a small shared result contract. Also move the simple
   SystemShadowDelphiRun/Candidate records out of `Core/Trader/SystemShadowLedger.cs`, or separate the
   daily evidence queries returning them. That repository/file chain currently reaches PaperGateComparison
   and live experiment code. Daily analysis should not need those live implementations.
5. **Prediction versus training.** Keep model loading, prediction schema checks and required native
   prediction libraries with an ML prediction component. Keep reusable feature/label definitions
   shared with Hercules and Athena; keep trainers out of the nightly dependency chain. Delphi
   uses ML predictions. Hermes, DataAudit and Athena do not need to run a trainer.

An especially small accidental link is `MarketDataAuditor` reading only `SpyBenchmarkIngestion.Symbol`.
That constant is declared alongside ingestion logic, which uses DailyBar and DailyBenchmarkPolicy.
Put the benchmark identity in the shared market definitions so DataAudit need not reference ingestion.

### Consumers that must continue working

- WPF still needs DelphiPublishedRecommendationReader, DelphiPersistedPresentationReader and
  DelphiPresentationSnapshot. The persisted reader also reconstructs legacy evidence using saved
  facts and deterministic calculators; preserving that fallback is part of extraction.
- Daily settings still call `DelphiSettingsChange.VerifyArtifacts()` →
  `UnifiedProfitSignalModel.VerifyStoredArtifact()`. This loads/checks saved ML model schemas.
  Removing daily execution therefore does not remove all WPF ML dependencies. Keep a narrow shared
  validation service unless changing settings behavior is separately agreed.
- WPF Scorecards uses CalibrationOutcomeRepository and report calculators shared with Athena.
- `Core/Practice/PracticeSession.cs` still invokes DelphiWorkflow for historical replay. It must
  reference the extracted daily application, or receive it through its existing evaluator boundary.
- The collector, trading engine and Hercules also consume shared facts/rules. Update their project
  references to the same implementation; do not duplicate logic or change the installed processes.

Moving types between assemblies changes module identity. The current settings identity adjustment
is equivalent because both types remain in Core. A later split must deliberately identify the
daily analysis build/model contract, rather than accidentally recording the new contracts assembly's
identity. Review internal access, saved JSON shapes, provenance, model schemas and file-path assumptions
as part of that move.

### Recommended sequence and remaining choices

The implemented **DataAudit and Hermes** slices above build with no Core reference or ML
packages. Next separate daily result/evidence contracts and move Delphi's daily analysis and Athena's
calibration functionality. Keep responsibilities separate enough that DataAudit does not inherit
Delphi's ML packages through a single all-purpose nightly library. Project names and the exact number
of further shared projects remain design choices; DataAudit and Hermes are implemented and accepted.

Success means an unrelated broker/UI edit does not rebuild the four nightly applications. A change
to a shared rule they actually use must still rebuild the affected apps. Precompiled release reuse
and bounded retention address disk accumulation independently of this extraction.

There is also a package-level candidate: `Core/Core.csproj` references Microsoft.ML.TimeSeries, and
`Core/packages.lock.json` shows it brings Microsoft.ML.Mkl.Redist, the large library found in the
disk inventory. The semantic scan found no direct source dependency on TimeSeries in Core or the
four CLIs, and a text check also found none in ML.Train. This supports reviewing that package's
necessity; it does not prove it can be removed without validating saved-model loading and any
indirect runtime requirements. Core's ML package references remain unchanged by these extractions.

### Evidence and limits of this mapping

A local Roslyn semantic scan inspected 578 Core source files and all source in the four CLIs,
using the .NET 10 reference assemblies and restored package compile references. Each analysis
compilation had zero errors. It did not launch application code. The private scanner and complete
file/dependency lists are under `artifacts/nightly-ui-20261101/dependencies/`.

| App | Core files referenced directly | Core files reached if each referenced source file moves unchanged |
|---|---:|---:|
| DataAudit | 2 | 68 |
| Hermes | 38 | 116 |
| Delphi | 4 | 181 |
| Athena | 19 | 111 |

These are conservative whole-file compile dependencies, including unused methods, co-located
types and constants. They are not runtime call counts or a list of files that must all move.
For example, DataAudit's SPY constant and DailyBar's mixed file exaggerate its real needs.
The mapping does not resolve reflection, saved-model deserialization, package trimming or all
runtime deployment requirements. Source-level package candidates and proposed assembly boundaries
require focused builds/tests after actual extraction. No package size saving is claimed here.

## Candidate acceptance checks and next decisions

Before release-pipeline implementation, settle release activation and the routine development workflow, then the
schedule and whether the dashboard action belongs in the first increment. A final accepted ADR
should supersede the affected ADR-0078 source/build rules and identify any other changed contracts.

Validation should show that package preparation failure keeps the prior release selectable, a
run uses one complete release throughout, and each run records the actual executable/source
identity. The selected package needs the existing preparation-date, stage, model-assignment and
database prerequisites. Repeated nights should reuse binaries without creating another full copy.
Cleanup must respect active users of a package, including the running collector and trading engine.

Governing references are [ADR-0078](../adr/0078-isolated-latest-source-nightly-builds.md),
[ADR-0110](../adr/0110-nightly-isolated-symbol-warnings.md),
[ADR-0113](../adr/0113-nightly-data-audit-report.md),
[ADR-0114](../adr/0114-explicit-nightly-preparation-date.md), and the
[nightly operating procedure](../running.md#guarded-nightly-pipeline).
