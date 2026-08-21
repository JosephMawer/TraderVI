# ADR-0007: Liquidity floor on Delphi's universe filter

- **Status:** Accepted
- **Date:** 2026-06-XX
- **Domains:** decision-engine, market-microstructure, risk-management, data-pipeline

## Context

Delphi's universe filter previously had **one** gate beyond "enough history":
an *affordability* ceiling (`lastClose <= deployableCapital / 10`) so the
sizer can always buy at least 10 shares from the configured capital. There
was no floor on either price or liquidity.

This produced a real, observed failure mode on a recent run: the #1 ranked
pick was **CRWY** with a ~2,038-share 20-day average volume; the #2 and #3
picks were sub-$0.25 names. Two problems compound:

1. **Signal validity (out-of-distribution).** Both the ML probabilities
   (`BinaryUp10`, `BinaryDown10`, `BreakoutEnhanced`) and Relative Strength
   (RS) features are trained and normalized against a universe where price
   discovery actually happens. On a 2k-share-per-day tape, intraday prints
   are dominated by single-lot noise; the trained distributions of
   `Volume`, `VolumeZScore`, and return autocorrelations do not apply.
   The model's confidence is *real-numbered* but its calibration to the
   actual symbol is not.
2. **Execution integrity.** Even if the signal were right, the order
   itself would move the tape: a market order for any meaningful share
   count on a sub-$0.25 name with 2k-share daily volume crosses the
   visible book and walks the ladder. The realised fill price diverges
   sharply from the close used to compute the signal — so even a correct
   forecast turns into a losing trade on execution alone.

The affordability ceiling alone is insufficient because the failure mode
is at the *bottom* of the price/volume distribution, not the top.

## Decision

Add a two-part **liquidity floor** to Delphi's universe filter, applied
inside the same load loop as the existing affordability gate, with these
initial defaults (tunable; intended to be refined from probe data):

- **Minimum last close `>= $1.00`** — excludes penny stocks where bid/ask
  ticks are a non-trivial fraction of price and where ML/RS calibration
  is weakest.
- **Minimum 20-day average daily volume `>= 50,000` shares** — chosen as
  a coarse first cut: a 100-share order is < 0.2% of daily volume, well
  within typical market-order absorption for a TSX-listed name. Tighter
  than this risks excluding legitimate small-caps; looser keeps the
  CRWY-class names in the universe.

Both thresholds are applied per-symbol *before* the symbol is added to
`allBars`. Each gate increments its own skipped counter:

- `SkippedLowPrice` (price < `MinPriceFloor`)
- `SkippedLowVolume` (20d avg volume < `MinVolume20d`)

These join the existing `SkippedHistory` and `SkippedPrice` (affordability
ceiling) buckets in the Delphi "Loaded / Skipped" console line and in both
`BuildDiagnostic()` (Universe section) and `BuildSummary()` (liquidity-gate
line) of `DelphiReportBuilder`, per the repository instruction about
surfacing every new pipeline gate in the report builder.

## Alternatives considered

- **Drop the affordability ceiling, use a single broad liquidity score.**
  Rejected: the two gates address different problems (capital fit vs
  signal/execution validity). Collapsing them obscures *why* a symbol
  was skipped, which hurts review and tuning.
- **Use dollar-volume (`price × 20d_avg_volume`) as a single combined
  threshold.** Rejected for v1: it has a defensible theoretical basis
  (it's roughly proxy for what a market order can absorb without
  slippage), but it conflates two distinct failure modes — a $50 stock
  with 30k volume and a $0.50 stock with 3M volume have wildly different
  ML-calibration and execution profiles. Keep them as separate gates
  for now so each can be tuned independently from probe data.
- **Filter post-ranking instead of pre-evaluation.** Rejected: filtering
  after evaluation wastes compute on symbols we'd never trade, and the
  ranked output would still show illiquid names — confusing for the
  reviewer and risking accidental override.
- **Use a hard 90-day average instead of 20-day.** Rejected for v1:
  20-day matches the existing sort key (`avg 20-day volume`) for
  consistency, and a thin name's 20-day window already smooths out
  single-day spikes. Revisit if we observe a thinly-traded name passing
  the 20-day gate via a one-week volume blip.

## Consequences

- **Lose:** access to genuinely thin microcaps that occasionally move
  hard. By design — those moves are unforecastable with our current ML
  features and unfillable at the signalled price. If we ever want to
  trade them, that's a different strategy with different sizing rules,
  not Delphi.
- **Gain:** the #1 pick is always something Delphi's signals can
  *meaningfully* score and the trader (manual or future Sentinel) can
  *meaningfully* fill.
- **Locked in:** the two thresholds are surfaced in every Delphi report,
  so any tuning is visible and auditable. The skip-bucket counters give
  us telemetry to revisit defaults (e.g., if `SkippedLowVolume`
  consistently dwarfs `Loaded`, the 50k floor is too tight).
- **What would tell us this is wrong:** if backtests on the rejected
  bucket (price ∈ [$0.25, $1.00] OR 20d vol ∈ [10k, 50k]) show positive
  edge net of slippage modelling, the floor is set too high. Until that
  evidence exists, the conservative defaults stand.

## Review questions

1. Why are the two parts of the liquidity floor (price floor and volume
   floor) kept as *separate* gates with separate counters, rather than
   collapsed into a single dollar-volume threshold?
2. Why does the liquidity floor sit in Delphi's universe filter
   (pre-evaluation) rather than as a post-ranking suppression?
3. The 20-day average volume threshold is `>= 50,000`. What observable
   telemetry would justify loosening or tightening this number, and
   where in the Delphi output is that telemetry surfaced?
4. CRWY had ~2,038 share/day average volume. Beyond execution slippage,
   *why* does that low a volume break the ML probability signals
   specifically (not just the trade fill)?
