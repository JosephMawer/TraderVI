# Concept: Price-weighted contribution as a Dow proxy on a cap-weighted basket

**Referenced by:** [ADR-0003](../adr/0003-weighting-indicator-narrow-advance.md)
**Domains:** finance-fundamentals, math-statistics, technical-indicators

## What problem this solves

Granville's original Weighting indicators (#15/#16) work on the **DJIA**,
which is **price-weighted**: each constituent contributes to the index in
proportion to its share price, regardless of company size. A $300 stock
moves the DJIA roughly 10× as much per percentage point as a $30 stock.
Granville observed that on many headline DJIA up-days, only a few
high-priced names were doing all the work — and these "narrow" advances
tended to stall.

Our benchmark is **XIU**, which is **cap-weighted**: each constituent
contributes in proportion to its market capitalization. A $200B mega-cap
moves XIU far more than a $5B small-cap regardless of share price. So
the *literal* Granville narrative ("look for high-priced names dominating
the move") does not apply.

But the *underlying idea* — "narrow leadership predicts a stalled move" —
is index-agnostic. We just need a way to make narrowness visible on a
basket where the natural weighting already smooths it out.

## The proxy we use

For each XIU constituent *i*, define its contribution as:

```
weight_i        = price_i / Σ price_j         (Dow-style price weight)
contribution_i  = weight_i × return_i
```

This is **deliberately not** XIU's true index math. It's a *proxy* designed
to surface the kind of asymmetry Granville cared about — a few high-priced
names dominating the apparent direction.

### Why use the proxy at all instead of cap weights?

Two reasons:

1. **It's interpretable as Granville intended.** "Narrow leadership" is a
   conceptually price-weighted phenomenon. A cap-weighted view of
   contribution would smooth over exactly the structure we want to detect.
2. **It's simple to compute.** Cap weights require continuously refreshed
   market-cap data (or float-adjusted shares-outstanding × price). Price
   weights need only the closing price, which we already have in
   `dbo.DailyBars`. Lower data dependency = fewer failure modes.

### Why call it a "proxy" instead of a real index?

Because **XIU's headline return is not the sum of our `contribution_i`
values**. It's the cap-weighted average return. We compute the price-
weighted contributions *only to derive ScoreB (concentration) and ScoreC
(narrowness)* — not to predict XIU's return value. ScoreB and ScoreC are
*structural* descriptors of the day's move, not return forecasts.

## How ScoreB and ScoreC use it

- **ScoreB (concentration).** Look at constituents moving *same direction
  as XIU*. Sort by `|contribution_i|` descending. Take the top K = 3. Their
  share of the same-direction `Σ|contribution_i|` is ScoreB. Empirically
  centred around 0.5 — half the same-direction "push" coming from 3 of
  ~30 same-direction names is the *normal* base rate, not an anomaly. Only
  the right tail (ScoreB > ~0.7) is unusual.

- **ScoreC (narrowness).** Of the constituents that moved at all (skip
  ties), what fraction moved *against* XIU's direction? Empirically
  centred around 0.34 — about a third disagreeing is normal noise. The
  right tail (ScoreC > 0.50) is genuinely rare and the part Granville's
  hypothesis cares about.

Crucially, ScoreB and ScoreC are **not redundant**. ScoreB can be high on
broad-participation days (top-3 just happens to dominate the *aggregate*
size of pushes) without being narrow. ScoreC can be high without one
dominant name doing the lifting (many tiny same-direction moves with a few
large counter-moves). The AND-gate captures the Granville thesis: narrow
**and** top-heavy.

## Why this matters beyond Weighting

The price-weighted contribution proxy is reusable for any future indicator
that asks "who moved the index today?". We chose it deliberately to keep
the conceptual machinery shared with the historical literature
(Granville, Murphy, etc.), even where the underlying index has switched
from price-weighted to cap-weighted in the intervening decades.

## When to revisit

- If we add a **cap-weight provider** (float-adjusted shares × price), we
  could compute a *true* contribution ladder. Worth doing if (a) ScoreB
  starts misbehaving on a known divergence between price weight and cap
  weight, or (b) we want to validate Granville's thesis with first-
  principles math rather than a proxy.
- If we ever change benchmarks (XIC, sector ETFs, etc.) the proxy still
  works without modification — only the constituent list changes.

## Review questions

1. Why don't we use XIU's real cap weights for ScoreB/ScoreC?
2. What does the proxy preserve from Granville's original DJIA logic, and
   what does it deliberately abandon?
3. Why are ScoreB and ScoreC not measuring the same thing?
4. What is the proxy *not* trying to predict, and why is that distinction
   load-bearing for the indicator's interpretation?
