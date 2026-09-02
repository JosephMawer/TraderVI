# ADR-0041: Date-aligned relative-strength inputs

- **Status:** Accepted
- **Date:** 2026-09-01
- **Domains:** decision-engine, data-pipeline, time-series, technical-indicators
- **Related:** ADR-0002, ADR-0010, ADR-0011, ADR-0013, ADR-0019, ADR-0020, ADR-0022

## Context

The immediate problem is that Delphi passed undated stock, sector, and XIU close arrays to the
relative-strength calculator. The calculator documented that the arrays contained the same sessions,
but enforced only their minimum length. A recent sector slice could therefore be combined with the
oldest equally sized prefix of a longer stock or XIU history, and an interior missing session could
shift later values without detection.

The parent problem is that relative strength is a primary Continuation ranking input and part of the
Breakout ranking key. A plausible numeric result is unsafe when its observations describe different
market sessions. ADR-0019 proves only that each candidate's newest daily bar matches XIU; it does not
prove the histories are internally aligned.

The root goal is to keep official recommendations and calibration evidence comparable, reproducible,
and honest about missing market data while the advisory loop accumulates cohorts.

## Decision

Use explicit completed-session dates throughout live relative-strength calculation:

1. `RelativeStrengthCalculator` accepts dated close observations rather than bare numeric arrays.
2. XIU supplies the canonical ordered session sequence through the requested target date.
3. Every return and rolling Z-score uses prices from its exact canonical endpoint dates. If a required
   stock or sector endpoint is missing or has ambiguous duplicate observations, that metric is `null`;
   the calculator never chooses a duplicate by source order, clips histories, compresses gaps,
   forward-fills prices, or substitutes a nearby session. Duplicate stock/sector observations degrade
   that symbol's coverage without aborting the Delphi workflow.
4. Fail the Delphi workflow when the required canonical window contains a duplicate XIU session or an
   invalid XIU close. XIU defines shared session truth for every symbol, so a corrupt canonical observation
   is not treated as a per-symbol degraded input.
5. Return date-coverage facts with the features, including missing and duplicate canonical sessions and
   whether an interior or stale-tail gap exists after the first observation.
6. Preserve the existing fallback to XIU when no usable sector series was loaded and label it separately.
   A partially present or stale sector series is not silently replaced by XIU.
7. Surface full-canonical-coverage counts plus alignment-gap counts and symbols in Delphi's console,
   diagnostic report, presentation
   snapshot, and desktop diagnostics.
8. Keep every RS weight, horizon, lens gate, ranking formula, and fallback policy otherwise unchanged.

The complete current feature set needs 61 canonical sessions: a 60-session return requires 61 price
endpoints, while the 10-session RS Z-score with a 20-observation window needs 30. The former `80`
diagnostic was a conservative but inaccurate `60 + 20` description; it did not match the implemented
Z-score horizon. The sector query may retain additional history as operating headroom.

Because corrected inputs can change ranks, official evidence created before and after this correction
must not be treated as one unchanged strategy implementation. Existing evidence remains immutable.
Before the next official Delphi run, complete the permitted validation, record a new strategy/code
identity boundary, and decide through read-only analysis whether affected historical runs require an
audit label or exclusion from comparative performance claims. No database operation or migration is
authorized by this ADR.

## Alternatives considered

- **Keep numeric arrays and align their trailing elements.** Rejected because equal trailing lengths do
  not detect interior gaps or prove that any element represents the same session.
- **Inner-join all three series and compute over the compressed result.** Rejected because removing a
  missing XIU session changes the meaning of a 10- or 60-session horizon and hides degraded coverage.
- **Forward-fill missing closes.** Rejected because it invents a tradable observation and suppresses a
  data-quality defect.
- **Abort the entire Delphi run for any stock or sector RS gap.** Rejected for this slice because exact
  unaffected metrics remain valid and typed degradation is visible. A corrupt canonical XIU window is
  different: it invalidates the shared session reference and therefore fails the workflow. A future
  stricter per-symbol eligibility policy requires evidence and a separate decision.

## Consequences

**Easier:**

- Unequal history lengths cannot silently compare different sessions.
- Interior and stale-tail gaps are visible to operators and tests.
- Future Hermes backfill code has a date-safe calculator contract to reuse.

**Harder:**

- Some metrics that previously returned plausible shifted values now return `null`.
- Callers must retain dates and handle coverage explicitly.
- The first official run after activation begins a new strategy/code boundary even though no weight or
  ranking thesis intentionally changed.

**Would tell us this was wrong:**

- Evidence establishes a reviewed market-data source whose missing daily observations should represent
  a different, explicit carry-forward convention rather than unavailable data.
- XIU ceases to be the accepted canonical completed-session source.

## Review questions

1. Why is matching each series' latest date insufficient for relative-strength alignment?
2. What happens when an exact stock or sector endpoint is missing?
3. Why is the full current RS feature depth 61 canonical sessions rather than 80?
4. Why must pre- and post-correction official evidence retain a visible code/strategy boundary?
