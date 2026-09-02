# Architecture

- Glossary: `docs/glossary.md`
- Running: `docs/running.md`
- Models: `docs/models.md`
- Strategy: `docs/strategy.md`
- Design Rules: `docs/design-rules.md`
- System Design: `docs/system-design.md`
- Oracle (LLM layer) Rules: `docs/oracle-rules.md`
- Oracle (LLM layer) Phases: `docs/oracle-phases.md`

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
- Keeps orchestration in the host-neutral `DelphiWorkflow`; CLI and WPF are adapters
- Loads enabled models from registry (`DelphiBootstrap`)
- Instantiates:
  - `UnifiedPatternSignalModel` for pattern models
  - `UnifiedProfitSignalModel` for profit models
- Computes market regime (XIU + SPY)
- Loads A/D line breadth and injects as a gate
- Evaluates Granville's day-to-day indicators (Plurality, Disparity)
- Computes live Relative Strength per stock (stock vs sector, stock vs market)
- Produces rankings and sizing decisions via `TradeDecisionEngine`
- Builds the human/diagnostic reports and a versioned typed presentation snapshot from the same evaluated facts
- Embeds new presentation snapshots in immutable calibration run context; saved-session readers use the matching run or explicitly labelled date-aligned legacy reconstruction

### Project Documentation Reader
- `Core.Documentation.ProjectMarkdownCatalog` discovers repository Markdown, excluding tool and generated directories, and provides title/path/content filtering
- `Core.Documentation.MarkdownLinkResolver` canonicalizes relative targets, refuses traversal outside the repository, and opens only catalogued Markdown internally
- `TraderVI.WPF.Documentation.MarkdownFlowDocumentRenderer` maps the accepted Markdown subset to native WPF `FlowDocument` elements
- The WPF Project Docs tab is read-only, defaults to `Docs/project-status.md`, reloads edited files on Refresh, and opens HTTP(S) links only after a click
- The reader has no SQL, market-data, model, artifact, or trading side effect

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

### Oracle (LLM Layer — Phase 2)
- Strictly downstream narration/critique layer over `TradeDecisionEngine`. Never feeds back into scoring (see `docs/oracle-rules.md`).
- `Core.Oracle.DecisionDossier` — structured, JSON-serializable audit unit per pick (decision summary, ML breakdown, Granville, RS, market context, gate trace, strategy ref). `SchemaVersion` field per Rule R8.
- `Core.Oracle.DecisionDossierBuilder` — pure builder from a `RankedPick` + market context.
- `DecisionDossierRepository` — persists/reads `[dbo].[DecisionDossier]`.
- `Core.Oracle.Llm.ILlmClient` — provider-neutral LLM contract (`MockLlmClient` default, `OpenAiLlmClient`, `DotLlmClient` Phase 5 stub). Selected via `LlmClientFactory.FromEnvironment()` reading `ORACLE_LLM_PROVIDER` / `ORACLE_LLM_MODEL` / `OPENAI_API_KEY`.
- `Core.Oracle.Prompts.DossierPromptBuilder` — builds per-pick critique and market-wide summary prompts from dossier JSON. Enforces `MinSupportedSchemaVersion` (Rule R8) and exposes SHA-256 `ComputePromptHash` (Rule R3).
- `LlmNarrativeRepository` — persists/reads `[dbo].[LlmNarrative]` (prompts, response, provider, model, tokens, cost, latency).
- **Oracle console app** — Phase 2 entry point. Reads dossiers for a pick date, prompts the configured LLM, and writes narratives. Run modes: per-pick critique, market summary, `--dry-run`.

## Database Tables

| Table | Purpose | Written By | Read By |
|-------|---------|-----------|---------|
| `[DailyBars]` | OHLCV daily bars | Hermes | Hercules, Delphi |
| `[AdvanceDeclineLine]` | Market breadth | Hermes | Delphi |
| `[SectorIndices]` | TSX sector index snapshots | Hermes | Delphi (Granville, RS) |
| `[StockSectorMap]` | Stock → sector index mapping | Hermes | Delphi (Granville, RS) |
| `[ModelRegistry]` | Trained ML model metadata | Hercules | Delphi |
| `[DailyPick]` | Daily recommendations | Delphi | Sentinel (planned) |
| `[StrategyVersion]` | Immutable strategy configuration plus initial code/decision identity | Reviewed manual migration | Delphi, Athena, Scorecards |
| `[GranvilleIndicatorLog]` | Granville indicator history | Delphi | Analysis |
| `[RelativeStrengthFeatures]` | RS feature history | Hermes (planned) | Hercules (planned) |
| `[DecisionDossier]` | Structured per-pick audit unit for LLM layer | Delphi | Oracle |
| `[LlmNarrative]` | Per-pick and market-wide LLM narratives (prompt + response + cost) | Oracle | Analysis, future debate loop |
| `[IntradayPollObservation]` | Versioned request/receipt and source-quality audit | Shared paper monitor | Dashboard, coverage, and delayed-outcome evaluation |
| `[IntradayEvidenceBar]` | Immutable completed 5-minute storage and direct 15-minute policy bars | Shared paper monitor | Paper policy and delayed-outcome evaluation |

### Operational gotchas

- **Delete order for date-scoped reruns:** the FK chain is
  `LlmNarrative` → `DecisionDossier` → `DailyPick`. Always delete
  child-first; reverse order trips `FK_DecisionDossier_DailyPick`.
  Encoded once in `Core/Runtime/DelphiWorkflow.cs`; hosts must call the shared
  workflow rather than reproduce this deletion behavior.
- **OpenAI `gpt-5*` request shape:** `temperature` is locked to `1` and
  must be omitted; `max_tokens` was renamed to `max_completion_tokens`.
  Handled in `Core/Oracle/Llm/OpenAiLlmClient.cs`. Practical default for
  Oracle is `gpt-5-mini` (no org verification required).

## Data Flow

| Program | Runs | Reads | Writes |
|---------|------|-------|--------|
| **Hermes** | Daily (post-close) | TMX API | `[DailyBars]`, `[AdvanceDeclineLine]`, `[SectorIndices]`, `[StockSectorMap]` |
| **Hercules** | Weekly / on-demand | `[DailyBars]`, `[RelativeStrengthFeatures]` (planned), `ProfitModelRegistry` | `.zip` models, `[ModelRegistry]` |
| **Delphi** | Daily (pre-market) | `[DailyBars]`, `[ModelRegistry]`, `[AdvanceDeclineLine]`, `[SectorIndices]`, `[StockSectorMap]` | `[DailyPick]`, `[GranvilleIndicatorLog]`, `[DecisionDossier]`, console output |
| **TraderVI paper monitor** | WPF or headless console; immediate start plus 15-minute regular-session cadence | active linked positions, delayed TMX intraday bars | `[IntradayPollObservation]`, `[IntradayEvidenceBar]`, position snapshots, and database-only ghost exits; no broker orders |
| **TraderVI.WPF** | Interactive tabbed shell | linked paper state, saved recommendations, matching Delphi presentation evidence, repository Markdown, and host-neutral workflows | shared paper-monitor writes; Data Audit, saved-Delphi refresh, and Project Docs are read-only; confirmed official Delphi runs have Delphi's documented SQL effects |
| **Oracle** | Daily (post-Delphi) | `[DecisionDossier]` | `[LlmNarrative]`, console output |
| **TraderVI** | Manual and monitored ghost execution | `[DailyPick]`, linked positions, intraday evidence | ghost positions/trades and intraday evidence; no live broker orders |
