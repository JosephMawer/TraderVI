# Copilot Instructions (TraderVI)

## System Goal

TraderVI is a daily-stock trading system for **short-term aggressive momentum rotation** on the TSX, with strict risk controls.

### Core Philosophy
- **Ensemble confidence**: Use multiple diverse signals (not redundant models) to increase conviction.
- **Aggressive rotation**: Continuously seek the best opportunity; don't hold losers.
- **Capital preservation first**: Stop-loss rules override all model predictions.
- **Iterative improvement**: Track experiments, version strategies, measure everything.

## Strategy Logic
- Keep **BreakoutEnhanced** as a setup filter.
- Determine direction using **BinaryUp10** and **BinaryDown10** with **DirectionEdge = P(up) - P(down)**.
- Optionally add **DirUp5** as a smoother "base drift" signal: **Edge = 0.3×P(dir) + 0.5×P(up) - 0.5×P(down)**.
- Apply down-probability veto for longs.
- Implement a market regime filter requiring benchmark (XIU) uptrend/positive return.
- Rank candidates by **DirectionEdge** then composite.
- Maintain the **Advance-Decline Line** as a rule-based market breadth/regime indicator, not a direct ML feature, unless explicitly revisited.

## Architecture

### Programs

| Program | Purpose | Frequency |
|---------|---------|-----------|
| **Hermes** | Import OHLCV data from TMX | Daily (after market close) |
| **Hercules** | Train/retrain ML models | Weekly or on-demand |
| **Delphi** | Evaluate universe, rank picks, output recommendations | Daily (pre-market) |
| **Sentinel** (planned) | Intraday monitoring, stop-loss execution, rotation triggers | Continuous (market hours) |
| **TraderVI** (later) | Automated order execution via Wealthsimple API | Event-driven |

#### Delphi: Reporting & Pipeline Requirements
- When adding any new signal, gate, indicator, or data source to Delphi's evaluation pipeline, update Core.Runtime.DelphiReportBuilder to include the new data:
  - Add the new property/data to BuildDiagnostic() for detailed, machine-parseable output.
  - Add the new property/data to BuildSummary() for human-readable summaries.
- Wire the new property where the report builder is constructed in Delphi/Program.cs so the builder receives the new data during evaluation.
- Keep diagnostics concise, consistent, and machine-friendly (typed fields, stable keys) to support downstream analysis and automated testing.

### Data Flow

## Decision Thresholds
- Document decision thresholds as initial defaults intended to be tuned based on Hercules training outputs (AUC, optimal threshold, decile lift), rather than fixed constants.

## Reference Documents
- **`Docs/design-rules.md`** — Authoritative rules for what is rule-based vs ML-based, active/rejected models, feature builders, decision flow, and thresholds. Consult before generating model or decision-engine code.
- **`Docs/system-design.md`** — Human-readable system reference with full context on architecture, experiments, and future direction.
- **`Docs/adr/`** — Architecture Decision Records. Lightweight, numbered (`NNNN-kebab-title.md`), tagged by domain. See `Docs/adr/README.md` for the template and domain taxonomy.
- **`Docs/concepts/`** — Conceptual explanations referenced by ADRs.
- **`Docs/reviews/`** — Self-test material: `flashcards.md`, `review-log.md`, `open-questions.md`.

---

## Decision-Making & Learning Methodology

The user is using TraderVI as a vehicle for learning across several domains
(ML, time-series, technical analysis, finance, etc.). The following rules
apply to **every** non-trivial design conversation, not just the first one.

### Conversational style
- **One question at a time** (or small groups of tightly related questions).
  Avoid dumping 5+ questions in a single turn — it makes progress feel tedious.
- **Restate context before each question.** Briefly (1–2 sentences) ground
  the question in the surrounding decision so the user understands *why*
  it's being asked, not just what's being asked.
- **Always provide a recommended answer** with reasoning, so the user can
  push back rather than guess what you'd prefer.

### Rephrasing as a learning checkpoint
- Before locking in any non-trivial decision, periodically ask the user to
  **rephrase the problem or proposed solution in their own words**. Frame
  this as a learning aid, not a comprehension test.

### After a decision is reached
- Propose an **ADR entry** under `Docs/adr/` for any meaningful decision
  (lightweight ADRs — granularity is a feature, not a bug).
- If the decision introduced a new conceptual idea, propose a matching
  **concept entry** under `Docs/concepts/`.
- Tag every ADR/concept with 1–4 domains from the taxonomy in
  `Docs/adr/README.md`.
- Update `Docs/reviews/flashcards.md` with 1–3 new Q/A cards per ADR.
- If the decision was deferred, log the open question in
  `Docs/reviews/open-questions.md`.

### Review mode
- When the user says **"review"**, **"quiz me"**, or similar, enter review
  mode: load the relevant ADRs, concepts, and flashcards, and ask questions
  one at a time. Optionally filter by domain tag if the user specifies one.
- After the session, append a summary to `Docs/reviews/review-log.md`
  noting strong/weak areas.

### What counts as "non-trivial"
- Anything that picks one approach over a viable alternative.
- Anything that introduces a threshold, magic number, or tunable parameter.
- Anything that adds a new dependency, table, file, or external service.
- Pure mechanical edits (renames, formatting, adding a missing using) do
  *not* require ADRs or rephrasing checkpoints.
