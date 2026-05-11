# ADR-0003: Weighting indicator (Granville #15–#16) — narrow-advance warning gate

- **Status:** Accepted
- **Date:** 2026-05-09
- **Domains:** technical-indicators, decision-engine, math-statistics, finance-fundamentals

## Context

Granville's Weighting indicators (#15–#16) target the DJIA, a *price-weighted*
index where a handful of high-priced names can swing the headline number even
when most constituents disagree. The original thesis: when an index move is
carried by very few names, the move is suspect.

Our benchmark is **XIU**, a cap-weighted TSX 60 ETF. Cap-weighting already
neutralizes the price-weighted distortion Granville was exploiting, so a
literal port doesn't apply. We need to translate the *idea* — "narrow
leadership predicts a stalled move" — into a form that fits a cap-weighted
basket while staying interpretable.

We also need this decision to be empirically grounded. Prior Granville
categories (Plurality, Disparity, Leadership, Most Active) were implemented
from the book's rules directly. Weighting is the first category we
**backtest before shipping**, because the reformulation is non-trivial and we
want evidence that whatever we build earns its place in the composite.

## Decision

Implement Weighting as a single `IGranvilleIndicatorGroup` (per ADR-0001)
that produces **one Granville result** when a narrow-advance condition is
met. The group is a **long-side warning gate**, not a directional bet.

### Scoring

For each XIU trading day, with at least 50 of 60 constituents present
(graceful degradation gate):

- **Contribution proxy.** For each constituent *i*, compute a price-weighted
  contribution `w_i = (price_i / Σ price_j) × return_i`. This is an
  *intentional* Dow-style proxy on a cap-weighted basket (see
  [concepts/price-weighted-contribution.md](../concepts/price-weighted-contribution.md))
  — it makes "few names dominate" visible in a way cap weights would hide.

- **ScoreB (concentration).** Of the constituents moving *with XIU's
  direction*, what fraction of total |contribution| is captured by the top
  K = 3 names? Range 0–1, higher = more top-heavy.

- **ScoreC (narrowness).** Of the constituents that moved at all (excluding
  ties), what fraction moved *against* XIU? Range 0–1, higher = fewer names
  participated with the index = "narrower" move.

Flat-XIU days (`XiuReturn == 0`) are skipped entirely — ScoreC has no
meaningful "with-index" direction on those days.

### Trigger rule (v1)

```
ScoreB >= 0.50  AND  ScoreC >= 0.60  AND  XiuReturn > 0
```

When triggered, the group emits a single `GranvilleResult` with:
- `IndicatorNumber = 15` (combined #15/#16 — see "Consequences" for why)
- `Category = IndicatorCategory.Weighting`
- `Name = "Weighting #15/#16: Narrow Advance"`
- `Signal = IndicatorSignal.Bearish`
- `GranvillePoints = -1`
- `Description` includes ScoreB, ScoreC, top-3 contributors, and the
  constituent coverage.

On any other day (no trigger), the group returns a single Neutral result
(zero points), consistent with how `PluralityIndicators` handles its
no-signal case.

### Empirical basis

Backtested via `Tools/Backtest.Weighting` over 2020-01-02 → 2026-05-06 on
60 XIU constituents (1,557 scored days):

| Subset | N | 1d mean (%) | 1d hit | 5d mean (%) | baseline 1d mean (%) |
|---|---:|---:|---:|---:|---:|
| All days | 1,557 | +0.053 | 55.4% | +0.246 | — |
| Baseline up-days | 876 | +0.035 | 56.6% | +0.225 | — |
| **v1 ∩ up-days (full sample)** | **13** | **−0.294** | **46.2%** | **−0.239** | +0.035 |
| v1 ∩ up-days (2020–2023 half) | 7 | −0.381 | 57.1% | +0.204 | −0.011 |
| v1 ∩ up-days (2023–2026 half) | 6 | −0.193 | 33.3% | −0.754 | +0.080 |
| v1 ∩ down-days | 17 | +0.114 | 47.1% | +0.456 | +0.076 |

**The 1-day reversal effect is the only finding that holds across both
sub-periods.** 5d and 10d behavior is regime-dependent.

## Alternatives considered

- **Literal Granville #15/#16 on a price-weighted proxy index alone.**
  Rejected: we already need ScoreC for the narrowness side of the thesis;
  collapsing back to a single proxy-return value loses that information.

- **AND-gate vs. weighted average of ScoreB and ScoreC.** Tested both
  directions implicitly via candidate rules. AND-gate at strict thresholds
  was the only configuration with measurable 1d edge that survived sub-period
  splitting. Weighted-average alternatives (e.g., `0.4×B + 0.6×C ≥ 0.55`)
  diluted both signals and the predictive content washed out.

- **Looser thresholds for more triggers (Opt1, Opt2 from calibration).**
  Rejected: forward-return tests showed the edge concentrates in the strict
  corner. Loosening trades specificity for nothing.

- **Treat as a hard Delphi-level gate (suppress new longs on trigger).**
  Reasonable, but a bigger commitment than 13 historical triggers justify.
  Defer to a future ADR once we have live evidence. For v1, the indicator
  flows through `GranvilleComposite` like every other category.

- **Per-indicator emission (#15 and #16 as separate `GranvilleResult`s).**
  Granville's original #15 and #16 are the two directional cases (narrow
  advance / narrow decline). In the cap-weighted reformulation our trigger
  is fundamentally one-sided — we *do not* fire on down-days because down-day
  triggers show a mean-reversion bounce, not a narrow-decline warning. One
  `GranvilleResult` with combined numbering reflects this honestly.

- **Static breadth threshold of 40%** (from earlier design discussions).
  Superseded by the empirical ScoreC ≥ 0.60 threshold derived from the
  distribution + forward-return analysis.

## Consequences

- **New context dependency.** `GranvilleMarketContext` gains an
  `XiuConstituentBars` field carrying today's and yesterday's closes for the
  60 XIU constituents. Delphi/Hermes must populate it. Until populated,
  the group degrades gracefully to Neutral (matches the existing pattern for
  optional context fields).
- **Constituent source.** v1 reads from `Core.Config.Xiu60Constituents`
  (static list, reviewed 2026-05-07). Promotion to a DB-backed
  `XiuConstituentMembership` table is deferred.
- **MaxRawPointRange update.** `GranvilleComposite.MaxRawPointRange()` must
  add `-1` (Weighting bearish max) to the bearish floor. New range:
  bearish `[-10, +13]` bullish.
- **DelphiReportBuilder.** Per `.github/copilot-instructions.md`, the diagnostic
  and summary outputs must surface ScoreB, ScoreC, trigger status, and top
  contributors when the group is registered.
- **Thresholds are provisional.** 0.50 / 0.60 are v1 defaults calibrated on
  N = 13 narrow-advance triggers. Re-validate at 12 months in production OR
  N = 30 triggers in the live window, whichever comes first.
- **No 5-day or 10-day claim.** ADR explicitly does *not* claim multi-day
  predictive power; sub-period analysis showed regime-dependence beyond 1d.
- **Asymmetric application.** The group never fires on down-days; this is
  load-bearing for the interpretation. If a future analysis finds value in
  down-day triggers, that becomes a separate indicator, not an expansion of
  this one.

## Review questions

1. Why was Granville's literal #15/#16 not portable to XIU, and what
   reformulation did we ship instead?
2. What do **ScoreB** and **ScoreC** measure, and why do we need *both* —
   what fails if we use only one?
3. Why is the indicator one-sided (up-days only)? What did the down-day
   triggers show in backtest?
4. What's the strongest evidence the v1 thresholds aren't just curve-fit
   noise — and what's the strongest argument that they *are*?
5. What two trigger conditions in the rule are *empirical* (chosen from
   data) and which one is *structural* (chosen from the indicator's
   intended role)?
6. What concrete event would make us tighten, loosen, or retire these
   thresholds in the next iteration?
