# ADRs Grouped by Domain

Manually maintained. Every ADR must appear under each tag declared in its header.

## architecture

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

## data-pipeline

- [ADR-0005](0005-defer-granville-dullness-21-22.md) — Defer Granville Dullness indicators
- [ADR-0007](0007-liquidity-floor-universe-filter.md) — Liquidity floor on Delphi's universe
- [ADR-0009](0009-exclude-leveraged-inverse-etps-from-delphi-universe.md) — Exclude leveraged/inverse ETPs
- [ADR-0012](0012-sector-index-historical-backfill.md) — Sector-index historical backfill
- [ADR-0016](0016-obv-per-symbol-soft-ranking-signal.md) — OBV as a per-symbol soft ranking signal
- [ADR-0018](0018-manual-migrations-and-simple-recovery-backups.md) — Manual migrations and SIMPLE-recovery backups
- [ADR-0019](0019-delphi-strict-history-freshness-eligibility.md) — Delphi strict history-freshness eligibility
- [ADR-0020](0020-immutable-calibration-evidence-ledger.md) — Immutable calibration evidence ledger
- [ADR-0024](0024-coverage-scorecard-and-market-session-cohorts.md) — Coverage scorecard and market-session cohort identity

## data-sources

- [ADR-0004](0004-genuity-us-confirming-indices.md) — US confirming-index source and staleness gate
- [ADR-0028](0028-delayed-intraday-swing-monitor-and-exit-policy.md) — Delayed intraday swing monitor and exit policy

## machine-learning

- [ADR-0020](0020-immutable-calibration-evidence-ledger.md) — Immutable calibration evidence ledger
- [ADR-0022](0022-champion-challenger-evidence-and-promotion.md) — Champion/challenger evidence and promotion

## llm

*(none yet)*

## time-series

- [ADR-0016](0016-obv-per-symbol-soft-ranking-signal.md) — OBV as a per-symbol soft ranking signal

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

## market-microstructure

- [ADR-0006](0006-granville-light-volume-25-28.md) — Granville Light Volume indicators
- [ADR-0007](0007-liquidity-floor-universe-filter.md) — Liquidity floor on Delphi's universe
- [ADR-0009](0009-exclude-leveraged-inverse-etps-from-delphi-universe.md) — Exclude leveraged/inverse ETPs
- [ADR-0015](0015-trade-logging-ghost-execution-and-position-lifecycle.md) — Manual trade logging and position lifecycle
- [ADR-0021](0021-calibration-outcome-and-paper-execution-contract.md) — Calibration outcome and paper-execution contract
- [ADR-0023](0023-primary-swing-policy-and-experimental-opening-confirmation.md) — Primary swing policy and experimental opening confirmation
- [ADR-0025](0025-three-session-swing-mark-to-market-outcome.md) — Three-session swing mark-to-market outcome
- [ADR-0026](0026-three-session-swing-excursion-measures.md) — Three-session swing excursion measures
- [ADR-0028](0028-delayed-intraday-swing-monitor-and-exit-policy.md) — Delayed intraday swing monitor and exit policy

## risk-management

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

## decision-engine

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

## finance-fundamentals

- [ADR-0002](0002-xiu-as-benchmark-index.md) — XIU as the system benchmark
- [ADR-0003](0003-weighting-indicator-narrow-advance.md) — Weighting narrow-advance warning
- [ADR-0004](0004-genuity-us-confirming-indices.md) — Genuity confirming-index source and staleness gate
- [ADR-0006](0006-granville-light-volume-25-28.md) — Granville Light Volume indicators
