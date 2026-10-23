# Architecture Decision Records (ADRs)

This folder records meaningful design decisions made on TraderVI. Each ADR
captures *why* a choice was made, what alternatives were rejected, and what
review questions can be used to test understanding later.

We use **lightweight ADRs**: any meaningful decision gets one, even
indicator-level choices. The goal is dense review material, not formal
architecture documentation.

## Conventions

- **Reader experience:** start substantive decisions with a concise executive summary and practical consequence; follow the [briefing style](../briefing-style.md) and [template](../templates/executive-brief.md). Keep detailed rationale and stable section links available below.
- **Filename:** `NNNN-kebab-title.md` (numeric prefix, zero-padded to 4).
- **Status flow:** `Proposed` â†’ `Accepted` â†’ optionally `Superseded by ADR-XXXX`.
- **Domains:** every ADR lists 1â€“4 domain tags (see taxonomy below). Tags
  drive the [by-domain index](by-domain.md) and review-mode filtering.
- **Review questions:** every ADR ends with 2â€“4 questions that will be used
  to quiz the author in future review sessions.

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

| # | Title | Status | Domains |
|---|---|---|---|
| [0107](0107-build-review-within-existing-dated-permission.md) | Build review within existing dated permission | Implemented, tested, installed; operator approval/enable and fresh evaluation verified October 23 | architecture, risk-management |
| [0106](0106-exact-selected-session-risk-receipt-storage.md) | Exact selected-session risk receipt storage | Installed a090757f; original risk event recovered into operator-approved October 23 permission | architecture, risk-management |
| [0103](0103-real-execution-independent-of-paper-research.md) | Real execution independent of paper research | Independence, verification/audits and dashboard-close lifecycle accepted; staged delivery targets tonight, feasibility unverified | architecture, data-pipeline |
| [0105](0105-reviewed-selected-session-continuation.md) | Reviewed selected-session continuation | Installed; October 23 continuation approved and host separately enabled by operator | architecture, risk-management, user-interface |
| [0104](0104-scoped-recovery-and-sequential-history-audits.md) | Scoped recovery and sequential history audits | Accepted; source implemented, native qualification pending; real trading remains disabled | architecture, data-pipeline |
| [0102](0102-bounded-immutable-live-evaluation-save-recovery.md) | Bounded immutable Live evaluation-save recovery | Accepted SQL-only fix; native cause/capacity unverified | architecture, data-pipeline, risk-management |
| [0101](0101-october22-portfolio-rules-only-review.md) | October 22 Portfolio rules only review | Accepted preparation only; operator approval/enable separate | architecture, risk-management, user-interface |
| [0100](0100-live-entry-and-order-decision-traces.md) | Live entry and order decision traces | Accepted October 21; source/offline validation | architecture, decision-engine, risk-management |
| [0099](0099-exact-report-sharing-in-live-writes.md) | Exact report sharing in Live snapshot writes | Accepted October 21; offline validation in progress | architecture, data-pipeline |
| [0098](0098-verified-snapshot-read-reuse-and-build-review.md) | Verified snapshot read reuse and dated build review | Accepted October 21; native acceptance pending | architecture, risk-management |
| [0097](0097-reviewed-october21-portfolio-rules-only-source-switch.md) | Reviewed October 21 Portfolio rules only source switch | Accepted October 21; explicit operator approval/enable required | architecture, risk-management, decision-engine, user-interface |
| [0096](0096-pre-open-october21-approval-and-armed-host.md) | Pre-open October 21 approval and armed execution host | Accepted October 21 | architecture, risk-management |
| [0095](0095-current-readiness-without-daily-verification-replay.md) | Current approval without replaying completed broker verification | Accepted October 20; current-readiness continuation | architecture, risk-management |
| [0094](0094-real-tfsa-execution-status-presentation.md) | Real TFSA execution and buying permission presentation | Scope accepted; detailed design draft for review before code | architecture, user-interface |
| [0093](0093-narrow-read-only-broker-checks.md) | Narrowly approved broker-read helper | Accepted; installed and native read verified October 18 | architecture, data-sources |
| [0092](0092-scoped-supervised-delphi-live-tfsa-session.md) | First supervised Delphi Live TFSA scope | Operator scope accepted; Core tested, not installed/activated; original qualifications retained | architecture, risk-management, market-microstructure |
| [0091](0091-local-read-only-database-checks.md) | Fixed local read-only database checks | Accepted; installed and verified October 18 | architecture, data-pipeline |
| [0090](0090-progressive-individual-profit-trailing.md) | Progressive trailing protection for individual winners | Accepted; installed/enabled for 15 active paper portfolios October 18; off-session preservation verified | risk-management, decision-engine |
| [0089](0089-tmx-session-context-and-supplementary-records.md) | Stable TMX context, supplementary records and causal clock waits | Installed; observed-end/stale-source follow-up's first scheduled checks/current marks verified | data-pipeline, data-sources, time-series |
| [0088](0088-source-symbol-aliases-for-monitoring.md) | Source symbol aliases for monitoring | Installed; symbol resolution and comparison-source adoption verified | architecture, data-sources |
| [0087](0087-ai-momentum-portfolio-boundaries.md) | AI Momentum mandate and enforced protection | Budgeted proposals and learning consultation in source; evaluation, host integration and protection parameters pending | architecture, llm, risk-management |
| [0085](0085-portfolio-wide-ongoing-tfsa-management.md) | Portfolio-wide ongoing TFSA management | Ongoing Core recovery and saved TFSA controls source/tested; native qualification, family coverage and execution host unfinished | architecture, risk-management, user-interface |
| [0086](0086-supervised-broker-strategy-session.md) | Durable supervised broker strategy session | Source/offline tests and installed storage; launch NO-GO pending native qualification and rollout | architecture, risk-management, market-microstructure |
| [0083](0083-opening-account-values-and-shared-engine-hosting.md) | Opening account values and shared engine hosting | Continuation and orchestration tested; native source qualification pending | architecture, data-sources |
| [0084](0084-external-broker-trade-synchronization.md) | External broker trade synchronization | Installed; resolved-order native acceptance verified | architecture, data-sources, risk-management, user-interface |
| [0078](0078-isolated-latest-source-nightly-builds.md) | Isolated latest-source nightly builds | Accepted; implemented | architecture, data-pipeline |
| [0079](0079-broker-reconciliation-and-market-handover.md) | Broker reconciliation and first market-order handover | Reconciliation implemented; execution pending | architecture, risk-management, market-microstructure, user-interface |
| [0080](0080-reviewed-broker-history-import.md) | Reviewed broker history import | Implemented; migration and native linked refresh verified | architecture, risk-management, user-interface |
| [0082](0082-compact-live-snapshots-and-backup-retention.md) | Compact Live snapshots and verified backup retention | Installed and verified September 17; bounded history reads remain | architecture, data-pipeline |
| [0081](0081-broker-account-baselines-and-protection-ownership.md) | Broker account baselines and protection ownership | Initial setup/store implemented; migration 035 installed; operational baseline not established | architecture, risk-management |
| [0069](0069-direct-wealthsimple-authentication.md) | Direct Wealthsimple authentication | Accepted; isolated browser-session handoff verified | architecture |
| [0070](0070-wealthsimple-desktop-sign-in.md) | Wealthsimple desktop sign-in and session status | Implemented; embedded login and live refresh validation pending | architecture |
| [0071](0071-native-wealthsimple-login.md) | Native Wealthsimple sign-in | Implemented; operator login confirmed | architecture |
| [0075](0075-manual-market-buy-ticket.md) | Shared manual market-buy ticket and recoverable orders | Implemented; offline validation; native live order unverified | architecture, user-interface, risk-management |
| [0074](0074-wealthsimple-market-buy-protocol.md) | Wealthsimple market-buy protocol and attempt recovery | Client implemented; WPF manual integration in ADR-0075 | architecture, risk-management, market-microstructure |
| [0073](0073-permanent-tfsa-and-whole-share-execution.md) | Permanent TFSA and whole-share execution | Transition preview implemented; activation unfinished | architecture, risk-management, market-microstructure |
| [0072](0072-wealthsimple-tfsa-account-display.md) | Read-only Wealthsimple TFSA landing page | Implemented; live account reads verified | architecture |
| [0068](0068-main-account-trading-overview.md) | Main-account Trading overview | Accepted; workspace implemented; navigation follow-up verified offline | user-interface |
| [0067](0067-fixed-gate-paper-comparison-pairs.md) | Fixed-gate paper comparison pairs | Accepted; implementation pending | architecture, decision-engine, risk-management, user-interface |
| [0063](0063-historical-practice-environment-direction.md) | Historical practice environment direction | Accepted; first milestone implemented in source | architecture, data-pipeline, user-interface, machine-learning |
| [0061](0061-portfolio-comparison-workspace.md) | Portfolio comparison workspace | Accepted navigation; descriptive display implemented | user-interface, architecture |
| [0001](0001-granville-plugin-architecture.md) | Granville indicator plug-in architecture | Accepted | architecture, technical-indicators |
| [0002](0002-xiu-as-benchmark-index.md) | XIU as the system benchmark index | Accepted | finance-fundamentals, decision-engine |
| [0003](0003-weighting-indicator-narrow-advance.md) | Weighting indicator (Granville #15â€“#16) â€” narrow-advance warning gate | Accepted | technical-indicators, decision-engine, math-statistics, finance-fundamentals |
| [0004](0004-genuity-us-confirming-indices.md) | Genuity indicators (Granville #17â€“#20) â€” US confirming-index source & staleness gate | Accepted | technical-indicators, decision-engine, data-sources, finance-fundamentals |
| [0005](0005-defer-granville-dullness-21-22.md) | Defer Granville Dullness indicators (#21 and #22) | Accepted | technical-indicators, decision-engine, data-pipeline, math-statistics |
| [0006](0006-granville-light-volume-25-28.md) | Granville Light Volume indicators (#25â€“#28) â€” tape Ã— leadership-quality | Accepted | technical-indicators, decision-engine, market-microstructure |
| [0007](0007-liquidity-floor-universe-filter.md) | Liquidity floor on Delphi's universe filter (price â‰¥ $1, 20d vol â‰¥ 50k) | Accepted | decision-engine, market-microstructure, risk-management, data-pipeline |
| [0008](0008-genuity-19-magnitude-tolerance-band.md) | Genuity #19 magnitude-ratio Â±5% tolerance buffer (refines ADR-0004) | Accepted | technical-indicators, decision-engine, math-statistics |
| [0009](0009-exclude-leveraged-inverse-etps-from-delphi-universe.md) | Exclude leveraged/inverse ETPs from Delphi's ranking universe | Accepted | decision-engine, data-pipeline, risk-management, market-microstructure |
| [0010](0010-rs-z-score-composite-additive.md) | RS Z-score composite (`CompositeScoreZ`) â€” additive only, no ranking change | Accepted | technical-indicators, decision-engine, math-statistics |
| [0011](0011-rs-equal-weighted-with-direction-edge-in-ranking.md) | RS equal-weighted with `DirectionEdge` in Delphi pick ranking | Accepted (scoped to Breakouts lens by ADR-0013/0014) | decision-engine, technical-indicators, math-statistics |
| [0012](0012-sector-index-historical-backfill.md) | Sector-index historical backfill from TMX `getTimeSeriesData` | Accepted | data-pipeline, technical-indicators, decision-engine |
| [0013](0013-multi-lens-decision-architecture.md) | Multi-lens decision architecture (lens = thesis Ã— gate stack Ã— ranking key) | Accepted | architecture, decision-engine |
| [0014](0014-continuations-lens-trend-confirmation.md) | Continuations lens â€” trend-confirmation gate + RS-primary ranking (executed) | Accepted | decision-engine, technical-indicators, risk-management |
| [0015](0015-trade-logging-ghost-execution-and-position-lifecycle.md) | Manual trade logging with ghost execution and position lifecycle | Accepted | architecture, risk-management, market-microstructure |
| [0016](0016-obv-per-symbol-soft-ranking-signal.md) | On-Balance Volume as a per-symbol soft ranking signal | Accepted | decision-engine, technical-indicators, data-pipeline, time-series |
| [0017](0017-codex-native-instructions-and-project-status.md) | Codex-native instructions and explicit project-status structure | Accepted | architecture |
| [0018](0018-manual-migrations-and-simple-recovery-backups.md) | Manual database migrations and SIMPLE-recovery backups | Accepted | architecture, data-pipeline, risk-management |
| [0019](0019-delphi-strict-history-freshness-eligibility.md) | Delphi strict history-freshness eligibility | Accepted | decision-engine, data-pipeline, risk-management |
| [0020](0020-immutable-calibration-evidence-ledger.md) | Immutable calibration evidence ledger | Accepted | architecture, data-pipeline, decision-engine, machine-learning |
| [0021](0021-calibration-outcome-and-paper-execution-contract.md) | Calibration outcome and paper-execution contract | Accepted | architecture, math-statistics, market-microstructure, risk-management |
| [0022](0022-champion-challenger-evidence-and-promotion.md) | Champion/challenger evidence and promotion | Accepted | decision-engine, machine-learning, math-statistics, risk-management |
| [0023](0023-primary-swing-policy-and-experimental-opening-confirmation.md) | Primary swing policy and experimental opening confirmation | Accepted | architecture, decision-engine, market-microstructure, risk-management |
| [0024](0024-coverage-scorecard-and-market-session-cohorts.md) | Coverage scorecard and market-session cohort identity | Accepted | architecture, data-pipeline, decision-engine, math-statistics |
| [0025](0025-three-session-swing-mark-to-market-outcome.md) | Three-session swing mark-to-market outcome | Accepted | architecture, math-statistics, market-microstructure, risk-management |
| [0026](0026-three-session-swing-excursion-measures.md) | Three-session swing excursion measures | Accepted | architecture, math-statistics, market-microstructure, risk-management |
| [0027](0027-lens-tradeability-scorecards-and-cohort-aggregation.md) | Lens tradeability scorecards and cohort aggregation | Accepted | architecture, decision-engine, math-statistics, risk-management |
| [0028](0028-delayed-intraday-swing-monitor-and-exit-policy.md) | Delayed intraday swing monitor and exit policy | Accepted | architecture, data-sources, market-microstructure, risk-management |
| [0029](0029-intraday-ghost-entry-pilot.md) | Intraday ghost-entry pilot | Accepted | architecture, market-microstructure, risk-management |
| [0030](0030-intraday-evidence-and-poll-audit-ledger.md) | Intraday evidence and poll-audit ledger | Accepted | architecture, data-pipeline, data-sources, market-microstructure, risk-management |
| [0031](0031-automatic-ghost-exit-execution.md) | Automatic policy-triggered ghost exit execution | Accepted | architecture, market-microstructure, risk-management |
| [0032](0032-live-paper-trading-wpf-dashboard.md) | Live paper-trading WPF dashboard | Accepted | architecture, data-pipeline, risk-management |
| [0033](0033-tabbed-desktop-shell-and-shared-workflows.md) | Tabbed desktop shell and shared application workflows | Accepted | architecture, data-pipeline, risk-management |
| [0034](0034-delphi-shared-workflow-and-desktop-tab.md) | Delphi shared workflow and desktop tab | Accepted | architecture, data-pipeline, decision-engine, risk-management |
| [0035](0035-delphi-operator-workspace-and-presentation-snapshot.md) | Delphi operator workspace and presentation snapshot | Accepted | architecture, data-pipeline, decision-engine |
| [0036](0036-native-project-documentation-reader.md) | Native read-only project documentation reader | Accepted | architecture |
| [0037](0037-operator-confirmed-delphi-paper-entry.md) | Operator-confirmed Delphi pick to paper position | Accepted | architecture, market-microstructure, risk-management |
| [0038](0038-cohort-weighted-official-prediction-scorecards.md) | Cohort-weighted official prediction scorecards | Accepted | architecture, decision-engine, machine-learning, math-statistics |
| [0039](0039-unified-ghost-real-dashboard-and-scorecard-workspace.md) | Unified Ghost/Real dashboard and scorecard workspace | Accepted | architecture, data-pipeline, market-microstructure, risk-management |
| [0040](0040-reconstructible-delayed-intraday-outcome-fill.md) | Reconstructible delayed-intraday outcome fill | Accepted | architecture, data-pipeline, market-microstructure, math-statistics, risk-management |
| [0041](0041-date-aligned-relative-strength-inputs.md) | Date-aligned relative-strength inputs | Accepted | decision-engine, data-pipeline, time-series, technical-indicators |
| [0042](0042-strategy-code-identity-and-scoped-official-evidence.md) | Strategy/code identity and scoped official evidence | Accepted | architecture, data-pipeline, decision-engine, math-statistics |
| [0043](0043-preserve-leadership-source-missingness.md) | Preserve leadership-source missingness | Accepted | data-pipeline, data-sources, decision-engine, technical-indicators |
| [0044](0044-unlinked-real-position-monitoring.md) | Unlinked Real position monitoring | Accepted | architecture, data-pipeline, market-microstructure, risk-management |
| [0045](0045-five-minute-collection-and-nonoverlapping-intraday-chunks.md) | Five-minute collection and non-overlapping intraday chunks | Accepted | architecture, data-pipeline, data-sources, market-microstructure, risk-management |
| [0046](0046-guarded-nightly-operations-runner.md) | Guarded nightly operations runner | Accepted | architecture, data-pipeline, decision-engine, risk-management |
| [0047](0047-discretionary-real-entry-from-non-buy-row.md) | Discretionary Real entry from a non-Buy Delphi row | Accepted | architecture, decision-engine, market-microstructure, risk-management |
| [0048](0048-consequential-operational-transaction-boundaries.md) | Consequential operational transaction boundaries | Accepted | architecture, data-pipeline, risk-management |
| [0049](0049-canonical-database-write-contracts.md) | Canonical database write contracts | Accepted | architecture, data-pipeline, risk-management |
| [0050](0050-build-only-ssdt-ci-boundary.md) | Build-only SSDT CI boundary | Accepted | architecture, data-pipeline, risk-management |
| [0051](0051-system-shadow-portfolios.md) | System-selected Shadow V1 portfolios | Accepted | architecture, data-pipeline, decision-engine, market-microstructure, risk-management |
| [0052](0052-working-tree-state-is-provenance-not-evidence-quality.md) | Working-tree state is provenance, not evidence quality | Accepted | architecture, data-pipeline, decision-engine |
| [0053](0053-delphi-live-v1.md) | Delphi Live V1 | Accepted | architecture, data-pipeline, decision-engine, machine-learning, market-microstructure, risk-management, technical-indicators |
| [0054](0054-delphi-live-preview-and-historical-replay.md) | Delphi Live preview and historical replay | Accepted (exploratory execution estimates) | architecture, data-pipeline, market-microstructure |
| [0055](0055-independent-strategies-to-approved-live-execution.md) | Independent strategies to approved live execution | Accepted direction; implementation incomplete | architecture, decision-engine, risk-management, market-microstructure |
| [0056](0056-dated-profit-model-input-contract.md) | Dated profit-model input contract | Accepted; operationally adopted | architecture, machine-learning, data-pipeline, decision-engine |
| [0057](0057-preserved-model-sets-and-explicit-selection.md) | Preserved model sets and explicit selection | Accepted; supported daily workflow validated | architecture, machine-learning, data-pipeline, decision-engine |
| [0058](0058-daily-ingestion-and-observed-benchmark-confirmation.md) | Daily ingestion and observed benchmark confirmation | Accepted; implemented and operationally verified | architecture, data-pipeline, decision-engine |
| [0059](0059-delphi-model-and-threshold-settings.md) | Delphi model-set and threshold settings | Implemented in source; operational selection pending | architecture, decision-engine, machine-learning, user-interface |
| [0060](0060-central-settings-and-scoped-configuration.md) | Central settings and scoped configuration | Accepted direction; broader implementation pending | architecture, user-interface, decision-engine |

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

## Review questions
1. ...
2. ...
3. ...
```

- [ADR-0066: Operational paper daily-source adoption](0066-operational-paper-daily-source-adoption.md) â€” newer valid same-session publications replace the operational source with preserved history and fresh confirmation.
