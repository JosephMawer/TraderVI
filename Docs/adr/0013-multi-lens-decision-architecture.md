# ADR-0013 — Multi-lens decision architecture (a lens = thesis × gate stack × ranking key)

- **Status:** Accepted
- **Date:** 2026-05-24
- **Tags:** architecture, decision-engine
- **Supersedes:** —
- **Related:** ADR-0011 (RS+Edge ranking — now scoped to the Breakouts lens),
  ADR-0014 (Continuations lens — first non-breakout lens),
  ADR-0007/0009 (universe filters, shared upstream of all lenses).

## Context

Up through 2026-05-23 Delphi ran a **single** evaluation pipeline: one ordered
gate stack (`TradePipeline.FromConfig`) whose *setup* stage was the breakout
filter (`SetupGate`, a floor on `BreakoutEnhanced` probability), followed by one
ranking key (`DirectionEdge + RScomp`, ADR-0011). Every recommendation the system
produced was therefore implicitly a **breakout** thesis: "this name is likely to
clear a range in the next ~10 days."

The strategy goal (`copilot-instructions.md`) is *aggressive momentum rotation*.
Much of that rotation is **continuation** — a name that is *already* leading keeps
leading — which is a different thesis from breakout and wants different gating
(confirmed uptrend, not breakout probability) and different ranking (realized
leadership / RS first, with forward edge as confirmation).

We tried to picture serving both theses by tuning the one shared gate stack with
flags. That collapses two distinct hypotheses into one pipeline and makes the
"which thesis won?" question unanswerable after the fact.

A **lens** (jargon, defined here): a self-contained way of viewing the universe,
expressed as a `(thesis → gate stack → ranking key)` triple. Two lenses can share
all market-level inputs (regime, breadth, Granville, RS) and the same per-symbol
scoring, yet produce different shortlists because they **gate** and **rank**
differently.

## Decision

Make the **lens a first-class architectural unit**, not a feature flag on a shared
pipeline.

1. Introduce `RankingLens` (enum), `LensDefinition` (the triple: a
   `TradePipeline` gate stack + a `PrimaryKey` ranking selector), and
   `LensCatalog` (factories that assemble each lens from `StrategyConfig`) in
   `Core/Trader/`.
2. Refactor `TradeDecisionEngine` so `Evaluate(history, pipeline)` takes the gate
   stack explicitly and `EvaluateAndRank(lens, …)` ranks survivors by the lens's
   `PrimaryKey`, then `DirectionEdge`, then `CompositeScore`. Per-symbol scoring
   (composite, probabilities, edge) is computed **once** and is identical across
   lenses — only gating and ordering differ.
3. Run **two lenses** in Delphi each day:
   - **Continuations** (ADR-0014) — the **executed** recommendation (B1).
   - **Breakouts** (the legacy pipeline + ADR-0011 ranking) — **journaled only**
	 (B3), shown as supplemental awareness and a continuity baseline.
4. Persist a `[Lens]` discriminator on `dbo.DailyPick` (default `'Breakout'` for
   schema back-compat) so the two theses' outcomes can be compared later. Read
   APIs (`GetPicksByDate`, etc.) default to `'Continuation'` so "the picks" means
   the executed lens.

Only the executed (Continuations) lens emits `DecisionDossier`s and sizing; the
Breakouts lens writes picks only.

## Alternatives considered

- **(a) One shared pipeline with a `mode`/flag toggling breakout-vs-continuation
  gating.** Rejected: re-creates the "tune one gate to do two jobs" problem,
  branches scoring/ranking logic with conditionals, and loses per-thesis
  attribution. Adding a third view later would mean a third branch, not a new
  object.
- **(b) Two completely separate engines/programs.** Rejected: duplicates the
  expensive per-symbol scoring and all market-level setup (regime, breadth,
  Granville, RS), and risks the two drifting out of sync.
- **(c) Keep breakout-only; add continuation later.** Rejected: the user
  explicitly wants both views *now*, with continuation driving execution and
  breakout retained for comparison.

## Consequences

**Locks us into:**
- A contract that per-symbol scoring is lens-independent; only gate stack and
  ranking key may vary per lens. A future lens that needs *different scoring*
  would force a larger refactor.
- A `[Lens]` column on every `DailyPick` row and a convention that read paths
  default to the executed lens.

**Easier:**
- Adding a future lens (e.g., mean-reversion, pullback-to-MA) is a new
  `LensCatalog` factory + enum value — no branching of existing pipelines.
- Per-thesis performance attribution: outcomes can be sliced by `[Lens]`.
- The breakout thesis is preserved verbatim as a measurable baseline.

**Harder:**
- Daily evaluation now runs the gate stack twice (cheap relative to scoring, which
  runs once) and writes ~2× `DailyPick` rows.
- Anyone querying `DailyPick` must be lens-aware; forgetting the filter mixes
  executed and journaled rows.

**Would tell us this was wrong:**
- The two lenses converge to near-identical shortlists day after day (the split
  adds storage/complexity but no information).
- A future thesis genuinely needs lens-specific *scoring*, breaking the
  shared-scoring contract this ADR locks in.

## Review questions

1. Define "lens" as used in TraderVI, and name the three components of the triple.
2. What is computed **once** and shared across lenses, and what is allowed to
   differ per lens?
3. Which lens drives the executed recommendation, which is journaled, and how are
   they told apart in the database?
4. Why was "one pipeline with a mode flag" rejected in favor of separate
   `LensDefinition`s?
