# Open Questions

Things we punted on and need to revisit. Cleared as decisions are made
(those then become ADRs).

## Active

### Oracle (LLM layer) — follow-ups deferred during Phase 2 pause
- **Q:** Should narrative quality have an automated lint pass
  (e.g., regex that flags adjectives without an adjacent `(Field=value)`
  citation)? Currently Rule #8 of the system prompt asks the model to
  quantify, but we don't enforce it. Cheap Phase-3 add.
- **Q:** Pre-compute deterministic qualitative buckets in C#
  (`RelativeStrength.CompositeScoreBucket = weak|neutral|strong` from
  empirical quantiles) and have prompts parrot them rather than coining
  freeform adjectives?
- **Q:** Add a real `ClosestGate` field to `DecisionDossier` (the gate with
  the narrowest pass margin) so the model can answer "what nearly killed
  this pick?" without guessing. The previous freeform version of the
  question was dropped because the model always answered "Granville".
- **Q:** Phase 3 debate-loop schema: should `[LlmConversation]` reference
  `DossierId` directly, or go through a new `[OracleSession]` parent so a
  single session can span multiple dossiers (e.g., "why these 7 picks and
  not those 7?")? Decide before writing the table.
- **Q:** Per-dossier token-budget enforcement (Rule R4) is not yet
  implemented — currently the only guard is the implicit prompt size. Add
  before Phase 4 (news context) since that's where prompts will balloon.
- **Q:** Regression eval set (Rule R6) — we have no canned dossiers +
  expected key points yet. Build before Phase 3 ships so prompt edits can
  be evaluated objectively.

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
