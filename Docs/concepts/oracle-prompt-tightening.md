# Oracle Prompt Tightening — Lessons & Patterns

This doc captures what we learned getting the Oracle (LLM) narration layer to
produce auditable, non-repetitive, non-hallucinated output over real
`DecisionDossier` data. It applies to anyone editing `DossierPromptBuilder.cs`
or extending the Oracle layer in later phases.

> Companion docs: `oracle-rules.md` (binding rules), `oracle-phases.md`
> (roadmap), `glossary.md` (Dossier, Granville, RS).

---

## TL;DR — what made the prompts good

1. **Strip null/default fields from the dossier JSON before sending it.**
   Models read `ExpectedReturn = 0` as *"no anticipated profit"*, not
   *"unset"*. Same for `Confidence` when it duplicates `CompositeScore`.
2. **Pre-compute cross-pick context once** (`MarketSharedContext`) and tell
   the model what's shared today — both *shared warnings* and *shared
   confirmations*. Without this every pick says "Trend10/Trend30/MaCrossover
   all Buy", because they fire on every pick.
3. **Force every qualitative adjective to cite a number.** *"Weak relative
   strength"* is filler; *"weak (RelativeStrength.CompositeScore=-0.42)"* is
   an audit trail.
4. **Remove duplicate citation paths.** If `DirectionEdge` exists on both
   `Decision` and `MlSignals`, the model will randomly cite either — keep
   one canonical path in the JSON view.
5. **Cache by SHA-256 of the full prompt.** Lets us iterate on prompts cheaply
   — only changed rows hit the API.

---

## The model gotchas that bit us

### OpenAI `gpt-5` family quirks
- **`temperature` is locked to `1`.** Sending `0.2` returns 400. Solution: omit
  the field for `gpt-5*` models (see `OpenAiLlmClient.cs`).
- **`max_tokens` was renamed to `max_completion_tokens`.** Sending `max_tokens`
  also fails. Same client-side conditional handles it.
- **`gpt-5` (the full model) requires org verification.** `gpt-5-mini` and
  `gpt-5-nano` do not. For Oracle's workload (critique over structured data)
  `gpt-5-mini` is the right default.
- **There is no `gpt-5.5`.** The lineup is `gpt-5-nano` → `gpt-5-mini` → `gpt-5`.
  If marketing renames something, verify at
  <https://platform.openai.com/docs/models> before changing config.

### DB delete ordering (Delphi rerun)
The FK chain is `LlmNarrative → DecisionDossier → DailyPick`. Deleting in the
wrong order throws `FK_DecisionDossier_DailyPick`. Always delete child-first:

```text
LlmNarrative   (by date)
DecisionDossier(by date)
DailyPick      (by date)
```

(`Delphi/Program.cs` does this; mirror it anywhere else you delete by date.)

---

## The two patterns worth memorizing

### Pattern A — `MarketSharedContext`

**Problem:** the prompt asks the model to explain *why* each pick was selected,
but most of the *why* is identical for every pick — trend confirmations,
broad-market Granville indicators, the same uptrend backdrop. Output becomes
boilerplate.

**Solution:** before generating per-pick prompts, scan the whole batch and
collect every signal that fired on ≥ 70% of picks:

- Granville indicators with **negative** points → "shared warnings"
- Granville indicators with **positive** points → "shared confirmations"
- ML/rule signals where `Hint != Hold` → "shared confirmations"

Inject the result into each per-pick prompt under a `SHARED_TODAY` block and
explicitly instruct the model: *"do not restate unless this pick is materially
stronger or weaker than the shared baseline on that dimension."*

**Side benefit:** the market summary uses the same struct to call out
*tension* between shared signals and the market posture (e.g. broad
near-term-decline warnings vs. an uptrend benchmark).

### Pattern B — Curated JSON view, not raw dossier

`DossierPromptBuilder.ProjectPerPickView(...)` does three things the raw
dossier would not:
1. Drops `Confidence` when it equals `CompositeScore`.
2. Drops `ExpectedReturn` when it is `0` (treats zero as "unset").
3. Rebuilds `MlSignals` *without* `DirectionEdge` so the canonical path is
   `Decision.DirectionEdge`.

Combined with `JsonIgnoreCondition.WhenWritingNull` this means the model
never sees a null or zero-default field, and every cited path is unique.

---

## System-prompt rules (current state)

| # | Rule | Why it exists |
|---|---|---|
| 1 | Never produce a number not in the dossier; cite by field path | R2 |
| 2 | Never invent symbols/sectors/indicator names | R2 |
| 3 | Never recommend changing the decision | R1 |
| 4 | Treat missing/null as *"not known"*, NOT as *"zero"* | Session learning |
| 5 | Be terse — short paragraphs/bullets | Token cost |
| 6 | Plain text — no markdown headers, no JSON | Output stability |
| 7 | Don't restate `SHARED_TODAY` signals unless an outlier | Pattern A |
| 8 | **Quantification:** every adjective must cite `(Field=value)` | Stop "modest"/"soft" filler |
| 9 | Cite each path exactly as it appears in the JSON | Stop `MlSignals.DirectionEdge` style hallucinations |

If you add a rule, also bump `MinSupportedSchemaVersion` if the rule
depends on a new dossier field.

---

## Known remaining weaknesses (for future passes)

- **"Closest gate" question was dropped** — the model was always guessing
  "Granville" since that's the only gate that emits warnings. Re-add only if
  `DecisionDossierBuilder` precomputes a real `ClosestGate` field with the
  narrowest pass margin.
- **Quantification rule is unverified.** Rule #8 *asks* the model to cite
  values; we don't *enforce* it post-hoc. A post-processing lint step
  (regex for adjective-without-citation) is a cheap Phase-3 add.
- **Bucketed labels would beat freeform adjectives.** Pre-compute
  `RelativeStrength.CompositeScoreBucket = "weak"|"neutral"|"strong"` from
  quantiles server-side; have the prompt parrot deterministic labels rather
  than coining its own.

---

## Where the code lives

| Concern | File |
|---|---|
| System prompt + per-pick/market builders + shared-context computation | `Core/Oracle/Prompts/DossierPromptBuilder.cs` |
| Dossier schema (and `CurrentSchemaVersion`) | `Core/Oracle/DecisionDossier.cs` |
| Dossier construction | `Core/Oracle/DecisionDossierBuilder.cs` |
| OpenAI client (handles GPT-5 quirks) | `Core/Oracle/Llm/OpenAiLlmClient.cs` |
| Provider selection / config | `Core/Oracle/Llm/LlmClientFactory.cs` |
| Cache lookup by `PromptHash` | `Core/Db/LlmNarrativeRepository.cs` |
| CLI + cache orchestration + markdown export | `Oracle/Program.cs` |
| User-secrets keys | `secrets.json` under `Oracle.csproj`'s `UserSecretsId` |

---

## Reading order for a cold pickup

1. `oracle-rules.md` — what is and isn't allowed.
2. `oracle-phases.md` — what is done and what's next.
3. **This doc** — why the prompts look the way they do.
4. `Core/Oracle/Prompts/DossierPromptBuilder.cs` — the actual prompts.
5. Last `[Oracle] ...` run output for the most recent date — concrete grounding.
