# Architecture

- Glossary: `docs/glossary.md`
- Running: `docs/running.md`
- Models: `docs/models.md`
- Strategy: `docs/strategy.md`
- Design Rules: `docs/design-rules.md`
- System Design: `docs/system-design.md`

## Components

### Hermes (Market Data Collector)
- Downloads historical daily OHLCV bars from TMX GraphQL API
- Computes and stores the Advance-Decline line
- Collects TSX sector index snapshots (`[dbo].[SectorIndices]`)
- Refreshes stock → sector mappings (`[dbo].[StockSectorMap]`) on a 7-day staleness schedule
- Stores bars in SQL (`[dbo].[DailyBars]`)
- Uses `TmxClient` + `QuoteRepository` + `AdvanceDeclineRepository` + `SectorIndexRepository` + `StockSectorRepository`

### Hercules (Training Pipeline)
- Trains ML.NET models (LightGBM) and writes `.zip` artifacts
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
- Computes market regime (XIU + SPY)
- Loads A/D line breadth and injects as a gate
- Evaluates Granville's day-to-day indicators (Plurality, Disparity)
- Computes live Relative Strength per stock (stock vs sector, stock vs market)
- Produces rankings and sizing decisions via `TradeDecisionEngine`

### Granville Market Timing Layer
- Rule-based market-level overlay on top of ML signals
- `GranvilleComposite` aggregates all `IGranvilleIndicatorGroup` implementations
- Produces a composite adjustment ∈ [-0.10, +0.10] applied to every stock's score
- Currently active groups: Plurality (#1–#4), Disparity (#5–#6)
- Context shared via `GranvilleMarketContext` (A/D line, sector snapshots, stock-sector mappings)

### Relative Strength Layer
- Per-stock feature layer comparing stock vs sector vs market (XIU)
- Horizons: 5d, 10d, 20d, 60d (raw return difference + Z-score normalization)
- `RelativeStrengthCalculator` — pure stateless computation
- `RelativeStrengthRepository` — DB persistence for Hercules training
- Delphi computes live; Hermes backfills historical to DB
- Used as both a ranking signal (Delphi) and ML feature (Hercules, planned)

### Sector Infrastructure
- `TsxSectorSymbols` — maps `^TT*` symbols to sector names
- `TsxSectorMap` — normalizes TMX sector metadata strings to sector index symbols
- `SectorIndexRepository` — daily sector index close prices
- `StockSectorRepository` — stock → sector index mapping

## Database Tables

| Table | Purpose | Written By | Read By |
|-------|---------|-----------|---------|
| `[DailyBars]` | OHLCV daily bars | Hermes | Hercules, Delphi |
| `[AdvanceDeclineLine]` | Market breadth | Hermes | Delphi |
| `[SectorIndices]` | TSX sector index snapshots | Hermes | Delphi (Granville, RS) |
| `[StockSectorMap]` | Stock → sector index mapping | Hermes | Delphi (Granville, RS) |
| `[ModelRegistry]` | Trained ML model metadata | Hercules | Delphi |
| `[DailyPick]` | Daily recommendations | Delphi | Sentinel (planned) |
| `[StrategyVersions]` | Strategy config versioning | Manual | Delphi |
| `[GranvilleIndicatorLog]` | Granville indicator history | Delphi | Analysis |
| `[RelativeStrengthFeatures]` | RS feature history | Hermes (planned) | Hercules (planned) |

## Data Flow

| Program | Runs | Reads | Writes |
|---------|------|-------|--------|
| **Hermes** | Daily (post-close) | TMX API | `[DailyBars]`, `[AdvanceDeclineLine]`, `[SectorIndices]`, `[StockSectorMap]` |
| **Hercules** | Weekly / on-demand | `[DailyBars]`, `[RelativeStrengthFeatures]` (planned), `ProfitModelRegistry` | `.zip` models, `[ModelRegistry]` |
| **Delphi** | Daily (pre-market) | `[DailyBars]`, `[ModelRegistry]`, `[AdvanceDeclineLine]`, `[SectorIndices]`, `[StockSectorMap]` | `[DailyPick]`, `[GranvilleIndicatorLog]`, console output |
| **Sentinel** | Continuous (planned) | `[DailyBars]`, `[DailyPick]`, live quotes | Alerts, `[TradeLog]` |
| **TraderVI** | Event-driven (planned) | `[DailyPick]`, Sentinel signals | Wealthsimple API orders |