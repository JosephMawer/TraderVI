# Running TraderVI (End-to-End)

This repo currently operates in **advisory mode** (it prints recommendations). Automated trading is planned via Wealthsimple (`Core/WSTrade`).

See `docs/glossary.md` for terms like Lookback, HorizonBars, and TaskType.

- Glossary: `docs/glossary.md`
- Architecture: `docs/architecture.md`
- Models: `docs/models.md`
- Strategy: `docs/strategy.md`

## 1) Collect Market Data (Hermes)

**Goal:** Populate/refresh all market data tables.

Project:
- `Hermes`

Entry:
- `Hermes/Program.cs`

Behavior:
- Loads TSX symbols from `SymbolsRepository`
- Downloads historical daily bars from TMX GraphQL API
- Upserts into `[dbo].[DailyBars]`
- Updates the Advance-Decline line in `[dbo].[AdvanceDeclineLine]`
- Collects TSX sector index snapshots into `[dbo].[SectorIndices]`
- Refreshes stock → sector mappings into `[dbo].[StockSectorMap]` (7-day staleness check)
- Future: backfill `[dbo].[RelativeStrengthFeatures]` for Hercules training

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
- Future: RS features from `[dbo].[RelativeStrengthFeatures]` will be joined into the training data.

## 3) Run Advisory Recommendations (Delphi)

**Goal:** Compute today's best pick (single-position rotation strategy) and output supporting signals.

Project:
- `Delphi`

Entry:
- `Delphi/Program.cs`

Behavior:
1. Loads enabled models from `ModelRegistry` using `DelphiBootstrap`
2. Computes market regime from XIU + SPY benchmarks
3. Loads A/D line breadth and injects as a gate
4. Evaluates Granville day-to-day indicators (Plurality, Disparity) using sector snapshots
5. Loads all equity symbols and their daily bars
6. Computes live Relative Strength per stock (stock vs sector, stock vs market)
7. Runs ML model inference over the filtered universe
8. Ranks candidates by DirectionEdge → RS Composite → Composite Score
9. Outputs top picks + sizing recommendation for the single best trade
10. Optionally saves picks to `[dbo].[DailyPick]`

## Troubleshooting

### Multiple models loaded for the same TaskType
Ensure the model registry enforces:
- only one enabled model per TaskType
- old enabled models are disabled when a new enabled model is inserted

### ML.NET schema mismatch errors
Feature vectors must be fixed-size. Ensure `SchemaDefinition` is used during:
- training (`LoadFromEnumerable(..., schemaDefinition)`)
- inference (`CreatePredictionEngine(..., inputSchemaDefinition: schemaDefinition)`)

### Sector snapshots empty
If Delphi reports "No sector index snapshots loaded":
- Verify Hermes has run `UpdateSectorIndicesAsync()` at least once
- Check that `[dbo].[SectorIndices]` has recent data
- Verify TMX GraphQL is resolving `^TT*` symbols (some are empirically discovered and may stop working)

### Stock-sector mappings unmapped
If Hermes reports many unmapped sectors:
- Check `TsxSectorMap.SectorToIndex` for missing sector name variants
- TMX returns inconsistent sector names (e.g., "Finance" vs "Financials") — add both