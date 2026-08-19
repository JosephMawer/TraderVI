# ADR-0016 — On-Balance Volume (OBV) as a per-symbol soft ranking signal

- **Status:** Accepted
- **Date:** 2026-05-26
- **Tags:** decision-engine, technical-indicators, data-pipeline, time-series
- **Supersedes:** —
- **Related:** ADR-0001 (Granville plug-in architecture — *why OBV is NOT in it*),
  ADR-0010 (RS Z-score composite), ADR-0011 (equal-weighted additive RS in ranking —
  *the pattern OBV mirrors*), ADR-0013 (multi-lens architecture),
  ADR-0014 (Continuations lens).

## Context

Granville's **On-Balance Volume (OBV)** is a running cumulative volume figure: add the
session's volume on an up-close, subtract it on a down-close, leave it unchanged on a flat
close. The premise is that volume precedes price — accumulation/distribution shows up in the
OBV line before it confirms in price.

Two facts shaped the design:

1. **OBV is per-symbol and cumulative.** Unlike the market-wide Granville indicators
   (#1–#56), which read breadth/benchmark state once per day, OBV is computed *per stock*
   and its value only has meaning relative to an anchor. The **absolute** number is
   meaningless; the **shape** — specifically the zigzag of upside/downside breakouts
   ("field trend") — is the signal.

2. **It must persist and be maintained incrementally.** The cumulative cannot be recomputed
   from a fixed lookback every run without drift, and Hermes does not run strictly daily, so
   the series must be stored and continued from its last anchor across multi-day gaps.

The open question was *how OBV should influence the daily picks*. Initially we leaned
report-only, deferring all scoring influence to the upcoming **Climax indicator** (a planned
market-wide volume-exhaustion signal that will aggregate per-symbol OBV breakouts). On
review we decided OBV and Climax are **complementary, not redundant** — OBV is per-symbol
*confirmation* (does this stock's volume agree with its price?), Climax is market-wide
*regime* — so OBV should contribute at the **stock level** to the day's top picks now, with
Climax layered on later.

## Decision

Implement OBV end-to-end as a **per-symbol soft ranking signal**, deliberately **separate**
from the Granville #1–#56 plug-in framework (ADR-0001):

**Storage & maintenance**
- New table `dbo.SymbolObv` (`Symbol, Date, Obv BIGINT, CreatedAt`), mirroring
  `AdvanceDeclineLine`'s cumulative-series style. Repository: `Core.Db.SymbolObvRepository`
  (series reads, `GetLatestAsync` seed, `MERGE` upsert, retention prune).
- `Core.Indicators.Indicators.CalculateOBV` computes the cumulative with seeded continuation
  overloads so Hermes can extend from the last stored anchor and fill multi-day gaps.
- Hermes `UpdateObvAsync` runs **every pass** right after the A/D Line update, then prunes to
  a rolling window (`Core.Constants.ObvRetentionMonths = 6`). Pruning the tail is safe
  because the cumulative is already baked into retained rows.
- Initial seeding is a one-off **Sandbox probe** (`obv-backfill`), idempotent and using the
  same window/retention as Hermes.

**Classification**
- `Core.Indicators.ObvFieldTrendCalculator.Classify(series, breakoutWindow)` is a *pure*
  classifier returning `ObvFieldTrend` (`Rising` / `Falling` / `Doubtful` / `Indeterminate`)
  plus diagnostics (latest UP/DOWN designation, pivots). It bakes in **no** trading score.

**Soft ranking tilt (the scoring decision)**
- OBV does **not** enter the engine composite (`ComputeCompositeFromRoles`), which is ML-roles
  only. Instead it mirrors the **RS pattern** (ADR-0011): a per-symbol `double` injected via
  `TradeDecisionEngine.ObvTilts` and folded into each lens's ranking key.
- The tilt is an **additive constant**, symmetric with how RS adds in:

```
obvTilt = +ObvSignalWeight   if field trend == Rising   (volume confirms)
		  −ObvSignalWeight   if field trend == Falling   (volume contradicts)
		  0                  if Doubtful / Indeterminate / no data
```

- Both lenses add it to their `PrimaryKey`:
  - Continuation:  `rs + obvTilt`
  - Breakout:      `DirectionEdge + rs + obvTilt`
- `ObvSignalWeight = 0.10` and `ObvBreakoutWindow = 20` live in `StrategyConfig`. The window
  is the single source of truth Delphi passes to `Classify`.
- It is a **tilt, never a gate**: OBV can reorder survivors but can never block a candidate.

**Reporting**
- Delphi surfaces OBV as confirmation in `DelphiReportBuilder`: a dedicated field-trend block
  and an `OBV` column (trend arrow + UP/DN) in the diagnostic Top-Picks table, plus a
  confirms/contradicts/neutral line for the best pick in the human summary.

## Alternatives considered

- **(a) Report-only; no ranking influence until Climax ships.**
  Rejected: OBV is per-symbol confirmation and Climax is market-wide regime — they operate at
  different levels, so withholding OBV's stock-level signal leaves ensemble conviction on the
  table for no benefit. We keep OBV foundational *and* let it tilt now.
- **(b) Fold OBV into the engine composite as another weighted model.**
  Rejected: the composite is built from **ML model roles** with registry weights; OBV is a
  rule-based indicator. Injecting it there would mean faking a role/weight and muddying a
  clean abstraction. The RS-style ranking-key injection is the established seam for non-ML
  per-symbol scores.
- **(c) Make OBV a gate** (e.g., require non-Falling field trend to pass).
  Rejected: too aggressive for an unproven signal on this universe — it would veto legitimate
  early entries where volume hasn't confirmed yet. Soft tilt degrades gracefully.
- **(d) Scale the tilt by edge/RS magnitude (proportional nudge).**
  Rejected for v1 in favor of the simplest symmetric constant, matching ADR-0011's
  equal-weight philosophy. Revisit once we measure OBV's outcome contribution.
- **(e) Put OBV inside the Granville #1–#56 plug-in framework.**
  Rejected: that framework is market-wide and read once per day; OBV is per-symbol and
  cumulative. Forcing it in would break the framework's cohesion (ADR-0001).

## Consequences

**Locks us into:**
- A specific *constant additive* interpretation assuming `ObvSignalWeight` (0.10) sits at a
  reasonable scale next to `RScomp` (~±0.2) and `DirectionEdge`. If OBV's effective influence
  proves too strong/weak, this ADR must be revisited.
- Picks now depend on `dbo.SymbolObv` being current. If the table is empty or too short
  (e.g., before the backfill probe runs), `Classify` returns `Indeterminate`, tilt is 0, and
  ranking silently reverts to RS/Edge for those names. The Delphi OBV coverage line is the
  alert path.

**Easier:**
- Per-symbol volume confirmation is now visible *in the ordering*, not just diagnostics.
- Provides the substrate the future **Climax indicator** will aggregate — OBV breakout
  designations already exist per symbol.

**Harder:**
- Backtest comparability — picks before this ADR cannot be compared one-for-one to picks
  after; longitudinal studies must segment around this date (same caveat as ADR-0011).
- One extra DB read per loaded symbol in Delphi (series fetch), bounded by the universe size.

**Would tell us this was wrong:**
- The OBV tilt repeatedly elevates names with worse realized 1–5 day returns than the
  pre-tilt #1 across a paper-trade window.
- Field trend is `Indeterminate`/`Doubtful` so often that the tilt is effectively never
  applied (signal adds no information on this universe).

## Field notes

**First live read — 2026-06-06 (233-symbol Delphi run).** Wiring validated end-to-end:
`0 indeterminate`; 57 Rising / 36 Falling / 140 Doubtful; and the ±0.1 tilt verifiably
reordered the leaderboard — ESI (raw `RScomp` +0.761) was pushed *below* BTE (+0.753)
because BTE is `Rising` and ESI `Doubtful`. OBV did **not** move the executed pick (FM and
ORE were both `Doubtful` → tilt 0), which is the intended graceful-degradation behaviour.

Two calibration items were logged to `Docs/reviews/open-questions.md` (section "On-Balance
Volume soft tilt — first live-read calibration"):

1. **Weight scale.** `ObvSignalWeight = 0.10` may be strong relative to the *gate-passer*
   `RScomp` spread (~±0.1), even though it is modest against the full-universe spread
   (~±0.2). The names that clear every gate cluster near ±0.1, so the tilt can act as the
   deciding vote rather than a gentle nudge. To be resized from a paper-trade window, not guessed.
2. **Structural `Doubtful` inflation.** The 60% `Doubtful` rate is partly mechanical:
   `ObvFieldTrendCalculator.Classify` only calls `Rising`/`Falling` with ≥2 UP *and* ≥2 DOWN
   pivots, so sparse-pivot symbols are forced to `Doubtful` (partial evidence toward the
   "signal too quiet" failure condition above). This also throttles the planned **Climax**
   indicator, which aggregates the same UP/DOWN designations — making it an explicit input
   to the Climax ADR.

## Review questions

1. Why is OBV kept *out* of the Granville #1–#56 plug-in framework?
2. Why does the OBV tilt go into the lens ranking key rather than the engine composite?
3. How does the tilt map field trend to a number, and why is it a tilt and not a gate?
4. Why is pruning `dbo.SymbolObv`'s tail safe for a cumulative series, and how does Hermes
   continue the series across a multi-day gap?
