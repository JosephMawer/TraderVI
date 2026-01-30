# Architecture

- Glossary: `docs/glossary.md`
- Running: `docs/running.md`
- Models: `docs/models.md`
- Strategy: `docs/strategy.md`

## Components

### Hermes (Market Data Collector)
- Downloads historical daily OHLCV bars
- Stores bars in SQL (`[dbo].[DailyBars]`)
- Uses `TmxClient` + `QuoteRepository`

### Hercules (Training Pipeline)
- Trains ML.NET models and writes `.zip` artifacts
- Registers models in `[dbo].[ModelRegistry]`
- Uses:
  - Pattern training: `Core.ML.Engine.Patterns.UnifiedPatternTrainer`
  - Profit training: `Core.ML.Engine.Profit.UnifiedProfitTrainer`

### Model Registry (DB)
`[dbo].[ModelRegistry]` stores:
- model metadata (TaskType, lookback, horizon, thresholds)
- `ZipPath` to ML.NET `.zip`
- enabled/disabled status

Rule:
- Only one enabled model per `TaskType` (disable older models when inserting a new enabled one).

### Delphi / Runtime
- Loads enabled models from registry (`DelphiBootstrap`)
- Instantiates:
  - `UnifiedPatternSignalModel` for pattern models
  - `UnifiedProfitSignalModel` for profit models
- Produces rankings and sizing decisions via `TradeDecisionEngine`