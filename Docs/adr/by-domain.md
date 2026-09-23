# ADRs Grouped by Domain

Manually maintained. Every ADR must appear under each tag declared in its header.

## architecture

- [ADR-0084](0084-supervised-broker-strategy-session.md) — Durable supervised broker session and original-order recovery

- [ADR-0083](0083-opening-account-values-and-shared-engine-hosting.md) — Shared morning account-value recovery for a future scheduled engine

- [ADR-0082](0082-compact-live-snapshots-and-backup-retention.md) — Lossless compact portfolio revisions and two verified backups per location

- [ADR-0079](0079-broker-reconciliation-and-market-handover.md) — Protected broker-history reconstruction before SQL and execution integration
- [ADR-0080](0080-reviewed-broker-history-import.md) — Reviewed account binding and append-only SQL broker observations
- [ADR-0081](0081-broker-account-baselines-and-protection-ownership.md) — Initial account risk references and protection ownership at activation

- [ADR-0078](0078-isolated-latest-source-nightly-builds.md) — Verified current-source copies and locked isolated builds

- [ADR-0075](0075-manual-market-buy-ticket.md) — Shared manual ticket, protected order journal and recovery
- [ADR-0076](0076-wealthsimple-market-sell-protocol.md) — Shared buy/sell transport and compatible attempt protection
- [ADR-0077](0077-manual-holdings-market-sell.md) — Shared protected manual buy/sell journal and remaining-holdings verification

- [ADR-0074](0074-wealthsimple-market-buy-protocol.md) — Market-buy protocol and attempt recovery

- [ADR-0073](0073-permanent-tfsa-and-whole-share-execution.md) — Permanent TFSA and whole-share execution

- [ADR-0072](0072-wealthsimple-tfsa-account-display.md) — Read-only TFSA landing page

- [ADR-0071](0071-native-wealthsimple-login.md) — Native Wealthsimple sign-in

- [ADR-0070](0070-wealthsimple-desktop-sign-in.md) — Wealthsimple desktop sign-in and session status

- [ADR-0069](0069-direct-wealthsimple-authentication.md) — Direct Wealthsimple authentication

- [ADR-0067](0067-fixed-gate-paper-comparison-pairs.md) — Fixed-gate paper comparison pairs

- [ADR-0063](0063-historical-practice-environment-direction.md) — Historical practice environment direction

- [ADR-0060](0060-central-settings-and-scoped-configuration.md) — Central settings and scoped configuration

- [ADR-0059](0059-delphi-model-and-threshold-settings.md) — Delphi model-set and threshold settings

- [ADR-0058](0058-daily-ingestion-and-observed-benchmark-confirmation.md) — Daily ingestion and observed benchmark confirmation

- [ADR-0057](0057-preserved-model-sets-and-explicit-selection.md) — Preserved model sets and explicit selection

- [ADR-0056](0056-dated-profit-model-input-contract.md) — Dated profit-model input contract

- [ADR-0055](0055-independent-strategies-to-approved-live-execution.md) — Independent strategies to approved live execution

- [ADR-0001](0001-granville-plugin-architecture.md) — Granville indicator plug-in architecture
- [ADR-0013](0013-multi-lens-decision-architecture.md) — Multi-lens decision architecture
- [ADR-0015](0015-trade-logging-ghost-execution-and-position-lifecycle.md) — Manual trade logging and position lifecycle
- [ADR-0017](0017-codex-native-instructions-and-project-status.md) — Codex-native instructions and project-status structure
- [ADR-0018](0018-manual-migrations-and-simple-recovery-backups.md) — Manual migrations and SIMPLE-recovery backups
- [ADR-0020](0020-immutable-calibration-evidence-ledger.md) — Immutable calibration evidence ledger
- [ADR-0021](0021-calibration-outcome-and-paper-execution-contract.md) — Calibration outcome and paper-execution contract
- [ADR-0023](0023-primary-swing-policy-and-experimental-opening-confirmation.md) — Primary swing policy and experimental opening confirmation
- [ADR-0024](0024-coverage-scorecard-and-market-session-cohorts.md) — Coverage scorecard and market-session cohort identity
- [ADR-0025](0025-three-session-swing-mark-to-market-outcome.md) — Three-session swing mark-to-market outcome
- [ADR-0026](0026-three-session-swing-excursion-measures.md) — Three-session swing excursion measures
- [ADR-0027](0027-lens-tradeability-scorecards-and-cohort-aggregation.md) — Lens tradeability scorecards and cohort aggregation
- [ADR-0028](0028-delayed-intraday-swing-monitor-and-exit-policy.md) — Delayed intraday swing monitor and exit policy
- [ADR-0029](0029-intraday-ghost-entry-pilot.md) — Intraday ghost-entry pilot
- [ADR-0030](0030-intraday-evidence-and-poll-audit-ledger.md) — Intraday evidence and poll-audit ledger
- [ADR-0031](0031-automatic-ghost-exit-execution.md) — Automatic policy-triggered ghost exit execution
- [ADR-0032](0032-live-paper-trading-wpf-dashboard.md) — Live paper-trading WPF dashboard
- [ADR-0033](0033-tabbed-desktop-shell-and-shared-workflows.md) — Tabbed desktop shell and shared application workflows
- [ADR-0034](0034-delphi-shared-workflow-and-desktop-tab.md) — Delphi shared workflow and desktop tab
- [ADR-0035](0035-delphi-operator-workspace-and-presentation-snapshot.md) — Delphi operator workspace and presentation snapshot
- [ADR-0036](0036-native-project-documentation-reader.md) — Native read-only project documentation reader
- [ADR-0037](0037-operator-confirmed-delphi-paper-entry.md) — Operator-confirmed Delphi pick to paper position
- [ADR-0038](0038-cohort-weighted-official-prediction-scorecards.md) — Cohort-weighted official prediction scorecards
- [ADR-0039](0039-unified-ghost-real-dashboard-and-scorecard-workspace.md) — Unified Ghost/Real dashboard and scorecard workspace
- [ADR-0040](0040-reconstructible-delayed-intraday-outcome-fill.md) — Reconstructible delayed-intraday outcome fill
- [ADR-0042](0042-strategy-code-identity-and-scoped-official-evidence.md) — Strategy/code identity and scoped official evidence
- [ADR-0044](0044-unlinked-real-position-monitoring.md) — Unlinked Real position monitoring
- [ADR-0045](0045-five-minute-collection-and-nonoverlapping-intraday-chunks.md) — Five-minute collection and non-overlapping intraday chunks
- [ADR-0046](0046-guarded-nightly-operations-runner.md) — Guarded nightly operations runner
- [ADR-0047](0047-discretionary-real-entry-from-non-buy-row.md) — Discretionary Real entry from a non-Buy Delphi row
- [ADR-0048](0048-consequential-operational-transaction-boundaries.md) — Consequential operational transaction boundaries
- [ADR-0049](0049-canonical-database-write-contracts.md) — Canonical database write contracts
- [ADR-0050](0050-build-only-ssdt-ci-boundary.md) — Build-only SSDT CI boundary
- [ADR-0051](0051-system-shadow-portfolios.md) — System-selected Shadow V1 portfolios
- [ADR-0052](0052-working-tree-state-is-provenance-not-evidence-quality.md) — Working-tree state is provenance, not evidence quality
- [ADR-0053](0053-delphi-live-v1.md) — Delphi Live V1

- [ADR-0054](0054-delphi-live-preview-and-historical-replay.md) — Delphi Live preview and historical replay

## data-pipeline

- [ADR-0078](0078-isolated-latest-source-nightly-builds.md) — Verified current-source copies and locked isolated builds

- [ADR-0063](0063-historical-practice-environment-direction.md) — Historical practice environment direction

- [ADR-0058](0058-daily-ingestion-and-observed-benchmark-confirmation.md) — Daily ingestion and observed benchmark confirmation

- [ADR-0057](0057-preserved-model-sets-and-explicit-selection.md) — Preserved model sets and explicit selection

- [ADR-0056](0056-dated-profit-model-input-contract.md) — Dated profit-model input contract

- [ADR-0005](0005-defer-granville-dullness-21-22.md) — Defer Granville Dullness indicators
- [ADR-0007](0007-liquidity-floor-universe-filter.md) — Liquidity floor on Delphi's universe
- [ADR-0009](0009-exclude-leveraged-inverse-etps-from-delphi-universe.md) — Exclude leveraged/inverse ETPs
- [ADR-0012](0012-sector-index-historical-backfill.md) — Sector-index historical backfill
- [ADR-0016](0016-obv-per-symbol-soft-ranking-signal.md) — OBV as a per-symbol soft ranking signal
- [ADR-0018](0018-manual-migrations-and-simple-recovery-backups.md) — Manual migrations and SIMPLE-recovery backups
- [ADR-0019](0019-delphi-strict-history-freshness-eligibility.md) — Delphi strict history-freshness eligibility
- [ADR-0020](0020-immutable-calibration-evidence-ledger.md) — Immutable calibration evidence ledger
- [ADR-0024](0024-coverage-scorecard-and-market-session-cohorts.md) — Coverage scorecard and market-session cohort identity
- [ADR-0030](0030-intraday-evidence-and-poll-audit-ledger.md) — Intraday evidence and poll-audit ledger
- [ADR-0032](0032-live-paper-trading-wpf-dashboard.md) — Live paper-trading WPF dashboard
- [ADR-0033](0033-tabbed-desktop-shell-and-shared-workflows.md) — Tabbed desktop shell and shared application workflows
- [ADR-0034](0034-delphi-shared-workflow-and-desktop-tab.md) — Delphi shared workflow and desktop tab
- [ADR-0035](0035-delphi-operator-workspace-and-presentation-snapshot.md) — Delphi operator workspace and presentation snapshot
- [ADR-0039](0039-unified-ghost-real-dashboard-and-scorecard-workspace.md) — Unified Ghost/Real dashboard and scorecard workspace
- [ADR-0040](0040-reconstructible-delayed-intraday-outcome-fill.md) — Reconstructible delayed-intraday outcome fill
- [ADR-0041](0041-date-aligned-relative-strength-inputs.md) — Date-aligned relative-strength inputs
- [ADR-0042](0042-strategy-code-identity-and-scoped-official-evidence.md) — Strategy/code identity and scoped official evidence
- [ADR-0043](0043-preserve-leadership-source-missingness.md) — Preserve leadership-source missingness
- [ADR-0044](0044-unlinked-real-position-monitoring.md) — Unlinked Real position monitoring
- [ADR-0045](0045-five-minute-collection-and-nonoverlapping-intraday-chunks.md) — Five-minute collection and non-overlapping intraday chunks
- [ADR-0046](0046-guarded-nightly-operations-runner.md) — Guarded nightly operations runner
- [ADR-0048](0048-consequential-operational-transaction-boundaries.md) — Consequential operational transaction boundaries
- [ADR-0049](0049-canonical-database-write-contracts.md) — Canonical database write contracts
- [ADR-0050](0050-build-only-ssdt-ci-boundary.md) — Build-only SSDT CI boundary
- [ADR-0051](0051-system-shadow-portfolios.md) — System-selected Shadow V1 portfolios
- [ADR-0052](0052-working-tree-state-is-provenance-not-evidence-quality.md) — Working-tree state is provenance, not evidence quality
- [ADR-0053](0053-delphi-live-v1.md) — Delphi Live V1

- [ADR-0054](0054-delphi-live-preview-and-historical-replay.md) — Delphi Live preview and historical replay

## data-sources

- [ADR-0083](0083-opening-account-values-and-shared-engine-hosting.md) — Exact dated broker account values; native morning alignment remains unverified

- [ADR-0004](0004-genuity-us-confirming-indices.md) — US confirming-index source and staleness gate
- [ADR-0028](0028-delayed-intraday-swing-monitor-and-exit-policy.md) — Delayed intraday swing monitor and exit policy
- [ADR-0030](0030-intraday-evidence-and-poll-audit-ledger.md) — Intraday evidence and poll-audit ledger
- [ADR-0043](0043-preserve-leadership-source-missingness.md) — Preserve leadership-source missingness
- [ADR-0045](0045-five-minute-collection-and-nonoverlapping-intraday-chunks.md) — Five-minute collection and non-overlapping intraday chunks

## machine-learning

- [ADR-0063](0063-historical-practice-environment-direction.md) — Historical practice environment direction

- [ADR-0059](0059-delphi-model-and-threshold-settings.md) — Delphi model-set and threshold settings

- [ADR-0057](0057-preserved-model-sets-and-explicit-selection.md) — Preserved model sets and explicit selection

- [ADR-0056](0056-dated-profit-model-input-contract.md) — Dated profit-model input contract

- [ADR-0020](0020-immutable-calibration-evidence-ledger.md) — Immutable calibration evidence ledger
- [ADR-0022](0022-champion-challenger-evidence-and-promotion.md) — Champion/challenger evidence and promotion
- [ADR-0038](0038-cohort-weighted-official-prediction-scorecards.md) — Cohort-weighted official prediction scorecards
- [ADR-0053](0053-delphi-live-v1.md) — Delphi Live V1

## llm

*(none yet)*

## time-series

- [ADR-0016](0016-obv-per-symbol-soft-ranking-signal.md) — OBV as a per-symbol soft ranking signal
- [ADR-0041](0041-date-aligned-relative-strength-inputs.md) — Date-aligned relative-strength inputs

## technical-indicators

- [ADR-0001](0001-granville-plugin-architecture.md) — Granville indicator plug-in architecture
- [ADR-0003](0003-weighting-indicator-narrow-advance.md) — Weighting narrow-advance warning
- [ADR-0004](0004-genuity-us-confirming-indices.md) — Genuity confirming-index source and staleness gate
- [ADR-0005](0005-defer-granville-dullness-21-22.md) — Defer Granville Dullness indicators
- [ADR-0006](0006-granville-light-volume-25-28.md) — Granville Light Volume indicators
- [ADR-0008](0008-genuity-19-magnitude-tolerance-band.md) — Genuity #19 magnitude tolerance
- [ADR-0010](0010-rs-z-score-composite-additive.md) — Additive RS Z-score composite
- [ADR-0011](0011-rs-equal-weighted-with-direction-edge-in-ranking.md) — Equal-weighted RS in Breakout ranking
- [ADR-0012](0012-sector-index-historical-backfill.md) — Sector-index historical backfill
- [ADR-0014](0014-continuations-lens-trend-confirmation.md) — Continuation lens trend confirmation
- [ADR-0016](0016-obv-per-symbol-soft-ranking-signal.md) — OBV as a per-symbol soft ranking signal
- [ADR-0041](0041-date-aligned-relative-strength-inputs.md) — Date-aligned relative-strength inputs
- [ADR-0043](0043-preserve-leadership-source-missingness.md) — Preserve leadership-source missingness
- [ADR-0053](0053-delphi-live-v1.md) — Delphi Live V1

## market-microstructure

- [ADR-0084](0084-supervised-broker-strategy-session.md) — Reconciled exits before buys and one-attempt market orders

- [ADR-0079](0079-broker-reconciliation-and-market-handover.md) — Regular-session market handover direction and cumulative-fill accounting

- [ADR-0074](0074-wealthsimple-market-buy-protocol.md) — Whole-share market orders and actual fill observations
- [ADR-0076](0076-wealthsimple-market-sell-protocol.md) — Captured market sells and independent sell eligibility
- [ADR-0077](0077-manual-holdings-market-sell.md) — Explicit whole-holding market execution and limit-ticket preview

- [ADR-0073](0073-permanent-tfsa-and-whole-share-execution.md) — Permanent TFSA and whole-share execution

- [ADR-0055](0055-independent-strategies-to-approved-live-execution.md) — Independent strategies to approved live execution

- [ADR-0006](0006-granville-light-volume-25-28.md) — Granville Light Volume indicators
- [ADR-0007](0007-liquidity-floor-universe-filter.md) — Liquidity floor on Delphi's universe
- [ADR-0009](0009-exclude-leveraged-inverse-etps-from-delphi-universe.md) — Exclude leveraged/inverse ETPs
- [ADR-0015](0015-trade-logging-ghost-execution-and-position-lifecycle.md) — Manual trade logging and position lifecycle
- [ADR-0021](0021-calibration-outcome-and-paper-execution-contract.md) — Calibration outcome and paper-execution contract
- [ADR-0023](0023-primary-swing-policy-and-experimental-opening-confirmation.md) — Primary swing policy and experimental opening confirmation
- [ADR-0025](0025-three-session-swing-mark-to-market-outcome.md) — Three-session swing mark-to-market outcome
- [ADR-0026](0026-three-session-swing-excursion-measures.md) — Three-session swing excursion measures
- [ADR-0028](0028-delayed-intraday-swing-monitor-and-exit-policy.md) — Delayed intraday swing monitor and exit policy
- [ADR-0029](0029-intraday-ghost-entry-pilot.md) — Intraday ghost-entry pilot
- [ADR-0030](0030-intraday-evidence-and-poll-audit-ledger.md) — Intraday evidence and poll-audit ledger
- [ADR-0031](0031-automatic-ghost-exit-execution.md) — Automatic policy-triggered ghost exit execution
- [ADR-0037](0037-operator-confirmed-delphi-paper-entry.md) — Operator-confirmed Delphi pick to paper position
- [ADR-0039](0039-unified-ghost-real-dashboard-and-scorecard-workspace.md) — Unified Ghost/Real dashboard and scorecard workspace
- [ADR-0040](0040-reconstructible-delayed-intraday-outcome-fill.md) — Reconstructible delayed-intraday outcome fill
- [ADR-0044](0044-unlinked-real-position-monitoring.md) — Unlinked Real position monitoring
- [ADR-0045](0045-five-minute-collection-and-nonoverlapping-intraday-chunks.md) — Five-minute collection and non-overlapping intraday chunks
- [ADR-0047](0047-discretionary-real-entry-from-non-buy-row.md) — Discretionary Real entry from a non-Buy Delphi row
- [ADR-0051](0051-system-shadow-portfolios.md) — System-selected Shadow V1 portfolios
- [ADR-0053](0053-delphi-live-v1.md) — Delphi Live V1

- [ADR-0054](0054-delphi-live-preview-and-historical-replay.md) — Delphi Live preview and historical replay

## risk-management

- [ADR-0084](0084-supervised-broker-strategy-session.md) — Continued references, protection transfer and recovery

- [ADR-0079](0079-broker-reconciliation-and-market-handover.md) — Reconciliation blocks, retained holdings and exits before new buys
- [ADR-0080](0080-reviewed-broker-history-import.md) — Preserve legacy protection while preparing broker account history
- [ADR-0081](0081-broker-account-baselines-and-protection-ownership.md) — Preserve known holds and references; initialize missing baselines from observed real value

- [ADR-0075](0075-manual-market-buy-ticket.md) — Reviewed manual quantities, fresh account checks and uncertain-order recovery
- [ADR-0076](0076-wealthsimple-market-sell-protocol.md) — Preventing cross-side retransmission and preserving cumulative sell fills
- [ADR-0077](0077-manual-holdings-market-sell.md) — Durable local share reservations across manual buys and sells

- [ADR-0074](0074-wealthsimple-market-buy-protocol.md) — Durable attempt markers and uncertain submission outcomes

- [ADR-0073](0073-permanent-tfsa-and-whole-share-execution.md) — Permanent TFSA and whole-share execution

- [ADR-0067](0067-fixed-gate-paper-comparison-pairs.md) — Fixed-gate paper comparison pairs

- [ADR-0065](0065-one-session-live-engineering-activation.md) — One-session Delphi Live engineering activation
- [ADR-0064](0064-one-session-shadow-engineering-override.md) — One-session Shadow engineering override

- [ADR-0055](0055-independent-strategies-to-approved-live-execution.md) — Independent strategies to approved live execution

- [ADR-0007](0007-liquidity-floor-universe-filter.md) — Liquidity floor on Delphi's universe
- [ADR-0009](0009-exclude-leveraged-inverse-etps-from-delphi-universe.md) — Exclude leveraged/inverse ETPs
- [ADR-0014](0014-continuations-lens-trend-confirmation.md) — Continuation lens trend confirmation
- [ADR-0015](0015-trade-logging-ghost-execution-and-position-lifecycle.md) — Manual trade logging and position lifecycle
- [ADR-0018](0018-manual-migrations-and-simple-recovery-backups.md) — Manual migrations and SIMPLE-recovery backups
- [ADR-0019](0019-delphi-strict-history-freshness-eligibility.md) — Delphi strict history-freshness eligibility
- [ADR-0021](0021-calibration-outcome-and-paper-execution-contract.md) — Calibration outcome and paper-execution contract
- [ADR-0022](0022-champion-challenger-evidence-and-promotion.md) — Champion/challenger evidence and promotion
- [ADR-0023](0023-primary-swing-policy-and-experimental-opening-confirmation.md) — Primary swing policy and experimental opening confirmation
- [ADR-0025](0025-three-session-swing-mark-to-market-outcome.md) — Three-session swing mark-to-market outcome
- [ADR-0026](0026-three-session-swing-excursion-measures.md) — Three-session swing excursion measures
- [ADR-0027](0027-lens-tradeability-scorecards-and-cohort-aggregation.md) — Lens tradeability scorecards and cohort aggregation
- [ADR-0028](0028-delayed-intraday-swing-monitor-and-exit-policy.md) — Delayed intraday swing monitor and exit policy
- [ADR-0029](0029-intraday-ghost-entry-pilot.md) — Intraday ghost-entry pilot
- [ADR-0030](0030-intraday-evidence-and-poll-audit-ledger.md) — Intraday evidence and poll-audit ledger
- [ADR-0031](0031-automatic-ghost-exit-execution.md) — Automatic policy-triggered ghost exit execution
- [ADR-0032](0032-live-paper-trading-wpf-dashboard.md) — Live paper-trading WPF dashboard
- [ADR-0033](0033-tabbed-desktop-shell-and-shared-workflows.md) — Tabbed desktop shell and shared application workflows
- [ADR-0034](0034-delphi-shared-workflow-and-desktop-tab.md) — Delphi shared workflow and desktop tab
- [ADR-0037](0037-operator-confirmed-delphi-paper-entry.md) — Operator-confirmed Delphi pick to paper position
- [ADR-0039](0039-unified-ghost-real-dashboard-and-scorecard-workspace.md) — Unified Ghost/Real dashboard and scorecard workspace
- [ADR-0040](0040-reconstructible-delayed-intraday-outcome-fill.md) — Reconstructible delayed-intraday outcome fill
- [ADR-0044](0044-unlinked-real-position-monitoring.md) — Unlinked Real position monitoring
- [ADR-0045](0045-five-minute-collection-and-nonoverlapping-intraday-chunks.md) — Five-minute collection and non-overlapping intraday chunks
- [ADR-0046](0046-guarded-nightly-operations-runner.md) — Guarded nightly operations runner
- [ADR-0047](0047-discretionary-real-entry-from-non-buy-row.md) — Discretionary Real entry from a non-Buy Delphi row
- [ADR-0048](0048-consequential-operational-transaction-boundaries.md) — Consequential operational transaction boundaries
- [ADR-0049](0049-canonical-database-write-contracts.md) — Canonical database write contracts
- [ADR-0050](0050-build-only-ssdt-ci-boundary.md) — Build-only SSDT CI boundary
- [ADR-0051](0051-system-shadow-portfolios.md) — System-selected Shadow V1 portfolios
- [ADR-0053](0053-delphi-live-v1.md) — Delphi Live V1

## decision-engine

- [ADR-0067](0067-fixed-gate-paper-comparison-pairs.md) — Fixed-gate paper comparison pairs

- [ADR-0064](0064-one-session-shadow-engineering-override.md) — One-session Shadow engineering override

- [ADR-0060](0060-central-settings-and-scoped-configuration.md) — Central settings and scoped configuration

- [ADR-0059](0059-delphi-model-and-threshold-settings.md) — Delphi model-set and threshold settings

- [ADR-0058](0058-daily-ingestion-and-observed-benchmark-confirmation.md) — Daily ingestion and observed benchmark confirmation

- [ADR-0057](0057-preserved-model-sets-and-explicit-selection.md) — Preserved model sets and explicit selection

- [ADR-0056](0056-dated-profit-model-input-contract.md) — Dated profit-model input contract

- [ADR-0055](0055-independent-strategies-to-approved-live-execution.md) — Independent strategies to approved live execution

- [ADR-0002](0002-xiu-as-benchmark-index.md) — XIU as the system benchmark
- [ADR-0003](0003-weighting-indicator-narrow-advance.md) — Weighting narrow-advance warning
- [ADR-0004](0004-genuity-us-confirming-indices.md) — Genuity confirming-index source and staleness gate
- [ADR-0005](0005-defer-granville-dullness-21-22.md) — Defer Granville Dullness indicators
- [ADR-0006](0006-granville-light-volume-25-28.md) — Granville Light Volume indicators
- [ADR-0007](0007-liquidity-floor-universe-filter.md) — Liquidity floor on Delphi's universe
- [ADR-0008](0008-genuity-19-magnitude-tolerance-band.md) — Genuity #19 magnitude tolerance
- [ADR-0009](0009-exclude-leveraged-inverse-etps-from-delphi-universe.md) — Exclude leveraged/inverse ETPs
- [ADR-0010](0010-rs-z-score-composite-additive.md) — Additive RS Z-score composite
- [ADR-0011](0011-rs-equal-weighted-with-direction-edge-in-ranking.md) — Equal-weighted RS in Breakout ranking
- [ADR-0012](0012-sector-index-historical-backfill.md) — Sector-index historical backfill
- [ADR-0013](0013-multi-lens-decision-architecture.md) — Multi-lens decision architecture
- [ADR-0014](0014-continuations-lens-trend-confirmation.md) — Continuation lens trend confirmation
- [ADR-0016](0016-obv-per-symbol-soft-ranking-signal.md) — OBV as a per-symbol soft ranking signal
- [ADR-0019](0019-delphi-strict-history-freshness-eligibility.md) — Delphi strict history-freshness eligibility
- [ADR-0020](0020-immutable-calibration-evidence-ledger.md) — Immutable calibration evidence ledger
- [ADR-0022](0022-champion-challenger-evidence-and-promotion.md) — Champion/challenger evidence and promotion
- [ADR-0023](0023-primary-swing-policy-and-experimental-opening-confirmation.md) — Primary swing policy and experimental opening confirmation
- [ADR-0024](0024-coverage-scorecard-and-market-session-cohorts.md) — Coverage scorecard and market-session cohort identity
- [ADR-0027](0027-lens-tradeability-scorecards-and-cohort-aggregation.md) — Lens tradeability scorecards and cohort aggregation
- [ADR-0034](0034-delphi-shared-workflow-and-desktop-tab.md) — Delphi shared workflow and desktop tab
- [ADR-0035](0035-delphi-operator-workspace-and-presentation-snapshot.md) — Delphi operator workspace and presentation snapshot
- [ADR-0038](0038-cohort-weighted-official-prediction-scorecards.md) — Cohort-weighted official prediction scorecards
- [ADR-0041](0041-date-aligned-relative-strength-inputs.md) — Date-aligned relative-strength inputs
- [ADR-0042](0042-strategy-code-identity-and-scoped-official-evidence.md) — Strategy/code identity and scoped official evidence
- [ADR-0043](0043-preserve-leadership-source-missingness.md) — Preserve leadership-source missingness
- [ADR-0046](0046-guarded-nightly-operations-runner.md) — Guarded nightly operations runner
- [ADR-0047](0047-discretionary-real-entry-from-non-buy-row.md) — Discretionary Real entry from a non-Buy Delphi row
- [ADR-0051](0051-system-shadow-portfolios.md) — System-selected Shadow V1 portfolios
- [ADR-0052](0052-working-tree-state-is-provenance-not-evidence-quality.md) — Working-tree state is provenance, not evidence quality
- [ADR-0053](0053-delphi-live-v1.md) — Delphi Live V1

## math-statistics

- [ADR-0003](0003-weighting-indicator-narrow-advance.md) — Weighting narrow-advance warning
- [ADR-0005](0005-defer-granville-dullness-21-22.md) — Defer Granville Dullness indicators
- [ADR-0008](0008-genuity-19-magnitude-tolerance-band.md) — Genuity #19 magnitude tolerance
- [ADR-0010](0010-rs-z-score-composite-additive.md) — Additive RS Z-score composite
- [ADR-0011](0011-rs-equal-weighted-with-direction-edge-in-ranking.md) — Equal-weighted RS in Breakout ranking
- [ADR-0021](0021-calibration-outcome-and-paper-execution-contract.md) — Calibration outcome and paper-execution contract
- [ADR-0022](0022-champion-challenger-evidence-and-promotion.md) — Champion/challenger evidence and promotion
- [ADR-0024](0024-coverage-scorecard-and-market-session-cohorts.md) — Coverage scorecard and market-session cohort identity
- [ADR-0025](0025-three-session-swing-mark-to-market-outcome.md) — Three-session swing mark-to-market outcome
- [ADR-0026](0026-three-session-swing-excursion-measures.md) — Three-session swing excursion measures
- [ADR-0027](0027-lens-tradeability-scorecards-and-cohort-aggregation.md) — Lens tradeability scorecards and cohort aggregation
- [ADR-0038](0038-cohort-weighted-official-prediction-scorecards.md) — Cohort-weighted official prediction scorecards
- [ADR-0040](0040-reconstructible-delayed-intraday-outcome-fill.md) — Reconstructible delayed-intraday outcome fill
- [ADR-0042](0042-strategy-code-identity-and-scoped-official-evidence.md) — Strategy/code identity and scoped official evidence

## finance-fundamentals

- [ADR-0002](0002-xiu-as-benchmark-index.md) — XIU as the system benchmark
- [ADR-0003](0003-weighting-indicator-narrow-advance.md) — Weighting narrow-advance warning
- [ADR-0004](0004-genuity-us-confirming-indices.md) — Genuity confirming-index source and staleness gate
- [ADR-0006](0006-granville-light-volume-25-28.md) — Granville Light Volume indicators

## user-interface

- [ADR-0079](0079-broker-reconciliation-and-market-handover.md) — Account-history reconciliation and regular-session market handover direction
- [ADR-0080](0080-reviewed-broker-history-import.md) — Initial import review and explicit refresh of linked broker history

- [ADR-0075](0075-manual-market-buy-ticket.md) — Reusable grid Buy action and editable whole-share ticket
- [ADR-0077](0077-manual-holdings-market-sell.md) — Immediate holdings-row sell-all controls and protected results window

- [ADR-0068](0068-main-account-trading-overview.md) — Main real-account overview, separate paper portfolios and accessible strategy review

- [ADR-0067](0067-fixed-gate-paper-comparison-pairs.md) — Fixed-gate paper comparison pairs

- [ADR-0063](0063-historical-practice-environment-direction.md) — Historical practice environment direction

- [ADR-0061](0061-portfolio-comparison-workspace.md) — Linked comparison, performance and challenger review views

- [ADR-0060](0060-central-settings-and-scoped-configuration.md) — Central settings and scoped configuration

- [ADR-0059](0059-delphi-model-and-threshold-settings.md) — Delphi model-set and threshold settings

- Paper operations: [ADR-0066](0066-operational-paper-daily-source-adoption.md) — automatic source revisions and research separation.
