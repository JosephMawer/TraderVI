# Paper calibration and outcome feedback

- **Status:** Background design brief; implementation decisions accepted and refined in ADR-0020 through ADR-0032
- **Date:** 2026-08-23
- **Domains:** architecture, data-pipeline, decision-engine, machine-learning, math-statistics, risk-management
- **Related ADRs:** ADR-0002, ADR-0007, ADR-0009, ADR-0010, ADR-0011, ADR-0013, ADR-0014, ADR-0015, ADR-0016, ADR-0019

## Purpose of this document

TraderVI needs more than a simulated brokerage ledger. It needs a durable observation and feedback system that can answer two different questions:

1. **Were Delphi's predictions and technical observations correct?**
2. **Could a consistent, realistically executable policy based on Delphi's recommendations have made money while controlling losses?**

This document records the original background, objectives, boundaries, proposed architecture, measurement framework, and open questions for that system. It remains a concept/design brief rather than an ADR. ADR-0020 through ADR-0032 are authoritative where they accept or refine a proposal in this brief; unaccepted wording here remains background or a proposed default.

The paper-calibration system may use a working name such as **Athena**, but the application name is not yet decided.

## Summary

The proposed system sits above Hermes and Delphi:

```text
Hermes imports completed market sessions
                    |
                    v
Delphi publishes versioned predictions and recommendations
                    |
                    v
Immutable observation ledger
       |                            |
       v                            v
Prediction outcomes          Tradeable outcomes
(all evaluated candidates)   (Delphi recommendations only)
       |                            |
       +-------------+--------------+
                     v
       Metrics, feature audits, and shadow portfolios
                     |
                     v
       Champion/challenger calibration proposals
                     |
                     v
        Human approval + new StrategyVersion
```

The feedback loop is:

> Observe → measure → diagnose → propose → validate → approve → version → monitor.

The system must automate observation and measurement before it automates calibration. It must never silently rewrite weights, thresholds, features, gates, or the active strategy. Early evidence may create provisional challengers, but the current strategy remains the champion until a separately validated change is explicitly approved.

## Objectives

### Primary objectives

- Preserve exactly what Delphi knew and believed at recommendation time.
- Evaluate prediction quality using outcomes aligned with each model's actual label definition.
- Evaluate tradeability using entry, exit, cost, sizing, and portfolio rules that could have been followed without look-ahead.
- Compare Continuation and Breakout as distinct theses.
- Determine whether Delphi's ordering adds value, not merely whether selected symbols later rose.
- Measure how individual technical observations, model probabilities, gates, and ranking components contribute to outcomes.
- Establish baselines and targets before changing weights or thresholds.
- Support champion/challenger experiments without changing the operational recommendation.
- Detect drift, missing outcomes, data leakage, unstable weights, redundant indicators, and other calibration-quality problems.
- Preserve enough provenance that any result can be replayed and explained later.

### Longer-term objectives

- Decide whether the current one-position concentration rule should remain, or whether a diversified short-term selector produces a better return/risk tradeoff.
- Calibrate raw relative strength versus Z-scored relative strength.
- Calibrate `ObvSignalWeight` from evidence rather than intuition.
- Evaluate whether deterministic gates preserve capital or reject too many future winners.
- Audit Granville cross-family overlap before changing point weights.
- Establish a stable outcome dataset that Hercules and future walk-forward tools can consume without recreating historical decisions using today's code.
- Eventually allow a local or remote LLM to narrate deterministic calibration reports, while keeping all calculations and strategy changes outside the LLM.

## Non-goals for the first implementation

- Live brokerage integration or automated order placement.
- Automatic activation of a newly tuned strategy.
- Continuous online retraining after every outcome.
- Using an LLM to calculate metrics, choose weights, override gates, or promote a strategy.
- News or fundamentals ingestion.
- Changing the current Delphi recommendation solely to make the paper system easier to build.
- Treating backtested profit as proof that future live trading will be profitable.
- Replacing DataAudit; market-data integrity and recommendation-outcome integrity are related but separate audit domains.

## Existing foundation

TraderVI already has useful building blocks:

- `dbo.DailyPick` stores published picks, ranks, lens, probabilities, sizing, and `StrategyVersionId`.
- Delphi publishes both Continuation and Breakout top lists.
- Continuation is the operationally executed/recommended lens; Breakout is journaled as a comparison thesis.
- Continuation picks receive versioned `DecisionDossier` snapshots for deterministic explanation and Oracle narration.
- `StrategyConfig` and `dbo.StrategyVersion` provide a starting point for versioned thresholds.
- `TradeLog`, `ActivePosition`, and ghost-mode `TradeManager` provide a manual simulated-position lifecycle.
- `BacktestHarness` provides a minimal single-series next-bar walk-forward harness.
- Oracle already separates deterministic dossiers from provider-neutral LLM narration.
- DataAudit and the Delphi freshness eligibility rule provide independent data-quality defenses.

These pieces are not yet a calibration ledger:

- `DailyPick` contains only published top selections, not every evaluated candidate or near miss.
- A Delphi rerun replaces picks for a date, so `DailyPick` is an operational current-state table rather than immutable experiment history.
- Breakout picks do not currently receive full DecisionDossiers.
- The manual ghost-trade tables do not automatically link every simulated fill to its originating pick and policy.
- The current backtest harness does not replay Delphi's cross-sectional universe, two ranking lenses, market gates, portfolio rotation, or historical strategy/model versions.
- No process waits for outcomes to mature and attaches them to the exact prediction snapshot that produced them.

## Core design principles

### 1. Preserve the observation before observing the result

Every official or exploratory Delphi evaluation must have a unique run identifier. A rerun on the same recommendation date is a new run, not a replacement of the prior evidence.

At minimum, provenance must identify:

- recommendation date;
- canonical market-data session;
- run purpose, such as official paper run or exploratory replay;
- strategy version and complete effective configuration;
- model registry/artifact versions;
- code version, preferably the Git commit;
- feature/dossier schema version;
- universe membership and exclusion counts;
- data-quality/freshness state;
- creation timestamp.

The operational `DailyPick` workflow may continue to represent the latest published picks for a date. The calibration ledger must remain immutable even if operational rows are refreshed.

### 2. Separate facts shared across lenses from lens decisions

Per-symbol model probabilities and most technical observations are computed once. Lens eligibility and ordering differ.

A logical evidence model therefore needs three levels:

1. **Run snapshot** — shared market context and version provenance.
2. **Candidate snapshot** — symbol-level prices, model signals, technical observations, and shared calculations.
3. **Lens evaluation** — Continuation or Breakout gate trace, ranking key, eligibility, rank, and publication status.

This avoids duplicating large shared snapshots while preserving exactly why the same symbol ranked differently under each thesis.

### 3. Prediction quality and tradeability are different products

A prediction can be technically correct yet unprofitable after delayed entry, slippage, stops, or reversal. A profitable trade can also occur even when a specific model label was false. The system must report both views without collapsing them into one score.

### 4. Derived outcomes are reproducible and idempotent

The outcome evaluator should use locally stored market data and write only missing matured outcomes. Re-running it must produce the same result for the same versioned outcome definition and source data.

If an outcome definition changes, create a new definition/version. Do not rewrite old outcomes in place and silently change historical reports.

### 5. Calibration is controlled experimentation

The active strategy is the **champion**. Alternative configurations are **challengers**. A challenger may be replayed against saved candidate observations or run prospectively in shadow mode, but it cannot become active merely because it performed best on the data used to invent it.

## The two outcome windows

### Prediction outcomes

#### Population

Prediction outcomes should cover the complete eligible/evaluated symbol universe, including:

- gate passers;
- symbols rejected by each deterministic gate;
- ranked and unranked candidates;
- candidates that did not enter the published top list;
- both Continuation and Breakout lens evaluations where lens-specific attribution is required.

Capturing only winners introduces selection bias and prevents threshold, gate, and rank analysis.

#### Label-aligned outcomes

The first outcome definitions should reproduce the enabled model contracts exactly:

- `BinaryUp10`: observation-session close to the tenth future session close; positive event at return ≥ +4%.
- `BinaryDown10`: the same close-to-close return; positive veto event at return ≤ -4%.
- `BreakoutEnhanced`: whether any high in the next ten sessions reaches the prior 20-session high plus 1%.
- `VolExpansionRelative10`: evaluate using the exact enabled labeler's future-window definition.

These are research outcomes, not executable fills. They deliberately start from the observation close because that is how the model labels were trained.

#### Prediction metrics

At minimum:

- event base rate;
- Brier score, `mean((probability - event)^2)`;
- probability reliability/calibration by bucket;
- calibration error and over/under-confidence;
- AUC or rank discrimination where sample size supports it;
- lift by probability decile;
- Spearman rank information coefficient between ranking signal and future return;
- top-1/top-3/top-5/top-decile return lift;
- gate pass/fail outcome comparison;
- coverage, missingness, and maturity counts.

These metrics should be sliced by:

- lens;
- market regime;
- strategy/model version;
- security type;
- sector where meaningful;
- volatility/liquidity bucket;
- recommendation/rank bucket;
- technical signal state, such as OBV Rising/Doubtful/Falling.

### Tradeable outcomes

#### Population

Tradeable outcomes must be formed only from recommendations actually published by Delphi, not from arbitrary candidates discovered after the fact.

The initial recommendation pools are:

- persisted Continuation picks, with Continuation remaining the operational champion;
- persisted Breakout picks as a separately labelled shadow thesis.

The system may test multiple selection/portfolio policies over those published lists. It must not claim that an unselected full-universe candidate was a trade Delphi recommended.

#### Proposed entry convention

The default proposal is entry at the next completed trading session's open after the market-data session used by Delphi. This avoids pretending that a trade could be filled at the same close whose data produced the prediction.

The final ADR must decide:

- how to handle Delphi runs that occur after the next market open;
- missing next-session bars;
- halted or gapped securities;
- whether entry uses raw open, a slippage-adjusted open, or a volume-aware fill model;
- how commissions and bid/ask spread are represented.

#### Path and terminal outcomes

For each recommendation and policy, capture:

- actual entry session and price;
- return after 1, 5, 10, and 20 completed sessions;
- XIU return over the identical interval;
- excess return, `symbol return - XIU return`;
- maximum favourable excursion (MFE);
- maximum adverse excursion (MAE);
- time to MFE and MAE;
- warning and stop-loss hits;
- high-water mark and drawdown path;
- exit date, price, reason, gross return, costs, and net return;
- maturity and data-quality state.

Daily OHLC data cannot always tell whether a profit target or stop was hit first when both fall inside the same day's range. Such cases must be flagged as path-ambiguous or resolved by a documented conservative rule; they must not receive an optimistic ordering by accident.

#### Two tradeability views

The system should distinguish:

1. **Selection-quality portfolios** — normalized or fractional allocations used to compare selectors without allowing current capital or integer shares to dominate the result.
2. **Capital-constrained portfolios** — use configured capital, reserve, integer shares, affordability, costs, and explicit fill assumptions.

Both matter. The first asks whether diversification improves selection quality; the second asks whether it was realistically usable at the account size in effect at that time.

## Portfolio policies to compare

The current one-position rule remains the operational champion, but it must not constrain research.

Initial shadow policies should include:

- **Top-1 concentrated** — current selection philosophy and direct baseline.
- **Top-3 equal-weight** — basic diversification.
- **Top-5 equal-weight** — broader diversification.
- **Rank-weighted** — more capital to higher-ranked recommendations, with a versioned weighting formula.

Later candidates may include volatility-scaled or risk-parity allocations, but those introduce additional assumptions and should follow the simple selectors.

Every policy needs explicit rules for:

- initial entry;
- maximum simultaneous positions;
- cash and reserve handling;
- replacement/rotation when a new recommendation arrives;
- minimum improvement required before rotating;
- stop-loss and warning behavior;
- maximum holding period;
- re-entry and duplicate-symbol handling;
- costs, slippage, and integer-share constraints.

Changing a policy creates a new policy version; it does not rewrite prior simulated trades.

## Continuation and Breakout scorecards

### Common economic scoreboard

Both lenses should be compared on common outcomes:

- top-ranked 10-session gross and net return;
- 10-session excess return versus XIU;
- top-3/top-5 average and median return;
- win rate and loss rate;
- average win, average loss, expectancy, and profit factor;
- lower-tail return and large-loss frequency;
- drawdown and recovery time;
- turnover and estimated trading cost;
- time in market and cash utilization;
- performance by regime and liquidity bucket.

The initial primary target is proposed as:

> Improve the top-ranked recommendation's average 10-session excess return versus XIU without materially worsening lower-tail loss, drawdown, or turnover.

Absolute profit remains important, but XIU-relative performance distinguishes selector skill from a generally rising or falling market.

### Continuation-specific questions

- Do higher RS ranks produce better 5-session and 10-session excess returns?
- Does the edge persist through the intended holding window rather than appearing only on day one?
- Does `CompositeScoreZ` separate future leaders better than raw `CompositeScore`?
- Does the OBV tilt improve ordering within gate passers?
- Is the OBV tilt large relative to the observed RS dispersion among gate passers?
- Does `TrendConfirmationGate` exclude more future losers than winners?
- Is Continuation performance stable across market regimes and sectors?

Useful metrics include rank IC, top-minus-bottom rank-bucket return, excess-return persistence, MFE/MAE, and OBV-state conditional lift.

### Breakout-specific questions

- Did the defined breakout event occur within ten sessions?
- How quickly did it occur?
- What adverse excursion occurred before the breakout?
- Did the breakout follow through economically after it occurred?
- How often did a technically correct breakout prediction still produce a losing trade?
- Does Breakout ranking order breakout speed, MFE, or terminal return?
- Which setup/gate combinations distinguish follow-through from false breaks?

A Breakout model may correctly predict a future threshold crossing while the resulting trade loses money. Event accuracy and trade profitability must therefore remain separate fields and reports.

## Baselines and evidence levels

### Baselines

At minimum, compare against:

- XIU over the same sessions;
- the current Continuation Top-1 policy;
- the journaled Breakout lens;
- simple Top-3 and Top-5 selectors over the same published lists.

Once full candidate observations are available, useful deterministic challengers include:

- current ranking with OBV tilt set to zero;
- raw RS versus Z-scored RS ranking;
- alternative OBV weights;
- gate-threshold variants;
- simple highest-RS or highest-DirectionEdge selectors.

These are ablations and challengers, not retrospective changes to what Delphi actually recommended.

### Graduated evidence rather than a rigid waiting period

Sixty sessions should be a confidence tier, not a prohibition on learning.

Proposed evidence levels:

- **First 10 matured sessions:** validate timing, joins, maturity, label reproduction, reports, and audit behavior. Findings may fix measurement bugs but should not be treated as strategy evidence.
- **20–30 matured sessions:** permit provisional, low-risk challenger experiments when historical walk-forward evidence points in the same direction. Do not silently promote them.
- **Approximately 60 matured sessions:** stronger live evidence for promotion, especially when results cover more than one regime and agree with held-out historical tests.
- **Higher-risk changes:** removing a safety gate, increasing concentration, or materially loosening downside protection requires stronger evidence than a small ranking-weight adjustment.

Ten-session outcomes overlap heavily from one recommendation day to the next. Twenty-five picks from one date are also correlated. Reports must not pretend these are independent samples. The principal live sample count should be matured recommendation sessions/cohorts, with symbol-level observations reported as additional evidence.

No numeric threshold should become permanent merely because it won among many alternatives on one window. Record how many variants were tried and preserve an untouched forward-validation period.

## Champion/challenger calibration

### Champion

The champion is the exact active strategy/model/policy version that produced the operational recommendation. It remains unchanged while evidence accumulates.

### Challenger

A challenger is a fully versioned alternative that may differ in one controlled dimension, for example:

- OBV weight;
- raw RS versus Z-scored RS;
- one gate threshold;
- Top-1 versus Top-3 selector;
- rotation or holding policy.

Prefer changing one family at a time so attribution remains possible.

### Proposed promotion contract

Before activation, a challenger should have:

1. A predeclared hypothesis, primary metric, risk guardrails, and evaluation window.
2. Reproducible performance from immutable observations.
3. Improvement on the primary metric.
4. No unacceptable deterioration in downside, drawdown, turnover, or coverage.
5. A distinct forward-validation window not used to invent or select it.
6. Human review and approval.
7. A new `StrategyVersion`, policy version, ADR, or other appropriate immutable decision record.
8. A rollback comparison that keeps the former champion available.

Automatic measurement and automatic challenger generation may be acceptable later. Automatic promotion is explicitly out of scope.

## Feature, weighting, and gate audits

Each signal or technical component should receive a recurring scorecard containing:

- availability and missingness;
- firing/state distribution;
- value distribution and drift;
- future-return lift;
- rank IC;
- performance by market regime;
- correlation with other signals;
- incremental value after accounting for the current ranker;
- influence magnitude relative to other ranking components;
- stability across time windows;
- sample size and uncertainty.

Initial targeted audits include:

- `ObvSignalWeight` relative to gate-passer RS dispersion.
- OBV Rising/Doubtful/Falling outcome separation.
- raw RS versus `CompositeScoreZ` ordering and stability.
- DirectionEdge versus RS contribution by lens.
- each gate's rejected-winner and rejected-loser rates.
- Granville family co-firing and sign redundancy.
- CLX confirmation/divergence outcome usefulness after sufficient history exists.
- model probability calibration drift.

## Outcome-system integrity audits

The feedback system itself must be audited. At minimum detect:

- duplicate or overwritten runs;
- observations without a strategy/model/code version;
- outcomes joined to the wrong market session;
- use of bars not available at decision time;
- missing or insufficiently mature horizons;
- symbol changes, halted listings, and absent entry bars;
- future-dated or stale source histories;
- inconsistent label implementations between training and evaluation;
- portfolio fills that violate available capital or share-count rules;
- ambiguous same-day stop/target ordering;
- survivorship bias from evaluating only today's active universe;
- accidental mixing of Continuation and Breakout rows;
- changes to feature schemas without a version bump;
- reports that mix different champion/configuration versions.

Calibration reports should lead with coverage and audit status. A performance number derived from incomplete or mismatched observations must be visibly degraded or blocked.

## Proposed application boundary

The evaluator should be a separate console application or service, not hidden inside Hermes or Delphi.

- **Hermes** imports and derives market data, then creates the database backup checkpoint.
- **Delphi** computes and publishes predictions/recommendations.
- **Calibration evaluator** reads immutable observations and newly available local bars, then writes versioned derived outcomes and reports.
- **DataAudit** independently checks classification, freshness, mapping, and structural market-data integrity.
- **Hercules** trains registered models from explicitly prepared historical features.
- **Oracle/LLM** may later narrate persisted deterministic reports.

The evaluator will write derived database records, so it is not a read-only audit. Its writes should be narrow, append-only or idempotent, reconstructible, and independent of external market services.

Whether the evaluator is launched manually, after Hermes, or through later automation is an operational decision. Its correctness must not depend on Visual Studio or on being embedded in Hermes.

## Logical persistence model

The final names and normalization are undecided, but the design likely needs these logical entities:

### Recommendation run

One immutable row per Delphi evaluation, containing run identity, dates, purpose, code/model/strategy versions, shared market context, and universe/audit counts.

### Candidate observation

One immutable snapshot per symbol per run containing shared prediction and technical facts. A versioned JSON payload may preserve the full shape, while selected columns support efficient metrics and joins.

### Lens evaluation

One row per candidate, run, and lens containing gate trace or first failure, ranking key, direction, eligibility, rank, and whether the candidate was published by Delphi.

### Outcome definition

A versioned contract describing timing, horizon, benchmark, label, fill, cost, stop, and ambiguity rules. Reports must identify which definition produced them.

### Candidate outcome

Matured prediction and price-path results joined to the immutable candidate observation.

### Shadow policy and portfolio run

Versioned selector/allocation/rotation rules plus their simulated fills, positions, equity curve, and costs.

### Calibration experiment/proposal

A durable record of hypothesis, champion, challenger, data windows, primary metric, guardrails, results, decision, and approval status.

The new task must compare this model against extending existing tables. The default bias should be to preserve `DailyPick`, `DecisionDossier`, `TradeLog`, and `ActivePosition` semantics and add purpose-built calibration history rather than overloading operational tables.

## LLM boundary and deferred dotLLM work

DotLLM integration is explicitly postponed. The architecture should preserve the existing `ILlmClient` seam, but the first calibration implementation must provide full value without an LLM.

A future LLM may:

- summarize deterministic weekly reports;
- compare champion and challenger evidence;
- translate metric changes into understandable hypotheses;
- surface unusual combinations for human investigation;
- maintain a readable research journal;
- help create regression/evaluation fixtures.

It may not:

- compute returns or calibration statistics;
- invent missing outcomes;
- directly modify weights, thresholds, models, or gates;
- promote a challenger;
- use context that was not persisted and versioned;
- bypass `Docs/oracle-rules.md`.

If future work wants the LLM to generate formal calibration proposals, that role must be reconciled explicitly with Oracle Rule R1 and isolated from deterministic validation and human approval.

## Proposed implementation phases

### Phase A — Measurement contract and ADRs

- Finalize recommendation/run semantics.
- Define prediction and tradeable outcome timing.
- Define initial metrics, maturity, benchmark, cost, and ambiguity rules.
- Define immutability and versioning requirements.
- Define champion/challenger evidence and approval rules.
- Record decisions in ADRs before schema implementation.

### Phase B — Immutable evidence capture

- Add reviewed, additive manual migration(s).
- Add run, candidate, and lens-evaluation domain/repository types.
- Extend Delphi to emit the complete evaluation snapshot.
- Preserve the existing `DailyPick` and DecisionDossier workflow.
- Add focused tests for reruns, version provenance, lens separation, and stale-history exclusion.

### Phase C — Prediction outcome evaluator

- Reuse the production labelers where possible so training and evaluation definitions cannot drift.
- Attach matured 1/5/10/20-session and exact model-event outcomes.
- Add idempotency and maturity/integrity audits.
- Produce deterministic Continuation/Breakout and feature scorecards.

### Phase D — Tradeable outcome evaluator

- Implement the agreed next-session entry and cost rules.
- Compute MFE, MAE, stops, terminal results, and XIU-relative results.
- Flag path ambiguity explicitly.
- Produce recommendation-level tradeability reports.

### Phase E — Shadow portfolios

- Implement Top-1, Top-3 equal-weight, Top-5 equal-weight, and rank-weighted policies.
- Report normalized selection quality separately from capital-constrained feasibility.
- Add equity curves, drawdown, turnover, and policy attribution.

### Phase F — Calibration experiments

- Freeze the current champion.
- Add one-variable challengers and ablations.
- Add walk-forward and forward-validation windows.
- Store proposals and decisions without automatic promotion.

### Phase G — Optional LLM narration

- Revisit dotLLM/local inference only after deterministic reports and evaluation fixtures are stable.
- Keep the LLM strictly downstream and auditable.

## Confirmed direction

The following are agreed starting constraints:

- Maintain separate prediction and tradeable outcome windows.
- Prediction outcomes examine the complete evaluated candidate universe.
- Tradeable outcomes are based only on recommendations published by Delphi.
- Measure Continuation and Breakout separately.
- Do not constrain research portfolios to the current one-position policy.
- Keep Top-1 as the operational champion while comparing diversified shadow selectors.
- Preserve immutable, versioned observations and outcomes.
- Use champion/challenger calibration with human approval.
- Use graduated evidence levels rather than forbidding all learning until 60 sessions.
- Hold off on dotLLM implementation.
- Continue the manual, additive database-migration workflow; do not publish a DACPAC.

## Proposed defaults requiring a decision

These items were proposals when this brief was written. They are now resolved by ADR-0020 (evidence/provenance), ADR-0021 (initial outcomes/execution/evaluator), ADR-0022 (evidence/promotion), ADR-0023 (primary swing direction and separate opening/intraday challengers), ADR-0024 (coverage and market-session cohort identity), ADR-0025 (the initial three-session swing mark-to-market measure), ADR-0026 (MFE/MAE excursion measurement), and ADR-0027 (separate lens tradeability scorecards and cohort aggregation). The ADRs are authoritative where wording differs.

The following are recommendations, not yet accepted decisions:

- Enter tradeable simulations at the next session open.
- Measure horizons at 1, 5, 10, and 20 sessions.
- Use 10-session excess return versus XIU as the first primary economic metric.
- Compare Top-1, Top-3 equal-weight, Top-5 equal-weight, and rank-weighted policies.
- Treat 10, 20–30, and approximately 60 matured sessions as measurement, provisional-challenger, and stronger-promotion evidence tiers.
- Store the complete candidate snapshot once per run and separate lens evaluations.
- Add purpose-built calibration tables instead of changing operational tables into experiment history.
- Run the outcome evaluator as a separate local application using no external market services.

## Open decisions for the next task

Resolved on 2026-08-23. Decisions 1–5, 13, 16–18 are covered by ADR-0020; decisions 6–11, 14–16 are covered by ADR-0021; decisions 12 and 18 are covered by ADR-0022. This list remains as design-history context rather than an active question queue.

1. What exact event creates an official immutable recommendation run?
2. Should exploratory Delphi reruns be persisted, and how are they labelled so they cannot enter official paper results?
3. Which full-universe candidates are captured: every symbol loaded, every model-evaluated symbol, or every symbol reaching lens evaluation?
4. Which data belongs in normalized columns versus a versioned JSON snapshot?
5. How are model artifact identity and Git commit recorded reliably from Visual Studio and terminal launches?
6. Is the next-session open the correct entry, and what happens if Delphi runs after that open?
7. What initial slippage, spread, commission, and fill assumptions are defensible for TSX names and a small account?
8. What are the first exit/rotation policies for Top-1, Top-3, and Top-5 portfolios?
9. How are same-day stop/target ambiguities handled with daily OHLC data?
10. Should normalized portfolios allow fractional shares while capital-constrained portfolios require integer shares?
11. Is 10-session XIU-relative return the primary metric for both lenses, or should Breakout have a separate primary event metric with economic return as a co-primary guardrail?
12. What minimum maturity, regime coverage, and uncertainty are required for different classes of change?
13. Which historical observations can be reconstructed point-in-time correctly, and which must begin prospectively?
14. Can existing labeler code be reused directly by the evaluator without pulling training concerns into the runtime boundary?
15. How should the new evaluator be named and launched initially?
16. Which reports are console, SQL views, CSV, or a future UI?
17. When should an outcome or report be considered invalid rather than merely degraded?
18. How should calibration proposal approval relate to `StrategyVersion`, code constants, model registry rows, and ADRs?

## Success criteria for the first useful release

The first release is useful when:

- every official Delphi run has immutable, versioned evidence;
- both lenses and the full evaluated universe are distinguishable;
- model-label outcomes mature without look-ahead and reproduce the production label definitions;
- tradeable outcomes exist only for Delphi-published recommendations;
- Top-1, Top-3, and Top-5 can be compared without changing the live recommendation;
- reports clearly distinguish sample maturity, symbol observations, and independent recommendation cohorts;
- a rerun cannot overwrite prior calibration history;
- audits detect incomplete, mismatched, stale, or ambiguous outcomes;
- the current champion can be compared with a challenger without activating it;
- no LLM or external market service is required.

## Common pitfalls and misconceptions

- **A larger number of pick rows is not the same as a large independent sample.** Picks from one date and overlapping ten-session windows are correlated.
- **Prediction success is not trade success.** A correct breakout event can still lose money after entry and costs.
- **A backtest using today's universe or today's code is not automatically point-in-time correct.** Survivorship and version leakage can dominate the result.
- **The best result among many tried variants is biased upward.** Record attempted challengers and reserve forward validation.
- **A diversified selector can improve drawdown while lowering the best-case return.** Compare risk and return together.
- **The current one-position rule is a policy, not a truth.** Preserve it as the champion without forcing every research portfolio to inherit it.
- **An LLM explanation is not statistical evidence.** It may explain a deterministic result but cannot substitute for one.
- **Mutable operational tables are not experiment history.** Calibration evidence must remain immutable and versioned.
- **Missing outcomes are not neutral outcomes.** Coverage and maturity must be explicit.
- **Changing an outcome definition changes the experiment.** Version it instead of rewriting history.

## Review questions

1. Why must prediction outcomes and tradeable outcomes remain separate?
2. Why does prediction calibration need rejected and unranked candidates, while tradeable evaluation is restricted to Delphi-published recommendations?
3. Why should recommendation runs be immutable even when Delphi is rerun for the same date?
4. What does the one-position champion tell us that Top-3 and Top-5 shadow portfolios do not, and vice versa?
5. Why are 25 picks on one date not 25 independent experiments?
6. What prevents a challenger from overfitting the same window that created it?
7. Which facts must be persisted to reproduce a recommendation months later?
8. What should happen when daily OHLC data cannot establish whether a stop or target occurred first?
9. Why should the evaluator be separate from Hermes and Delphi?
10. What evidence would justify replacing the one-position rule with a diversified selector?
