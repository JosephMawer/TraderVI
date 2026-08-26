# Trading Strategy (Current)

- Glossary: `docs/glossary.md`
- Models: `docs/models.md`

## Strategy: Aggressive Single-Position Rotation
- Allocate most/all available capital into the single top-ranked opportunity.
- Primary paper-policy direction: Delphi selects from completed daily data, then a delayed 15-minute advisory monitor manages the open swing throughout each session (ADR-0028).
- A same-day exit is allowed. Most positions should close within five completed sessions; a profitable position may trail through no more than ten sessions under the version-1 challenger defaults.
- Intraday management of an open swing is part of this policy. A separate strategy that enters specifically to trade an intraday wave remains experimental and must not be blended into the swing results.
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
- Current operational/ghost baseline: drawdown reaches -10% from entry → sell; model recommendations do not override it.
- ADR-0028 paper challenger: the -10% alert may be deferred only by a fresh, published, very strong Breakout signal; -20% is never bypassed.
- Delayed data means an alert cannot guarantee either threshold price. This challenger remains advisory and unpromoted.

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
