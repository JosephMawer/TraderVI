# Trading Strategy (Current)

- Glossary: `docs/glossary.md`
- Models: `docs/models.md`

## Strategy: Aggressive Single-Position Rotation
- Allocate most/all available capital into the single top-ranked opportunity.
- Intended holding window: ~1–2 weeks.
- TSX-only for now.

## Decision Inputs (“Hints”)
The system uses multiple model outputs (“hints”) when selecting the best single trade candidate:

Primary:
- Direction (Buy/Hold/Sell) + confidence over the target horizon

Supporting:
- Breakout probability (event model)
- Volatility expansion probability (event model)
- Expected return regression (treated as a ranking hint, not a single truth source)
- Pattern models (context/confirmation)

## Rotation Rule (Reduce Churn)
- Do not rotate too frequently.
- Switch only if:
  - the new candidate’s overall score/hints are sufficiently better than the current holding
  - and (optionally) its expected return hint exceeds the current holding by at least:
    - `RotationMinExpectedReturnDelta` (configurable)

## Risk Rules (Capital Preservation)
- Warning: drawdown reaches -5% from entry → alert / tighter monitoring
- Stop-loss: drawdown reaches -10% from entry → sell (hard exit)
- Stop-loss overrides model recommendations.