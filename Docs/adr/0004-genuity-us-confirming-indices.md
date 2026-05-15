# ADR-0004: Genuity indicators (Granville #17–#20) — US confirming-index source & staleness gate

- **Status:** Accepted
- **Date:** 2026-05-12
- **Domains:** technical-indicators, decision-engine, data-sources, finance-fundamentals

## Context

Granville's **Genuity** group (#17–#20) asks: "Is XIU's daily move *genuine*,
or is it an unconfirmed local move that's likely to reverse?" Granville's
original device was the Dow Transports confirming the Dow Industrials. Our
benchmark is **XIU** (cap-weighted TSX 60 ETF). The natural Canadian-only
analog (TSX Composite vs XIU) is too autocorrelated to be informative — both
are dominated by the same large-cap basket. We need an *independent*
confirming surface.

TSX large-caps are macro-correlated to US large-caps on a daily horizon, so
**US broad-market indices** make a credible confirming surface: when XIU moves
without the US moving with it, the move is locally driven (FX, sector
rotation, single-name news) and less likely to persist.

The question was therefore **which US indices and which source**.

### Source validation (Sandbox probe)

We needed daily OHLC for the confirming indices, free, reliable, no key.
Three sources were tried:

| Source | Result |
|---|---|
| **TMX** (`^GSPC:US`, `^NYA:US`) | Symbols *recognized* but `historicalTimeSeries` returned no bars / null fields. Not usable. |
| **Stooq** CSV download | Returned a captcha / API-key gate, not CSV. Not usable. |
| **Yahoo Finance** `query1.finance.yahoo.com/v7/finance/chart/{symbol}` | Returned correct daily OHLC for `^GSPC`, `^NYA`, `^DJI`. **Accepted.** |

Yahoo's chart endpoint is the same surface used by `yfinance` and most
open-source bridges: no key, no cookie/crumb, no captcha. A browser-like
User-Agent is required to avoid edge 401s. Indices typically publish
`volume = 0`; we ignore volume — only `Close` feeds Genuity.

## Decision

### Confirming indices

Use two US broad-market indices:

- **`^GSPC`** — S&P 500 Composite (primary)
- **`^NYA`** — NYSE Composite (broader breadth confirmer)

Symbol constants live in `Core.TMX.UsIndexSymbols`. The list is intentionally
short; expanding it widens daily ingestion and the diagnostic surface.

### Data source

Implement `IUsIndexDataSource` and a concrete `YahooChartUsIndexDataSource`
(in `Core.TMX`) that hits the Yahoo `chart` endpoint with a browser UA, a
light retry on transient errors, and JSON parsing into a typed `UsIndexBar`
record. The interface keeps the source swappable if Yahoo's contract ever
breaks.

### Storage

A new SQL table **`[dbo].[UsIndexBars]`** stores `(Symbol, Date, Open, High,
Low, Close, Volume, CreatedAt)` with PK `(Symbol, Date)`. We chose the name
*UsIndexBars* (plural, "bars" not "indices") because the table stores
*time-series bars* — analogous to `DailyBars` — not metadata about the
indices themselves. Access goes through `UsIndexBarsRepository` which uses a
session-scoped temp table + `MERGE` for idempotent upsert.

### Ingestion (Hermes)

Hermes calls `UpdateUsIndexBarsAsync(backfillYearsIfEmpty: 10)` after the
existing TSX backfill, A/D update, sector update, stock-sector refresh, and
leadership update. On first run, it backfills 10 years per symbol; on
subsequent runs, it requests only the incremental window since the latest
stored bar. Each symbol gets a ~300 ms delay between requests to stay polite.

### Evaluation (Delphi)

`GranvilleMarketContext.UsIndexBars` is an
`IReadOnlyDictionary<string, IReadOnlyList<UsIndexBar>>` keyed by canonical
symbol. Delphi loads ~30 calendar days per US symbol from the repository
before each evaluation — enough history for the 5-day trend check (#20) plus
a buffer for US/Canada holiday offsets.

### Scoring (`GenuityIndicators`)

Four indicators are emitted on each evaluation:

- **#17** — XIU vs `^GSPC` same-day directional confirmation. Same sign ⇒
  `±1` per XIU direction; opposite sign ⇒ invert XIU's directional implication.
- **#18** — XIU vs `^NYA` same-day directional confirmation (same logic).
- **#19** — XIU vs `^GSPC` magnitude proportionality. With matching signs, if
  `|XIU return| / |^GSPC return|` falls outside `[1/3, 3]`, the move is flagged
  disproportionate and XIU's directional implication is inverted.
- **#20** — XIU vs `^GSPC` 5-day trend alignment. Aligned signs ⇒ confirmed
  trend (`±1`); divergent signs ⇒ invert XIU's 5-day direction.

### Staleness gate

If either confirming index's most recent bar trails XIU's by more than
**1 calendar day**, all four indicators short-circuit to a *single* Neutral
result with a "Stale US data" diagnostic. The 1-day threshold tolerates the
common "Canadian close published before US bar appears" race without falsely
reporting confirmation/divergence.

### Magnitude floor (flat-tape gate)

Same-day indicators (#17, #18, #19) require `|XIU return| ≥ FlatReturnEpsilon`
**and** `|US return| ≥ FlatReturnEpsilon` before they emit a directional
result; otherwise they return Neutral with a "move too small to evaluate"
diagnostic. The prior is **10 bps (`0.0010`)** — chosen because:

- Sub-10 bps days are effectively flat tape. `sign(return)` is dominated by
  noise (a one-cent tick on a $50 XIU bar is ~2 bps).
- "Confirming a near-zero move" is meaningless. With a 5 bps prior, a
  -0.06 % XIU print confirmed by a -0.16 % S&P print produced a Bearish −1
  with the label `(confirmed)` — operationally noise, narratively
  misleading.
- 10 bps is small enough that ordinary sessions still produce signals; on
  the May-2026 walk-forward set, ~9 % of XIU days fall under the threshold.

#20 (5-day trend) keeps its own epsilon check on the 5-day return, not on
today's return, so flat days don't suppress trend alignment.

### Label conventions

Same-day directional indicators (#17, #18) name their result by the
**direction confirmed**, not just the fact of confirmation:

- `Genuity #17: S&P 500 confirmation (upside confirmed)` ⇒ Bullish +1
- `Genuity #17: S&P 500 confirmation (downside confirmed)` ⇒ Bearish −1
- `Genuity #17: S&P 500 confirmation (non-confirmation)` ⇒ invert XIU's
  directional implication

Without the direction word, the gate pipeline showed
`Granville warning: Genuity #17 confirmation (confirmed)` paired with a
Bearish point, which forced the reader to reconcile "confirmed" and
"warning" against each other. The directional word makes the result
self-explanatory in summary output.

### Composite integration

`GranvilleComposite` registers `GenuityIndicators` and raises
`MaxRawPointRange()` from 13 to 17 so Genuity's contribution stays within the
existing `MaxCompositeAdjustment = 0.10` envelope.

## Consequences

- Yahoo is an unofficial public endpoint with no SLA. If it changes shape or
  rate-limits us, the `IUsIndexDataSource` seam lets us swap in another
  source (e.g., FMP, Polygon free tier) without touching Genuity logic.
- Volume is unreliable for indices (often `0`, especially intraday). Genuity
  only consumes `Close`; this is intentional.
- We deliberately use *only* `^GSPC` and `^NYA`. Adding Russell / DJIA / VIX
  would expand the diagnostic surface; defer until there's a concrete need.
- Two US indices = max +2/-2 raw points from #17/#18 alone, plus ±1 each from
  #19/#20. Genuity can therefore push the composite by up to ~0.04 in
  absolute terms — meaningful but not dominant.
- Staleness is determined by *date gap*, not by wall-clock time. This is
  resilient to running Delphi at different hours of the day.

## Open questions

- Should we ever trade off `^NYA` for `^DJI` or VIX in #17/#18? Probably not
  for v1 — `^NYA` is broader and more orthogonal to `^GSPC`.
- Should the magnitude band `[1/3, 3]` in #19 be tuned empirically? Likely
  yes once we have enough live signal, but the prior is intentional and
  matches Granville's "out of proportion" language.

## References

- ADR-0001 — Granville indicators as per-category groups
- ADR-0002 — Why XIU is the benchmark
- ADR-0003 — Weighting indicator (cap-weighted reformulation precedent)
- `Core/Indicators/Granville/GenuityIndicators.cs`
- `Core/TMX/YahooChartUsIndexDataSource.cs`
- `Core/Db/UsIndexBarsRepository.cs`
- `TraderDB/dbo/Tables/UsIndexBars.sql`
