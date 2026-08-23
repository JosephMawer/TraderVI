# ADR-0019 — Delphi strict history-freshness eligibility

- **Status:** Accepted
- **Date:** 2026-08-22
- **Domains:** decision-engine, data-pipeline, risk-management

## Context

Delphi compares symbols cross-sectionally using daily momentum, relative strength, OBV, model probabilities, affordability, and liquidity. If one symbol's newest price bar is older than the rest of the universe, those calculations describe different market sessions. The resulting rank can look valid while relying on a stale close and missing the latest return and volume.

The read-only DataAudit app detects universe-wide freshness problems, but it is an operational diagnostic and is not guaranteed to run immediately before every Delphi evaluation. Delphi therefore needs an independent defense at its own decision boundary.

## Decision

- Treat the newest stored XIU daily bar as Delphi's canonical completed TSX market-data session, consistent with ADR-0002.
- Stop the evaluation without writing recommendations if no XIU daily history exists and the canonical session therefore cannot be established.
- After enforcing the minimum history length, exclude any symbol whose newest daily bar date does not exactly equal that XIU session.
- Apply the rule before affordability, liquidity, relative-strength, OBV, model evaluation, lens gates, and ranking.
- Count completed XIU sessions rather than calendar days when reporting how far behind an excluded symbol is.
- Reject a symbol dated after the canonical XIU session as a session mismatch instead of silently mixing dates.
- Keep the tolerance fixed at zero sessions. It is a data-integrity invariant, not a strategy threshold to tune in `StrategyConfig`.
- Surface the reference date, exclusion count, and stable per-symbol details in Delphi's diagnostic report. Surface a warning in the summary whenever exclusions occur.
- Keep DataAudit as the independent, read-only universe diagnostic. Delphi's gate protects recommendation execution; DataAudit finds broader classification, mapping, integrity, and freshness issues before or after ingestion and migrations.

## Alternatives considered

- **Rely only on DataAudit.** Rejected because an audit may not run immediately before Delphi and cannot guarantee runtime eligibility.
- **Put the check inside Hermes.** Rejected as the only protection because Hermes is the mutating importer being checked, and Delphi may run later against a database whose state changed independently.
- **Add freshness to each Delphi lens's `ITradeGate` stack.** Rejected because model and indicator calculations would already have consumed stale bars, and the same universe invariant would be duplicated across lenses.
- **Permit one or more missing sessions.** Rejected because even one missing close and volume observation changes short-horizon momentum and cross-sectional comparisons. A later relaxation requires evidence and a new decision.
- **Use the latest A/D Line date as the reference.** Rejected because the gate is about per-symbol OHLCV comparability, and XIU is the canonical TSX price benchmark.

## Consequences

**Easier:**

- Every symbol reaching Delphi's scoring and ranking stages represents the same completed TSX session.
- A clean DataAudit is helpful but no longer a prerequisite for runtime safety.
- Stale and future-dated mismatches are explicit in both operator and diagnostic output.

**Harder:**

- A single missed symbol import removes that symbol from consideration until its history catches up.
- If XIU itself is stale, Delphi consistently anchors to that older session; separate audit/operational checks must still detect that the whole database is behind the real market.
- If XIU history is absent, Delphi cannot produce recommendations until the benchmark data is repaired.
- The strict tolerance cannot be relaxed through strategy configuration.

**Would tell us this was wrong:**

- Measured evidence shows that intentional one-session-lagged instruments can be compared safely and are necessary to the strategy universe.
- XIU ceases to be a reliable canonical TSX session source.
- A future intraday or multi-frequency design needs explicit as-of timestamps rather than daily-session equality.

## Review questions

1. Why is freshness enforced before model evaluation rather than as a lens gate?
2. Why does Delphi use XIU instead of the A/D Line to define its canonical price session?
3. What different responsibilities do DataAudit and Delphi's freshness eligibility rule have?
4. Why is the allowed lag fixed at zero instead of stored in `StrategyConfig`?
