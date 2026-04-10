# Design Rules (TraderVI)

> This file is referenced by `.github/copilot-instructions.md`.
> Rules here are authoritative for code generation.

## Rule-Based vs ML-Based Components

### Rule-Based (non-ML) — DO NOT feed into feature vectors

| Component | Purpose | Where |
|-----------|---------|-------|
| **Advance-Decline Line** | Market breadth / regime gate | `AdvanceDeclineCalculator` → `BreadthScore` |
| **XIU Regime Filter** | Benchmark uptrend / 20d return gate | `TradeDecisionEngine.ComputeRegime` |
| **Stop-Loss (-10%)** | Hard exit override | `TradeDecisionEngine` / Sentinel (planned) |
| **Drawdown Warning (-5%)** | Alert / tighter monitoring | Sentinel (planned) |
| **Down-Probability Veto** | Block longs when P(down) ≥ 20% | `AggregateAllSignals` |
| **Rotation Threshold** | Prevent churn; require sufficient edge delta | `PositionSizer` / future Sentinel |
| **Pattern Confirmation** | Light check: patternSells ≤ patternBuys | `AggregateAllSignals` |

These components act as **gates or modifiers** on the ML-driven ranking. They are never encoded as ML features unless explicitly revisited.

### ML-Based — Trained models producing probability scores

| Model (TaskType) | Kind | AUC | Status | Role |
|-------------------|------|-----|--------|------|
| `BreakoutEnhanced` | Binary | 0.81 | ✅ Active | Primary setup filter |
| `BinaryUp10` | Binary | 0.70 | ✅ Active | P(up ≥ +4% in 10d) — direction |
| `BinaryDown10` | Binary | — | ✅ Active | P(down ≤ -4% in 10d) — veto signal |
| `VolExpansionRelative10` | Binary | 0.66 | ✅ Active | Vol expansion confirmation |
| `RelStrengthCont10_2pct` | Binary | 0.65 | ✅ Active | Cross-sectional momentum |
| `Trend10`, `Trend30`, `MaCrossover` | Pattern | — | ✅ Active | Light confirmation only |

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
| `AtrVolatilityBreakoutFeatureBuilder` | Price/vol sequence + ATR + breakout dist + momentum + SMA | BinaryUp10, BinaryDown10, VolExpansion, RelStrength base |
| `EnhancedFeatureBuilder` | AtrVolBreakout + TrendMomentum (26 extra) | BreakoutEnhanced |
| `MarketContextFeatureBuilder` | XIU momentum/vol/beta/relative strength (20 features) | RelStrengthCont10_2pct |
| `DirectionFeatureBuilder` | Direction-specific features | DirUp5 variants (all disabled) |
| `TrendMomentumFeatureBuilder` | RSI, MACD, Bollinger, OBV, relative strength vs XIU | Part of EnhancedFeatureBuilder |
| `PriceVolumeFeatureBuilder` | Simple price/volume sequence | Pattern models |
| `PriceWithMaFeatureBuilder` | Price + MA crossover features | MaCrossover pattern |

## Decision Flow (order matters)

1. **Regime gate**: XIU uptrend OR 20d positive — else Hold
2. **Breadth gate**: A/D BreadthScore — warn/block if strongly negative
3. **Down-probability veto**: P(down) ≥ 20% → Hold
4. **Setup filter**: BreakoutEnhanced ≥ 30%
5. **Direction filter**: DirectionEdge (P(up) − P(down)) ≥ 5%
6. **Buy conditions**: composite thresholds + pattern confirmation
7. **Ranking**: DirectionEdge → Composite → topN
8. **Sizing**: SinglePositionAllIn with reserve cash

## Thresholds

All decision thresholds are **initial defaults** intended to be tuned based on Hercules training outputs (AUC, optimal threshold, decile lift). They are not permanent constants.

| Threshold | Current Value | Location |
|-----------|---------------|----------|
| `minCompositeScore` | 0.35 | `AggregateAllSignals` |
| `strongBuyThreshold` | 0.50 | `AggregateAllSignals` |
| `minBreakoutProb` | 0.30 | `AggregateAllSignals` |
| `minUpProb` | 0.25 | `AggregateAllSignals` |
| `maxDownProb` | 0.20 | `AggregateAllSignals` |
| `minDirectionEdge` | 0.05 | `AggregateAllSignals` |
| `stopLoss` | -10% | Risk rules (not yet wired) |
| `warningDrawdown` | -5% | Risk rules (not yet wired) |
| `breadthVetoThreshold` | -0.3 (proposed) | `TradeDecisionEngine` |

## Key Design Decisions

1. **A/D Line is rule-based, not an ML feature.** It serves as a market breadth regime indicator. Do not add it to feature builders unless explicitly revisited.
2. **Ensemble of diverse signals, not redundant models.** Each active model answers a different question (setup? direction? vol regime? relative strength?).
3. **Stop-loss overrides everything.** No ML prediction can override the -10% hard exit.
4. **Regression is a ranking hint only.** Do not use regression output as a primary buy/sell signal.
5. **Pattern models are confirmation only.** They cannot generate Buy on their own.