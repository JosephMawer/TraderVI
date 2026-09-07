# Strategy direction: repository alignment review

**Date:** 2026-09-06  
**Authority:** the operator requested documentation of the direction and a review of the entire codebase.  
**Direction:** [ADR-0055](../adr/0055-independent-strategies-to-approved-live-execution.md).  
**Method:** repository-wide static review; documentation edits only. Existing modified/untracked work was preserved.

## Verdict

The independent paper-account foundation is aligned. The complete path from competing strategies to an
approved real-account recommendation source and broker execution is not implemented yet. Delphi Live's
own policy-promotion protocol is useful and deliberately narrower than that destination.

The immediate problem is checking that direction against actual code. The parent problem is attributing
trustworthy performance to an identifiable strategy. The root goal is dependable advice followed by
explicitly authorized execution. Before using portfolio results to choose a production strategy, address
the model activation/feature/provenance issues and the post-fill exposure defect below. Preserve frozen
V1 behavior while correcting measurement and identity contracts.

This is a review of all project areas and their consequential integration paths, not a claim that every
line or numerical routine is proven correct. No current strategy was declared successful. No private
model artifacts, live database state, broker account or external service was inspected.

## Scorecard

| Dimension | Assessment | Evidence and implication |
|---|---|---|
| Domain design | Strong foundation | Daily selection, deterministic gates, calibration outcomes and independent paper accounts have distinct responsibilities. Complete-strategy adoption needs a common contract. |
| Workflow cohesion | Mixed | Live workflows expose injected stores/clock/source seams; Delphi and Hermes still combine many loading, evaluation, persistence and reporting stages. |
| Module boundaries | Mixed | Hosts use Core, and the default WPF shell avoids the legacy broker screen. Core also contains SQL, providers, ML, policies and a compiled broker prototype in one assembly. |
| Persistence integrity | Mixed | Live revisions/lease fencing and atomic daily publication/opening are strengths. Model activation is non-atomic; exact loaded-artifact binding and some stored policy semantics remain incomplete. |
| Automated change safety | Mixed | Substantial policy, timing and source-contract tests exist. Model activation/feature parity and workflow-produced exposure have gaps; batched CI commands can hide an earlier failure. |
| Documentation | Mixed, improved by this review | ADRs preserve behavior and authorization boundaries. This review records the broader accepted direction and distinguishes implemented V1 from future capabilities. |
| Operational/broker readiness | Not assessed at runtime | Source exposes blockers. A prior successful build or replay does not establish live collection reliability or broker readiness. |

## Direction gaps

### D1. The common portfolio list is not a common strategy competition or promotion route

**Confirmed; static evidence sufficient. Missing capability, not a frozen-V1 regression.**

Daily Shadow has four fixed basket definitions in [SystemShadowLedger.cs:17](../../Core/Trader/SystemShadowLedger.cs#L17).
Live experiments accept Live policy identifiers and three threshold families in
[DelphiLiveExperimentPolicy.cs:12](../../Core/Trader/DelphiLive/DelphiLiveExperimentPolicy.cs#L12);
`ValidateOneFamily` intentionally rejects other changes. Their comparison creation writes only Live
ledgers in [DelphiLiveExperimentRepository.cs:286](../../Core/Db/DelphiLiveExperimentRepository.cs#L286).
The Portfolios view simply combines separate read models in
[PortfoliosViewModel.cs:114](../../TraderVI.WPF/Viewmodels/PortfoliosViewModel.cs#L114).
The daily recommendation reader still loads daily picks in
[DelphiPublishedRecommendationReader.cs:22](../../Core/Runtime/DelphiPublishedRecommendationReader.cs#L22).
There is no shared selection consumed by these paths to route the winning complete strategy to Real recommendations.

Raw account fields are not yet a fair contest: daily Shadow uses 0.25% friction per side
([SystemShadowPolicy.cs:21](../../Core/Trader/SystemShadowPolicy.cs#L21)); Live uses causal bid/ask or
tagged estimated fills ([DelphiLiveExecutionPolicy.cs:101](../../Core/Trader/DelphiLive/DelphiLiveExecutionPolicy.cs#L101)).
Start dates, capital, marks and coverage can also differ. Define complete-strategy identity, comparison
eligibility, human selection and account-specific recommendation routing without changing either V1
policy or treating their present balances as a universal leaderboard.

### D2. Daily Shadow records a policy label but executes current code defaults

**Confirmed; static evidence sufficient for the future change hazard. No incorrect current V1 fill asserted.**

[SystemShadowRepository.cs:145](../../Core/Db/SystemShadowRepository.cs#L145) saves the generation's policy
version; order creation saves friction at line 876. Runtime loading omits the version
([line 585](../../Core/Db/SystemShadowRepository.cs#L585)) and pending-order loading omits friction
([line 772](../../Core/Db/SystemShadowRepository.cs#L772)). Filling calls current policy defaults
([line 915](../../Core/Db/SystemShadowRepository.cs#L915)), including sizing at lines 983 and 1019.
[ShadowPortfolioGeneration.sql](../../TraderDB/dbo/Tables/ShadowPortfolioGeneration.sql) carries a label,
not an executable immutable settings/code manifest.

That is workable for one fixed V1, but editing defaults can change an existing account's behavior despite
its old version label. Bind runtime behavior and fill assumptions to each generation before running or
promoting multiple complete versions. Preserve existing ledger history.

### D3. Wealthsimple code is a dormant prototype, not an execution-ready adapter

**Confirmed; static evidence sufficient. Broker access feasibility and behavior were not assessed.**

[TradeManager.cs:175](../../Core/Trader/TradeManager.cs#L175) only logs that live routing is unavailable.
Its public constructor permits `ghost=false`, but BUY has already committed the tracked ledger at lines
63–82 before `PlaceOrder` returns at lines 84–94. All discovered active callers use Ghost mode.
The actual order request in [WSTrade.cs:200](../../Core/WSTrade/WSTrade.cs#L200) is commented out; the
prototype also contains fixed account assumptions. The legacy
[MainWindow.xaml.cs:305](../../TraderVI.WPF/MainWindow.xaml.cs#L305) has authentication and plaintext token
persistence. It is not the startup screen: [App.xaml:5](../../TraderVI.WPF/App.xaml#L5) starts the paper dashboard.
No current real order execution was found or performed.

Quarantine or replace the prototype before broker work. A future adapter needs approved order intents,
acknowledgements, rejections, partial fills, fees and reconciliation against real account state. A security
identifier lookup alone is not the missing integration. Account/authentication examples were not copied
into this report.

## Correctness and integrity findings

### F1. High priority: training implicitly activates replacement strategy inputs and overwrites artifacts

**Confirmed; static evidence sufficient for the coupling and overwrite risk. Current artifact/registry
state was not inspected. Training/promotion separation is an unchanged roadmap gap.**

[Hercules Program.cs:115](../../ML.Train/Program.cs#L115) chooses a fixed ZIP path per task.
[UnifiedProfitTrainer.cs:503](../../Core/ML/Engine/Profit/UnifiedProfitTrainer.cs#L503) saves there and returns
training success. Hercules records `Keep` and calls `InsertModel(isEnabled: true)` at
[lines 167 and 185](../../ML.Train/Program.cs#L167).
[ModelRegistryRepository.cs:78](../../Core/Db/ModelRegistryRepository.cs#L78) disables the prior enabled
model before a separate insert at line 121. Delphi then loads globally enabled rows in
[DelphiBootstrap.cs:23](../../Core/Runtime/DelphiBootstrap.cs#L23), independently of the daily strategy
identity loaded in [DelphiWorkflow.cs:79](../../Core/Runtime/DelphiWorkflow.cs#L79).
The SQL [StrategyVersionModel](../../TraderDB/dbo/Tables/StrategyVersionModel.sql) association has no
source-level C# consumer found in this review.

A successful training operation can change the next recommendations without paper-strategy evaluation
and explicit promotion. Reused paths also mean old registry rows need not retain the original bytes for
rollback; interrupted activation can leave an incomplete active set. Create immutable candidate artifacts,
separate training/evaluation from activation, and atomically activate a reviewed strategy-bound manifest.
No source-level tests referencing bootstrap/model activation or the trainer boundary were found.

### F2. High priority: training and inference supply different inputs to BreakoutEnhanced

**Confirmed source mismatch; runtime validation required to determine the impact on current model artifacts.**

[ProfitModelRegistry.cs:112](../../Core/ML/Engine/Profit/ProfitModelRegistry.cs#L112) enables BreakoutEnhanced
with `EnhancedFeatureBuilder`. [Hercules Program.cs:124](../../ML.Train/Program.cs#L124) supplies XIU market
bars during training, passed through [EnhancedFeatureBuilder.cs:23](../../Core/ML/Engine/Patterns/Features/EnhancedFeatureBuilder.cs#L23).
The nested [TrendMomentumFeatureBuilder.cs:176](../../Core/ML/Engine/Patterns/Features/TrendMomentumFeatureBuilder.cs#L176)
computes relative-performance feature slots 22/23 when those bars exist and otherwise leaves them zero.
Inference builds features directly in [UnifiedProfitSignalModel.cs:85](../../Core/ML/Engine/Profit/UnifiedProfitSignalModel.cs#L85).
Searches of Core and the inference hosts found no inference-side assignment of `MarketBars`.

Define one explicit, dated input contract for training and inference, then test feature parity on the same
observations. These are existing embedded market-relative features, separate from the planned database RS
feature integration. This review does not assert which features the current ZIP used or quantify a change
in predictions. No source-level test for this training/inference boundary was found.

### F3. Medium priority: model provenance is reread after inference

**Confirmed; static evidence sufficient for the missing identity binding. No affected stored run demonstrated.**

[DelphiWorkflow.cs:118](../../Core/Runtime/DelphiWorkflow.cs#L118) loads the engine; model bytes are read at
[UnifiedProfitSignalModel.cs:45](../../Core/ML/Engine/Profit/UnifiedProfitSignalModel.cs#L45).
Later, [DelphiWorkflow.cs:1039](../../Core/Runtime/DelphiWorkflow.cs#L1039) asks
[CalibrationProvenance.cs:30](../../Core/Calibration/CalibrationProvenance.cs#L30) to reread enabled rows and
hash their current paths. [CalibrationRunAuditPolicy.cs:25](../../Core/Calibration/CalibrationRunAuditPolicy.cs#L25)
checks completeness by count, not correspondence with the engine's loaded instances.

Concurrent artifact/registry replacement can therefore stamp evidence with a different model identity
while retaining the expected count. Resolve, hash and load one immutable manifest and carry that exact
identity through the run. An ordinary missing-model count is separately rejected before official
publication; this finding is about an apparently complete but mismatched identity.

### F4. Medium priority: post-fill Live marks contain pre-fill positions

**Confirmed; static evidence sufficient. A descriptive exposure defect, not evidence of incorrect cash or
promotion-return calculations. No workflow was run.**

After a fill, [DelphiLiveActionWorkflow.cs:324](../../Core/Trader/DelphiLive/DelphiLiveActionWorkflow.cs#L324)
records a checkpoint mark. `CurrentNav` at line 417 filters actual holdings and supplies checkpoint prices
for new buys, but `CheckpointMark` stores the original `input.ExactCheckpointMarks` at
[line 409](../../Core/Trader/DelphiLive/DelphiLiveActionWorkflow.cs#L409).
[DelphiLivePortfolioScorecard.cs:55](../../Core/Trader/DelphiLive/DelphiLivePortfolioScorecard.cs#L55) sums
those saved positions over the recomputed net asset value (NAV: cash plus marked holdings).

A sale can leave sold exposure in the saved checkpoint; a buy omits the new exposure until a later cycle.
The advanced view labels this “Mean checkpoint exposure.” Persist the same normalized position marks used
for NAV. Add a producer-to-consumer regression for a buy and full liquidation; current scorecard fixtures
at [DelphiLivePortfolioScorecardTests.cs:77](../../TraderVI.Core.Tests/DelphiLivePortfolioScorecardTests.cs#L77)
do not cover this workflow-produced relationship. The newer account read model rejects mismatched marks,
which can also leave a recently traded account without a usable displayed valuation until the next mark.

### F5. Medium priority: the legacy CLI creates Ghost positions the supported monitor excludes

**Confirmed; static evidence sufficient. No current orphaned position asserted.**

[TraderVI Program.cs:77](../../TraderVI/Program.cs#L77) calls `TradeManager.Buy` without a saved pick.
[TradeManager.cs:43](../../Core/Trader/TradeManager.cs#L43) defaults to an unlinked Ghost entry, accepted by
[TrackedPositionOpeningRepository.cs:179](../../Core/Db/TrackedPositionOpeningRepository.cs#L179).
But [TrackedPositionScope.cs:13](../../Core/Trader/TrackedPositionScope.cs#L13) excludes unlinked Ghost,
and [PaperTradingMonitor.cs:73](../../Core/Trader/PaperTradingMonitor.cs#L73) and
[PaperDashboardViewModel.cs:180](../../TraderVI.WPF/Viewmodels/PaperDashboardViewModel.cs#L180) apply that scope.

The advertised CLI route can create a lifecycle outside the monitored, provenance-backed paper path.
Route it through the supported saved-pick entry contract or explicitly retire it; do not weaken the scope
guard merely to display unsupported entries.

### F6. Medium priority: Hermes stage failures can be classified as successful completion

**Confirmed; static evidence sufficient. Existing completion-semantics gap; no failed current run inferred.**

[Hermes Program.cs:221](../../Hermes/Program.cs#L221) catches US-index failures without propagating failure;
sector failures are counted and printed at [line 645](../../Hermes/Program.cs#L645). The program can still
reach its post-run backup and return normally. The nightly runner's failure patterns in
[nightly-runner.json:28](../../Operations/nightly-runner.json#L28) do not match these messages, and
[Invoke-TraderVINightly.ps1:395](../../Operations/Invoke-TraderVINightly.ps1#L395) accepts exit zero as success.

This can permit the next Delphi stage after incomplete ingestion. Downstream freshness/missingness gates
remain useful, but successful backup creation does not prove all source stages completed. Introduce typed
complete/degraded/failed outcomes with explicit required-source handling instead of relying on log wording.

### F7. Medium priority: batched CI commands do not preserve every command failure

**Confirmed source omission; likely false-green consequence under ordinary PowerShell native-command
semantics. Runner behavior was not exercised.**

[dotnet-ci.yml:33](../../.github/workflows/dotnet-ci.yml#L33) batches five restores and
[line 42](../../.github/workflows/dotnet-ci.yml#L42) batches five builds without per-command exit checks.
An earlier application-only failure can be superseded by the final WPF command's exit status. The SSDT
step correctly checks its own exit code at line 85. Use separate steps or a checked loop. CI currently
covers Core tests and five application builds; Oracle, Hercules, DataAudit, Sandbox and the two Tools
projects have no focused build in this workflow. Their omission is a coverage limit, not a failure claim.

### F8. Medium priority before label changes: historical outcomes use current label definitions

**Confirmed future change hazard; static evidence sufficient. No historical mislabelling asserted.**

[CalibrationOutcomeRepository.cs:114](../../Core/Db/CalibrationOutcomeRepository.cs#L114) retrieves pending
official candidates across strategy identities. [Athena Program.cs:185](../../Athena/Program.cs#L185)
evaluates them using [PredictionOutcomeCalculator.cs:161](../../Core/Calibration/PredictionOutcomeCalculator.cs#L161),
which iterates current `ProfitModelRegistry.All` labelers under the fixed `PredictionLabels10` version 1
definition. A future label edit can change the meaning of outcomes for still-pending historical cohorts.

Resolve an immutable outcome/label definition for each cohort and version semantic changes. Current
unchanged labels avoid this trigger; the contract does not enforce that preservation itself. Official
comparative reports do positively scope one active strategy and do not pool older identities.

## Other retained findings and changeability concerns

| Concern | Evidence, confidence and consequence | Next treatment |
|---|---|---|
| Risk-default ownership | Confirmed/static: [StrategyConfig.cs:87](../../Core/Trader/StrategyConfig.cs#L87) uses 0.35 downside veto; [DownProbabilityGate.cs:6](../../Core/Trader/Gates/DownProbabilityGate.cs#L6) and repository insertion default use 0.20. Config warning is −5%; [TradeManager.cs:16](../../Core/Trader/TradeManager.cs#L16) uses −8%. Delphi passes explicit config, so this is not proof the active veto uses the fallback. | Unchanged September 1 open question. Classify/version settings before any normalization; preserve old positions. |
| Published/eligible source contract | Confirmed/static: [DelphiLiveFrozenSource.cs:178](../../Core/Trader/DelphiLive/DelphiLiveFrozenSource.cs#L178) rejects published-but-ineligible evidence; [DelphiLiveSessionRepository.cs:271](../../Core/Db/DelphiLiveSessionRepository.cs#L271) filters eligible rows; [DelphiLivePreviewRepository.cs:31](../../Core/Db/DelphiLivePreviewRepository.cs#L31) includes all published rows. | Retain the existing open question. Reconcile the producer/selector contracts explicitly without retuning entry gates. |
| Oracle cache identity/publication | Confirmed/static: [DossierPromptBuilder.cs:295](../../Core/Oracle/Prompts/DossierPromptBuilder.cs#L295) hashes prompt text, and [Oracle Program.cs:190](../../Oracle/Program.cs#L190) reuses a matching hash without provider/model checks. Replacement deletes before separately inserting at line 213. | Lower-priority advisory provenance/reliability: include generation settings in identity and replace atomically. No source-level cache test reference found. Oracle remains downstream of trading. |
| UI work precedes collection ticks | Confirmed ordering; operational impact unmeasured: [PaperDashboardWindow.xaml.cs:56](../../TraderVI.WPF/PaperDashboardWindow.xaml.cs#L56) awaits dashboard/portfolio refresh before Live tick. [DelphiLivePortfolioReader.cs:26](../../Core/Db/DelphiLivePortfolioReader.cs#L26) reads full snapshots for all generations each refresh. | Measure during authorized shakedown. Before scaling/always-on hosting, decouple presentation refresh from scheduling and bound historical reads. No current missed deadline inferred. |
| Broad Core and concrete composition | Confirmed/static: [Core.csproj](../../Core/Core.csproj) includes persistence, providers, ML and policies; [SystemShadowController.cs](../../Core/Trader/SystemShadowController.cs) constructs concrete repositories/providers. Several view models directly construct repositories. | Protect consequential seams first, then extract interfaces/modules where they reduce changes and enable workflow tests. File size alone is not a defect. |

## Strengths to preserve

- **Independent ownership:** [DelphiLiveLedgerContracts.cs:164](../../Core/Trader/DelphiLive/DelphiLiveLedgerContracts.cs#L164)
  checks immutable account identity, cash/fill reconciliation and position ownership. The
  [holding source](../../Core/Db/DelphiLiveHoldingSource.cs#L17) can observe other account types without
  granting action authority. Separate SQL ledgers retain daily Shadow, Live, Ghost/Real and research evidence.
- **Causal Live actions:** [DelphiLiveActionWorkflow.cs:94](../../Core/Trader/DelphiLive/DelphiLiveActionWorkflow.cs#L94)
  handles protective sells before buys, persists decisions before fresh quote attempts, and commits fills
  with own-account cash/positions. Lease and expected-revision fencing protect durable commits. Action and
  monitor tests exercise ordering, retry/restart behavior, persistent exits and cash protection.
- **Deliberate Live promotion:** [DelphiLiveExperimentWorkflow.cs:124](../../Core/Trader/DelphiLive/DelphiLiveExperimentWorkflow.cs#L124)
  and its policy enforce engineering, paired discovery and untouched confirmation evidence plus explicit
  human action. Comparisons begin as equal-capital cash-only accounts. Promotion preserves operational
  holdings and immutable history. Passing evidence alone cannot issue the human command
  ([test](../../TraderVI.Core.Tests/DelphiLiveExperimentPolicyTests.cs#L97)).
- **Rule/ML separation and consistent lenses:** [DelphiBootstrap.cs:29](../../Core/Runtime/DelphiBootstrap.cs#L29)
  creates deterministic patterns directly. [TradeDecisionEngine.cs:178](../../Core/Trader/TradeDecisionEngine.cs#L178)
  shares evaluated facts across independent lens gates; the [lens test](../../TraderVI.Core.Tests/TradeDecisionEngineLensTests.cs#L17)
  characterizes this behavior. Relative-strength calculations use canonical dated endpoints and retain
  missingness ([calculator](../../Core/RelativeStrength/RelativeStrengthCalculator.cs#L35)); leadership
  acquisition separately preserves unavailable source facts.
- **Official publication boundary:** [CalibrationEvidenceRepository.cs:338](../../Core/Db/CalibrationEvidenceRepository.cs#L338)
  rejects invalid official evidence before [operational publication](../../Core/Runtime/DelphiWorkflow.cs#L1127).
  [DelphiOperationalPublicationRepository.cs:63](../../Core/Db/DelphiOperationalPublicationRepository.cs#L63)
  replaces same-day projections atomically, including a valid empty result. Tracked entry similarly
  commits BUY plus position together. Source contract tests cover these boundaries.
- **Real fills remain operator-reported:** [TrackedExecutionMode.cs:58](../../Core/Trader/TrackedExecutionMode.cs#L58)
  and the monitor allow automatic exits only for Ghost. Real holdings never become official calibration
  outcomes. The default WPF screen presents manual reconciliation rather than the legacy broker prototype.
- **Read surfaces do not activate accounts:** [DelphiLivePortfolioReader.cs:12](../../Core/Db/DelphiLivePortfolioReader.cs#L12)
  is SELECT-only; Live selections are blocked from daily Shadow commands. Replay remains a local estimated
  account. Incomplete/superseded marks are not silently replaced with invented valuations.
- **Operational containment:** the nightly runner checks source/artifact stability and single-instance
  execution. [TraderDbBackupService.cs:119](../../Core/Db/TraderDbBackupService.cs#L119) uses checksum backup,
  verification, no-overwrite copies and matching hashes. SSDT CI builds only; deployment is disabled in
  [TraderDB.sqlproj:165](../../TraderDB/TraderDB.sqlproj#L165). These are source findings, not new operational tests.

## Representative change traces

| Intended change | Necessary path | Avoidable change amplification / missing contract |
|---|---|---|
| Train a replacement profit model | Hercules → feature/label definition → artifact → registry → Delphi bootstrap → evidence | Today fitting, overwrite and activation are coupled, and provenance is reread. A single immutable strategy/model manifest should connect these stages. |
| Compare a new complete paper strategy | Selection + policy + account generation → fills/marks → evidence → review → recommendation assignment | Daily version strings do not select runtime code; Live accepts only its three V1 families; no common comparison/routing contract. Preserve the useful per-family engines behind an explicit future adoption boundary. |
| Move monitoring to another host | Core workflow → SQL stores + reviewed calendar + clock/provider + scheduler | Live's injected workflows are an exemplar. Desktop composition and UI scheduling still need separation; daily controller has concrete dependencies and no source-level controller test found. |
| Add approved broker execution | Selected strategy + actual account state → approved order intent → adapter → acknowledgements/fills → reconciliation | Legacy boolean Ghost mode and log-only success cannot supply this contract. Paper receipts/fills must not become actual execution facts. |

## Stabilization order

1. Correct evidence/measurement defects with focused tests: F2 feature parity, F3 loaded identity, F4
   post-fill marks and F7 per-command CI results. Determine the appropriate strategy/outcome version
   boundary for semantic corrections; do not relabel historical results. F1's artifact overwrite means
   any newly authorized training work must first address immutable candidate publication/activation.
2. Establish complete version ownership: F1 activation separation, D2 daily runtime dispatch, F8 outcome
   definitions and risk-default ownership. Align source contracts and retire unsupported legacy entry paths.
3. Continue separately authorized observation/shakedown and fix F6 completion semantics. Resolve short
   sessions/calendar coverage before December 24. A historical replay supplies no clean live timing cohort.
4. Design fair comparisons across families and a durable human-approved recommendation selection using
   actual target-account state. Reconcile ADR-0022/0053 without weakening their existing evidence rules.
5. Build and independently validate the future broker boundary, then request separate execution authority.
   Address broader module extraction and advisory-only cache cleanup in bounded work alongside these phases.

## Coverage, inventory and prior baseline

Inventory used `rg --files --hidden` for `.cs`, `.xaml`, `.sql`, `.ps1`, `.yml/.yaml`, `.csproj` and
`.sqlproj`, excluding `.git`, `artifacts`, `bin`, `obj` and migration history. It includes current untracked
source and dormant/commented code. Physical lines include comments and blanks; these metrics are navigation
aids, not active-code size or quality scores. Documentation, JSON configuration, solution wiring and
relevant migration contracts were reviewed separately rather than included in these counts.

| Area | Files | Physical lines | Review depth |
|---|---:|---:|---|
| Core | 281 | 49,258 | Deep traces of selection, model load/train/provenance, calibration/publication, Shadow/Live actions, ledger/version/promotion and provider contracts; supporting indicator/model/helper inventories and targeted inspection. |
| TraderVI.WPF | 40 | 8,152 | Startup/scheduling, recommendation/portfolio projections, account command guards, promotion controls, manual Real flows, replay and dormant broker screen. |
| TraderVI.Core.Tests | 65 | 11,549 | Source-level coverage mapped to consequential contracts; no tests executed in this review. |
| TraderDB | 76 | 2,794 | Runtime/table/version/ledger relationships and SQL deployment boundary; installed schema not verified. |
| Delphi | 3 | 190 | Entire host composition and shared-workflow boundary. |
| ML.Train / Hercules | 2 | 227 | Entire host composition and active training/registry/artifact consumers. |
| Athena | 2 | 633 | Outcome selection/calculation and official report scope. |
| Hermes | 2 | 1,101 | Full stage composition, source failure handling and backup boundary. |
| TraderVI | 3 | 492 | CLI dispatch, entry/exit, paper-monitor and legacy execution consumers. |
| Oracle | 2 | 381 | Provider composition, cache/provenance, narrative persistence and lack of decision authority. |
| DataAudit | 2 | 129 | Host/workflow/read-only repository. |
| Sandbox | 15 | 2,123 | Probe registry, explicit side effects, replay/collection isolation and supporting probe contracts. |
| Tools | 4 | 584 | Both backfill/backtest hosts; provider and commit-mode boundaries. |
| Operations | 3 | 710 | Nightly runner, source/artifact checks, scheduling and status handling; JSON read separately. |
| .github | 1 | 90 | Test/build steps, coverage and build-only database job. |
| SQL Server | 1 | 26 | Ancillary script inventory; no execution. |
| **Total** | **502** | **78,439** | **13 C# projects and one SQL project mapped.** |

All C# hosts reference Core; it is the main dependency hub. Largest areas included the 1,519-line daily
Shadow repository, 1,374-line Delphi workflow and 1,088-line Hermes host. The 891-line legacy TMX file is
commented out; its line count is not active provider complexity. Supporting numerical routines were not
individually re-derived, and this report should not be read as exhaustive mathematical verification.

The September 1 audit is referenced in roadmap/open questions, but no standalone baseline report was
located. A complete historical finding-by-finding or numeric trend claim is therefore unavailable.

| Prior concern | Current assessment |
|---|---|
| Undated minimum-length RS alignment | Improved/resolved in the reviewed path: dated canonical endpoints and regression source exist under ADR-0041. |
| Leadership missingness | Improved/resolved in reviewed acquisition/scoring contracts under ADR-0043. |
| Repeated lens scoring | Improved/resolved in reviewed path: shared facts and direct lens characterization. |
| Daily publication / position-opening atomicity | Improved/resolved in reviewed paths under ADR-0048; model/narrative publication are separate remaining concerns. |
| Hercules activation separation, Hermes completion, risk defaults | Unchanged known gaps, now traced to concrete producers/consumers. |
| CI and build-only SSDT | Improved coverage; F7 remains a source-level failure-propagation gap. |
| Delphi Live, daily Shadow and common strategy direction | Newly assessed surfaces/direction; not claimed as regressions against a baseline predating them. |
| Full deployed-schema/artifact/runtime equivalence | Unable to verify in this static review. |

## Validation and limits

Only source/document reads, inventory, static call/data-flow tracing and documentation checks were performed.
All 83 local links in the three new documents resolved, cited source lines were within file bounds, and
all three documents were registered in the solution. `git diff --check` found no whitespace errors; Git
reported its existing LF-to-CRLF conversion notices. The frozen Delphi Live concept has no diff.
The earlier 600 passing Core tests and successful WPF/solution builds are historical evidence recorded in
project status; they were not rerun and do not resolve these findings. No benchmark, workflow, SQL query,
schema deployment, migration, market request, model training/artifact inspection, broker operation, commit
or push was performed by this review. Dependency-security advisories previously reported remain separate
from compiler warnings; no dependency audit or new build result is claimed here.

Before operational adoption, separately authorized validation must establish actual artifact feature parity,
installed-schema contracts, SQL concurrency/recovery, live source freshness and capacity, calendar coverage,
and eventually broker execution/reconciliation. Source evidence identifies the work; it does not establish
that any existing private portfolio or stored historical cohort is corrupted.
