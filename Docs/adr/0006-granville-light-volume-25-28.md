# ADR-0006: Granville Light Volume indicators (#25–#28) — tape × leadership-quality

- **Status:** Accepted
- **Date:** 2026-05-22
- **Domains:** technical-indicators, decision-engine, finance-fundamentals

## Context

Granville's **Light Volume** group asks one question across four cells: on a
day where market participation is unusually thin, what does the *quality* of
market leadership say about whether the day's move is meaningful?

The four indicators (paraphrased from Granville):

| # | Tape | Leadership quality | Reading |
|---|---|---|---|
| 25 | Rise on light volume | Deteriorating | Rise lacks conviction — **bearish** |
| 26 | Rise on light volume | Improving | Light volume tolerable; leaders carrying — **bullish** |
| 27 | Decline on light volume | Improving | Decline not yet exhausted — **bearish** |
| 28 | Decline on light volume | Deteriorating | Selling exhaustion — **strong bullish** |

Three independent inputs are required:

1. **Volume regime** — is today's XIU volume "light"?
2. **Direction** — is XIU rising or declining today?
3. **Leadership quality** — improving or deteriorating?

Each axis needs an unambiguous, leak-free, machine-evaluable definition that
also lines up with how we evaluate everything else in the Granville surface.

## Decision

### Light volume threshold

Define:

```
LightVolume = XIU.Volume(t) / SMA20Prior(XIU.Volume)(t) < 0.85
SMA20Prior(t) = mean(XIU.Volume[t-20 .. t-1])      // EXCLUDES today (t)
```

- Reference series: **XIU** (`Core.TMX.TsxBenchmarkSymbols.Xiu`). XIU is the
  same benchmark already used for regime, weighting, and breadth context.
- **Excludes today** from the SMA so the ratio is leak-free between live and
  backtest evaluation (no look-ahead via today's own volume).
- **Threshold 0.85** (i.e. ≥ 15% below the trailing average) is the initial
  default — to be tuned from probe data, not treated as a fixed constant.

### Direction

Define direction from the **XIU 1-day close-to-close return**, with a small
configurable dead-band to suppress flat-tape noise:

```
Rise    : XiuReturn1d >  DirectionDeadBand   (default +10 bps)
Decline : XiuReturn1d < -DirectionDeadBand
Flat    : otherwise → no #25–#28 fires
```

XIU price return is a **different data source** than the breadth series that
drives Leadership #7–#10, so #25–#28 add genuinely new information rather
than re-asserting the Leadership group on light-volume days.

### Leadership quality

Reuse `LeadershipCalculator.ComputeQuality(...)` over the breadth-derived
`LeadershipHistory` already present on `GranvilleMarketContext`. Only
`Improving` or `Deteriorating` enables a #25–#28 firing; `Stable` and
`Indeterminate` collapse to a neutral no-fire result.

### Co-firing with Leadership #7–#10 — strictly additive

#25–#28 evaluate independently from #7–#10. They may co-fire on the same
day. This is intentional:

- Inputs differ. Leadership #7–#10 read the **shape** of the leadership
  series alone. Light Volume reads **leadership quality × tape price ×
  tape volume**. Two of those three axes are not in #7–#10.
- Calibration is a numbers problem, not a wiring problem. If empirical
  testing later shows overweighting, we'll tune point values or normalization
  rather than coupling the rule families.

### Context shape — co-located `MarketTapeContext`

Add a single new field to `GranvilleMarketContext`:

```
MarketTapeContext? MarketTape { get; init; }
```

`MarketTapeContext` co-locates today's tape facts that any indicator group
might need:

- `Date`, `XiuVolume`, `XiuVolumeSma20Prior`, `XiuClose`, `XiuPrevClose`
- Derived nullable: `XiuVolumeRatio20`, `XiuReturn1d`

We chose co-location over separate `XiuVolumeContext` / `XiuPriceContext`
fields because future Granville groups (e.g. volume-quality, gap behavior)
will read the same handful of facts. One field, one builder, one diagnostic
section keeps the context coherent.

### Data source

`MarketTapeCalculator.Build(IReadOnlyList<DailyBar> xiuBarsAscending)` produces
the context from the **existing** XIU bars already loaded by
`QuoteRepository.GetDailyBarsAsync(TsxBenchmarkSymbols.Xiu)` for the regime
section. No new SQL surface, no new HTTP source.

- Returns `null` only when the bar list is empty.
- Returns a populated context whose derived properties are `null` when
  there aren't enough prior sessions (< 21) for the SMA — the indicator
  group surfaces a `NeutralNoData` result in that case.

### Scoring

Points follow the asymmetry inherent in Granville's text:

| # | Signal | Points |
|---|---|---|
| 25 | Bearish | -1 |
| 26 | Bullish | +1 |
| 27 | Bearish | -1 |
| 28 | StrongBullish | +2 |

At most **one** of #25–#28 fires per day (the 2×2 of rise/decline ×
improving/deteriorating is mutually exclusive once the dead-band filters
flat tape).

### Normalization

`GranvilleComposite.MaxRawPointRange()` raises its headroom value to **19**
to account for the Light Volume group's contribution (worst-case
single-firing bullish = +2, bearish = -1) layered on top of the existing
range.

## Consequences

### Positive

- Closes #25–#28 without coupling them to Leadership #7–#10.
- All inputs are already in the system; no new data ingestion is needed.
- The `MarketTapeContext` field is a reusable hook for later Granville groups
  that need same-day price/volume facts.
- Graceful degradation: every input has a `NeutralNoData` fallback, so
  Delphi keeps running when XIU history is short or leadership data is thin.

### Negative

- #25–#28 are deterministic but not yet calibrated. They will mechanically
  fire whenever tape + quality + dead-band align, regardless of empirical
  edge.
- The 0.85 light-volume threshold and 10 bps dead-band are unvalidated;
  probe data will likely refine both.

### Tunables to revisit

- `LightVolumeIndicators.LightVolumeThreshold` (default 0.85m)
- `LightVolumeIndicators.DirectionDeadBand` (default 0.0010m)
- Whether #28's StrongBullish weighting (+2) is too generous once we have
  empirical hit-rate / forward-return data.

## Validation

No unit tests are written for this group (consistent with the current
project policy of probe-based validation). Probes to add later:

- Sandbox probe that prints, per recent session, the three axes and which
  (if any) indicator fired.
- Probe that backtests #25–#28 firings against forward XIU returns to
  validate or retune the thresholds and point values.

## Related ADRs

- **ADR-0001** — Granville plugin architecture (group registration).
- **ADR-0002** — XIU as the canonical benchmark index.
- **ADR-0003** — Weighting indicator #15/#16 (separate Granville group that
  already drives the `XiuConstituentBars` data path).
- **ADR-0004** — Genuity indicators #17–#20 (separate Granville group, US
  confirming indices).
