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

### Hybrid Components

| Component | Rule-Based | ML | Status |
|-----------|-----------|-----|--------|
| **Relative Strength** | Ranking signal + future gating | LightGBM feature columns | Ranking active; ML planned after backfill |

### ML Components

These are trained models that produce probability scores:

**Active Models (5 profit):**

1. **BreakoutEnhanced** (AUC 0.81) — "Is a breakout above the 20-day high happening?" — Primary **setup filter**. If this doesn't fire, we don't trade.

2. **BinaryUp10** (AUC 0.70) — "Will return ≥ +4% in the next 10 days?" — Direction signal.

3. **BinaryDown10** — "Will return ≤ -4% in the next 10 days?" — **Veto signal**. High P(down) blocks longs.

4. **VolExpansionRelative10** (AUC 0.66) — "Will volatility expand significantly?" — Confirmation that a big move is expected.

5. **RelStrengthCont10_2pct** (AUC 0.65) — "Will this stock outperform XIU by ≥ 2% in 10 days?" — Cross-sectional momentum.

6. **Trend10 / Trend30 / MaCrossover** — Pattern presence models. Used only for light confirmation, never as primary buy signals.

**How they combine:**
- `DirectionEdge = P(up) - P(down)` — primary ranking metric
- `Composite = 0.40×Breakout + 0.25×Up + 0.15×VolExp + 0.10×RelStr + 0.10×ensemble avg + Granville adj`
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
XIU Regime Filter (rule-based) ↓ pass A/D Breadth Gate (rule-based) ↓ pass Granville Composite (rule-based, ±0.10 adjustment) ↓ always (modifies score) Down-Probability Veto (ML P(down)) ↓ pass Setup Filter (ML BreakoutEnhanced ≥ 30%) ↓ pass Direction Filter (ML DirectionEdge ≥ 5%) ↓ pass Buy Conditions (composite thresholds + pattern check) ↓ pass Rank by DirectionEdge → RS Composite → Composite ↓ Size: SinglePositionAllIn

## Granville Market Timing Layer

TraderVI is incrementally implementing Granville's day-to-day market indicators as a **rule-based**
overlay on top of the ML ranking stack.

### Active Granville groups

- **Plurality (#1–#4)** — based on advance/decline breadth vs `XIU`
- **Disparity (#5–#6)** — TSX-adapted real-economy divergence signal
- **Leadership (#7–#10)** — market leadership quality vs directional state

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

### Leadership adaptation for TSX

Granville's Leadership concept asks: **are the most influential stocks still outperforming, or is the
market advance (or decline) being driven by a narrowing base?** In cap-weighted indexes like the TSX,
a few large names can sustain the index while internal participation deteriorates.

TraderVI defines leadership through three complementary layers:

**Layer 1 — New-High/New-Low Breadth** (`NHNL_10`):
Are actual winners still being produced? Computed from stored OHLCV data using a 252-trading-day
lookback via `NewHighLowCalculator`. A healthy advance shows expanding or stable new highs with
contained new lows. Smoothed with EMA-10 to reduce daily noise.

**Layer 2 — Active-Stock Breadth** (`ActiveBreadth_10`):
Where is the urgent capital going? Top-50 stocks by **dollar volume** (price × shares, a better
proxy for capital flow than raw share count) are fetched daily via `GetMarketMoversAsync("dollarvolume")`.
The ratio of advancers to decliners in this basket is smoothed with EMA-10.

**Layer 3 — Large-Cap Relative Strength** (`LargeCapRS_20`):
Are the biggest names outperforming or underperforming the broad market? Measured as the 20-day
return of XIU (TSX 60 proxy) minus the 20-day return of `^TXCE` (S&P/TSX Composite Equal Weight
Index). Strong leaders are fine; **isolated** leaders (TSX 60 up, equal-weight down) are a warning.

**Important**: `^TXCE` is the Composite Equal Weight Index, not `^TXEW` (which is TSX 60 Equal Weight).
Both are defined in `TsxBenchmarkSymbols.cs`.

**State determination** (upswing/downswing):
These are not single up/down days. They represent directional legs defined by the composite
leadership series:
- **Upswing**: ≥ 2 of 3 series rising, none deeply negative (threshold: −0.10)
- **Downswing**: ≥ 2 of 3 series falling, NHNL_10 < 0
- **Indeterminate**: mixed or insufficient data

**Quality determination** (improving/deteriorating):
The 3-point slope consistency of NHNL_10 and ActiveBreadth_10 determines whether leadership
quality is trending up or down.

**Indicator signals**:

| # | Condition | Signal | Interpretation |
|---|-----------|--------|----------------|
| **7** | Quality deteriorates + upswing | Bearish (−1) | Near-term decline likely |
| **8** | Quality deteriorates + downswing | Bullish (+2) | Near-term advance likely (exhaustion) |
| **9** | Quality improves + downswing | StrongBearish (−1) | Decline likely to continue |
| **10** | Quality improves + upswing | StrongBullish (+2) | Advance likely to continue |

**Data pipeline**:
- Hermes stores daily `LeadershipSnapshot` to `[dbo].[LeadershipData]`
- Delphi loads 50-day history → `LeadershipCalculator` → `LeadershipIndicators`
- Graceful degradation: neutral signal if < 12 days of history available

### Composite point range

The `GranvilleComposite` normalizes net Granville points to a `CompositeAdjustment` in
[−0.10, +0.10]. The theoretical raw point range across all active groups:

| Group | Max Bullish | Max Bearish |
|-------|------------|------------|
| Plurality (#1–#4) | +4 | −2 |
| Disparity (#5–#6) | +2 | −2 |
| Leadership (#7–#10) | +4 | −2 |
| **Total** | **+10** | **−6** |

## Relative Strength Layer

### Design

RS measures how a stock performs relative to its sector and the market across multiple horizons:

| Feature | Formula |
|---------|---------|
| `RS_StockVsSector_{h}d` | `Return(stock, h) - Return(sector_index, h)` |
| `RS_StockVsMarket_{h}d` | `Return(stock, h) - Return(XIU, h)` |
| `RS_SectorVsMarket_{h}d` | `Return(sector_index, h) - Return(XIU, h)` |
| `RS_Z_*` | `(RS_today - mean(RS_20d)) / std(RS_20d)` |

Horizons: 5d, 10d, 20d, 60d.

### Composite Score
`0.5 × RS_StockVsMarket_10d + 0.3 × RS_StockVsSector_10d + 0.2 × RS_SectorVsMarket_10d`

Weights are initial defaults, intended to be tuned via Hercules feature importance.

### Lifecycle
- **Delphi**: computes live from in-memory price arrays (does not read DB)
- **Hermes**: backfills historical to `[dbo].[RelativeStrengthFeatures]` (planned — stock-vs-XIU is fully backfillable from 2020; stock-vs-sector only from when sector collection started)
- **Hercules**: reads from DB, includes RS columns as LightGBM features (planned)

### Sector data limitation
`[SectorIndices]` only has data from when Hermes started collecting. For historical backfill:
- Stock vs XIU features are available from 2020 (full DailyBars history)
- Stock vs Sector features are only available from sector collection start date
- Delphi gracefully degrades: if no sector data, falls back to XIU (making RS_StockVsSector ≈ 0)

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

### Sector symbols

| Symbol | Sector | Confirmed |
|--------|--------|-----------|
| `^TTEN` | Energy | ✅ TMX Money |
| `^TTFS` | Financials | ✅ |
| `^TTHC` | Health Care | ✅ TMX Money |
| `^TTIN` | Industrials | ✅ |
| `^TTTK` | Technology | ✅ |
| `^TTUT` | Utilities | ✅ |
| `^TTMT` | Materials | ✅ |
| `^TTCD` | Consumer Discretionary | ⚠️ Verify |
| `^TTCS` | Consumer Staples | ⚠️ Verify |
| `^TTRE` | Real Estate | ⚠️ Verify |
| `^TTTS` | Communication Services | ⚠️ Verify |

### Benchmark index symbols

| Symbol | Index | Usage |
|--------|-------|-------|
| `^GSPTSE` | S&P/TSX Composite | Broad-market benchmark |
| `^TX60` | S&P/TSX 60 | Large-cap leadership proxy |
| `^TXCE` | S&P/TSX Composite Equal Weight | Breadth comparison for Leadership indicators |
| `XIU` | iShares S&P/TSX 60 ETF | Tradable TSX 60 proxy (used throughout system) |

**Important**: `^TXCE` ≠ `^TXEW`. TXCE is the **Composite** equal-weight; TXEW is the **TSX 60** equal-weight. For leadership analysis, the Composite equal-weight is correct because we want to compare cap-weighted leadership against the full breadth of the Composite universe.

Defined in `Core/TMX/TsxBenchmarkSymbols.cs`.

### Why this exists

This gives TraderVI a stable internal lookup it controls and enables:
- sector-relative strength
- sector concentration/risk checks
- future Granville leadership/weighting work
- resilience if TMX naming/schema changes

The stock-sector map is refreshed by Hermes only when stale (currently **7 days**), since sector
metadata changes far less often than daily prices.

## Future Direction

### Near-Term (Active)
- **RS backfill** for Hercules training (stock-vs-XIU first)
- **Hercules RS features** — retrain models with RS columns
- **Granville Features (#11+)** — next indicator group

### Medium-Term (Planned)
- **Sentinel**: Intraday monitoring with stop-loss execution and rotation triggers
- **RS gating**: Block trades with extreme negative RS Z-scores
- **Backtest harness**: Walk-forward simulation using historical picks vs actual outcomes
- **Strategy versioning**: Compare strategy versions via `[dbo].[StrategyVersions]`
- **Threshold tuning**: Use Hercules AUC/lift outputs to set optimal thresholds per model

### Long-Term (Exploratory)
- **TraderVI execution**: Automated order placement via Wealthsimple API
- Sector rotation signals (group by TSX sector, rotate into strongest)
- Intraday features from TMX time series (requires Sentinel)
- Earnings/event calendar integration
- Revisit A/D line as ML feature (only if rule-based gate proves insufficient)
- Ensemble stacking (use model outputs as features for a meta-model)

## Data Flow

| Program | Runs | Reads | Writes |
|---------|------|-------|--------|
| **Hermes** | Daily (post-close) | TMX API, `[DailyBars]` | `[DailyBars]`, `[AdvanceDeclineLine]`, `[SectorIndices]`, `[StockSectorMap]`, `[LeadershipData]`, `[RelativeStrengthFeatures]` (planned) |
| **Hercules** | Weekly / on-demand | `[DailyBars]`, `[RelativeStrengthFeatures]` (planned), `ProfitModelRegistry` | `.zip` models, `[ModelRegistry]` |
| **Delphi** | Daily (pre-market) | `[DailyBars]`, `[ModelRegistry]`, `[AdvanceDeclineLine]`, `[SectorIndices]`, `[StockSectorMap]`, `[LeadershipData]` | `[DailyPick]`, `[GranvilleIndicatorLog]`, console output |
| **Sentinel** | Continuous (planned) | `[DailyBars]`, `[DailyPick]`, live quotes | Alerts, `[TradeLog]` |