# ADR-0008: Genuity #19 magnitude-ratio ±5% tolerance buffer

- **Status:** Accepted
- **Date:** 2026-06-XX
- **Domains:** technical-indicators, decision-engine, math-statistics
- **Refines:** [ADR-0004](0004-genuity-us-confirming-indices.md) (Genuity #17–#20 source & staleness gate)

## Context

Genuity #19 (`Core/Indicators/Granville/GenuityIndicators.cs`,
`BuildMagnitudeProportionality`) compares `|XIU return| / |^GSPC return|`
on same-direction days. When the ratio falls outside the hard band
`[0.33, 3.00]`, the indicator declares the move "disproportionate" and
fires a full bearish signal (inverting XIU's directional implication).

The hard boundary is a step function: a ratio of **0.34** is fully
proportionate (Neutral, 0 pts), and a ratio of **0.32** is fully bearish
(-1 pt). On a recent Delphi run, the actual ratio was **0.32** — a
hair under the 0.33 floor — and #19 fired Bearish, contributing a full
negative point to the Granville composite. There is no plausible
econometric story under which 0.32 means something meaningfully
different from 0.34. The cliff is an artefact of choosing round numbers
for the threshold, not a real regime boundary.

This is a textbook case of *boundary sensitivity*: a tunable parameter
whose value lies in a flat region of the loss landscape produces an
output that flips at a single point. Ensemble indicators are supposed
to express *graded* conviction, not bang-bang decisions, near their
boundaries.

## Decision

Apply a **±5% tolerance buffer** around each hard boundary of the
magnitude-ratio band, and **abstain (return Neutral / 0 points) inside
the buffer**:

| Region | Signal | Points |
|---|---|---|
| `ratio > 3.00` (above hard upper bound) | Bearish (if XIU up) / Bullish (if XIU down) | ±1 |
| `2.85 ≤ ratio ≤ 3.00` (upper buffer) | Neutral, "borderline" | 0 |
| `0.35 ≤ ratio ≤ 2.85` (core proportionate band) | Neutral, "proportionate" | 0 |
| `0.33 ≤ ratio < 0.35` (lower buffer) | Neutral, "borderline" | 0 |
| `ratio < 0.33` (below hard lower bound) | Bearish (if XIU up) / Bullish (if XIU down) | ±1 |

Buffer edges are computed as `lowerBound × (1 + 0.05)` and
`upperBound × (1 - 0.05)`. The buffer width is **multiplicative** (5% of
the bound's own magnitude), not additive, because the underlying ratio
is itself multiplicative — a 5% absolute window on a ratio of 0.33 is
disproportionately wider than the same window on a ratio of 3.0.

The diagnostic description distinguishes "borderline" from "proportionate"
so the buffer-zone abstentions are auditable from the Delphi diagnostic
log.

## Alternatives considered

- **Half-point damping inside the buffer** (the original proposal: fire
  Bearish/Bullish with `GranvillePoints = ±0.5`). Rejected for v1
  because `GranvilleResult.GranvillePoints` is `int` system-wide and
  every consumer (composite adjustment math, DB log, weighting
  arithmetic) assumes integer points. Promoting the field to a real
  number is a separate cross-cutting change. Logged as a follow-up in
  `Docs/reviews/open-questions.md`.
- **Additive ±0.05 buffer** (`[0.28, 0.38]` lower, `[2.95, 3.05]`
  upper). Rejected because the lower-side window would be ~15% of the
  bound's magnitude while the upper-side window would be ~1.7% — wildly
  asymmetric for what is meant to be a symmetric notion of "borderline".
  Multiplicative ±5% is symmetric in log-ratio space, which matches the
  geometry of the underlying quantity.
- **Soft logistic damping (`tanh`-style)** smoothly interpolating
  between full-fire and no-fire across the buffer. Rejected: needs a
  fractional-point representation (same blocker as the half-point
  option) and adds a second tuning parameter (the steepness) that we
  have no calibration data for.
- **No-fire only — no buffer at all, lower the hard floor**
  (e.g. raise lower to 0.25). Rejected: just moves the cliff to a new
  location with no principled basis. The cliff is the problem, not its
  location.

## Consequences

- **Easier:** marginal Genuity #19 firings no longer flip the composite
  on a single decimal-place wobble. Borderline cases show up in the
  diagnostic as `"Genuity #19: Magnitude borderline"` and visibly
  abstain rather than silently downvote.
- **Harder:** the gap between "core proportionate" and "disproportionate"
  is now wider — a directional ratio of 0.30 still fires bearish, but
  a ratio of 0.34 no longer registers as borderline either (it's in the
  core band). The buffer only catches the immediate edge.
- **Locked in:** integer GranvillePoints. Until that's revisited, all
  Genuity-style boundary indicators that adopt this pattern will be
  *abstain-on-buffer*, not *damp-on-buffer*.
- **What would tell us this is wrong:** if backtests over the relevant
  history show that the abstained borderline cases had *significant*
  forward-return divergence (i.e. they really were bearish and we lost
  information by abstaining), the buffer is too wide — or we need to
  bite the bullet and move to fractional points.

## Review questions

1. Why is the ±5% buffer multiplicative on the boundary value rather
   than additive on the ratio?
2. The v1 form abstains inside the buffer instead of damping. What
   single property of `GranvilleResult` forced that choice, and what
   would a fractional-point implementation look like?
3. With a buffer of 5%, what ratios trigger a full bearish fire when
   XIU rises by +0.20% and the S&P rises by +0.65%?
4. Why doesn't lowering the hard floor (e.g. from 0.33 to 0.25) solve
   the boundary-sensitivity problem?
