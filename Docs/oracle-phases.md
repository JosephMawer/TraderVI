# Oracle (LLM Layer) — Phased Rollout

The Oracle component adds an LLM-driven narration, critique, and Q&A layer
*downstream* of `TradeDecisionEngine`. It treats every trading decision as a
structured **`DecisionDossier`** and asks the LLM to reason over it — never
to compute or override it. See `oracle-rules.md` for the binding rules.

---

## Phase 1 — Dossier Emission ✅ *(done — 2026-05)*

**Goal:** make every Delphi decision auditable as a single structured row.
No LLM dependency yet.

- New record types in `Core/Oracle/` describing the dossier shape
  (decision summary, ML signal contributions, Granville breakdown, RS scores,
  market regime, gate trace, strategy version).
- New `[dbo].[DecisionDossier]` table storing the JSON payload + schema
  version + foreign key to `[DailyPick].[PickId]`.
- `DecisionDossierRepository` for idempotent per-date writes.
- Delphi builds and persists a dossier for every saved pick.

**Done when:** for any past run with `saveToDB=true`, every row in
`[DailyPick]` has a matching `[DecisionDossier]` row whose JSON round-trips
back into the C# record types.

## Phase 2 — Narration ✅ *(done — 2026-05)*

**Goal:** generate a human-readable summary per pick + a market-wide summary.

- Added `Oracle/` console app (sibling to Delphi).
- Added `Core/Oracle/Llm/ILlmClient.cs` with `MockLlmClient`, `OpenAiLlmClient`,
  and `DotLlmClient` (Phase 5 stub) selected via `LlmClientFactory.FromConfiguration(...)`.
- Added `[LlmNarrative]` table: `(NarrativeId, DossierId, PickDate, Scope, Symbol,
  PromptHash, PromptText, ResponseText, Provider, ModelName, Temperature,
  InputTokens, OutputTokens, CostUsd, LatencyMs, SchemaVersion, CreatedUtc)`.
- `DossierPromptBuilder` generates per-pick critique + market-wide summary
  prompts. SHA-256 prompt hash drives an **incremental cache**: identical prompts
  reuse persisted narratives without an API call.
- `Oracle/Program.cs` supports `--print`, `--markdown`, `--dry-run`, `--force`
  and prints `[cache]` vs `[api]` per row.
- Config moved to **user-secrets** (`UserSecretsId` in `Oracle.csproj`) with
  env-var fallback. Keys: `Oracle:Llm:Provider`, `Oracle:Llm:Model`,
  `Oracle:OpenAi:ApiKey`, optional `Oracle:OpenAi:InputPer1KUsd` /
  `OutputPer1KUsd`.

**Prompt-tightening pass shipped on top of Phase 2** (see
`concepts/oracle-prompt-tightening.md`):
- System prompt forbids invented fields, restates null-vs-zero rule,
  requires field-cited quantification for every adjective.
- `MarketSharedContext` pre-computes signals fired by ≥ 70% of picks (warnings,
  Granville confirmations, ML/rule confirmations) and instructs the per-pick
  prompt to suppress those callouts.
- Per-pick JSON view strips defaults (`ExpectedReturn=0`), duplicates
  (`Confidence` when it equals `CompositeScore`), and alternate citation paths
  (`MlSignals.DirectionEdge` removed; `Decision.DirectionEdge` is canonical).

**Done — validated:** running Oracle after Delphi produces one narrative row
per dossier + one market summary; narratives cite dossier fields by name;
caching reuses identical-prompt rows; `gpt-5` and `gpt-5-mini` both supported
(the OpenAI client handles `max_completion_tokens` and the locked `temperature=1`
quirk for the GPT-5 family).

### Phase 2 pause-point snapshot *(2026-05)*

- Last successful run: `dotnet run --project Oracle -- 2026-05-10 --print --markdown`
  on 7 picks, model `gpt-5`, total cost ~$0.0015.
- Quality review noted in `concepts/oracle-prompt-tightening.md`: per-pick output
  is auditable and de-duplicated; remaining weakness is qualitative adjectives
  without numeric backing — partially addressed by Rule #8 (quantification).
- Phases 3-5 deferred — pick up by re-reading `oracle-rules.md` first, then
  this file, then the prompt-tightening concept doc.

## Phase 3 — Debate Loop

**Goal:** interactive push-back, threaded by dossier id.

- Add `[LlmConversation]` table: `(TurnId, DossierId, TurnIndex, Role,
  Content, ModelName, PromptHash, TokenInput, TokenOutput, CostUsd, CreatedUtc)`.
- CLI: `oracle chat <dossier-id>` pins the dossier in the system prompt and
  exchanges turns with the model.
- Every turn persists; replay is just re-reading the table.

**Done when:** disagreements with the LLM are durable, inspectable, and
linkable back to the exact dossier that triggered them.

## Phase 4 — External Context (News + Fundamentals)

**Goal:** enrich the dossier with non-technical inputs.

- New ingestion sibling (think `Hermes.News`) with its own dedupe + provenance.
- New tables: `[NewsItem]`, `[FundamentalsSnapshot]`, joined to symbols.
- Extend `DecisionDossier` schema (bump `SchemaVersion`) with curated,
  summarized references — never raw article bodies in prompts.
- Update prompt templates to require minimum schema version (per Rule R8).

**Done when:** dossiers reference news + fundamentals; LLM narratives can
cite specific headlines or fundamentals fields by name.

## Phase 5 — Backtest Narration

**Goal:** retroactively narrate historical dossiers to study reasoning evolution.

- Add bulk replay tool: feed N historical dossiers through `ILlmClient`,
  bypass cost ceiling under an explicit flag.
- Use `DotLlmClient` (local) for cost reasons.
- Compare narratives across providers via the eval fixtures from Rule R6.

**Done when:** we can replay a quarter's worth of dossiers and diff
narratives across model/provider/temperature settings.

---

## Sequencing principle

Each phase produces value standalone and is fully decoupled from the next:
Phase 1 is valuable as an audit log even if we never call an LLM. Phase 2
works without news. Phase 3 works without backtest replay. This lets us
stop, evaluate, and pivot at any phase boundary.
