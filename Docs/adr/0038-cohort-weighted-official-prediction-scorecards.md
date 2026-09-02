# ADR-0038: Cohort-weighted official prediction scorecards

- **Status:** Accepted
- **Date:** 2026-08-27
- **Domains:** architecture, decision-engine, machine-learning, math-statistics
- **Related:** ADR-0020, ADR-0021, ADR-0022, ADR-0024, ADR-0027, ADR-0029, ADR-0037

## Context

The immediate problem is turning matured official Delphi predictions into model-calibration, lens-ranking, and diagnostic performance evidence. The parent problem is distinguishing whether Delphi predicted well from whether a later paper or real trading controller executed well. The root goal is to improve Delphi from reproducible evidence while every strategy change remains under human control.

Coverage and three-session tradeability scorecards already exist. They do not yet answer whether each model's probabilities are honest, whether higher lens ranks lead to better ten-session returns, or which captured technical and market states are associated with different outcomes. Manually timed ghost positions and actual broker holdings have different selection and fill contracts and cannot repair this gap by being mixed into the official population.

## Decision

Add a deterministic version-1 official prediction scorecard over `PredictionLabels10` version 1. Include every candidate from non-invalid `OfficialPaper` runs and exclude `ExploratoryReplay`, reconstructed, operational ghost, and real-trade rows. Require exact definition identity, run purpose, run/cohort linkage, candidate uniqueness, one Continuation plus one Breakout row per candidate, unique per-run lens ranks, correct outcome session, schema version, and the four accepted event labels.

Preserve market-session dependence with nested weighting:

1. candidates have equal weight inside their official run;
2. official reruns have equal weight inside `MarketDataAsOf`;
3. distinct `MarketDataAsOf` cohorts have equal weight.

Continue ADR-0024's 95% usable-coverage floor. Counts remain visible below the floor, but probability, rank, and slice performance fields are blocked. A matured empty cohort remains visible but cannot unlock performance for an incomplete non-empty cohort.

### Probability calibration

Report `BinaryUp10`, `BinaryDown10`, `VolExpansionRelative10`, and `BreakoutEnhanced` separately against their exact version-1 label events:

- Brier score, where lower squared probability error is better;
- area under the receiver-operating-characteristic curve (AUC), computed per run only when both event classes exist and then nested by cohort;
- ten fixed reliability buckets `[0.0,0.1)` through `[0.9,1.0]`, including predicted and observed event rates;
- expected calibration error (ECE), using each reliability bucket's nested cohort weight;
- within-run probability deciles and event-rate lift versus that run's full candidate baseline.

Probability ties use immutable `CandidateId` ordering. Outcomes never break a probability or rank tie. A model-specific metric also requires at least 95% usable probability coverage; unsupported AUC remains null rather than being invented.

### Lens rank performance

Keep Continuation and Breakout separate. Within each lens, use candidates that passed that lens's gate stack. Calculate Spearman rank information coefficient per supported run between higher-is-better `-Rank` and ten-session return, then apply nested cohort weighting. Report Top-1, Top-3, Top-5, and top-decile mean ten-session return, excess return versus XIU, and return lift versus the full eligible population of the same run.

These are prediction-ranking diagnostics, not capital-constrained portfolios and not the published-recommendation tradeability scorecard from ADR-0027.

### Diagnostic slices

For usable matured candidates, show cohort-weighted ten-session return, excess return, and the four event rates by:

- OBV state;
- captured market regime;
- captured sector-index identity;
- first lens gate result (`Pass` or the first failed gate);
- published lens;
- observation-session dollar volume: low below $1 million, medium from $1 million through below $5 million, and high at least $5 million;
- observation-session high-low range divided by close: low below 2%, medium from 2% through below 5%, and high at least 5%.

The last two are explicitly one-session diagnostic proxies, not new Delphi liquidity or volatility gates. Slice counts and contributing cohorts must remain visible. A slice is descriptive association, not causal evidence and never changes a weight automatically.

### Output and execution boundary

Athena prints the advanced report after outcome coverage. Provide five invariant-culture CSV artifacts under export schema version 1: coverage, models, reliability/deciles, lens rank, and slices. An explicit `--scorecard-csv DIRECTORY` option writes them and refuses to overwrite an existing artifact.

ADR-0042 supersedes only the export identity contract: the same five artifacts now use export schema
version 2 so every file carries the selected strategy ID/name, initial code commit, and decision
reference. The version-1 metric formulas and weighting remain unchanged.

The scorecard requires no schema migration and performs no broker action. The user's actual EDR holding and its current ghost monitoring mirror remain outside this official scorecard. A later trading-dashboard decision must add a durable execution mode such as `Ghost` or `Real`; a ghost icon may reinforce that distinction but cannot be its only control or data boundary.

## Alternatives considered

- **Average every candidate row directly.** Rejected because repeated official runs and candidate-rich sessions would receive extra weight.
- **Use only published picks.** Rejected for model calibration because it would hide most predictions and introduce selection bias; published tradeability remains separately reported.
- **Mix manual ghost or real fills into official outcomes.** Rejected because their timing, selection, sizing, and execution contracts differ.
- **Use outcome values to settle tied probabilities.** Rejected as look-ahead leakage.
- **Automatically retune a signal when its slice looks favourable.** Rejected because correlated signals, repeated testing, and small cohorts can create misleading associations.

## Consequences

- Athena can explain probability honesty, lens ordering, and conditional behavior without changing Delphi.
- Reruns remain auditable without masquerading as independent evidence.
- Early reports will often be blocked or statistically unsupported; this is expected and visible.
- The fixed diagnostic buckets are version-1 report definitions. Changing their boundaries or metric formulas requires a new report version.
- Ghost-controller, real-execution, and portfolio scorecards remain separate future contracts.

## Review questions

1. Why can a model have useful AUC but poor probability calibration?
2. How does nested run/cohort weighting prevent a Delphi rerun from inflating evidence?
3. Why do favourable OBV or gate slices generate a challenger hypothesis rather than an automatic weight change?
4. Why can an actual EDR fill not enter the official `PredictionLabels10` scorecard?
