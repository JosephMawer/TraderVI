# Glossary

## Core Market Data
- **Daily bar (OHLCV)**: A daily aggregate record containing:
  - **Open**: first trade price of the day
  - **High**: highest price of the day
  - **Low**: lowest price of the day
  - **Close**: last trade price of the day
  - **Volume**: shares traded during the day

- **TSX**: Toronto Stock Exchange. TraderVI is TSX-focused for now.

## Windows and Horizons
- **Lookback**: Number of bars used as model input (e.g., 30 bars). The input window is typically the *most recent* `Lookback` bars.
- **HorizonBars**: Number of bars in the future used to define the prediction target (e.g., 5d or 10d forward).

## Models and Tasks
- **TaskType**: A unique string key identifying a trained model task. Examples:
  - Pattern tasks: `Trend10`, `Trend30`, `MaCrossover`
  - Profit tasks: `ExpectedReturn10`, `Direction10`

- **Pattern model**: A model that predicts whether a technical pattern is present in the latest lookback window.
  - Output is typically a probability of the pattern being present.
  - Labels often come from `IPatternDetector` (rule-based detector).

- **Profit model**: A forward-outcome model that predicts what happens after the lookback window.
  - **Regression** predicts expected forward return (used for ranking/sizing).
  - **3-way classification** predicts Buy/Hold/Sell (used for confirmation).

## Labeling
- **Detector (pattern labeling)**: `IPatternDetector` that assigns labels based on current-window shape/structure (e.g., slope > 0).
- **Labeler (profit labeling)**: `ILabeler` that assigns labels based on *future bars* (forward return over `HorizonBars`).

- **Forward return**: `(Price[t + Horizon] - Price[t]) / Price[t]`.
- **3-way label**: Discrete label derived from forward return thresholds:
  - Buy / Hold / Sell

## Inference Outputs
- **ExpectedReturn**: Regression output predicting future return over the horizon (e.g., +3.2% over 10 days).
- **Confidence**: A probability-like score used as confirmation (often derived from the top predicted class probability in 3-way classification).

## Signals and Decisions
- **Signal**: Output from a model (pattern or profit) for one symbol at one time.
- **Composite Score**: Weighted blend of all ML signal scores for a stock, plus Granville adjustment. The primary score used for gating.
- **DirectionEdge**: `P(up) - P(down)` — primary ranking metric. Measures net conviction for upward movement.
- **Ranking**: Sorting symbols by DirectionEdge → RS Composite → Composite Score.
- **Rotation**: Switching holdings from current symbol to a new symbol when the new symbol is sufficiently better.
- **RotationMinExpectedReturnDelta**: Minimum expected-return improvement required to rotate to a new pick (reduces churn).

## Market Context
- **Market Regime**: Rule-based assessment of XIU and SPY trend/momentum state. Gates all trading.
- **Breadth Score**: Numeric score from A/D line analysis (slope, SMA position, divergence). Used as a gate.
- **Granville Composite Adjustment**: A small modifier (±0.10 max) derived from Granville's day-to-day indicators, applied uniformly to all stocks.

## Relative Strength
- **RS (Relative Strength)**: Return difference between two series over a horizon. e.g., `RS_StockVsMarket_10d = Return(stock, 10d) - Return(XIU, 10d)`.
- **RS_Z**: Z-score normalization of RS. `(RS_today - mean(RS_20d)) / std(RS_20d)`. Measures how extreme today's RS is vs recent history.
- **RS Composite**: Weighted blend of 10d RS across all three axes (stock-vs-market, stock-vs-sector, sector-vs-market).

## Sector Infrastructure
- **TsxSectorSymbols**: Internal mapping of `^TT*` TMX sector index symbols to sector names (e.g., `^TTEN` → Energy).
- **TsxSectorMap**: Normalization layer mapping TMX sector metadata strings to `^TT*` symbols.
- **StockSectorMap**: Per-stock mapping to its sector index, stored in `[dbo].[StockSectorMap]`.

## Risk Management
- **Drawdown**: % decline from entry price.
- **Warning threshold**: -5% drawdown (alert/tighter monitoring).
- **Stop-loss**: -10% drawdown (hard exit; overrides model signals).

## System Components
- **Hermes**: Market data collector. Loads daily bars, A/D line, sector indices, and stock-sector mappings into the DB.
- **Hercules (`ML.Train`)**: Training pipeline. Trains models and writes `.zip` artifacts.
- **ModelRegistry**: DB table storing trained models and metadata; used by runtime to load enabled models.
- **Delphi**: Runtime inference and recommendation app (advisory mode). Evaluates Granville, computes live RS, runs ML models, ranks candidates.
- **Sentinel**: Planned intraday monitoring with stop-loss execution and rotation triggers.
- **WSTrade**: Wealthsimple integration (future automated execution).

## Gate Pipeline
- **Gate**: A sequential pass/fail check in `TradePipeline`. Each gate examines `GateContext` and can block a trade.
- **GateTrace**: Diagnostic log of which gates passed/failed for a given stock evaluation.

## Backtest and P&L Metrics
- **P&L (Profit and Loss)**: The net financial outcome of a strategy over a period. `EndingEquity − StartingEquity`, expressed in dollars or as a percentage. In TraderVI's backtests, P&L is computed from the simulated equity curve (entries, exits, slippage, commission applied).
- **Equity curve**: Time series of total account value over the backtest, one point per trading day. The primary visual artifact of a backtest run.
- **Total Return**: `(FinalEquity − InitialEquity) / InitialEquity`. The headline P&L number for a run.
- **CAGR (Compound Annual Growth Rate)**: Annualized total return. `(FinalEquity / InitialEquity)^(1 / Years) − 1`.
- **Drawdown**: Percent decline from a prior equity peak. Computed bar-by-bar against the running peak.
- **Max Drawdown**: The worst (largest) drawdown observed in the run. Primary risk metric.
- **Sharpe Ratio**: Risk-adjusted return. `mean(daily strategy returns) / std(daily strategy returns) × sqrt(252)`. Risk-free rate assumed 0 unless stated otherwise.
- **Sortino Ratio**: Like Sharpe but only penalizes downside volatility. Uses `std(negative daily returns)` in the denominator.
- **Hit Rate**: Fraction of closed trades with `RealizedReturn > 0`.
- **Average Winner / Average Loser**: Mean realized return of profitable trades and losing trades respectively. Ratio (`AvgWin / |AvgLoss|`) is the payoff ratio.
- **Exposure %**: Fraction of trading days the strategy held a position. `DaysInMarket / TotalDays`.
- **Benchmark**: Buy-and-hold reference. TraderVI uses **XIU** (iShares S&P/TSX 60 ETF) as the default benchmark for backtest comparison.
- **Walk-forward backtest**: Replay of a strategy through historical time where, on each date `T`, only data with date `< T − embargo` is used for any model or feature. Models are retrained periodically through the replay rather than once at the end.
- **Point-in-time correctness**: Invariant that no decision on date `T` may use information from any bar dated after `T − embargo`. The fundamental honesty constraint of a backtest.
- **As-of universe**: The set of symbols actually tradable on a given historical date `T` — excludes symbols that hadn't listed yet and (correctly) includes symbols that later delisted, up to their last bar.
- **Retrain cadence**: Backtest config controlling how often models are refreshed during the historical replay (e.g., every 1 month). Distinct from live ops cadence.
- **BacktestRunId**: Unique identifier tagging all artifacts (picks, equity, retrained model zips) from a single backtest invocation. Enables comparing multiple runs.