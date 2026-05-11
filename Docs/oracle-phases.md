# Oracle (LLM Layer) — Phased Rollout

The Oracle component adds an LLM-driven narration, critique, and Q&A layer
*downstream* of `TradeDecisionEngine`. It treats every trading decision as a
structured **`DecisionDossier`** and asks the LLM to reason over it — never
to compute or override it. See `oracle-rules.md` for the binding rules.

---

## Phase 1 — Dossier Emission *(this phase)*

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

## Phase 2 — Narration

**Goal:** generate a human-readable summary per pick + a market-wide summary.

- Add `Oracle/` console app (sibling to Delphi).
- Add `Core/Oracle/Llm/ILlmClient.cs` with `DotLlmClient` and
  `OpenAiLlmClient` implementations selected by config.
- Add `[LlmNarrative]` table: `(NarrativeId, DossierId, ModelName, ProviderName,
  PromptHash, PromptText, ResponseText, Temperature, TokenInput, TokenOutput,
  CostUsd, LatencyMs, SchemaVersion, CreatedUtc)`.
- Prompt template: stuff the dossier JSON into a system message, instruct the
  model to cite fields by name, forbid arithmetic.
- Nightly: read today's dossiers → produce per-pick narrative + a
  market-wide summary.

**Done when:** running Oracle after Delphi produces one narrative row per
dossier; narratives reference dossier fields by name and don't introduce
numbers absent from the dossier.

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
