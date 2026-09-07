# Strategy alignment implementation checklist

**Started:** 2026-09-06. **Scope:** the [September 6 alignment audit](reviews/strategy-direction-alignment-20260906.md).
**Authority:** phased source changes, isolated tests, restores and affected-project builds are authorized.
The operator subsequently authorized private-model compatibility checks, separate replacement training,
the required model-storage/strategy database changes, and the corrected daily selection. The later
missing-Friday/SPY repair explicitly authorized schedule correction, market-data refresh and the necessary
benchmark-source/policy repair under ADR-0058. The first standalone official Delphi run was subsequently
authorized and completed on September 6 at 22:43 Eastern. Unrelated
application workflows, market collection, paper-monitor activation and broker work remain unauthorized.

This is the durable implementation record. A decision or documentation edit does not resolve a code defect.
`Validated` means the listed checks passed; operational adoption is stated explicitly where verified.
`Deferred` identifies an unfinished dependency or later phase. `Blocked` identifies a decision or external
authorization required for the specific item. Findings not yet reverified here retain their audit status only.

## Findings and dependencies

| Finding | Implementation status | Validation / remaining work | Dependency and reason |
|---|---|---|---|
| F1 — training overwrites and activates models | Implemented and validated for the supported daily training/selection workflow | Hercules defaults to separate immutable candidates and disabled rows; data/source/model evidence is retained. Stored strategy/model assignments, transactional selection events and reviewed rollback are implemented. Four candidates trained; all 228 old registry rows remain unchanged. | Legacy explicit repository mutation APIs remain for phase-3 entry-path review; complete policy dispatch/risk ownership remains D2/S1. No automatic performance promotion is introduced. |
| F2 — training/inference feature parity | Implemented, validated and adopted for the active daily strategy | One dated builder serves real training-window and prediction-input paths. The accepted fallback trained four new models because old artifact semantics could not be proven. Initial v3.3 and current v3.4 use the same four artifacts and dated contract; actual bootstrap verification passed. [Cutover review](reviews/model-input-cutover-20260906.md). | The first complete scheduled corrected run has not been exercised here. ADR-0058 now independently checks the latest expected XIU endpoint; full historical calendar continuity remains separate. Old evidence retains its legacy identity. |
| F3 — provenance reread after inference | Implemented and validated | Same captured bytes are hashed and loaded; engine carries actual instance provenance; official audit compares tasks and verifies artifact identities. Synthetic replacement, missing identity and wrong-task cases pass; actual corrected bootstrap hashes match the stored assignment. | F1 now supplies preserved files and strategy-bound selection/rollback for the supported daily workflow. This does not prove historical artifact semantics or performance superiority. |
| F4 — post-fill Live positions/exposure | Implemented and validated in source | Saved marks now use the same positions as NAV. Two full-session workflow fixtures cover a buy held through close and a full liquidation, consuming marks through account valuation and exposure reports. | Restores frozen V1 valuation; no thresholds, fill prices or historical ledger rows changed. |
| F5 — unsupported unlinked Ghost CLI entry | Deferred: phase 3 | Reverify saved-pick entry consumers before changing or retiring the route. | Preserve the monitored-position scope and operator Real workflows. |
| F6 — Hermes completion semantics | Partly implemented; broader contract deferred to phase 3 | SPY is now a required collection/validation step that fails explicitly. General per-source classification and typed completion remain unfinished. | Preserve stock-only exclusions; the schedule correction does not resolve every partial-ingestion path. |
| F7 — CI loses earlier native-command failures | Implemented and validated locally | Every focused restore/build immediately checks its exit code. Twelve native-command fixture scenarios cover all five failure positions and success for both blocks. | Hosted GitHub Actions was not invoked; broader host coverage remains S6. |
| F8 — current labels evaluate historical pending outcomes | Deferred: phase 3 | Reverify Athena dispatch and cohort definition storage before introducing immutable label dispatch. | Preserve existing definitions and pending historical evidence; semantic label changes require a new definition. |
| D1 — common comparison and approved recommendation selection | Deferred: phase 4 | Missing capability remains. No universal winner metric or recommendation routing implemented. | F1–F4, F8, D2 and comparable coverage/cost identity; guided design must reconcile ADR-0022/0053 without weakening them. |
| D2 — daily Shadow version labels do not dispatch behavior | Deferred: phases 2–3 | Reverify generation/pending-order loading, policy dispatch and persisted friction. | Immutable strategy/policy ownership; preserve V1 and all prior fills. |
| D3 — dormant broker prototype | Deferred: quarantine with phase 3 legacy-path work; execution remains future | No broker adapter implemented or exercised. Reverify public entry paths before quarantine. | Broker design/access mechanism and execution authorization remain separate; preserve manual Real tracking. |

Secondary identifiers below are local checklist identifiers; they do not rename audit findings.

| Finding | Status | Dependency / reason |
|---|---|---|
| S1 — risk-default ownership | Deferred: phase 2 | Classify strategy settings versus position-lifecycle defaults before changing any number; preserve existing positions. Existing [open question](reviews/open-questions.md) remains. |
| S2 — published/eligible source mismatch | Deferred: phase 3 | Reconcile producer, SQL selector and pure selector against frozen V1, without changing entry gates. The first official v3.4 run saved 25 entries per lens with zero eligible in either lens; all published rows are Hold. Publication is not evidence of buy eligibility. |
| S3 — Oracle cache identity and non-atomic replacement | Deferred: phase 3 bounded advisory work | Include generation settings in identity and make replacement atomic after reverifying current source; Oracle retains no trading authority. |
| S4 — UI refresh before collection / unbounded account reads | Deferred: phase 3 source work; measurements blocked on operational authority | Scheduling and bounded history reads can be isolated; real deadline/capacity claims require authorized shakedown. |
| S5 — broad Core / concrete composition | Deferred: incremental after consequential seams | Extract interfaces only where needed for a concrete workflow contract and meaningful tests. No broad module rewrite. |
| S6 — omitted focused host builds in CI | Deferred: phase 3 CI coverage | F7 preserves existing five-host coverage. Hercules, Oracle, DataAudit, Sandbox and Tools coverage remains separately tracked. |
| S7 — short sessions / calendar extension | Deferred: reviewed behavior required before 2026-12-24 | Preserve full-session V1 and the existing calendar boundary. No calendar deployment or external lookup performed. |
| S8 — actual artifacts/schema/concurrency/source reliability | Partly validated; daily schedule/data repaired, full capacity work outstanding | The missing-Friday cause was a weekday-only midnight trigger. All-seven-day execution/supervision is installed, 481 daily bars were added, and benchmark/indicator endpoints now reach September 4. Full monitor capacity and broader source completion remain separate. |
| S9 — SPY missing history treated as positive confirmation (new readiness finding) | Implemented, validated and adopted under ADR-0058 | Actual SPY collection and 503 stored observations now support the v3.4 benchmark policy. Required XIU/SPY histories are validated before evaluation; complete-data formulas and all four models are unchanged. 676 tests, actual input/bootstrap checks, reviewed selection/rollback and history preservation pass. Not an original audit finding; US-only closures fail explicitly, and independent historical US-calendar gap detection remains outside this repair. |

## Phase 1 progress and validation

- [x] Inspect Git state and preserve pre-existing modified/untracked UI, replay, documentation and solution work.
- [x] Reverify F2, F3, F4 and F7 against current source before changing their paths.
- [x] Restore F4's producer/consumer valuation contract.
- [x] Preserve every focused CI native-command failure (F7).
- [x] Bind F3 to the loaded byte snapshot and engine instances; remove the second registry/path lookup.
- [x] Confirm .NET SDK 10.0.400; run all 648 Core tests successfully (0 failed, 0 skipped).
- [x] Focused checks: 22 portfolio/action/account tests and 15 provenance/audit/lens tests pass.
- [x] CI fixture: 12 scenarios pass using only synthetic `dotnet.cmd`, never a real restore/build command.
- [x] Build affected Delphi, Hercules and WPF projects with `dotnet build <project> --no-restore`.
- [x] Implement/test one dated F2 feature-input contract and record the ADR-0056 strategy boundary.
- [x] Test all four active training/prediction tasks, nonzero relative inputs, future data, duplicate boundary
  dates, stock exclusions, shared failure, reports, reviewed identity and changed-byte rejection.
- [x] Complete the initial phase 1 source handoff with F2 explicitly staged; the later authorized cutover is recorded below.
- [x] Separately review compatibility and complete the subsequently authorized replacement training,
  registration and cutover. The old artifacts were not declared compatible merely because shapes matched.

Local validation logs and the CI fixture are under `artifacts/alignment-phase1*`. The CI fixture extracts
the actual workflow blocks and checks exit status plus the exact executed command prefix after each injected failure.
The initial phase-1 checks used synthetic quotes, ledgers and bytes without operational access. The
subsequently authorized model work below adds real artifact inspection/training and selection; no market
service, full recommendation workflow, paper monitor, broker operation, commit, push or deployment ran.

The Core test build reports compiler warnings, primarily nullable-context warnings. WPF succeeds with
two CS0168 warnings for the existing unused `MainWindow.xaml.cs` exception variable (temporary/final
project); incremental Delphi and Hercules builds report zero warnings/errors. Separately, the test
project's dependency advisories include critical `System.Drawing.Common` 5.0.0, high/moderate
`System.Data.SqlClient` 4.8.1 and moderate `System.DirectoryServices.Protocols` and
`System.Security.Cryptography.Xml` 5.0.0. No dependency remediation is claimed. Full solution/SSDT
validation was not run in the initial source-only phase. The subsequent complete Release solution build
passed with Visual Studio MSBuild/SSDT; `git diff --check` reports no whitespace errors.

## Authorized model lifecycle and cutover validation

- [x] Inspect all four actual saved model files; ML.NET loads and dimensions match, but historical input
  semantics cannot be proven from their metadata or associated experiment records.
- [x] Preserve original bytes; train four separate dated candidates with unchanged thresholds.
- [x] Retain candidate/data hashes, training/test anchor dates, exclusions, metrics and exact source archive.
- [x] Back up and verify SQL plus the local OneDrive copy; apply additive migration 026 and preserve all
  64 pre-existing table row counts. Remote backup synchronization was not independently verified.
- [x] Verify both previous/corrected sets with actual ML loaders and synthetic finite predictions.
- [x] Rehearse forward and rollback transactions with rollback; apply only the authorized forward selection.
- [x] Verify the actual active Delphi bootstrap resolves `ProfitInputs.XiuDatedV1` and four exact artifacts.
- [x] Confirm all 228 previous model rows are identical and 38 historical tables retain matching row
  counts/checksums. Exactly four candidates, two immutable assignments and one selection event were added.
- [x] Pass all 655 Core tests and the complete Release solution build including the SQL project.
- [x] Complete the later authorized read-only readiness checks: selected files/source and publication
  links pass; current-session freshness fails and SPY missing-data confirmation is recorded as S9.
- [x] Repair the verified schedule gap and refresh daily/SPY data under later operator authority. Select
  ADR-0058/v3.4 using the same four models; verify 676 tests, full solution/SSDT, active prerequisites,
  unchanged 232 model rows and 38 historical tables. No Delphi/Athena publication/evaluation ran.
- [x] Complete the separately authorized first official v3.4 Delphi run: 209 candidates, 418 lens rows,
  `audit=Valid`, four exact assigned model hashes, saved presentation and current XIU/SPY evidence. All
  50 published rows are Hold; no trade qualifies. Seven stock input exclusions and missing leadership
  movers are reported. All 38 prior evidence/account snapshots match after excluding only the new rows.
  The affected Release build passed; application source and thresholds were unchanged.
- [ ] Observe the first normal scheduled corrected cohort; the full nightly pipeline was not manually launched.

## Boundary and next step

F3 and F4 restore existing identity/valuation contracts and leave historical evidence untouched. New runs
retain their actual code provenance; no older cohort is relabelled or retroactively repaired. F4 fixes
descriptive exposure while preserving the prior NAV, guard and fill calculations. F3 does not establish
that globally enabled models form an approved complete strategy. F1 now makes the daily model assignment
explicit; common performance comparison and human-approved recommendation selection remain D1.

F2's staging boundary was superseded by the operator's explicit inspection/training/cutover request.
The active strategy now owns the corrected model assignment, while the previous strategy/files remain
available for reviewed rollback. Existing labels and historical evidence remain intact; observed XIU
dates still do not prove independent exchange-calendar completeness.

The next concrete source phase is D2/S1: make stored paper-policy versions dispatch their actual behavior
and resolve ownership of risk defaults, followed by F8's historical outcome-definition dispatch and the
remaining phase-3 contracts. Preserve frozen V1 and existing positions. Common cross-strategy comparison
and human-approved recommendation selection remain phase 4, with no new promotion criteria invented here.
