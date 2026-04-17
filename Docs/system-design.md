# TraderVI System Design Reference

This document describes the current state of the TraderVI trading system — what's implemented, what was tried and rejected, and where things are headed.

For Copilot-enforceable rules, see `docs/design-rules.md`.

## Philosophy

TraderVI is a **short-term aggressive momentum rotation** system on the TSX. The core idea:

- Use **multiple diverse ML signals** (not one model) to build conviction
- Layer **rule-based safety gates** on top to preserve capital
- Rotate aggressively into the single best opportunity
- Never hold losers — stop-loss overrides all models

## What Is Rule-Based vs What Is ML-Based

### Rule-Based Components

These are hand-coded logic gates that override or modulate the ML signals:

| Gate | What It Does | Implementation |
|------|-------------|----------------|
| **XIU Regime Filter** | Blocks longs when XIU MA50 < MA200 AND 20d return < 0 | `TradeDecisionEngine.ComputeRegime()` |
| **A/D Line Breadth** | Blocks/warns longs when market breadth is deteriorating | `AdvanceDeclineCalculator.BreadthScore()` |
| **Down-Probability Veto** | Blocks longs when P(down ≤ -4%) ≥ 20% | `AggregateAllSignals()` |
| **Stop-Loss** | Hard exit at -10% drawdown from entry (overrides all) | Planned in Sentinel |
| **Drawdown Warning** | Alert at -5% drawdown | Planned in Sentinel |
| **Rotation Threshold** | Only switch positions if new pick is sufficiently better | Strategy config |
| **Pattern Confirmation** | Light bearish veto if pattern models disagree | `AggregateAllSignals()` |

**Key decision**: The A/D Line is maintained as a **rule-based regime indicator**, not a direct ML feature. This was explicitly decided because breadth is best used as a binary/graded gate, not a noisy input to gradient boosting.

### ML Components

These are trained models that produce probability scores:

**Active Models (5 profit + 3 pattern):**

1. **BreakoutEnhanced** (AUC 0.81) — "Is a breakout above the 20-day high happening?" — Primary **setup filter**. If this doesn't fire, we don't trade.

2. **BinaryUp10** (AUC 0.70) — "Will return ≥ +4% in the next 10 days?" — Direction signal.

3. **BinaryDown10** — "Will return ≤ -4% in the next 10 days?" — **Veto signal**. High P(down) blocks longs.

4. **VolExpansionRelative10** (AUC 0.66) — "Will volatility expand significantly?" — Confirmation that a big move is expected.

5. **RelStrengthCont10_2pct** (AUC 0.65) — "Will this stock outperform XIU by ≥ 2% in 10 days?" — Cross-sectional momentum.

6. **Trend10 / Trend30 / MaCrossover** — Pattern presence models. Used only for light confirmation, never as primary buy signals.

**How they combine:**
- `DirectionEdge = P(up) - P(down)` — primary ranking metric
- `Composite = 0.40×Breakout + 0.25×Up + 0.15×VolExp + 0.10×RelStr + 0.10×ensemble avg`
- Breakout is the "setup filter" (gate), direction edge is the "conviction meter"

## Feature Builders

Each ML model uses a **feature builder** to convert a window of daily bars into a float array:

| Builder | Key Features | Models Using It |
|---------|-------------|-----------------|
| **AtrVolatilityBreakout** | Normalized price/volume sequences, ATR%, breakout distance, momentum, SMA distance | BinaryUp10, BinaryDown10, VolExpansion |
| **Enhanced** | Above + RSI, MACD, Bollinger bands, OBV, relative strength vs XIU | BreakoutEnhanced |
| **MarketContext** | XIU momentum, volatility regime, beta, relative perf at multiple horizons, info ratio | RelStrengthCont10_2pct |
| **Direction** | Direction-specific features | All DirUp5 variants (currently disabled) |
| **TrendMomentum** | RSI, MACD signal/histogram, Bollinger %b/width, OBV trend, relative strength | Nested inside Enhanced |

## Tried and Rejected

| Experiment | What We Tried | Why It Failed |
|-----------|---------------|---------------|
| **Regression (ExpectedReturn)** | Predict exact 5d/10d return | R² negative — model worse than predicting the mean |
| **3-Way Classification** | Predict Buy/Hold/Sell | 40% accuracy — couldn't separate three classes |
| **DirUp5 (direction > 0)** | Predict if 5d return is positive | AUC 0.54 — essentially random |
| **Banded Direction** | Only label "meaningful moves" (> 0.5 ATR) | AUC ~0.52 — still random |
| **Setup-Conditional Direction** | Train direction only on breakout bars | AUC 0.52–0.53 — insufficient signal |
| **Meta-Labeling (BreakoutMeta)** | Predict if breakout leads to profit | AUC 0.52 — no discriminating power |
| **Volume-Conditional Meta** | Same but require volume surge | AUC 0.52 |
| **Risk-Adjusted Up/Down** | Label by return/ATR ratio | AUC 0.52–0.53 |
| **Triple Barrier** | Asymmetric profit/stop barriers | AUC 0.51 |
| **MarketContext features on BinaryUp** | Add XIU features to up-probability model | AUC 0.64 (worse than base 0.70) |
| **Enhanced features on BinaryUp** | Use richer feature set | No improvement over AtrVolatilityBreakout |
| **RelStrength 1% threshold** | Lower outperformance bar | AUC 0.60 (2% threshold is 0.65) |

**Lessons learned:**
- Short-horizon direction (5d) is very hard to predict on TSX
- Adding more features doesn't always help — simpler feature sets often win
- Setup-conditional training reduces sample count too much for TSX universe size
- Meta-labeling needs more signal in the base model to work
- Tail-event models (≥ +4%, ≤ -4%) work better than direction (> 0) models

## Current Decision Flow
XIU Regime Filter (rule-based) ↓ pass A/D Breadth Gate (rule-based)        ← being wired in ↓ pass Down-Probability Veto (ML P(down)) ↓ pass Setup Filter (ML BreakoutEnhanced ≥ 30%) ↓ pass Direction Filter (ML DirectionEdge ≥ 5%) ↓ pass Buy Conditions (composite thresholds + pattern check) ↓ pass Rank by DirectionEdge → Composite ↓ Size: SinglePositionAllIn


## Future Direction

### Near-Term (Planned)
- **Wire A/D BreadthScore** into `TradeDecisionEngine` as a regime gate
- **Sentinel**: Intraday monitoring with stop-loss execution and rotation triggers
- **Backtest harness**: Walk-forward simulation using historical picks vs actual outcomes

### Medium-Term
- **TraderVI execution**: Automated order placement via Wealthsimple API
- **Strategy versioning**: Compare strategy versions via `[dbo].[StrategyVersions]`
- **Threshold tuning**: Use Hercules AUC/lift outputs to set optimal thresholds per model

### Exploratory (Not Committed)
- Sector rotation signals (group by TSX sector, rotate into strongest)
- Intraday features from TMX time series (requires Sentinel)
- Earnings/event calendar integration
- Revisit A/D line as ML feature (only if rule-based gate proves insufficient)
- Ensemble stacking (use model outputs as features for a meta-model)

## Data Flow

| Program | Runs | Reads | Writes |
|---------|------|-------|--------|
| **Hermes** | Daily (post-close) | TMX API | `[DailyBars]`, `[AdvanceDeclineLine]`, `[SectorIndices]`, `[StockSectorMap]` |
| **Hercules** | Weekly / on-demand | `[DailyBars]`, `ProfitModelRegistry` | `.zip` models, `[ModelRegistry]` |
| **Delphi** | Daily (pre-market) | `[DailyBars]`, `[ModelRegistry]`, `[AdvanceDeclineLine]`, `[SectorIndices]`, `[StockSectorMap]` | `[DailyPick]`, console output |
| **Sentinel** | Continuous (planned) | `[DailyBars]`, `[DailyPick]`, live quotes | Alerts, `[TradeLog]` |
| **TraderVI** | Event-driven (planned) | `[DailyPick]`, Sentinel signals | Wealthsimple API orders |

## Granville Market Timing Layer

TraderVI is incrementally implementing Granville's day-to-day market indicators as a **rule-based**
overlay on top of the ML ranking stack.

### Active Granville groups

- **Plurality (#1–#4)** — based on advance/decline breadth vs `XIU`
- **Disparity (#5–#6)** — TSX-adapted real-economy divergence signal

### Disparity adaptation for TSX

Granville's original Disparity concept used **Dow Transports vs Dow Industrials**.
On the TSX, that exact structure does not exist, so TraderVI uses a modernized equivalent:

- **Cyclical basket**: `Energy + Industrials + Materials`
- **Benchmark**: `XIU`
- **Timeframes**:
  - 1-day percent change
  - 5-day rolling return

Interpretation:
- cyclical basket weaker than `XIU` → short-term bearish divergence
- cyclical basket stronger than `XIU` → short-term bullish confirmation

`XIU` is intentionally used for now because it is already available in the breadth/Granville pipeline.
A future revision could swap this to the raw TSX 60 index symbol if needed.

## TSX Sector Map

TraderVI now maintains its own stock → sector-index mapping rather than depending on TMX to expose
direct index membership.

### How it works

For each active TSX stock:
1. Hermes calls `TmxClient.GetQuoteDetailAsync(symbol)`
2. Reads TMX `sector` and `industry`
3. Normalizes the sector using `TsxSectorMap`
4. Stores:
   - stock symbol
   - sector
   - industry
   - mapped TSX sector index symbol
   - last refresh timestamp

### Why this exists

This gives TraderVI a stable internal lookup it controls and enables:
- sector-relative ranking
- sector concentration/risk checks
- future sector leadership analysis
- resilience if TMX naming/schema changes

The stock-sector map is refreshed by Hermes only when stale (currently **7 days**), since sector
metadata changes far less often than daily prices.