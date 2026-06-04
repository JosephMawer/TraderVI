# ADR-0014 — Continuations lens: trend-confirmation gate + RS-primary ranking (executed)

- **Status:** Accepted
- **Date:** 2026-05-24
- **Tags:** decision-engine, technical-indicators, risk-management
- **Supersedes:** —
- **Related:** ADR-0013 (multi-lens architecture — this is the first concrete
  non-breakout lens), ADR-0011 (RS+Edge ranking — the Breakouts lens's key),
  ADR-0010 (RS Z-score composite), ADR-0012 (sector backfill — RS prerequisite).

## Context

Per ADR-0013, a **lens** is a `(thesis → gate stack → ranking key)` triple. The
legacy pipeline encodes a **breakout** thesis (gate on `BreakoutEnhanced`
probability; rank by `DirectionEdge + RScomp`). The strategy is *aggressive
momentum rotation*, much of which is **continuation** — buy what is *already*
leading and let it keep leading — a thesis the breakout gate actively works
against (it can reject a confirmed leader that isn't currently coiling for a
range break).

We need a lens whose gate asks "is this in a confirmed uptrend?" instead of "is a
breakout likely?", and whose ranking puts **realized leadership (RS)** first with
forward probability (`DirectionEdge`) as a confirmation rather than the driver.

Relevant building blocks already exist:
- Rule-based pattern signals expose their public name via `TaskType`; `Hint ==
  Buy` when present. The available trend patterns are `Trend10`, `Trend30`, and
  `MaCrossover` (the 10/30 moving-average crossover).
- `RscompositeScores[symbol]` (raw `RScomp`) is reliable post-backfill (ADR-0012).

## Decision

Define the **Continuations lens** as the executed lens (B1):

**Gate stack** — replace the breakout `SetupGate` with a new
`TrendConfirmationGate`, keeping the shared market-level and capital-preservation
gates around it:

```
Regime → Breadth → Granville → DownProbability
	   → TrendConfirmation → Direction → Composite
```

`TrendConfirmationGate` passes only when **both** `Trend30` **and** `MaCrossover`
patterns are present (`Hint == Buy`):
- `Trend30` confirms a multi-week uptrend.
- `MaCrossover` (10/30) confirms the trend structurally.
- `Trend10` is **deliberately excluded**: it flips during routine pullbacks even
  while a name is still leading, so requiring it would reject healthy continuation
  candidates.

Breakout probability is **demoted to a soft composite input only** under this lens
— it no longer gates.

**Ranking key** — RS-primary:
```
primaryKey  = RScomp          // realized leadership drives the pick
secondaryKey = DirectionEdge  // forward edge confirms (Direction gate already
							  //   guarantees edge ≥ MinDirectionEdge)
tertiaryKey  = CompositeScore
```
Buy still sorts ahead of Hold. Missing `RScomp` defaults to `0`.

The Continuations lens is the **only** lens that emits `DecisionDossier`s and
sizing; the Breakouts lens is journaled for comparison (ADR-0013).

## Alternatives considered

- **(a) Require `Trend10 + Trend30 + MaCrossover` (all three).** Rejected: `Trend10`
  whipsaws on normal pullbacks and would reject leaders mid-consolidation, exactly
  the continuation entries we want.
- **(b) Keep breakout as a gate but *add* a trend requirement.** Rejected: that is
  the "tune one gate for two theses" anti-pattern ADR-0013 exists to avoid; it also
  shrinks the candidate set to the intersection of two unrelated theses.
- **(c) Rank by `DirectionEdge + RScomp` (the breakout key) here too.** Rejected:
  the continuation thesis is *about* realized leadership, so RS should lead, not be
  averaged 1:1 with forward probability. Using the same key would make the two
  lenses differ only by their gate, weakening the "different view" intent.
- **(d) Rank by `CompZ` (Z-scored RS) instead of raw `RScomp`.** Deferred for the
  same scale-consistency reasons as ADR-0011(d); revisit once we measure outcomes.

## Consequences

**Locks us into:**
- A two-pattern definition of "confirmed trend" (`Trend30` + `MaCrossover`). If
  those detectors change semantics, this gate's behavior shifts.
- RS as the *primary* selector for the executed pick — picks now depend even more
  directly on `dbo.SectorIndices` health than under ADR-0011.

**Easier:**
- The executed recommendation now aligns with the continuation thesis the
  strategy actually rotates on.
- Clear contrast against the journaled Breakouts lens enables per-thesis outcome
  measurement.

**Harder:**
- On days with few confirmed uptrends, the Continuations lens may surface fewer
  Buys than the breakout lens did — by design, but it changes day-to-day pick
  counts.
- Backtest comparability with pre-ADR (breakout-executed) picks is broken; slice
  longitudinal studies around 2026-05-24 and by `[Lens]`.

**Would tell us this was wrong:**
- Continuations picks realize systematically *worse* 1–5 day returns than the
  journaled Breakouts picks over a multi-week paper-trade window.
- `Trend30 + MaCrossover` proves too strict (chronically empty shortlists) or too
  loose (admits names already rolling over).

## Review questions

1. Which two patterns must both be present for `TrendConfirmationGate` to pass,
   and why is `Trend10` deliberately excluded?
2. How does the Continuations ranking key differ from the Breakouts (ADR-0011)
   key, and why does RS lead here?
3. What role does breakout probability still play in the Continuations lens?
4. Which lens emits dossiers/sizing, and what does that imply about which lens is
   "executed"?
