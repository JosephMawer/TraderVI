# ADR-0010 — Add `CompositeScoreZ` as an additive Z-score-normalized RS composite

- **Status:** Accepted
- **Date:** 2026-05-23
- **Tags:** technical-indicators, decision-engine, math-statistics
- **Supersedes:** —
- **Related:** ADR-0007 (liquidity floor), `Docs/reviews/open-questions.md` → "RS composite scale" and "DirectionEdge as primary ranking key".

## Context

The existing relative-strength (RS) composite — `CompositeScore` in
[`RelativeStrengthRow`](../../Core/RelativeStrength/RelativeStrengthRow.cs) — is a weighted blend of **raw 10-day return
differences** between a stock, its sector index, and XIU:

```
CompositeScore = 0.5 · (stockRet10d − xiuRet10d)
			   + 0.3 · (stockRet10d − sectorRet10d)
			   + 0.2 · (sectorRet10d − xiuRet10d)
```

The 2026-05-23 Delphi run exposed a usability problem with this composite:
the entire top-14 passing leaderboard rendered `RScomp = +0.000` at
3-decimal display precision. Tracing the pipeline confirmed this was
**not a bug** — most TSX stocks' 10-day return divergence vs sector / XIU
genuinely sits inside `±0.5%` (`±0.005`), which rounds to zero at three
decimals. The cosmetic fix (4-decimal display + raw `RS10d` column) was
applied immediately, but the underlying *scale* issue remained: the raw
composite cannot resolve candidate ordering when survivors cluster within
a few basis points of each other on a 10-day window.

The same row type already carries volatility-normalized variants:
- `RS_Z_StockVsSector`
- `RS_Z_StockVsMarket`
- `RS_Z_SectorVsMarket`

These rescale today's RS by the rolling 20-day standard deviation of the
same RS series — answering "*how extreme* is today's RS relative to its
own recent history?" in units of standard deviations. Typical values live
in roughly `±2`, giving real separation between candidates whose raw RS
clusters near zero.

The Z-score variants are computed and persisted, but no consumer blends
them into a single ranking-grade scalar.

## Decision

Introduce a new property `CompositeScoreZ` on `RelativeStrengthRow`,
computed in `RelativeStrengthCalculator.Compute` using the **same weights**
as the raw composite, applied to the Z-score variants:

```
CompositeScoreZ = 0.5 · RS_Z_StockVsMarket
				+ 0.3 · RS_Z_StockVsSector
				+ 0.2 · RS_Z_SectorVsMarket
```

Persist it in `dbo.RelativeStrengthFeatures.CompositeScoreZ`
(`FLOAT NULL`). Expose it in Delphi's console leaderboard and the
diagnostic report alongside the existing raw composite, so today's run
shows both values side-by-side.

**v1 is purely additive.** `CompositeScoreZ` does **not** participate in
Delphi ranking yet:
- Primary ranking key remains `DirectionEdge`.
- Secondary (de-facto tiebreaker) remains `CompositeScore` (the ensemble
  score, not the RS composite).
- Neither RS composite is yet a tiebreaker in the actual sort.

Promotion to ranking is deferred to a future ADR, conditional on an
out-of-sample IC (information coefficient — rank correlation between
score and realized forward return) comparison between `DirectionEdge`,
`CompositeScoreZ`, and a blend.

### Rationale for same weights

Using identical weights (`0.5 / 0.3 / 0.2`) for `CompositeScore` and
`CompositeScoreZ` keeps the *contribution shape* comparable across the
two composites — the only difference between them is the input scale.
This makes the two values directly comparable in diagnostics and makes
any future "is the Z variant materially better?" measurement a clean
apples-to-apples test.

### Rationale for additive-only v1

- Promoting a new score into ranking without measuring its IC against
  forward returns is exactly the kind of change the system's
  "iterative improvement, measure everything" philosophy is meant to
  prevent.
- The existing ranking is producing actionable picks; there is no
  emergency requiring an immediate ranking change.
- Side-by-side visibility gives the human reviewer a chance to build
  intuition for `CompositeScoreZ` magnitudes before it influences a
  trade.

## Alternatives considered

1. **Replace `CompositeScore` outright with the Z-score blend.**
   Rejected: destroys historical comparability for already-persisted
   rows and removes the raw composite, which still functions as a
   feature for ML training. Z-score variants are *additional* signal,
   not a replacement.

2. **Use percentile ranks within today's surviving universe instead of
   Z-scores.** Considered. Cleaner bounded output `[0, 1]`, but loses
   information about *how extreme* the leader is — a top stock at the
   99th percentile in a flat tape is very different from the same
   percentile in a strong-momentum tape. Z-scores preserve that
   magnitude.

3. **Promote `CompositeScoreZ` straight to a tiebreaker.** Rejected
   for v1: no IC evidence yet, and DirectionEdge-vs-RS-Z is itself an
   open question (`Docs/reviews/open-questions.md`). Mixing two
   policy changes in one ADR violates the "one decision per ADR"
   guideline.

4. **Different weights for the Z composite (e.g., heavier on stock-vs-sector).**
   Rejected for v1: tuning weights without IC evidence is speculative.
   Same-weights baseline first; tune in a follow-up ADR if warranted.

## Consequences

**Positive**
- RS as a signal becomes usable for human review — leaderboard shows a
  value with real separation between candidates.
- Persists historical Z-composite values for later IC measurement.
- No ranking behaviour change: zero blast radius for the existing
  trading flow.

**Negative**
- Historical rows backfilled by Hermes prior to this ADR do not have
  `CompositeScoreZ` populated. Backfill requires re-running the Hermes
  RS pipeline over the affected date range (tracked as a follow-up
  open question).
- Two RS composites now exist; future code must remember which one is
  intended in a given context.

**Neutral**
- The live `dbo.RelativeStrengthFeatures` table has been ALTERed to add
  the column; the SSDT DDL in `TraderDB/dbo/Tables/RelativeStrengthFeatures.sql`
  has been updated to match.

## Review questions

- After Hermes backfills `CompositeScoreZ`, measure 10-day IC on held-out
  2024–2025 data vs `DirectionEdge` and `CompositeScore`. Does the Z
  composite carry independent signal?
- Should `CompositeScoreZ` replace `CompositeScore` as the persisted
  "primary RS scalar" once the IC comparison is in hand, or should both
  remain side-by-side?
- Are the same `0.5 / 0.3 / 0.2` weights right for the Z composite, or
  does the volatility normalization change which horizon/dimension
  matters most?
