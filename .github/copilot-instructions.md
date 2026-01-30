# Copilot Instructions (TraderVI)

## System Goal (High Level)

TraderVI is a daily-stock trading system that:

1. Imports daily OHLCV market data (Hermes) into a local SQL database.
2. Trains ML.NET models (Hercules / `ML.Train`) to:
   - Detect technical patterns (trend/momentum/reversal/continuation).
   - Predict forward profit/return (regression) and direction confirmation (3-way classification).
3. Runs runtime inference (Delphi/TraderVI console) to:
   - Evaluate a universe of tickers daily.
   - Rank candidates primarily by predicted expected return.
   - Use direction-confidence confirmation to avoid low-quality picks.
   - Output the single best trade candidate and suggested position sizing.
4. Execution mode:
   - **Advisory-only for now** (prints recommendations; manual order placement).
   - **Later**: automated order placement via Wealthsimple API (see `Core\WSTrade\`).

## Trading Universe

- Default universe is **TSX constituents** sourced from the local DB via `SymbolsRepository`.
- A watchlist concept may be added later; TSX remains the primary universe.

## Primary Trading Strategy (Current)

- **Aggressive single-position rotation** (default):
  - Allocate most/all available capital into the single top-ranked stock.
  - Intended holding horizon: **~1–2 weeks** (aligns to profit model horizons like 5d/10d).
  - Avoid diversification for now.

### Rotation Rule (Reduce churn)

- Do **not** rotate positions too frequently.
- Only switch from the current holding to a new candidate when:
  - The new candidate’s predicted expected return exceeds the current holding’s expected return by a configurable delta:
    - `RotationMinExpectedReturnDelta` (e.g., +1.0% absolute).
- This delta should be configurable and not hardcoded in multiple places.

## Risk Management (Capital Preservation)

- Primary risk goal: _capital preservation_.
- Risk rules:
  - **Stop-loss**: sell if drawdown reaches **-10%** from entry (hard exit).
  - **Warning**: drawdown **-5%** should trigger alerts and tighter monitoring.
- Implementation notes:
  - Track entry price and current price per active holding.
  - Enforce stop-loss even if models still predict Buy.

## Core Architecture Rules (ML)

### Pattern detection (“pattern taxonomy + pluggable components”)

- Patterns are defined via `PatternDefinition` + `PatternRegistry`.
- Pattern labeling is done via `IPatternDetector`.
- Features are generated via `IFeatureBuilder`.
- Training uses `UnifiedPatternTrainer` with per-symbol time split.
- Runtime inference uses `UnifiedPatternSignalModel`.
- Use `SignalSemantics` to map model probability to `Buy/Hold/Sell` correctly.

### Profit prediction (forward outcome models)

- Profit labels come from `ILabeler` (e.g., `ForwardReturnLabeler`) using:
  - `HorizonBars` (prediction horizon)
  - thresholds (Buy/Hold/Sell bands)
- Profit models are defined via `ProfitModelDefinition` + `ProfitModelRegistry`.
- Training uses `UnifiedProfitTrainer`.
- Runtime inference uses `UnifiedProfitSignalModel`.
- Combine:
  - Regression expected return (ranking + sizing)
  - 3-way classification confidence (confirmation)

## Decisioning and Ranking (Runtime)

- Ranking priority:
  1. **ExpectedReturn** (regression output) descending.
  2. **Confidence** (3-way output) descending as confirmation.
- Default runtime behavior is to output a **single best pick** and size it aggressively (single-position mode).
- Use model outputs as signals; the `TradeDecisionEngine` is the final arbiter combining signals + risk rules.

## Model Registry Rules

- Models are stored as ML.NET `.zip` files and registered in `[dbo].[ModelRegistry]`.
- Only one enabled/active model per `TaskType`:
  - When inserting a new enabled model, disable older ones for that same `TaskType`.
- Runtime loading (`DelphiBootstrap`) should:
  - Load enabled models from registry
  - Skip missing model files
  - Avoid double-loading the same `TaskType`

## Data / Pipeline Rules

- Data source is Hermes (`Hermes\Program.cs`) using TMX client; stored via `Core.Db` repositories.
- Training must:
  - Avoid cross-ticker window contamination (never concatenate bar series across symbols).
  - Prefer per-symbol time splits for realistic evaluation.
- Data access guarantee: `QuoteRepository.GetDailyBarsAsync` orders daily bars by Date ASC (chronological) for downstream windowing/training.

## Coding Conventions

- Target: **.NET 10**, C# 14.
- Prefer small composable components:
  - detectors/labelers, feature builders, trainers, signal models, bootstrap
- Avoid hardcoding symbols (like `CEU`) in production paths; use DB symbol lists by default.
- Keep the system runnable end-to-end:
  - Hermes loads data → Hercules trains/registers → Delphi/TraderVI ranks best pick.
