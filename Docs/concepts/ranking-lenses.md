# Ranking lenses (multi-lens evaluation)

- **Domains:** architecture, decision-engine
- **Related ADRs:** ADR-0013 (multi-lens architecture), ADR-0014 (Continuations
  lens), ADR-0011 (Breakouts lens ranking key)

## Summary

A **lens** is a self-contained way of viewing the trading universe, expressed as a
`(thesis → gate stack → ranking key)` triple:

- **thesis** — the trading hypothesis ("a breakout is coming" vs "a leader keeps
  leading"),
- **gate stack** — the ordered list of `ITradeGate`s that decides which candidates
  are *eligible* under that thesis,
- **ranking key** — how eligible candidates are *ordered* against each other.

Multiple lenses can share the same market-level inputs and the same per-symbol
scoring, yet surface different shortlists because they gate and rank differently.

## Why it matters here

TraderVI rotates aggressively, and "the best opportunity" means different things
under different theses. A **breakout** thesis wants to gate on breakout
probability and rank by forward edge; a **continuation** thesis wants to gate on a
*confirmed uptrend* and rank by *realized leadership* (RS). Forcing one gate stack
to serve both — by adding flags and conditionals — collapses two hypotheses into
one and makes the question "which thesis actually drove today's pick?"
unanswerable.

Modeling each thesis as its own lens keeps them cleanly separated:

- We **run two lenses every day**. Continuations is **executed** (it drives the
  recommendation and emits dossiers/sizing). Breakouts is **journaled** for
  supplemental awareness and as a continuity baseline.
- We **even truly care about only one lens** (Continuations) for execution — but
  keeping the other as supplemental material is always useful: it shows what a
  different thesis would have picked, and gives us a measurable comparison.
- We can **add more lenses later** (e.g., mean-reversion, pullback-to-MA) as new
  `LensCatalog` factories, *without* branching any existing pipeline.

Every saved pick carries a `[Lens]` discriminator so the two views' outcomes can
be compared after the fact.

## Details

### The shared core vs the per-lens parts

Per-symbol scoring runs **once** and is identical across lenses:
`CompositeScore`, `UpProb`/`DownProb`, `DirectionEdge`, and the market-level
inputs (regime, breadth, Granville, RS). Only two things vary per lens:

| Part        | Continuations (executed)                         | Breakouts (journaled)                    |
|-------------|--------------------------------------------------|------------------------------------------|
| Setup gate  | `TrendConfirmationGate` (Trend30 + MaCrossover)  | `SetupGate` (BreakoutEnhanced floor)     |
| Ranking key | `RScomp` primary, `DirectionEdge` confirms       | `DirectionEdge + RScomp` (equal-weight)  |

Both lenses share the surrounding gates:
`Regime → Breadth → Granville → DownProbability → [setup] → Direction → Composite`.

### Code shape

- `RankingLens` (enum) — `Continuation`, `Breakout`.
- `LensDefinition` — bundles a `TradePipeline` (the gate stack) and a
  `Func<RankedPick, double, double> PrimaryKey` (the ranking selector; the second
  argument is the symbol's raw `RScomp`).
- `LensCatalog` — factories (`Continuation(config)`, `Breakout(config)`) that
  assemble each lens from `StrategyConfig`.
- `TradeDecisionEngine.EvaluateAndRank(lens, …)` — evaluates every symbol through
  the lens's gate stack and orders survivors by `lens.PrimaryKey`, then
  `DirectionEdge`, then `CompositeScore` (Buy always ahead of Hold).

### Persistence and querying

`dbo.DailyPick` has a `[Lens]` column (default `'Breakout'` for schema
back-compat). Delphi writes Continuations picks with `lens: "Continuation"` (plus
dossiers/sizing) and Breakouts picks with `lens: "Breakout"` (picks only). Read
APIs (`GetPicksByDate`, `GetTopPicksByDate`, `GetPickByDateAndSymbol`) default to
`'Continuation'`, so "the picks" means the executed lens unless a caller asks for
another.

### Adding a future lens

1. Add a value to `RankingLens`.
2. Add a factory to `LensCatalog` that assembles the gate stack and ranking key.
3. (Optionally) evaluate and journal it in Delphi with its own `[Lens]` label.

No existing pipeline is modified — the new view is purely additive.

## Review questions

1. What are the three components of a lens, and which one decides *eligibility*
   versus *ordering*?
2. What is shared across lenses and what varies, and why does that split make
   adding a future lens cheap?
3. Why keep the Breakouts lens at all if only Continuations is executed?
4. How does the `[Lens]` discriminator keep journaled picks from polluting "the
   picks" that downstream code reads?
