# ADR-0027: Lens tradeability scorecards and cohort aggregation

- **Status:** Accepted
- **Date:** 2026-08-25
- **Domains:** architecture, decision-engine, math-statistics, risk-management
- **Related:** ADR-0013, ADR-0022, ADR-0024, ADR-0025, ADR-0026

## Context

The immediate problem is to summarize the short-horizon economic outcomes of Delphi's Continuation and Breakout recommendations separately. The parent problem is determining which selection thesis is improving and where each one takes risk. The root goal is to tune Delphi from honest paper evidence while strategy changes remain under human control.

The existing outcome rows are candidate-level facts. A candidate may be published by one or both lenses, and deliberate official reruns may share one `MarketDataAsOf` session. Simply averaging every stored lens row would let reruns masquerade as independent evidence. Reporting only closing returns would also hide missed entries and the favourable/adverse path measured by MFE and MAE.

## Decision

Add deterministic read-only tradeability scorecards for the `Continuation` and `Breakout` lenses. Include only published lens rows from non-invalid `OfficialPaper` runs. A candidate published by both lenses contributes once to each lens report because each report evaluates a distinct selection decision.

Pair `SwingMarkToMarket3` and `SwingExcursion3` by candidate. Classify each expected published recommendation as:

- `EnteredValid` when both outcomes are mature and valid;
- `EnteredDegraded` when both are mature and usable but either is degraded;
- `NoEntry` when both definitions terminate as no-entry with usable audits;
- `Invalid` when both are terminal but their maturity states conflict, an audit is invalid, their shared entry facts disagree, or either payload cannot be validated;
- `Pending` while either definition is absent or pending.

Lead each lens report with official-run count, total and fully matured market-session cohorts, expected recommendations, entered-valid, entered-degraded, no-entry, invalid, and pending counts. Use ADR-0024's completion coverage, usable coverage, 95% reporting floor, and `NoEvidence`/`Blocked`/`Degraded`/`Ready` states. `NoEntry` is terminal and usable for coverage but never enters return or excursion averages. A cohort in which the lens abstained remains visible as matured, but an empty abstaining cohort cannot by itself unlock performance fields; at least one non-empty recommendation cohort must be fully terminal.

When the reporting floor is satisfied, show the cohort-weighted no-entry rate and, for each 1-, 2-, and 3-session horizon:

- mean net return after modeled costs;
- percentage of entered recommendations with net return above zero;
- mean net excess return versus XIU;
- mean raw MFE and signed raw MAE;
- mean MFE and MAE session ordinals.

Do not display those performance fields below the reporting floor. Counts and coverage remain visible so the reason for blocking is explicit.

Preserve market-session dependence with a three-level aggregation:

1. average candidate values within each official run and lens;
2. average official-run values within each `MarketDataAsOf` cohort;
3. average cohort values with equal weight across cohorts.

This means a reproducibility rerun does not increase cohort count or give its market session more weight than another session. Official runs are expected to represent the same accepted champion; intentional policy variants belong in `ExploratoryReplay` or a later calibration-experiment contract, not in this scorecard.

Treat all published recommendations as the recommendation-level population. Top-1, Top-3, Top-5, rank-weighted, and capital-constrained selection policies remain separate portfolio work under ADR-0021 and Phase E of the checklist.

The scorecards are descriptive. They do not satisfy ADR-0022's evidence tiers, compare uncertainty bounds, or authorize any lens, gate, rank, or exit-policy change.

## Alternatives considered

- **Average every candidate row directly.** Rejected because repeated official runs over one market session would receive extra weight.
- **Keep only the earliest official run per cohort.** Rejected because valid reproducibility runs are useful audit evidence; nested averaging preserves them without increasing cohort weight.
- **Combine Continuation and Breakout.** Rejected because their theses, gates, and rank order differ, and a combined result would hide which selection process produced the outcome.
- **Treat no-entry as zero return.** Rejected because it conflates execution availability with investment performance.
- **Show metrics at any non-zero coverage.** Rejected because incomplete rows can select a misleading subset of recommendations.

## Consequences

- Athena can compare the two lenses on the same cost-aware closing and excursion evidence.
- Reruns remain visible but cannot inflate the independent cohort count.
- Operators can distinguish poor returns, poor paths, unavailable entries, invalid evidence, and immature evidence.
- Later Top-N and portfolio reports can reuse the cohort-weighting rule without being confused with this all-published recommendation scorecard.
- No new SQL object is required; the report is a deterministic join and calculation over the existing ledger.

## Review questions

1. Why are Continuation and Breakout reported separately?
2. How does nested run-then-cohort aggregation prevent reruns from inflating evidence?
3. Why does `NoEntry` count as usable coverage but not as zero return?
4. Why are performance fields blocked below 95% usable coverage?
