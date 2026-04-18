# Trading Strategy (Current)

- Glossary: `docs/glossary.md`
- Models: `docs/models.md`

## Strategy: Aggressive Single-Position Rotation
- Allocate most/all available capital into the single top-ranked opportunity.
- Intended holding window: ~1–2 weeks.
- TSX-only for now.

## Decision Inputs ("Hints")
The system uses multiple model outputs ("hints") when selecting the best single trade candidate:

Primary:
- Direction (Buy/Hold/Sell) + confidence over the target horizon
- DirectionEdge = P(up) - P(down) — primary ranking metric

Supporting:
- Breakout probability (event model) — setup filter
- Volatility expansion probability (event model) — confirmation
- Relative Strength composite (live-computed) — ranking tiebreaker
- Pattern models (context/confirmation)
- Granville composite adjustment (market-level modifier)

## Ranking Order
1. Direction = Buy (always above Hold/Sell)
2. DirectionEdge (descending)
3. RS Composite Score (descending)
4. Composite Score (descending)

## Composite Score Formula

Composite = 0.40×Breakout + 0.25×Up + 0.15×VolExp + 0.10×RelStr + 0.10×ensemble_avg + Granville_adjustment  (∈ [-0.10, +0.10])

## Rotation Rule (Reduce Churn)
- Do not rotate too frequently.
- Switch only if:
  - the new candidate's overall score/hints are sufficiently better than the current holding
  - and (optionally) its expected return hint exceeds the current holding by at least:
    - `RotationMinExpectedReturnDelta` (configurable)

## Risk Rules (Capital Preservation)
- Warning: drawdown reaches -5% from entry → alert / tighter monitoring
- Stop-loss: drawdown reaches -10% from entry → sell (hard exit)
- Stop-loss overrides model recommendations.

## Gate Pipeline (sequential)
Each gate can block a trade. Order matters:

1. **RegimeGate** — XIU/SPY uptrend check
2. **BreadthGate** — A/D line breadth score threshold
3. **GranvilleGate** — Granville forecast gating (if strongly bearish)
4. **BreakoutGate** — BreakoutEnhanced probability minimum
5. **DirectionGate** — DirectionEdge minimum threshold
6. **DownVetoGate** — P(down) maximum threshold
7. **CompositeGate** — Composite score minimum
8. **PatternGate** — Pattern model confirmation (light)

Future (deferred):
- **RSGate** — Block trades with extreme negative RS Z-scores