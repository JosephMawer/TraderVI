# Oracle (LLM Layer) — Binding Rules

These rules govern every interaction with the LLM layer in TraderVI.
They are **binding constraints**, not suggestions. Every prompt template,
client implementation, and persistence path must respect them.

The goal is to use an LLM as a *narration, critique, and Q&A* layer over the
deterministic trading pipeline — never as a decision-maker, computer of
numbers, or unverifiable source of truth.

---

## R1. The LLM never influences scoring or ranking

- The LLM runs **strictly downstream** of `TradeDecisionEngine`.
- No prompt output may be fed back into composite score, gates, sizing,
  or ranking — directly or indirectly.
- Rationale: contaminating scoring with non-reproducible reasoning breaks
  backtests, replays, and falsifiability.

## R2. The LLM never computes numbers

- All numeric facts (returns, ratios, probabilities, edges, RS Z-scores,
  position sizes, etc.) **must be precomputed in C#** and embedded in the
  `DecisionDossier`.
- Prompts must instruct the model to **cite** dossier fields by name, not
  derive them. Treat any numeric value not present in the dossier as
  hallucination.

## R3. Every LLM call is fully reproducible

For every call we persist:
- Exact prompt text (system + user messages)
- Model name + version + provider
- Temperature, top_p, max_tokens, seed (if supported)
- Dossier id(s) referenced
- Response text, token usage, latency, cost
- A SHA-256 hash of the prompt for fast equality lookups

Without all of the above, "the LLM said X yesterday" becomes unfalsifiable.

## R4. Token + cost guardrails

- Per-dossier token budget enforced **before** the call (truncate or fail).
- Daily cost ceiling enforced at the client level; breaches abort the run.
- Historical bar series **never** dumped raw into prompts — summarize to
  the handful of features that already exist in the dossier.

## R5. News + fundamentals are Phase 4, not earlier

- Get the narration loop working over **existing structured signals first**.
- External ingestion (news, fundamentals) is a separate problem with its own
  dedupe, provenance, and rate-limit concerns — solving it before the core
  loop works delays the highest-value, lowest-risk slice.

## R6. Evaluation is part of the layer, not optional

- A canned set of historical dossiers with expected key points serves as the
  regression suite for prompts. Run it before any prompt change ships.
- Define "good": e.g., the LLM must correctly name which Granville group
  dominated the composite adjustment, must not invent symbols, must not
  contradict the dossier's direction field.

## R7. Provider-agnostic by construction

- All call sites go through `ILlmClient`. Concrete implementations
  (`DotLlmClient`, `OpenAiLlmClient`, etc.) are interchangeable via config.
- The dossier schema is the contract; prompts are template-driven so the
  same dossier can be replayed across providers for comparison.

## R8. Schema versioning is mandatory

- Every `DecisionDossier` payload carries an integer `SchemaVersion`.
- Prompt templates declare the minimum schema version they accept.
- A dossier read at a different schema version than its prompt expects is
  a hard error, not a silent best-effort.

## R9. The dossier is the audit unit

- Anything the LLM is shown must originate from a persisted `DecisionDossier`
  row. No "live" context smuggled in via globals, environment, or ad-hoc
  reads. If it matters to the reasoning, it belongs in the dossier.

## R10. Human override is first-class

- The Q&A / debate loop must preserve user push-back as durable
  conversation turns linked to the dossier id, so disagreements are
  inspectable later (and become future eval fixtures).
