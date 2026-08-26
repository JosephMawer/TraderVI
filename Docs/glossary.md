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
- **Sentinel**: Planned durable 15-minute advisory service around the ADR-0028 exit-policy engine. ADR-0029 provides a replay-only pilot monitor for linked ghost positions; persisted evidence, automated stop execution, and rotation remain deferred.
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

## Calibration & Statistical Jargon
- **Baseline**: The reference distribution we compare a triggered subset against. For a long-side signal, the natural baseline is "all up-days" (not "all days"), because the signal can only fire on up-days. Comparing to the wrong baseline manufactures fake edge.
- **Trigger**: The set of historical days where a rule's conditions are all true. "v1 trigger" = days where `ScoreB ≥ 0.50 AND ScoreC ≥ 0.60 AND XiuReturn > 0`.
- **Trigger rate**: `Triggers / Eligible days`. A trigger rate of 1.9% on 1,557 days means ~13 historical triggers — small N, treat conclusions as provisional.
- **∩ (intersection)**: Set-theory "AND". `v1 ∩ up-days` = days that are *both* in the v1 trigger set *and* are up-days for XIU. We use this notation in tables because it makes the slicing explicit.
- **Forward return**: Return measured *after* the signal date — what would have happened if you'd acted on the signal. `1d forward return on date T` = `(Close[T+1] − Close[T]) / Close[T]`. Distinct from the *signal-date* return that the rule fires on.
- **Hit rate (calibration sense)**: Fraction of triggered days whose forward return matches the rule's directional prediction. A bearish rule has a "hit" when forward return is negative. Distinct from the *trade* hit rate in the backtest metrics section above.
- **Sub-period split / robustness check**: Splitting the sample at a median date (e.g., 2023-03-03) and recomputing the result in each half. If a finding only holds in one half, it's regime-dependent — not a stable edge.
- **Regime-dependent**: A pattern that holds in some market environments (bull / bear / high-vol / low-vol) but not others. Multi-day signals in our Weighting backtest were regime-dependent; the 1-day reversal was not.
- **Curve-fit / overfit**: A rule that looks predictive on the data it was tuned on but fails out-of-sample, usually because the parameters were chosen to maximize an in-sample metric. Sub-period splits are our cheapest defense.
- **Empirical threshold vs. structural threshold**: An *empirical* threshold (e.g., `ScoreB ≥ 0.50`) was picked from the data's distribution and forward-return behavior. A *structural* threshold (e.g., `XiuReturn > 0` for a long-side warning) was picked from the indicator's intended role, not from data.
- **Graceful degradation**: When required input data is incomplete, the indicator returns a Neutral result instead of crashing or guessing. Example: Weighting requires ≥ 50 of 60 constituents present today; below that, it emits Neutral.

## Granville Architecture Terms
- **`IGranvilleIndicatorGroup`**: The plug-in contract every Granville category implements. Single method: `Evaluate(GranvilleMarketContext) → IReadOnlyList<GranvilleResult>`. See [ADR-0001](adr/0001-granville-plugin-architecture.md).
- **`GranvilleResult`**: Output record from one indicator firing. Fields: `IndicatorNumber`, `Category`, `Name`, `Signal` (Bullish/Bearish/StrongBullish/StrongBearish/Neutral), `GranvillePoints` (the book's even-bullish / odd-bearish weighting), `Description`.
- **`GranvilleComposite`**: The aggregator that runs every registered group and produces a `GranvilleDailyForecast` with `BullishCount`, `BearishCount`, `NetPoints`, and a normalized `CompositeAdjustment` capped at ±0.10.
- **`MaxCompositeAdjustment`**: The hard cap (currently 0.10) on how much *all* Granville rule signals combined can move the composite score. Prevents the rule-based layer from drowning out ML signals.
- **`MaxRawPointRange`**: The theoretical max absolute Granville-point value across all *currently registered* groups. Used as the normalizer in `CompositeAdjustment`. Must be updated whenever a new group is registered.

## Granville Weighting Specifics ([ADR-0003](adr/0003-weighting-indicator-narrow-advance.md))
- **XIU constituents**: The ~60 stocks underlying the iShares S&P/TSX 60 ETF (XIU). Source for v1 is the static list in `Core/Config/Xiu60Constituents.cs`.
- **Price-weighted contribution proxy**: `weight_i = price_i / Σ price_j`, then `contribution_i = weight_i × return_i`. A *deliberate* Dow-style proxy applied on a cap-weighted basket — see [concepts/price-weighted-contribution.md](concepts/price-weighted-contribution.md). Not used to predict XIU return; only to make narrowness visible.
- **ScoreB (concentration)**: Of the constituents moving *same direction as XIU*, the share of total `|contribution|` captured by the top K = 3 names. Range 0–1. Median ~0.54. High ScoreB = "a few names did most of the lifting."
- **ScoreC (narrowness)**: Of the constituents that moved at all today, the fraction moving *against* XIU's direction. Range 0–1. Median ~0.34. High ScoreC = "few names actually participated with the index."
- **Narrow advance**: A day where XIU rose but ScoreB and ScoreC both signal a thin, concentrated move. Granville's hypothesis: such moves stall.
- **Long-side warning gate**: An indicator that fires only on up-days, only to *caution* against new longs — not to suggest shorts. Weighting #15/#16 is one. Contrast with directional Plurality #1–#4 which fire bullish or bearish symmetrically.
