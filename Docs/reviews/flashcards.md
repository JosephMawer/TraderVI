# Review Flashcards

Question/answer pairs distilled from ADRs and concepts. Used in review
sessions ("quiz me" / "review").

## Conventions

- One card per `### Q:` heading.
- Tag each card with the same domain tags as ADRs/concepts.
- Cite the source (`Source: ADR-0001` or `Source: concepts/<file>.md`).
- Keep answers short — 1–4 sentences. The point is recall, not exposition.

## Cards

### Q: Why are Granville indicators implemented as per-category classes rather than one big class?
- **Domains:** architecture, technical-indicators
- **Source:** ADR-0001

**A:** Categories share derived state internally (e.g., Plurality #1–#4 all
reuse the advancers/decliners comparison) but have different data
dependencies between each other. Per-category grouping matches the natural
cohesion, allows incremental implementation, and isolates failures.

### Q: Why XIU instead of the raw S&P/TSX Composite?
- **Domains:** finance-fundamentals, decision-engine
- **Source:** ADR-0002

**A:** XIU is a tradable ETF with reliable TMX data, captures the high-volume
core of the TSX where the momentum strategy actually operates, and gives us
a single benchmark across all subsystems for consistency.

### Q: Why doesn't Granville's literal #15/#16 work on XIU?
- **Domains:** technical-indicators, finance-fundamentals
- **Source:** ADR-0003

**A:** Granville's #15/#16 exploit the DJIA's *price-weighted* construction,
where a few high-priced names can carry the index. XIU is cap-weighted,
which already neutralizes that distortion. We reformulated the *idea*
("narrow leadership predicts a stalled move") as ScoreB (top-3
concentration) + ScoreC (narrowness) instead of porting the rule directly.

### Q: What do ScoreB and ScoreC measure, and why do we need both?
- **Domains:** technical-indicators, math-statistics
- **Source:** ADR-0003, concepts/price-weighted-contribution.md

**A:** ScoreB measures how top-heavy the same-direction push was (top-3
share of |contribution|). ScoreC measures how many constituents disagreed
with XIU's direction. They aren't redundant: a move can be top-heavy
without being narrow (and vice versa). The AND-gate captures Granville's
"narrow *and* top-heavy" thesis, which is what predicts a stall.

### Q: Why is the Weighting indicator one-sided (fires only on up-days)?
- **Domains:** technical-indicators, decision-engine
- **Source:** ADR-0003

**A:** Backtest showed v1-rule down-day triggers had a positive 1d forward
return — i.e., a mean-reversion bounce, not a narrow-decline warning. The
indicator is interpreted as a long-side warning gate, so firing on down-days
would change its meaning. A down-side analogue, if ever justified, becomes
its own indicator — not an expansion of this one.

### Q: Why is the price-weighted contribution called a "proxy" rather than an index?
- **Domains:** finance-fundamentals, math-statistics
- **Source:** concepts/price-weighted-contribution.md

**A:** The sum of price-weighted `contribution_i` values does *not* equal
XIU's actual cap-weighted return. We use the proxy only to derive ScoreB
and ScoreC — structural descriptors of the day's move — not to predict
returns. The proxy preserves the Granville-style narrowness signal that
cap weights would smooth away.

### Q: What is Oracle and what can/can't it do?
- **Domains:** architecture, oracle
- **Source:** oracle-rules.md, oracle-phases.md

**A:** Oracle is a strictly *downstream* LLM narration/critique/Q&A layer
over `TradeDecisionEngine`. It reads persisted `DecisionDossier` rows and
writes `LlmNarrative` rows. It must never influence scoring, ranking,
sizing, or gates (Rule R1) and must never compute numbers — only cite
dossier fields by name (Rule R2).

### Q: Why is the `DecisionDossier` the audit unit, not the live runtime state?
- **Domains:** oracle, architecture
- **Source:** oracle-rules.md (R3, R9)

**A:** Reproducibility. "The LLM said X yesterday" is only falsifiable if
the exact inputs (dossier JSON), exact prompt text, model, provider, and
temperature are all persisted. The dossier is the contract; if a fact
matters to the reasoning it belongs in the dossier, never smuggled in via
globals or live reads.

### Q: Why does Oracle cache narratives by SHA-256 of the prompt?
- **Domains:** oracle, infrastructure
- **Source:** concepts/oracle-prompt-tightening.md

**A:** Two reasons. (1) Cost — re-running Oracle for the same date with an
unchanged prompt should not hit the API. (2) Iteration — when we tweak the
prompt template, *only* dossiers whose prompt actually changed regenerate;
everything else replays from the cache. The hash is stored in
`[LlmNarrative].PromptHash`.

### Q: What is `MarketSharedContext` and what problem does it solve?
- **Domains:** oracle
- **Source:** concepts/oracle-prompt-tightening.md

**A:** A per-batch struct listing every signal that fired on ≥ 70% of
today's picks (Granville warnings, Granville confirmations, ML/rule
confirmations). Each per-pick prompt is told to *not* restate those — only
deviations or outliers. Without this, every pick's narrative says the same
thing about Trend10/Trend30/MACrossover firing.

### Q: Why strip `Confidence` and `ExpectedReturn=0` from the dossier JSON sent to the model?
- **Domains:** oracle
- **Source:** concepts/oracle-prompt-tightening.md

**A:** Two distinct failure modes. `Confidence` often equals
`CompositeScore`, so the model double-cites the same number under two
names. `ExpectedReturn=0` is read literally as *"no anticipated profit"*
when it really means *"unset"*. `DossierPromptBuilder.ProjectPerPickView`
removes both before serialization.

### Q: What GPT-5-family API differences did Oracle have to handle?
- **Domains:** oracle, integration
- **Source:** concepts/oracle-prompt-tightening.md

**A:** Two: `temperature` is locked to 1 (must be omitted), and `max_tokens`
was renamed to `max_completion_tokens`. `OpenAiLlmClient` branches on
`model.StartsWith("gpt-5")`. Also: the full `gpt-5` requires OpenAI org
verification; `gpt-5-mini` does not, and is the practical default for
Oracle's structured-data workload.

### Q: What is the FK delete order when rerunning Delphi for a date?
- **Domains:** infrastructure, database
- **Source:** concepts/oracle-prompt-tightening.md

**A:** Child-first: `LlmNarrative` → `DecisionDossier` → `DailyPick`.
Reverse order trips `FK_DecisionDossier_DailyPick`. `Delphi/Program.cs`
encodes this; anything else that deletes by date must mirror it.

### Q: Why is news/fundamentals deferred to Phase 4 rather than added now?
- **Domains:** oracle, roadmap
- **Source:** oracle-rules.md (R5), oracle-phases.md

**A:** Each Oracle phase is intentionally standalone and valuable on its
own — Phase 1 is a useful audit log even without an LLM; Phase 2 narration
works on existing structured signals. News brings new problems (dedupe,
provenance, rate limits, schema-version bump) that would delay the
highest-value lowest-risk slice — so we ship the narration loop first and
add context later.

### Q: Why use US indices (^GSPC, ^NYA) to confirm XIU's daily move?
- **Domains:** technical-indicators, finance-fundamentals
- **Source:** ADR-0004

**A:** XIU and the TSX Composite are dominated by the same large-cap basket, so they're too autocorrelated to confirm each other. TSX large-caps are macro-correlated to US large-caps on a daily horizon, so US broad indices are an independent surface: when XIU moves without the US, the move is locally driven and less likely to persist.

### Q: Why does Genuity short-circuit when US bars trail XIU by more than one day?
- **Domains:** technical-indicators, data-sources
- **Source:** ADR-0004

**A:** Cross-border confirmation only means something when the bars compared belong to the same trading session. A 1-day tolerance covers the common 'Canadian close published before US bar appears' race; anything wider would falsely report confirmation or divergence on stale data, so Genuity emits a single Neutral 'Stale US data' result instead.

### Q: Why Yahoo's chart endpoint and not TMX or Stooq for US index data?
- **Domains:** data-sources, architecture
- **Source:** ADR-0004

**A:** TMX recognized the US symbols but returned empty OHLC; Stooq's free CSV download is gated by a captcha/API-key page. Yahoo's chart endpoint returns daily OHLC with no key/cookie/crumb (browser UA required). It's wrapped behind IUsIndexDataSource so it can be swapped without touching Genuity logic if Yahoo ever changes.

### Q: Why do Genuity #17/#18/#19 short-circuit on flat XIU days?
- **Domains:** technical-indicators, decision-engine
- **Source:** ADR-0004 (Magnitude floor)

**A:** Below ~10 bps, `sign(return)` is dominated by noise (one-cent tick on a $50 bar is ~2 bps), so 'confirming' a near-zero XIU move tells us nothing about whether the day's tape is genuine. Same-day Genuity indicators require both `|XIU return|` and `|US return|` to clear the 10 bps floor; #20 is unaffected because it operates on the 5-day return.
