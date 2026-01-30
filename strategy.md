# Trading Strategy (Current)

## Strategy: Aggressive Single-Position Rotation
- Allocate most/all available capital into the single top-ranked opportunity.
- Intended holding window: ~1–2 weeks.
- TSX-only for now.

## Ranking Logic
Primary selector:
1. Expected return (profit regression model) descending
2. Confirmation confidence (3-way classifier) descending

Pattern models provide contextual signals but do not replace profit ranking.

## Rotation Rule (Reduce Churn)
- Do not rotate too frequently.
- Switch only if:
  - new candidate’s expected return exceeds the current holding’s expected return by at least:
    - `RotationMinExpectedReturnDelta` (configurable)

## Risk Rules (Capital Preservation)
- Warning: drawdown reaches -5% from entry → alert / tighter monitoring
- Stop-loss: drawdown reaches -10% from entry → sell (hard exit)
- Stop-loss overrides model recommendations.