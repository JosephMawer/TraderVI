# Design Rules (TraderVI)

> This file is referenced by `.github/copilot-instructions.md`.
> Rules here are authoritative for code generation.

## Rule-Based vs ML-Based Components

### Rule-Based (non-ML) — DO NOT feed into feature vectors

| Component | Purpose | Where |
|-----------|---------|-------|
| **Advance-Decline Line** | Market breadth / regime gate | `AdvanceDeclineCalculator` → `BreadthScore` |
| **XIU Regime Filter** | TSX benchmark uptrend / 20d return gate | `TradeDecisionEngine.ComputeRegime` |
| **SPY Regime Filter** | S&P 500 cross-market confirmation | `TradeDecisionEngine.ComputeRegime` |
| **Stop-Loss (-10%)** | Hard exit override | `TradeDecisionEngine` / Sentinel (planned) |
| **Drawdown Warning (-5%)** | Alert / tighter monitoring | Sentinel (planned) |
| **Down-Probability Veto** | Block longs when P(down) ≥ 20% | `AggregateAllSignals` |
| **Down-Probability Penalty** | P(down) reduces composite score continuously | `GetCompositeScoreWithBreakdown` |
| **Rotation Threshold** | Prevent churn; require sufficient edge delta | `PositionSizer` / future Sentinel |
| **Pattern Confirmation** | Require at least 1 pattern Buy when patterns exist | `AggregateAllSignals` |

These components act as **gates or modifiers** on the ML-driven ranking. They are never encoded as ML features unless explicitly revisited.

### ML-Based — Trained models producing probability scores

| Model (TaskType) | Kind | AUC | Status | Role |
|-------------------|------|-----|--------|------|
| `BreakoutEnhanced` | Binary | 0.81 | ✅ Active | Primary setup filter |
| `BinaryUp10` | Binary | 0.70 | ✅ Active | P(up ≥ +4% in 10d) — direction |
| `BinaryDown10` | Binary | — | ✅ Active | P(down ≤ -4% in 10d) — veto + penalty signal |
| `VolExpansionRelative10` | Binary | 0.66 | ✅ Active | Vol expansion confirmation |
| `RelStrengthCont10_2pct` | Binary | 0.65 | ✅ Active | Cross-sectional momentum |
| `Trend10`, `Trend30`, `MaCrossover` | Pattern | — | ✅ Active | Confirmation only (require ≥1 Buy) |

### Rejected / Disabled Models (do not re-enable without new evidence)

| Model | AUC | Reason |
|-------|-----|--------|
| `DirUp5` | 0.54 | Random — no signal |
| `BandedDirUp5` | — | No improvement |
| `SetupDirUp5` / `SetupDirDown5` | 0.52–0.53 | Random |
| `BreakoutMeta15` / `VolBreakoutMeta15` | 0.52 | No signal |
| `ExpectedReturn10` | R² < 0 | Regression failed |
| `Direction10` (3-way) | 40% acc | Worse than random |
| `BreakoutPriorHigh10` | — | Superseded by BreakoutEnhanced |
| `BreakoutAtr10` | 0.55 | Marginal |
| `VolatilityExpansion10` | — | Old labeler, replaced |
| `BinaryUp10Market` | 0.64 | No improvement over base |
| `BinaryUp10Enhanced` | — | No improvement |
| `RiskAdjustedUp10` / `RiskAdjustedDown10` | 0.52–0.53 | No signal |
| `TripleBarrierUp10` / `TripleBarrierDown10` | 0.51 | No signal |
| `RelStrengthCont10_1pct` | 0.60 | 2% threshold version is better |

## Feature Builders (ML inputs)

| Builder | Features | Used By |
|---------|----------|---------|
| `AtrVolatilityBreakoutFeatureBuilder` | Price/vol sequence + ATR + breakout dist + momentum + SMA | BinaryUp10, BinaryDown10, VolExpansion base |
| `EnhancedFeatureBuilder` | AtrVolBreakout + TrendMomentum (26 extra) | BreakoutEnhanced |
| `MarketContextFeatureBuilder` | XIU momentum/vol/beta/relative strength (20 features) | RelStrengthCont10_2pct |
| `TrendMomentumFeatureBuilder` | RSI, MACD, Bollinger, OBV, relative strength vs XIU | Part of EnhancedFeatureBuilder |
| `PriceVolumeFeatureBuilder` | Simple price/volume sequence | Pattern models |
| `PriceWithMaFeatureBuilder` | Price + MA crossover features | MaCrossover pattern |

## Decision Flow (order matters)

1. **Regime gate**: XIU OR SPY uptrend — block only if BOTH bearish
2. **Breadth gate**: A/D BreadthScore ≤ -0.3 → Hold
3. **Down-probability veto**: P(down) ≥ 20% → Hold
4. **Setup filter**: BreakoutEnhanced ≥ 30%
5. **Direction filter**: DirectionEdge (P(up) − P(down)) ≥ 5%
6. **Buy conditions**: composite thresholds + pattern confirmation (≥1 pattern Buy)
7. **Ranking**: DirectionEdge → Composite → topN
8. **Sizing**: SinglePositionAllIn with reserve cash

## Composite Formula