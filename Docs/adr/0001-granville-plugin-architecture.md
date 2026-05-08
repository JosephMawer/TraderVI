# ADR-0001: Granville indicator plug-in architecture

- **Status:** Accepted
- **Date:** 2026-05-07 *(backfilled from existing implementation)*
- **Domains:** architecture, technical-indicators

## Context
Granville's "A Strategy of Daily Stock Market Timing" defines 56 day-to-day
indicators across ~20 categories (Plurality, Disparity, Leadership,
Weighting, etc.). We will implement them incrementally over many sessions,
and each category has different data dependencies (some need breadth, some
need per-constituent prices, some need volume profiles).

We needed a structure that:
- Allows incremental implementation (a category at a time, in any order).
- Keeps each category isolated so a buggy indicator can't poison others.
- Produces uniform output that downstream code (`Delphi`, reporting,
  composite scoring) can consume without knowing which categories exist.
- Lets the composite score scale automatically as more categories come
  online.

## Decision
Implement each Granville category as a class implementing
`IGranvilleIndicatorGroup` with a single `Evaluate(GranvilleMarketContext)`
method returning `IReadOnlyList<GranvilleResult>`. Aggregate them via a
`GranvilleComposite` that registers the active groups in its constructor
and produces a unified `GranvilleDailyForecast`.

## Alternatives considered
- **One monolithic `GranvilleIndicators` class with 56 methods.** Rejected:
  unwieldy, mixes data dependencies, makes incremental work harder, and
  forces all 56 to compile-and-pass before any can ship.
- **One class per individual indicator (#1, #2, ...).** Rejected: too granular;
  Granville's categories share derived state (e.g., Plurality #1–#4 all use
  the same advancers/decliners comparison), so per-category grouping
  matches the natural cohesion.
- **Configuration-driven (indicators defined in JSON/YAML).** Rejected:
  the indicators have rich, varied logic that doesn't compress to config;
  expressing them as code is clearer and type-safe.

## Consequences
- New categories require touching three places: a new class, registration in
  `GranvilleComposite`, and `MaxRawPointRange()` for normalization. This is
  documented in `.github/copilot-instructions.md`.
- The composite's `MaxCompositeAdjustment` cap (currently 0.10) bounds the
  influence of all rule-based Granville signals on the trade decision,
  preventing them from overwhelming ML signals — but also requires
  re-tuning as more categories are added.
- The plug-in shape extends naturally to indicators needing injected services
  (e.g., `IXiuConstituentProvider` for the upcoming Weighting category) by
  passing dependencies into the group's constructor.

## Review questions
1. Why did we reject the monolithic approach in favour of per-category classes?
2. What three places must be updated when adding a new indicator group, and
   why each one?
3. What signal would tell us the plug-in design is wrong (i.e., what kind of
   future indicator would force a redesign)?
4. Why is `MaxCompositeAdjustment` capped at 0.10 — what's the failure mode
   we're guarding against?
