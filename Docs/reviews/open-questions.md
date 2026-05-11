# Open Questions

Things we punted on and need to revisit. Cleared as decisions are made
(those then become ADRs).

## Active

### Weighting category (Granville #15–#16) — follow-ups after ADR-0003
- **Q:** Re-validate `ScoreB ≥ 0.50` / `ScoreC ≥ 0.60` thresholds after
  N ≥ 30 live triggers or 12 months in production (whichever first).
  Current v1 calibration sample is N = 13.
- **Q:** Should we eventually upgrade the price-weighted contribution proxy
  to *true* cap weights (float-adjusted shares × price)? Defer until we
  have a concrete divergence between proxy and cap-weighted views worth
  investigating.
- **Q:** Should narrow *down-day* behaviour become its own future
  indicator? Backtest showed mean-reversion bounce, not continuation —
  potentially a *bullish* short-horizon signal. Park until ADR-0003 has
  live data.
- **Q:** Promote `Core/Config/Xiu60Constituents.cs` to a DB-backed
  `XiuConstituentMembership` table with effective-dated rows? Needed if
  we move to point-in-time-correct backtests for the Weighting group.
- **Q:** Hermes-side validation that all 60 constituents are present in
  the universe — still deferred; runtime ≥ 50/60 graceful-degradation
  guard remains the contract.

## Resolved

- **ScoreB/ScoreC threshold defaults (`ScoreB ≥ 0.50`, `ScoreC ≥ 0.60`,
  `K = 3`)** — closed by **ADR-0003**. Empirically calibrated from
  distribution + forward-return analysis on 2020-01-02 → 2026-05-06.
- **AND-gate vs. weighted-average for ScoreB/ScoreC** — closed by
  **ADR-0003**. AND-gate at strict thresholds is the only configuration
  with measurable 1d edge surviving sub-period split. Weighted-average
  diluted both signals.
- **Price-weighted Dow-style proxy for v1** — closed by **ADR-0003** and
  the supporting `concepts/price-weighted-contribution.md`. True cap
  weights deferred (see follow-ups above).
- **Static C# constituent list for v1** — closed by **ADR-0003**. v1
  uses `Core/Config/Xiu60Constituents.cs` (reviewed 2026-05-07).
- **40% placeholder breadth threshold** — superseded by empirical
  `ScoreC ≥ 0.60` in **ADR-0003**.
- **Hermes / TMX dot-suffixed symbol handling** — resolved during
  calibration: TMX accepts `TECK.B`, `BBD.B`, `RCI.B`, `GIB.A`, `REI.UN`
  in canonical dot form; missing coverage was a seeding gap, not a
  symbol-format limitation. Backfilled via `Tools/Backfill.MissingXiu`.
