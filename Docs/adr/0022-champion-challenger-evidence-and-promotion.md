# ADR-0022: Champion/challenger evidence and promotion

- **Status:** Accepted
- **Date:** 2026-08-23
- **Domains:** decision-engine, machine-learning, math-statistics, risk-management
- **Related:** ADR-0010, ADR-0014, ADR-0016, ADR-0020, ADR-0021

## Context

The immediate problem is deciding when calibration evidence may change a strategy. The parent problem is preventing repeated experiments on overlapping cohorts from being mistaken for independent proof. The root goal is controlled learning that improves expected return without silently weakening capital protection.

## Decision

Keep the exact active strategy/model/policy as champion. A challenger is immutable and predeclares one hypothesis family, primary metric, risk guardrails, invention window, untouched forward window, variants tried, and expected decision artifact. Measurement and proposal generation never activate it.

Use official matured recommendation sessions/cohorts as the primary live sample count. Report symbol observations separately and use block/bootstrap or another cohort-aware uncertainty method that preserves overlapping-window dependence.

Evidence tiers are:

- **10 matured official cohorts:** measurement-system validation only. It may correct timing, joins, labels, reports, or audit defects, not justify strategy promotion.
- **20–30 cohorts:** provisional one-family shadow challengers are allowed when point-in-time historical walk-forward evidence agrees. They remain inactive.
- **60 cohorts:** ordinary low-risk ranking, weight, or diversification changes may be proposed when there are at least two defined regimes with at least ten cohorts each, an untouched forward window of at least twenty cohorts, and the cohort-aware 95% confidence interval for primary improvement excludes zero. Coverage must meet ADR-0020 and all guardrails must pass.
- **120 cohorts:** removing a safety gate, increasing concentration, materially loosening downside protection, or changing executable stop behavior additionally requires at least forty untouched forward cohorts, at least twenty cohorts in each represented regime, and confidence bounds showing no unacceptable deterioration in lower-tail loss or drawdown.

These are minimum promotion contracts, not guarantees. A human may reject a statistically qualifying challenger. A human may not waive missing provenance, leakage, forward validation, or an invalid report.

Approval is recorded on a calibration experiment/proposal with reviewer and UTC time. Configuration-only changes create a new `StrategyVersion`. Model changes require a new `ModelRegistry` artifact row and its association with the strategy. Gate/ranker/code-constant changes require an accepted ADR, a code commit, and a new strategy version referencing that code identity. Portfolio-policy changes create a new policy version; operational adoption also creates or links an appropriate strategy version. The former champion remains available for rollback comparison.

## Alternatives considered

- **Wait a fixed 60 sessions before learning anything.** Rejected because early cohorts are valuable for validating measurement and forming shadow hypotheses.
- **Promote the best backtest automatically.** Rejected because selection over many variants biases the winner upward and bypasses risk review.
- **Use symbol rows as independent samples.** Rejected because symbols in one cohort and overlapping 10-session windows share market exposure.
- **One threshold for every change.** Rejected because removing a downside gate is materially riskier than adjusting a small ranking tilt.

## Consequences

- Promotion is intentionally slow relative to measurement and shadow experimentation.
- The experiment registry must retain losing and abandoned variants, not only winners.
- Some regime requirements may take substantial calendar time; that is a safety property for consequential changes.
- DotLLM remains downstream and cannot calculate, approve, or activate proposals.

## Review questions

1. Why is a recommendation cohort the principal live sample rather than each symbol?
2. What can happen at 20–30 cohorts that cannot happen at ten?
3. Why do safety-gate removals require more evidence than an OBV-weight adjustment?
4. Which immutable records must change when a code-level challenger is approved?
