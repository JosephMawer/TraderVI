# ADR-0005: Defer Granville Dullness indicators (#21 and #22)

- **Status:** Accepted (defers implementation; revisit when sample expands)
- **Date:** 2026-05-14
- **Domains:** technical-indicators, decision-engine, data-pipeline, math-statistics

## Context

Granville's **Dullness** category consists of two indicators:

- **#21:** Dullness following a previous advance → bearish.
- **#22:** Dullness following a previous decline → bullish.

The intuition is that a *quiet tape after a directional run* signals
exhaustion of the trend's energy — buyers (or sellers) have stopped pushing.

Before writing any production code, we attempted a measurement-driven
calibration of an operational definition of "Dullness" against historical
XIU data. The calibration is implemented as a one-shot probe at
`Sandbox/Probes/DullnessCalibrationProbe.cs` and is rerunnable via:

```
dotnet run --project Sandbox -- dullness-calibrate
```

### Operational definition under test

A day was flagged "Dull" only if **all three** held (Option C — both volume
and range subdued, plus close barely moved):

| # | Condition | Default cut |
|---|---|---|
| D1 | `XiuVolume_today < c × median(XiuVolume, last 20 d)` | `c = 0.70` |
| D2 | `(High-Low)/Close < c × median((High-Low)/Close, last 20 d)` | `c = 0.60` |
| D3 | `\|ΔClose_today\|` (% vs. yesterday) | `< 0.25%` |

Rolling baselines used the **median** (not mean) over 20 trading days for
robustness against rebalance / ex-dividend spikes.

### Prior-trend classifiers tested

To split fires into the #21 vs. #22 buckets we tested two definitions in
sequence:

1. **Path A — 5-day return sign.** Positive ⇒ post-advance; negative ⇒ post-decline.
2. **Path B — proximity to 20-day extreme.** Today's close ≥ 0.98 × max(close, prior 20 d) ⇒ post-advance; ≤ 1.02 × min(...) ⇒ post-decline. Mirror days that satisfied both (very tight ranges) were re-bucketed as "flat".

### Results — XIU, 2020-01-02 → 2026-05-14 (1,579 eligible days)

| Metric | Path A | Path B |
|---|---|---|
| Dullness fire rate | 2.72% (43 fires) | 2.72% (43 fires) |
| **#21 hit rate @ h=5** (random=50%) | **32.3%** (n=31) | **34.3%** (n=35) |
| **#21 mean fwd return @ h=5** | +0.37% | +0.24% |
| **#22 hit rate @ h=5** | 58.3% (n=12) | 66.7% (n=3) |
| **#22 mean fwd return @ h=10** | +0.88% | +2.77% |

- **#21 is anti-predictive on this sample.** Hit rates sit below 50% at
  every forward horizon (h ∈ {1, 3, 5, 10}). Mean forward returns are
  positive — XIU went *up* after the rule predicted down. A full single-axis
  sensitivity sweep over the D1, D2, and D3 cuts kept #21's h=5 hit rate in
  the 19%–47% band — no threshold combination flips it.
- **#22 is directionally correct but statistically empty.** Under Path B the
  bucket collapsed to n=3, because XIU 2020-2026 spent very little time near
  20-day lows. Path A's n=12 has a 95% CI on its hit-rate that spans roughly
  39%–86% — overlapping random.
- **Tightening the prior-trend classifier (Path A → Path B) did not rescue
  the rule.** This rules out hypothesis #2 (crude classifier) and elevates
  hypothesis #1 (regime artifact) as the dominant explanation: 2020-2026 XIU
  is dominated by post-COVID melt-up + the 2023-2025 rally, with only the
  brief 2022 correction. Quiet days *after an advance* in a sustained bull
  regime are pauses-before-continuation, the opposite of what Granville
  described.

## Decision

**Defer implementation of Granville #21 and #22 indefinitely.** Do *not*
ship a `DullnessIndicators` class. Do *not* register a Dullness group in
`GranvilleComposite`. The category remains marked "Deferred" in
`Docs/design-rules.md`.

The decision will be revisited when **at least one** of the following
becomes available:

1. **Longer XIU history** — backfilled bars covering 2001–2019 so the sample
   includes the 2008-2009 bear, the 2011 correction, the 2015-2016 commodity
   slump, and the 2018 drawdown. This directly tests the regime hypothesis.
2. **A different universe** — Dullness applied to a broader symbol set
   (e.g., individual stocks with longer cumulative trading-day count, or
   sector indices with more bear-regime exposure) where post-decline
   episodes are more frequent.
3. **A reformulated rule** — if the data suggests Dullness behaves as a
   *continuation* signal in a momentum regime (which the positive #21 mean
   forward returns hint at), a re-derived indicator could ship under a new
   number. That would be a new ADR, not a revival of literal #21/#22.

The calibration probe (`Sandbox/Probes/DullnessCalibrationProbe.cs`) and the
generated per-day CSV (`dullness-backtest.csv`) are retained as the
experimental record. Rerunning the probe is a one-line command, so future
threads can re-validate against expanded data without redoing the analysis.

## Alternatives considered

- **Ship #21 and #22 with current thresholds at low weight.** Rejected.
  Knowingly registering a rule whose #21 half is anti-predictive on the only
  sample we have violates the "don't fool ourselves" / "measure everything"
  principles. The fact that #21 is one of 56 rules and the composite is
  capped does not justify shipping a documented-bad signal.
- **Ship #22 only, defer #21.** Rejected for now because Path B's #22
  bucket of n=3 (and Path A's n=12) is too small to claim a positive edge.
  An asymmetric rollout would still be reviving the topic before the
  underlying data problem is fixed.
- **Loosen #22's decline classifier (e.g., "within 4% of 50-day low") to
  recover sample size.** Rejected as a calibration move. Different
  proximity rules for advance vs. decline is an asymmetric overfit on a
  single-regime sample — exactly the kind of thing a 2008-inclusive
  re-calibration would either validate or destroy. Better to wait for the
  data and reconsider.

## Consequences

**Locked in:**
- The Dullness category stays in the Granville roadmap but is explicitly
  deferred, not silently skipped. The `IndicatorCategory.Dullness` enum
  member already exists; no code is registered against it.
- Future threads picking up Dullness must (a) widen the data sample first
  and (b) cite or supersede this ADR. The decision was data-driven, not
  preference-driven; reopening requires new data.

**What we gained even though we shipped no production code:**
- A reusable calibration harness (`DullnessCalibrationProbe`) and a
  rerunnable Sandbox dispatcher pattern that every future Granville rule
  with tunables can copy.
- Concrete evidence that *threshold-tuning a rule on a single-regime sample
  cannot fix a regime-dependent rule.* This pattern is likely to recur for
  other Granville indicators (Overdueness, Reversals, Gold) and we should
  expect to backfill before calibrating them too.

**What would tell us this decision was wrong:**
- Re-running the calibration over a 2001-2026 sample showing #21 and #22
  hit rates ≥ 55% at h ∈ {5, 10} with n ≥ 25 in each bucket. That would
  flip this ADR to "Superseded" and unlock a follow-up ADR specifying the
  final implementation.

## Review questions

1. Why did tightening the prior-trend classifier (Path A → Path B) not
   rescue #21? What did that result tell us about which hypothesis (regime
   vs. classifier) was dominant?
2. What's wrong with shipping #22 alone based on Path A's n=12 result?
   (Hint: confidence intervals on small-sample hit rates.)
3. What concrete data condition would justify revisiting this ADR? Name at
   least one specific metric and threshold.
4. Why is "loosen the decline classifier so #22 fires more" a worse move
   than "wait until we have a longer sample"?
