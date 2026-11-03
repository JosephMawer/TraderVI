# ADR-0124: Grade due ten-session predictions and label partial coverage

- **Status:** Accepted
- **Date:** 2026-11-03
- **Domains:** math-statistics, data-pipeline

Athena and the desktop show supported descriptive scores as soon as an official ten-session prediction can be graded. The report compares graded predictions with all predictions due. Below 95% it labels the result **Provisional**.

## Context

The November 1 inventory found usable outcomes for 9,281 of 9,284 due candidates. Another 2,506 were too recent. Counting both groups together hid the score even though nearly all due outcomes were available. The purpose of this report is to judge Delphi against the event each model predicted. Grading on an earlier day changes that event. [Measured evidence](../project-status-history-20261101.md#november-1--athena-prediction-scorecards-and-the-ten-session-window) and [prediction contract](0021-calibration-outcome-and-paper-execution-contract.md).

## Decision

- Count a candidate as due when ten distinct stored XIU daily sessions occur strictly after its observation date. XIU supplies the common market clock. Missing stock bars or outcomes cannot postpone that deadline. Count younger candidates separately as waiting.
- Count every due candidate in coverage. Grade valid and usable degraded outcomes. Show due candidates with pending or absent outcomes separately from unusable outcomes. Count all official runs and distinct observation sessions, including abstentions. An empty run supplies no predictions.
- Compute supported model, rank and slice metrics on gradable observations using the existing nested run and market-session weighting. Show **No score** when none can be graded, **Provisional** when some can be graded but coverage is below 95%, and **Ready** at or above 95%. Show the graded and due counts. Each model uses the same due denominator and additionally requires a usable saved probability. Some statistics, such as within-run AUC with only one event class, can still lack support.
- Apply this reporting rule to `PredictionLabels10` v1 only. The all-date inventory remains visible in Athena. CSV export schema v3 carries the report or model status alongside the metrics. Strategy promotion continues to require its separate evidence and review.

Lens selections and model deciles describe their graded subsets. For example, if the highest-ranked eligible candidate lacks an outcome, the top graded candidate is the next eligible candidate with an outcome. Displaying coverage alongside the score makes that limitation visible.

This supersedes the ten-session prediction reporting floor in [ADR-0020](0020-immutable-calibration-evidence-ledger.md), [ADR-0024](0024-coverage-scorecard-and-market-session-cohorts.md), [ADR-0038](0038-cohort-weighted-official-prediction-scorecards.md) and the desktop display rule in [ADR-0039](0039-unified-ghost-real-dashboard-and-scorecard-workspace.md). It revises the CSV format introduced in [ADR-0042](0042-strategy-code-identity-and-scoped-official-evidence.md).

## Review questions

1. A due stock has no outcome. How does it affect the score and the coverage fraction?
2. Why can the overall report be ready while a single model is provisional?
