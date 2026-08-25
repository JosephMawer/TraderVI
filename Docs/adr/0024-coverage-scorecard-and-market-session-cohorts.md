# ADR-0024: Coverage scorecard and market-session cohort identity

- **Status:** Accepted
- **Date:** 2026-08-23
- **Domains:** architecture, data-pipeline, decision-engine, math-statistics
- **Related:** ADR-0020, ADR-0021, ADR-0022
- **Refined by:** ADR-0025 for tradeable-definition population and `NoEntry` coverage
- **Extended by:** ADR-0027 for lens-level joint coverage and nested run/cohort aggregation

## Context

The immediate problem is deciding when Athena may display a performance metric and how it counts official evidence. The parent problem is preventing incomplete outcomes and repeated Delphi runs from looking like stronger evidence than they are. The root goal is to tune Delphi only from transparent, correctly weighted paper evidence.

`RecommendationDate` is the operational run date and may be a weekend or another calendar date using the same completed market data. Multiple immutable official runs may also exist for one completed market session. Those runs are valuable audit records, but they are not independent market observations.

## Decision

Lead every Athena result with one deterministic coverage scorecard per active prediction-outcome definition.

For each definition, report:

- official run count;
- total and fully matured market-session cohorts;
- expected candidate outcomes;
- valid, degraded, invalid, and pending outcome counts;
- completion coverage and usable coverage;
- whether the primary descriptive score is reportable.

Include only `OfficialPaper` runs whose run audit state is not `Invalid`. The expected population is every candidate in those runs. An official run with no evaluated candidates still counts as an abstaining run and market-session cohort but does not invent a pending candidate. A terminal outcome contributes to completion coverage. A `Valid` or `Degraded` terminal outcome contributes to usable coverage; an `Invalid` outcome remains visible but is excluded from usable performance input. Pending outcomes contribute to neither.

Define a prediction cohort by the run's completed `MarketDataAsOf` session. Repeated official runs over the same market session increase the visible run and candidate audit counts but never increase the independent cohort count. Downstream performance calculations must aggregate or otherwise preserve that market-session dependence before comparing cohorts.

Allow a primary descriptive score to be reported only when at least one cohort is fully matured and usable coverage is at least 95%. Below that floor, label the score `BLOCKED`. Meeting this reporting floor does not satisfy ADR-0022's 10/20–30/60/120-cohort evidence tiers and never authorizes a strategy change.

Use two separate percentages:

- **Completion coverage** = all terminal outcomes / expected outcomes.
- **Usable coverage** = valid plus degraded terminal outcomes / expected outcomes.

Show `Degraded` when a report clears the usable floor but contains degraded, invalid, or pending rows. Show `Ready` only when every expected row is terminal and valid. Show `NoEvidence` when no official candidates exist for the definition.

## Alternatives considered

- **Count `RecommendationDate` as the cohort.** Rejected because several run dates can use the same completed market session and therefore are not independent prediction evidence.
- **Count every official run as a cohort.** Rejected because deliberate reruns would inflate sample size.
- **Hide invalid outcomes from coverage.** Rejected because the operator needs to see both data completion and usable evidence loss.
- **Publish a metric at any non-zero coverage.** Rejected because a selected subset can look materially better or worse than the intended candidate population.

## Consequences

- The scorecard can honestly show one cohort alongside several audit runs.
- A 100% completed report can still have lower usable coverage when invalid outcomes exist.
- Performance and promotion reports must not treat rerun candidate rows as independent observations.
- The initial scorecard needs no new SQL object; its query and calculations remain deterministic application code.

## Review questions

1. Why is `MarketDataAsOf` the prediction-cohort key rather than `RecommendationDate` or `RunId`?
2. What is the difference between completion coverage and usable coverage?
3. Why can a 95%-covered score still be insufficient for tuning Delphi?
