# Running TraderVI (End-to-End)

This repo currently operates in **advisory mode** (it prints recommendations). Automated trading is planned via Wealthsimple (`Core/WSTrade`).

See `docs/glossary.md` for terms like Lookback, HorizonBars, and TaskType.

- Glossary: `docs/glossary.md`
- Architecture: `docs/architecture.md`
- Models: `docs/models.md`
- Strategy: `docs/strategy.md`

## 1) Collect Market Data (Hermes)

**Goal:** Populate/refresh `[dbo].[DailyBars]` for TSX symbols.

Project:
- `Hermes`

Entry:
- `Hermes/Program.cs`

Behavior:
- Loads TSX symbols from `SymbolsRepository`
- Downloads historical daily bars from TMX
- Upserts into `[dbo].[DailyBars]`

## 2) Train Models (Hercules / ML.Train)

**Goal:** Train ML.NET models and register them in `[dbo].[ModelRegistry]`.

Project:
- `ML.Train` (Hercules)

Entry:
- `ML.Train/Program.cs`

Behavior:
- Loads symbols from `SymbolsRepository`
- Loads bars per symbol via `QuoteRepository.GetDailyBarsAsync(...)`
- Trains enabled Pattern and Profit models (Registry-driven)
- Saves ML.NET model artifacts as `.zip`
- Inserts model metadata into `ModelRegistry` (and disables older enabled models for the same TaskType)

Notes:
- Training must avoid cross-ticker window contamination (windows are built per-symbol).
- Per-symbol time splits are used for evaluation realism.

## 3) Run Advisory Recommendations (Delphi)

**Goal:** Compute today’s best pick (single-position rotation strategy) and output supporting signals.

Project:
- `Delphi`

Entry:
- `Delphi/Program.cs`

Behavior:
- Loads enabled models from `ModelRegistry` using `DelphiBootstrap`
- Runs inference over the TSX universe
- Ranks primarily by expected return, confirmed by 3-way confidence
- Outputs best pick + sizing recommendation
- (Future) will integrate Wealthsimple execution via `Core/WSTrade`

## Troubleshooting

### Multiple models loaded for the same TaskType
Ensure the model registry enforces:
- only one enabled model per TaskType
- old enabled models are disabled when a new enabled model is inserted

### ML.NET schema mismatch errors
Feature vectors must be fixed-size. Ensure `SchemaDefinition` is used during:
- training (`LoadFromEnumerable(..., schemaDefinition)`)
- inference (`CreatePredictionEngine(..., inputSchemaDefinition: schemaDefinition)`)