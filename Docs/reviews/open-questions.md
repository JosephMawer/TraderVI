# Open Questions

Things we punted on and need to revisit. Cleared as decisions are made
(those then become ADRs).

## Active

### On-Balance Volume soft tilt — first live-read calibration (ADR-0016)
- **Q:** The first live Delphi OBV read (2026-06-06, 233 symbols) validated the wiring
  (`0 indeterminate`; the ±0.1 tilt verifiably reordered ESI below BTE) but surfaced two
  numbers that should be calibrated before the soft tilt is trusted long-term.
- **Finding 1 — is `ObvSignalWeight = 0.10` too strong vs. the *gate-passer* RScomp spread?**
  ADR-0016 sized the weight against the full-universe `RScomp` range (~±0.2). But the names
  that actually clear every gate and compete for the executable #1 slot cluster near
  `RScomp` ≈ ±0.1 (e.g. FM +0.097, ORE −0.009, DPM +0.022) — the high-RS names (MATR +0.91,
  ATH +0.77) all fail gates. So among gate-passers a ±0.1 tilt is the same magnitude as the
  entire RS spread and can act as the deciding vote rather than a gentle nudge. Resolve via a
  paper-trade IC/agreement window; candidate fix is to scale the tilt to gate-passer RS
  dispersion or lower the constant. Do **not** retune by guess.
- **Finding 2 — is the classifier's "≥2 pivots per side" rule inflating `Doubtful`?** First
  read was 140/233 (60%) `Doubtful`. Part of that is structural, not market chop:
  `ObvFieldTrendCalculator.Classify` only returns `Rising`/`Falling` when it has ≥2 UP *and*
  ≥2 DOWN pivots; once any opposing pivot exists with fewer than two per side it falls into
  the "too few pivots" `Doubtful` branch. With a 20-session breakout window over capped
  6-month retention, pivots are sparse, so 1–3-pivot symbols are forced `Doubtful`. This is
  conservative/safe for a soft tilt, but (a) keeps the signal quiet and (b) directly
  throttles the upcoming **Climax** indicator, which aggregates the same UP/DOWN
  designations — a thin pivot supply means a thin breadth tally. Decide whether to relax the
  pivot rule (e.g. allow a single-pivot-per-side higher-high/higher-low call) or accept the
  conservatism.
- **Tags:** decision-engine, technical-indicators, time-series, math-statistics
- **Status:** parked — wiring verified live (ADR-0016). Both numbers to be calibrated against
  a paper-trade window; Finding 2 is also an explicit input to the Climax ADR.

### Sector index history backfill (`dbo.SectorIndices` thin coverage)
- **Q:** The 2026-05-23 Delphi run exposed `RScomp`/`CompZ`/`RS10d` displaying `null` for the top-14 passing candidates while a few lower-ranked rows (SOFY, RDDY) showed real values. Root cause: `dbo.SectorIndices` currently holds only **7–8 trading days** of history per `^TTxx` symbol (range 2026-04-16 → 2026-05-22). `RelativeStrengthCalculator.Compute(...)` clamps `n = min(stockBars, sectorBars, marketBars)` to ~8, so `ReturnDiff(horizon=10)` returns `null`, `ComputeRsZ(horizon=10, zWindow=20)` requires `n ≥ 30` and also returns `null`, and the switch expression collapses `CompositeScore` and `CompositeScoreZ` to `null` for every sector-mapped symbol. The 5 "fallback to XIU" symbols show non-null values because XIU has full history, but the sector dimension is mathematically degenerate (StockVsSector ≈ StockVsMarket, SectorVsMarket ≈ 0) — those numbers carry no real sector information.
- **2026-05-23 partial fix (diagnostic, not data):** Delphi now emits an explicit RS-coverage block in both console and diagnostic report (min/max sector bars, fallback count, null-composite count) and a `⚠` warning when sector history < 80 bars. The bad data still produces null composites, but at least Delphi tells the truth about why instead of silently rendering empty cells.
- **Real fix needed:** backfill TraderDB sector indices to ≥ 80 trading days (max RS horizon 60d + Z window 20d). Open sub-questions:
  - Owner: should this live in Hermes (the daily TMX importer) as a one-shot backfill subcommand, or in a separate `Tools/Backfill` console app? Hermes-side keeps the data path centralized; standalone keeps a one-off concern out of the daily flow.
  - Source: TMX Money historical query (matches Hermes's existing transport) vs Yahoo (`^GSPTSE` siblings — but TSX sector indices are not all on Yahoo). Probably TMX.
  - Cadence: one-shot backfill + rely on Hermes daily appends afterward, or recurring "fill any gap to N bars on startup" guard. The latter is more robust to symbol-add events.
  - Retention floor: do we always need ≥ 80 bars, or do we want a longer buffer (e.g., 120) so Hercules can backtest RS feature lift on the same data Delphi sees live?
  - After backfill: re-validate Z-window choice (currently `zWindow = 20`) against actual sector volatility — 20 may be too short for sector indices that move smoothly.
- **Tags:** data-pipeline, technical-indicators, decision-engine
- **Status:** parked — diagnostic visibility shipped. Data backfill is the next ADR-tracked piece of work (candidate ADR-0011).

### Relative Strength composite scale — raw return diffs vs Z-score normalization
- **Q:** The 2026-05-23 Delphi run showed `RScomp = +0.000` (3-decimal display) for the entire top-14 passing list. Investigation in [`RelativeStrengthCalculator.cs`](../../Core/RelativeStrength/RelativeStrengthCalculator.cs) confirmed the values are **real, not a bug**: the composite is a weighted blend of raw 10-day return differences (`0.5×svm10 + 0.3×svs10 + 0.2×secvm10`), which for typical TSX stocks lives in `±0.005` (±0.5%) on a 10-day window. The leaderboard now displays 4 decimals + `RS10d` (raw stock-vs-XIU 10d) for visibility, but this is a *cosmetic* fix.
- The underlying issue is **scale**: as a tiebreaker, RS composite has almost no resolving power when the top candidates cluster within ±0.05% of XIU. The `RelativeStrengthRow` already carries `RS_Z_StockVsMarket` / `RS_Z_StockVsSector` / `RS_Z_SectorVsMarket` (Z-score normalization — today's RS expressed in std-devs of its own 20-day history), which would map the same data into a `~±2` range with real separation.
- **Update 2026-05-23 — partially resolved by ADR-0010.** A new `CompositeScoreZ = 0.5·Z_svm + 0.3·Z_svs + 0.2·Z_secvm` is now computed, persisted (`dbo.RelativeStrengthFeatures.CompositeScoreZ`), and displayed in both Delphi console and diagnostic alongside the raw composite. v1 is additive only — no ranking change. The remaining open sub-questions:
  - Historical Hermes RS rows do not yet have `CompositeScoreZ` populated; a full RS backfill is required before any IC measurement is meaningful.
  - Should `CompositeScoreZ` eventually *replace* `CompositeScore` as the persisted primary RS scalar, or coexist permanently?
  - Are the same `0.5 / 0.3 / 0.2` weights right for the Z composite, or does volatility normalization change which horizon/dimension matters most?
- **Tags:** technical-indicators, decision-engine, math-statistics
- **Status:** partially resolved — additive Z composite shipped (ADR-0010). Promotion to ranking is tracked in the separate "DirectionEdge as primary ranking key" question below. Backfill + weight-tuning questions remain parked.

### DirectionEdge as primary ranking key — re-evaluation
- **Q:** Delphi currently ranks by `DirectionEdge = P(up10) − P(down10)` (LightGBM probabilities for the next-10-day binary up/down models). This was chosen because (a) it's directly aligned with the trade horizon (~10 days), (b) it fuses two independently-trained models, partially cancelling shared bias, (c) it's bounded `[-1, +1]` and behaves like a calibrated conviction score, and (d) BreakoutEnhanced is already used as a setup *filter*, so the ranker needed an orthogonal directional axis.
- Is this still the right primary key? Concerns surfaced 2026-05-23:
  - P(up) and P(down) are both LightGBM outputs on overlapping feature sets — "independent" is aspirational, not measured.
  - Top picks routinely cluster within `±0.02` of each other on Edge, so the ranking is effectively determined by `CompositeScore` anyway in practice.
  - Relative strength — arguably the single best-validated momentum predictor in the public literature — is currently *not* in the ranking key at all.
- Candidate alternatives to evaluate:
  - Keep Edge primary; add `RS_Z_StockVsMarket` as an explicit tiebreaker before `CompositeScore`.
  - Promote a blended key: `0.6×Edge + 0.4×RS_Z` (after Z-normalization is in place).
  - Two-stage rank: Edge > threshold filters, then RS_Z ranks within the survivors.
- Measurement needed before deciding: out-of-sample IC (information coefficient) of Edge vs RS_Z vs blend against next-10-day return on held-out 2024–2025 data.
- **Tags:** decision-engine, ml-models, math-statistics
- **Status:** parked — no ranking change until Z-score composite lands and IC comparison is run.

### Leveraged/inverse ETP classification
- **Q:** ADR-0009 introduced `dbo.Symbols.IsLeveragedOrInverseEtp` as the authoritative gate that excludes products like NRGU/BetaPro/MegaLong/SavvyLong/LFG-Daily-2× from Delphi's ranking universe. The flag was set by a one-shot curated UPDATE against the current 62 known leveraged/inverse rows. The runtime `IsLeveragedOrInverseByName` guard in `Delphi/Program.cs` is a defense-in-depth net, not a primary classifier — it relies on a hand-maintained keyword marker list and is therefore brittle to new naming conventions.
- How do we keep the flag accurate as Hermes imports new symbols over time? Options:
  - Manual review queue (Hermes flags any new `SecurityType = 'Stock'` row whose `ShortName` matches the marker list and writes it to a `SymbolsPendingClassification` table for human approval).
  - Hermes-side classifier (run `IsLeveragedOrInverseByName` at import and write `IsLeveragedOrInverseEtp = 1` directly when it fires; rely on a periodic audit to catch misses).
  - Periodic audit query (cron-style: re-run the marker scan weekly against `dbo.Symbols WHERE IsLeveragedOrInverseEtp = 0` and surface unflagged candidates in a report).
- Residual risk to characterize: a brand-new BetaPro/Horizons product imported with a never-before-seen naming convention will pass both gates silently. What blast-radius bound should we accept (e.g., "at most 1 day in the leaderboard before the periodic audit flags it") before we commit to a heavier solution?
- **Tags:** decision-engine, data-pipeline, risk-management
- **Status:** parked — current curated flagging covers all known cases. Revisit when Hermes next imports a batch of new symbols or when an unflagged leveraged ETP appears in `TopPicks`.

### Granville cross-family overlap audit (#04 Plurality / #10 Leadership / #14 Most Active)
- **Q:** On 2026-06-?? these three indicators all fired `StrongBullish` simultaneously. Hypothesis: they report different *facets* of the same underlying evidence ("breadth was positive AND XIU rose"), which is acceptable ensemble behaviour. But before we tune Granville point weights we should deliberately audit cross-family correlation:
  - Compute pairwise daily co-firing rates across all 28 active Granville indicators over the available history.
  - Flag any pair with co-firing rate ≥ 0.85 and identical sign — these are candidates for either (a) deduplication via reduced point weight, or (b) explicit redundancy acknowledgement in the ensemble.
  - Specifically inspect: #04 Plurality vs #10 Leadership vs #14 Most Active (the trigger for this question); #15/#16 Weighting vs #04 Plurality; #17/#18/#19/#20 within Genuity.
- **Tags:** technical-indicators, decision-engine, math-statistics
- **Status:** parked — no indicator code changes until audit run. Revisit before next Granville point-weight tuning pass.

### Genuity #19 — fractional GranvillePoints follow-up
- **Q:** ADR-0008 introduced a ±5% tolerance buffer on the magnitude-ratio band by abstaining (Neutral/0) inside the buffer, because `GranvilleResult.GranvillePoints` is an `int` in v1 and "half-point damping" isn't representable. Should we promote `GranvillePoints` from `int` to `double`/`decimal` system-wide so borderline cases can contribute fractional conviction (e.g. ±0.5) rather than abstain entirely? Scope: every `IGranvilleIndicatorGroup` implementation, the composite adjustment math, the `GranvilleIndicatorLog` schema, and any DB-backed analytics that assume integer points.
- **Tags:** technical-indicators, decision-engine, math-statistics, architecture

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

### Granville Dullness (#21, #22) — deferred per ADR-0005
- **Q:** When (not if) we backfill XIU to 2001, rerun `dullness-calibrate`
  and check whether #21 / #22 hit-rates clear 55% at h ∈ {5, 10} with
  n ≥ 25 per bucket. If so, supersede ADR-0005 with an implementation ADR.
- **Q:** Is there a different universe (sector indices, individual stocks,
  or a non-TSX benchmark) where post-decline dull days are frequent enough
  to make #22 testable *now*, without waiting for a TSX backfill?
- **Q:** The positive mean forward return after #21 fires suggests Dullness
  may act as a *continuation* signal in a momentum regime. Should we
  re-derive Dullness as a continuation indicator under a new ADR rather
  than revive Granville's literal bearish framing?

### Delphi opening confirmation and swing exits (ADR-0023)

- **Q:** Which separately timestamped source should support the still-experimental opening-confirmation comparison, especially bid/ask spreads and opening gaps that ADR-0028's delayed OHLCV monitor does not establish?
- **Q:** Does a 5-, 15-, or 30-minute observation checkpoint add the best net value after missed winners, avoided losses, spreads, and slippage?
- **Q:** Does the user-selected TMX intraday method return consistently aligned 15-minute bars with enough retention to evaluate ADR-0028, and how should its delay and website-use limitations constrain raw-data retention?
- **Tags:** architecture, data-sources, decision-engine, market-microstructure, risk-management
- **Status:** partially resolved — ADR-0028 accepts 15-minute delayed management and the first exit-policy challenger. Opening confirmation and the verified intraday storage/retention contract remain open.

## Resolved

- **Switch Continuations lens ranking from raw `RScomp` to Z-scored `CompZ`**
  — logged 2026-05-24. The user has agreed this should happen "soon" (ADR-0014
  deferred it as option (d)). Before switching: confirm `CompZ` distribution is
  healthy post-backfill (min/max sector bars ≥ 80 so Z-window is valid); measure
  whether `CompZ` produces meaningfully different rank-orderings than raw `RScomp`
  over a 2–3 week paper-trade window; if yes, cut ADR updating ADR-0014.
  - **Prerequisite:** full `RelativeStrengthFeatures.CompositeScoreZ` backfill
    (Hermes historical RS rows need to be re-computed with Z columns populated).
  - **Tags:** decision-engine, technical-indicators, math-statistics
  - **Status:** deferred — needs RS backfill + outcome measurement first.

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
