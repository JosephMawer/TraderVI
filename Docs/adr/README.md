# Architecture Decision Records (ADRs)

This folder records meaningful design decisions made on TraderVI. Each ADR
captures why a choice was made, its scope and meaningful alternatives.

Use a lightweight ADR for a meaningful new behavior, architecture, evidence or safety decision,
including an indicator-level choice when its rationale matters. Implementing an existing decision,
routine troubleshooting and editorial cleanup do not automatically need another ADR. Extend an existing
record when the same decision or investigation is being clarified; create a new ADR only for a distinct
meaningful decision, and link explicit supersession where applicable. The goal is recoverable reasoning. Concept notes and learning cards are optional aids, not mandatory outputs.

## Conventions

- **Reader experience:** start substantive decisions with a concise executive summary and practical consequence; follow the [briefing style](../briefing-style.md) and [template](../templates/executive-brief.md). Keep detailed rationale and stable section links available below.
- **Filename:** `NNNN-kebab-title.md` (numeric prefix, zero-padded to 4).
- **Status flow:** `Proposed` â†’ `Accepted` â†’ optionally `Superseded by ADR-XXXX`.
- **Domains:** every ADR lists 1â€“4 domain tags (see taxonomy below). Tags
  drive the [by-domain index](by-domain.md) and review-mode filtering.
- **Review questions:** optional when useful for learning; not a requirement for every decision.

## Domain taxonomy

| Tag | Scope |
|---|---|
| `architecture` | Project structure, plug-in patterns, DI, separation of concerns |
| `data-pipeline` | Hermes, importers, DB schema, data quality |
| `data-sources` | External APIs, benchmark feeds, provider selection, staleness |
| `machine-learning` | Hercules, model training, feature engineering, AUC/thresholds |
| `llm` | LLM integration (placeholder until introduced) |
| `time-series` | EMAs, vol calc, rolling windows, lagging/leading |
| `technical-indicators` | Granville, breadth, momentum, RSI-like signals |
| `market-microstructure` | Order types, slippage, liquidity |
| `risk-management` | Stop-loss, position sizing, capital preservation rules |
| `decision-engine` | Delphi, ranking, gates, composite scoring |
| `user-interface` | Desktop operator views, settings and review flows |
| `math-statistics` | Probability, normalization, z-scores, distributions |
| `finance-fundamentals` | Index construction, weighting schemes, sector classification |

## Index

The index is navigation, not installation or readiness status. Read the linked ADR for its decision,
scope and supersession, and [project status](../project-status.md) for dated operational evidence.

| # | Title | Domains |
|---|---|---|
| [0128](0128-windows-owned-application-launch.md) | Windows-owned on-demand application launch | architecture |
| [0127](0127-paper-checkpoint-restart-and-incremental-verification.md) | Paper checkpoint restart and incremental overnight verification | architecture, data-pipeline |
| [0126](0126-engine-health-and-diagnostic-snapshot.md) | Flat Engine health and shared human/AI diagnostic records | user-interface, architecture |
| [0125](0125-reusable-strategy-sale-proceeds.md) | Reuse reconciled strategy sale proceeds under explicit ongoing approval | risk-management, architecture |
| [0124](0124-due-prediction-scorecard.md) | Grade due ten-session predictions and label partial coverage | math-statistics, data-pipeline |
| [0123](0123-edge-passkey-session-handoff.md) | Existing Edge passkeys through a paired session handoff | architecture |
| [0122](0122-existing-output-nightly-execution.md) | Reuse existing nightly outputs at 06:30 | architecture, data-pipeline |
| [0121](0121-independent-athena-build.md) | Athena builds independently of Core | architecture, data-pipeline |
| [0120](0120-independent-hermes-build.md) | Hermes builds independently of Core | architecture, data-pipeline |
| [0119](0119-independent-data-audit-build.md) | DataAudit builds independently of Core | architecture, data-pipeline |
| [0118](0118-delphi-desktop-results-only.md) | Delphi desktop results only | architecture, user-interface |
| [0117](0117-compact-trading-current-storage.md) | Compact trading current-state storage | architecture, data-pipeline |
| [0116](0116-temporary-paper-continuation-during-reboot-recovery.md) | Temporary Paper continuation during reboot recovery | architecture, data-pipeline |
| [0115](0115-exact-audit-prefix-hash-reuse.md) | Exact audit prefix hash reuse | architecture, data-pipeline |
| [0114](0114-explicit-nightly-preparation-date.md) | Explicit nightly preparation date | architecture, data-pipeline |
| [0113](0113-nightly-data-audit-report.md) | Nightly data-quality audit and report | architecture, data-pipeline, user-interface |
| [0112](0112-repository-documentation-outside-desktop.md) | Read documentation and architecture on GitHub (supersedes ADR-0036) | architecture, user-interface |
| [0111](0111-independent-collector-and-recovered-confirmation.md) | Independent collector and recovered confirmation | architecture, data-pipeline, decision-engine, risk-management |
| [0110](0110-nightly-isolated-symbol-warnings.md) | Continue nightly work after isolated ticker-source failures | architecture, data-pipeline |
| [0109](0109-explicit-ongoing-delphi-live-management.md) | Explicit ongoing Delphi Live management | architecture, risk-management, user-interface, market-microstructure |
| [0108](0108-strategy-workspace-and-independent-account-evidence.md) | Strategy workspace and independent account evidence | user-interface |
| [0107](0107-build-review-within-existing-dated-permission.md) | Build review within existing dated permission | architecture, risk-management |
| [0106](0106-exact-selected-session-risk-receipt-storage.md) | Exact selected-session risk receipt storage | architecture, risk-management |
| [0105](0105-reviewed-selected-session-continuation.md) | Reviewed selected-session continuation | architecture, risk-management, user-interface |
| [0104](0104-scoped-recovery-and-sequential-history-audits.md) | Scoped recovery and sequential history audits | architecture, data-pipeline |
| [0103](0103-real-execution-independent-of-paper-research.md) | Real execution independent of paper research | architecture, data-pipeline |
| [0102](0102-bounded-immutable-live-evaluation-save-recovery.md) | Bounded immutable Live evaluation-save recovery | architecture, data-pipeline, risk-management |
| [0101](0101-october22-portfolio-rules-only-review.md) | October 22 Portfolio rules only review | architecture, risk-management, user-interface |
| [0100](0100-live-entry-and-order-decision-traces.md) | Live entry and order decision traces | architecture, decision-engine, risk-management |
| [0099](0099-exact-report-sharing-in-live-writes.md) | Exact report sharing in Live snapshot writes | architecture, data-pipeline |
| [0098](0098-verified-snapshot-read-reuse-and-build-review.md) | Verified snapshot read reuse and dated build review | architecture, risk-management |
| [0097](0097-reviewed-october21-portfolio-rules-only-source-switch.md) | Reviewed October 21 Portfolio rules only source switch | architecture, risk-management, decision-engine, user-interface |
| [0096](0096-pre-open-october21-approval-and-armed-host.md) | Pre-open October 21 approval and armed execution host | architecture, risk-management |
| [0095](0095-current-readiness-without-daily-verification-replay.md) | Current approval without replaying completed broker verification | architecture, risk-management |
| [0094](0094-real-tfsa-execution-status-presentation.md) | Real TFSA execution and buying permission presentation | architecture, user-interface |
| [0093](0093-narrow-read-only-broker-checks.md) | Narrowly approved broker-read helper | architecture, data-sources |
| [0092](0092-scoped-supervised-delphi-live-tfsa-session.md) | First supervised Delphi Live TFSA scope | architecture, risk-management, market-microstructure |
| [0091](0091-local-read-only-database-checks.md) | Fixed local read-only database checks | architecture, data-pipeline |
| [0090](0090-progressive-individual-profit-trailing.md) | Progressive trailing protection for individual winners | risk-management, decision-engine |
| [0089](0089-tmx-session-context-and-supplementary-records.md) | Stable TMX context, supplementary records and causal clock waits | data-pipeline, data-sources, time-series |
| [0088](0088-source-symbol-aliases-for-monitoring.md) | Source symbol aliases for monitoring | architecture, data-sources |
| [0087](0087-ai-momentum-portfolio-boundaries.md) | AI Momentum mandate and enforced protection | architecture, llm, risk-management |
| [0086](0086-supervised-broker-strategy-session.md) | Durable supervised broker strategy session | architecture, risk-management, market-microstructure |
| [0085](0085-portfolio-wide-ongoing-tfsa-management.md) | Portfolio-wide ongoing TFSA management | architecture, risk-management, user-interface |
| [0084](0084-external-broker-trade-synchronization.md) | External broker trade synchronization | architecture, data-sources, risk-management, user-interface |
| [0083](0083-opening-account-values-and-shared-engine-hosting.md) | Opening account values and shared engine hosting | architecture, data-sources |
| [0082](0082-compact-live-snapshots-and-backup-retention.md) | Compact Live snapshots and verified backup retention | architecture, data-pipeline |
| [0081](0081-broker-account-baselines-and-protection-ownership.md) | Broker account baselines and protection ownership | architecture, risk-management |
| [0080](0080-reviewed-broker-history-import.md) | Reviewed broker history import | architecture, risk-management, user-interface |
| [0079](0079-broker-reconciliation-and-market-handover.md) | Broker reconciliation and first market-order handover | architecture, risk-management, market-microstructure, user-interface |
| [0078](0078-isolated-latest-source-nightly-builds.md) | Isolated latest-source nightly builds (superseded by ADR-0122 for nightly preparation) | architecture, data-pipeline |
| [0077](0077-manual-holdings-market-sell.md) | Immediate manual market sell from broker holdings | architecture, user-interface, risk-management, market-microstructure |
| [0076](0076-wealthsimple-market-sell-protocol.md) | Wealthsimple market-sell protocol and shared attempt protection | architecture, risk-management, market-microstructure |
| [0075](0075-manual-market-buy-ticket.md) | Shared manual market-buy ticket and recoverable orders | architecture, user-interface, risk-management |
| [0074](0074-wealthsimple-market-buy-protocol.md) | Wealthsimple market-buy protocol and attempt recovery | architecture, risk-management, market-microstructure |
| [0073](0073-permanent-tfsa-and-whole-share-execution.md) | Permanent TFSA and whole-share execution | architecture, risk-management, market-microstructure |
| [0072](0072-wealthsimple-tfsa-account-display.md) | Read-only Wealthsimple TFSA landing page | architecture |
| [0071](0071-native-wealthsimple-login.md) | Native Wealthsimple sign-in | architecture |
| [0070](0070-wealthsimple-desktop-sign-in.md) | Wealthsimple desktop sign-in and session status | architecture |
| [0069](0069-direct-wealthsimple-authentication.md) | Direct Wealthsimple authentication | architecture |
| [0068](0068-main-account-trading-overview.md) | Main-account Trading overview | user-interface |
| [0067](0067-fixed-gate-paper-comparison-pairs.md) | Fixed-gate paper comparison pairs | architecture, decision-engine, risk-management, user-interface |
| [0066](0066-operational-paper-daily-source-adoption.md) | Operational paper portfolios adopt newer daily publications | user-interface |
| [0065](0065-one-session-live-engineering-activation.md) | One-session Delphi Live engineering activation | risk-management |
| [0064](0064-one-session-shadow-engineering-override.md) | One-session Shadow candidate override for engineering observation | decision-engine, risk-management |
| [0063](0063-historical-practice-environment-direction.md) | Historical practice environment direction | architecture, data-pipeline, user-interface, machine-learning |
| [0062](0062-complete-strategy-recommendation-promotion.md) | Complete-strategy recommendation promotion | Not tagged in ADR |
| [0061](0061-portfolio-comparison-workspace.md) | Portfolio comparison workspace | user-interface, architecture |
| [0060](0060-central-settings-and-scoped-configuration.md) | Central settings and scoped configuration | architecture, user-interface, decision-engine |
| [0059](0059-delphi-model-and-threshold-settings.md) | Delphi model-set and threshold settings | architecture, decision-engine, machine-learning, user-interface |
| [0058](0058-daily-ingestion-and-observed-benchmark-confirmation.md) | Daily ingestion and observed benchmark confirmation | architecture, data-pipeline, decision-engine |
| [0057](0057-preserved-model-sets-and-explicit-selection.md) | Preserved model sets and explicit selection | architecture, machine-learning, data-pipeline, decision-engine |
| [0056](0056-dated-profit-model-input-contract.md) | Dated profit-model input contract | architecture, machine-learning, data-pipeline, decision-engine |
| [0055](0055-independent-strategies-to-approved-live-execution.md) | Independent strategies to approved live execution | architecture, decision-engine, risk-management, market-microstructure |
| [0054](0054-delphi-live-preview-and-historical-replay.md) | Delphi Live preview and historical replay | architecture, data-pipeline, market-microstructure |
| [0053](0053-delphi-live-v1.md) | Delphi Live V1 | architecture, data-pipeline, decision-engine, machine-learning, market-microstructure, risk-management, technical-indicators |
| [0052](0052-working-tree-state-is-provenance-not-evidence-quality.md) | Working-tree state is provenance, not evidence quality | architecture, data-pipeline, decision-engine |
| [0051](0051-system-shadow-portfolios.md) | System-selected Shadow V1 portfolios | architecture, data-pipeline, decision-engine, market-microstructure, risk-management |
| [0050](0050-build-only-ssdt-ci-boundary.md) | Build-only SSDT CI boundary | architecture, data-pipeline, risk-management |
| [0049](0049-canonical-database-write-contracts.md) | Canonical database write contracts | architecture, data-pipeline, risk-management |
| [0048](0048-consequential-operational-transaction-boundaries.md) | Consequential operational transaction boundaries | architecture, data-pipeline, risk-management |
| [0047](0047-discretionary-real-entry-from-non-buy-row.md) | Discretionary Real entry from a non-Buy Delphi row | architecture, decision-engine, market-microstructure, risk-management |
| [0046](0046-guarded-nightly-operations-runner.md) | Guarded nightly operations runner | architecture, data-pipeline, decision-engine, risk-management |
| [0045](0045-five-minute-collection-and-nonoverlapping-intraday-chunks.md) | Five-minute collection and non-overlapping intraday chunks | architecture, data-pipeline, data-sources, market-microstructure, risk-management |
| [0044](0044-unlinked-real-position-monitoring.md) | Unlinked Real position monitoring | architecture, data-pipeline, market-microstructure, risk-management |
| [0043](0043-preserve-leadership-source-missingness.md) | Preserve leadership-source missingness | data-pipeline, data-sources, decision-engine, technical-indicators |
| [0042](0042-strategy-code-identity-and-scoped-official-evidence.md) | Strategy/code identity and scoped official evidence | architecture, data-pipeline, decision-engine, math-statistics |
| [0041](0041-date-aligned-relative-strength-inputs.md) | Date-aligned relative-strength inputs | decision-engine, data-pipeline, time-series, technical-indicators |
| [0040](0040-reconstructible-delayed-intraday-outcome-fill.md) | Reconstructible delayed-intraday outcome fill | architecture, data-pipeline, market-microstructure, math-statistics, risk-management |
| [0039](0039-unified-ghost-real-dashboard-and-scorecard-workspace.md) | Unified Ghost/Real dashboard and scorecard workspace | architecture, data-pipeline, market-microstructure, risk-management |
| [0038](0038-cohort-weighted-official-prediction-scorecards.md) | Cohort-weighted official prediction scorecards | architecture, decision-engine, machine-learning, math-statistics |
| [0037](0037-operator-confirmed-delphi-paper-entry.md) | Operator-confirmed Delphi pick to paper position | architecture, market-microstructure, risk-management |
| [0036](0036-native-project-documentation-reader.md) | Native read-only project documentation reader (superseded by ADR-0112) | architecture |
| [0035](0035-delphi-operator-workspace-and-presentation-snapshot.md) | Delphi operator workspace and presentation snapshot | architecture, data-pipeline, decision-engine |
| [0034](0034-delphi-shared-workflow-and-desktop-tab.md) | Delphi shared workflow and desktop tab | architecture, data-pipeline, decision-engine, risk-management |
| [0033](0033-tabbed-desktop-shell-and-shared-workflows.md) | Tabbed desktop shell and shared application workflows | architecture, data-pipeline, risk-management |
| [0032](0032-live-paper-trading-wpf-dashboard.md) | Live paper-trading WPF dashboard | architecture, data-pipeline, risk-management |
| [0031](0031-automatic-ghost-exit-execution.md) | Automatic policy-triggered ghost exit execution | architecture, market-microstructure, risk-management |
| [0030](0030-intraday-evidence-and-poll-audit-ledger.md) | Intraday evidence and poll-audit ledger | architecture, data-pipeline, data-sources, market-microstructure, risk-management |
| [0029](0029-intraday-ghost-entry-pilot.md) | Intraday ghost-entry pilot | architecture, market-microstructure, risk-management |
| [0028](0028-delayed-intraday-swing-monitor-and-exit-policy.md) | Delayed intraday swing monitor and exit policy | architecture, data-sources, market-microstructure, risk-management |
| [0027](0027-lens-tradeability-scorecards-and-cohort-aggregation.md) | Lens tradeability scorecards and cohort aggregation | architecture, decision-engine, math-statistics, risk-management |
| [0026](0026-three-session-swing-excursion-measures.md) | Three-session swing excursion measures | architecture, math-statistics, market-microstructure, risk-management |
| [0025](0025-three-session-swing-mark-to-market-outcome.md) | Three-session swing mark-to-market outcome | architecture, math-statistics, market-microstructure, risk-management |
| [0024](0024-coverage-scorecard-and-market-session-cohorts.md) | Coverage scorecard and market-session cohort identity | architecture, data-pipeline, decision-engine, math-statistics |
| [0023](0023-primary-swing-policy-and-experimental-opening-confirmation.md) | Primary swing policy and experimental opening confirmation | architecture, decision-engine, market-microstructure, risk-management |
| [0022](0022-champion-challenger-evidence-and-promotion.md) | Champion/challenger evidence and promotion | decision-engine, machine-learning, math-statistics, risk-management |
| [0021](0021-calibration-outcome-and-paper-execution-contract.md) | Calibration outcome and paper-execution contract | architecture, math-statistics, market-microstructure, risk-management |
| [0020](0020-immutable-calibration-evidence-ledger.md) | Immutable calibration evidence ledger | architecture, data-pipeline, decision-engine, machine-learning |
| [0019](0019-delphi-strict-history-freshness-eligibility.md) | Delphi strict history-freshness eligibility | decision-engine, data-pipeline, risk-management |
| [0018](0018-manual-migrations-and-simple-recovery-backups.md) | Manual database migrations and SIMPLE-recovery backups | architecture, data-pipeline, risk-management |
| [0017](0017-codex-native-instructions-and-project-status.md) | Codex-native instructions and explicit project-status structure | architecture |
| [0016](0016-obv-per-symbol-soft-ranking-signal.md) | On-Balance Volume as a per-symbol soft ranking signal | decision-engine, technical-indicators, data-pipeline, time-series |
| [0015](0015-trade-logging-ghost-execution-and-position-lifecycle.md) | Manual trade logging with ghost execution and position lifecycle | architecture, risk-management, market-microstructure |
| [0014](0014-continuations-lens-trend-confirmation.md) | Continuations lens â€” trend-confirmation gate + RS-primary ranking (executed) | decision-engine, technical-indicators, risk-management |
| [0013](0013-multi-lens-decision-architecture.md) | Multi-lens decision architecture (lens = thesis Ã— gate stack Ã— ranking key) | architecture, decision-engine |
| [0012](0012-sector-index-historical-backfill.md) | Sector-index historical backfill from TMX `getTimeSeriesData` | data-pipeline, technical-indicators, decision-engine |
| [0011](0011-rs-equal-weighted-with-direction-edge-in-ranking.md) | RS equal-weighted with `DirectionEdge` in Delphi pick ranking (scoped by ADR-0013/0014) | decision-engine, technical-indicators, math-statistics |
| [0010](0010-rs-z-score-composite-additive.md) | RS Z-score composite (`CompositeScoreZ`) â€” additive only, no ranking change | technical-indicators, decision-engine, math-statistics |
| [0009](0009-exclude-leveraged-inverse-etps-from-delphi-universe.md) | Exclude leveraged/inverse ETPs from Delphi's ranking universe | decision-engine, data-pipeline, risk-management, market-microstructure |
| [0008](0008-genuity-19-magnitude-tolerance-band.md) | Genuity #19 magnitude-ratio Â±5% tolerance buffer (refines ADR-0004) | technical-indicators, decision-engine, math-statistics |
| [0007](0007-liquidity-floor-universe-filter.md) | Liquidity floor on Delphi's universe filter (price â‰¥ $1, 20d vol â‰¥ 50k) | decision-engine, market-microstructure, risk-management, data-pipeline |
| [0006](0006-granville-light-volume-25-28.md) | Granville Light Volume indicators (#25â€“#28) â€” tape Ã— leadership-quality | technical-indicators, decision-engine, market-microstructure |
| [0005](0005-defer-granville-dullness-21-22.md) | Defer Granville Dullness indicators (#21 and #22) | technical-indicators, decision-engine, data-pipeline, math-statistics |
| [0004](0004-genuity-us-confirming-indices.md) | Genuity indicators (Granville #17â€“#20) â€” US confirming-index source & staleness gate | technical-indicators, decision-engine, data-sources, finance-fundamentals |
| [0003](0003-weighting-indicator-narrow-advance.md) | Weighting indicator (Granville #15â€“#16) â€” narrow-advance warning gate | technical-indicators, decision-engine, math-statistics, finance-fundamentals |
| [0002](0002-xiu-as-benchmark-index.md) | XIU as the system benchmark index | finance-fundamentals, decision-engine |
| [0001](0001-granville-plugin-architecture.md) | Granville indicator plug-in architecture | architecture, technical-indicators |

See also:

---

## ADR template

Copy this for new ADRs. Replace `NNNN` with the next number.

```
# ADR-NNNN: <short title>

- **Status:** Proposed
- **Date:** YYYY-MM-DD
- **Domains:** tag1, tag2

## Context
What problem are we solving? What did we know going in?

## Decision
What did we choose? Stated as an imperative ("Use X for Y").

## Alternatives considered
- **Option A** â€” pros/cons, why rejected.
- **Option B** â€” pros/cons, why rejected.

## Consequences
What does this lock us into? What's harder now? What's easier? What
would tell us this decision was wrong?

## Review questions (optional)
1. ...
2. ...
3. ...
```

- [ADR-0066: Operational paper daily-source adoption](0066-operational-paper-daily-source-adoption.md) â€” newer valid same-session publications replace the operational source with preserved history and fresh confirmation.
