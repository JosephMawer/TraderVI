# ADR-0012 — Sector-index historical backfill from TMX `getTimeSeriesData`

- **Status:** Accepted
- **Date:** 2026-05-23
- **Tags:** data-pipeline, technical-indicators, decision-engine
- **Supersedes:** —
- **Related:** ADR-0010 (RS Z-score composite), ADR-0011 (RS in ranking).

## Context

Delphi's relative-strength composites — both raw `RScomp` and Z-normalized
`CompZ` — require sector-index history at least as deep as their longest
window: 60 trading days for the 60-day RS horizon plus a 20-day Z-window,
so a hard floor of ~80 bars per `^TT*` symbol. The 2026-05-22 Delphi run
exposed that `dbo.SectorIndices` held only 7–8 bars per symbol (one
snapshot per Hermes execution day), which caused `RScomp` to be `null` for
217 of 236 ranked candidates. Top picks displayed `null` RS columns;
ranking effectively collapsed to `DirectionEdge`-only.

A Sandbox probe (`Sandbox/Probes/TmxSectorHistoryProbe.cs`) verified that
TMX's GraphQL `getTimeSeriesData` query returns full multi-year daily OHLCV
for all 11 `^TT*` sector symbols (754 bars over a 3-year window) — the
same endpoint Hermes already uses for constituents. The snapshot endpoint
(`getQuoteForSymbols`) only delivers latest values and cannot reconstruct
history.

A first implementation attempt failed: using `MAX([Date]) WHERE Symbol = …`
as the resume cursor reported "Up-to-date" because the daily snapshot path
had already seeded yesterday's date, leaving the multi-year gap *before*
the earliest stored bar invisible to the cursor logic.

## Decision

Add `BackfillSectorIndexHistoryAsync(TmxClient)` to Hermes that:

1. Iterates `TsxSectorSymbols.All` (currently 11 `^TT*` symbols).
2. Probes per-symbol coverage via `SectorIndexRepository.GetCoverageAsync`
   → `(count, earliest, latest)`.
3. **Chooses a fetch mode by coverage shape, not by latest-date alone**:
   - **`[FULL]`** — refetch from `2020-01-01` when `count < 100` OR
	 `earliest > defaultStartDate + 30d`. Catches the "snapshot seeded
	 recent rows, no history" case.
   - **`[incr]`** — resume from `latest + 1` otherwise.
4. Calls `TmxClient.GetHistoricalTimeSeriesAsync(symbol, "day", start, end)`.
5. Sorts ascending, seeds `prevClose` from
   `SectorIndexRepository.GetLatestCloseBeforeAsync(symbol, firstBarDate)`,
   walks bars computing
   `PriceChange = close − prevClose`,
   `PercentChange = (close − prevClose) / prevClose × 100`.
6. Persists via `SectorIndexRepository.UpsertAsync` (MERGE keyed on
   `[Date]`, `[Symbol]` → idempotent).
7. Sleeps 500 ms between symbols (~2 req/sec; matches the constituent
   backfill cadence).

Wire it into `RunBackfillAsync()` **before** the existing
`UpdateSectorIndicesAsync(tmx)` so historical depth lands first; the
existing snapshot updater then layers today's tick on top.

Also fix a latent precision-truncation bug in `UpsertAsync`:
`SqlDbType.Decimal` parameters were created without `Precision`/`Scale`,
which defaults to `(18, 0)` and silently truncates `PriceChange` /
`PercentChange` (and `Price`) to integers despite a `DECIMAL(18, 4)`
column. All three parameters now explicitly set `Precision = 18, Scale = 4`.

## Alternatives considered

- **Compute sector indices from constituent stock returns.**
  Rejected: requires accurate market-cap weights, drift handling on
  rebalances, and reconciliation against published index levels.
  Reinvents an index TMX already publishes.
- **Use the snapshot endpoint `getQuoteForSymbols` and let history
  accumulate one day at a time.**
  Rejected: ~80 trading days = ~4 months of waiting before RS works at
  all, and zero bars on day 1 of any new sector being tracked.
- **`MAX([Date]) + 1` cursor.**
  Rejected after first attempt — the snapshot updater pre-seeds recent
  rows, so the cursor reports "up-to-date" while the multi-year gap
  remains. Coverage-shape gating (`count`, `earliest`) is required.
- **Yahoo / Stooq for sector data.**
  Rejected: TMX's `^TT*` namespace is the canonical TSX sector source,
  Yahoo's coverage of these symbols is inconsistent, and Stooq's
  free CSV is gated.

## Consequences

**Locks us into:**
- TMX as the sole sector-index data source. If TMX's `^TT*` namespace
  changes (renames, deprecations), Hermes will report `No bars returned`
  per affected symbol and `TsxSectorSymbols.All` must be updated.
- `2020-01-01` as the floor on history. Sufficient for current 80-bar
  RS requirements and any 1-year regime study; insufficient for
  multi-cycle backtests (raise the constant if needed).
- The `count < 100` / `earliest > start + 30d` coverage thresholds as
  the "needs full refetch" trigger. Tunable, but baked into the
  backfill function.

**Easier:**
- Both `RScomp` and `CompZ` now compute for every sector-mapped symbol.
- Adding a new sector to `TsxSectorSymbols.All` automatically triggers
  a `[FULL]` backfill on the next Hermes run — no manual seeding.
- Re-runs are idempotent (MERGE) so this routine is safe to leave wired
  into every Hermes execution.

**Harder:**
- `dbo.SectorIndices.PriceChange` / `PercentChange` semantics now mean
  "sequential-close delta across the full series" for historical rows
  *and* "today vs yesterday" for the snapshot append — functionally
  equivalent on daily bars, but anything that interpreted the column as
  strictly intraday-snapshot must be reviewed.
- One-shot data weight: each `[FULL]` run is ~1,604 rows × 11 symbols ≈
  17.6k rows of MERGE traffic. Cheap, but worth noting if more sectors
  are added.

**Would tell us this was wrong:**
- TMX retiring `getTimeSeriesData` or rate-limiting it below the 500 ms
  per-symbol cadence.
- Material divergence between TMX-published `^TT*` closes and an
  independent recomputation from constituents — would force a reconcile
  step.

## Review questions

1. Why did the first backfill attempt skip every symbol despite the new
   code path existing?
2. What two coverage conditions trigger the `[FULL]` refetch mode, and why
   isn't a latest-date cursor sufficient on its own?
3. What latent bug in `SectorIndexRepository.UpsertAsync` did this work
   incidentally fix, and what was the symptom?
4. Why was reconstructing sector indices from constituent stock returns
   rejected as a backfill source?
