# ADR-0121: Athena builds independently of Core

- **Status:** Accepted; implemented in source, operational evaluation not exercised
- **Date:** 2026-11-01
- **Domains:** architecture, data-pipeline
- **Related:** [ADR-0120](0120-independent-hermes-build.md), [ADR-0021](0021-calibration-outcome-and-paper-execution-contract.md), [ADR-0028](0028-delayed-intraday-swing-monitor-and-exit-policy.md), [ADR-0038](0038-cohort-weighted-official-prediction-scorecards.md)

## Decision and purpose

Athena owns outcome evaluation and builds without Core. The immediate problem is copying ML and
provider dependencies into an application that evaluates saved evidence. The parent problem is
nightly applications depending on unrelated Core changes. The root goal is dependable nightly
preparation with bounded storage. This extraction reduces dependencies; release reuse and retention
remain separate work in the [release design](../reviews/nightly-precompiled-releases-20261101.md).

## Ownership and actual consumers

| Location | Responsibility and consumers |
|---|---|
| `Athena/Calibration` | Prediction, swing and delayed-intraday outcome calculators; lens reports, reason presentation and CSV exports. Only Athena uses these production workflows. |
| `Athena/Db` | Outcome definition/maturation queries and writes, plus the historical intraday outcome-bar reader. Core retains collection and daily evidence publication repositories. |
| `TraderVI.Calibration/Labels` | Canonical task settings and pure label rules shared by Athena evaluation and Core's training/inference consumers, including Hercules and Delphi. |
| `TraderVI.Calibration/Reports` and `Contracts` | Coverage/official scorecards and persisted outcome/identity shapes used by Athena and WPF/Core. |
| `TraderVI.Calibration/Db` | Read-only official prediction evidence queries used by Athena and WPF; the breakout timeline query used by Athena and Core paper monitoring. |
| `TraderVI.Calibration/Policies` | Existing delayed-intraday exit policy and fresh-breakout resolver used by Athena replay and Core paper monitoring/settings. |

Athena references TraderVI.Calibration, which references MarketStorage and transitively MarketData.
Core also references Calibration. None of those shared projects references Core. Calibration has no
ML.NET or market-provider dependency. The existing SQLBase implementation remains in MarketStorage;
the moved repositories already use its query methods. WPF now constructs OfficialPredictionEvidenceReader
instead of Athena's writer-capable CalibrationOutcomeRepository.

Existing namespaces remain, including `Core.Calibration` and `Core.ML.Engine.Profit`; namespace names
do not imply a reference to Core.dll. A source file has one compiled owner.

## Shared labels without training dependencies

ProfitTaskRegistry contains the existing enabled/disabled task definitions, label instances, horizons,
roles, weights and thresholds. A labeler is the deterministic rule that assigns the observed outcome
used for training or evaluation; it does not train or load a model. Core's ProfitModelRegistry attaches
the existing feature builders to those shared definitions. Feature construction, training and model
execution remain in Core. No enabled flag, feature mapping, formula, label parameter or model setting
changes. Historical disabled task definitions remain available in the canonical catalog.

This supersedes only ADR-0021's placement of the canonical label catalog in ProfitModelRegistry.
Both callers still use the same label instances. Outcome JSON schemas, definition IDs, SQL text,
eligibility checks, costs, timing rules, exit policies, CLI options and exit codes remain unchanged.

## Validation and installation boundary

Athena.Tests references Athena and owns the outcome/report tests. It checks the resolved dependency
graph, persisted event/labeler consistency and the existing collector-version/policy/interval query
guards. Core tests verify the four enabled production tasks, feature builders and shared label
instances, alongside the existing trading/collector regressions. Builds and tests use isolated output
folders; measured results and limits are in [project status](../project-status.md).

Moving types requires a complete rebuilt package for any future installation. Existing provenance
and execution approvals are not changed or renewed by this source extraction; a Core.dll hash alone
is not a complete package fingerprint. Release packaging must inventory all shared assemblies and
qualify the normal installation/identity checks before activation. Do not replace individual DLLs
under the running collector, engine or WPF process.

No operational application, database evaluation, provider request, model training, schedule change,
release activation or old-artifact cleanup is performed here. The installed source-capturing nightly
runner can follow the new project graph and reviewed dependency locks at its next capture. It still
builds nightly until release reuse is implemented. Changes to genuinely shared rules still require
rebuilding their consumers; unrelated Core edits no longer require rebuilding Athena.
