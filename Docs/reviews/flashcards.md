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

### Q: Why were Granville Dullness indicators #21 and #22 deferred instead of shipped?
- **Domains:** technical-indicators, decision-engine, math-statistics
- **Source:** ADR-0005

**A:** Calibrating Option C (volume + range + close-change all subdued) on XIU 2020-2026 fired ~2.72% of days but #21's h=5 hit rate was 32-34% — anti-predictive. A sensitivity sweep over D1/D2/D3 kept #21 stuck below 50% at every horizon, and tightening the prior-trend classifier from 5-day return sign to 20-day extreme-proximity didn't help. #22's bucket was n=3-12, too small to claim an edge. The most likely explanation is regime: 2020-2026 XIU is a bull run with one short correction, so quiet days after advances are pauses-before-continuation, not exhaustion. Revisit after backfilling 2001-2019 or moving to a different universe.

### Q: Why didn't tightening Dullness's prior-trend classifier (Path A → Path B) rescue Granville #21?
- **Domains:** technical-indicators, math-statistics
- **Source:** ADR-0005

**A:** Path A used the 5-day return sign; Path B used proximity to the 20-day high/low. Both produced #21 hit rates in the 32-34% band at h=5 — well below 50%. That ruled out hypothesis #2 ("the classifier was too crude") and elevated hypothesis #1 ("the rule is regime-dependent and 2020-2026 is the wrong regime"). No threshold combination over D1/D2/D3 flipped #21 above random, which is the signature of a sample/regime problem rather than a calibration problem.

### Q: Why do Genuity #17/#18/#19 short-circuit on flat XIU days?
- **Domains:** technical-indicators, decision-engine
- **Source:** ADR-0004 (Magnitude floor)

**A:** Below ~10 bps, `sign(return)` is dominated by noise (one-cent tick on a $50 bar is ~2 bps), so 'confirming' a near-zero XIU move tells us nothing about whether the day's tape is genuine. Same-day Genuity indicators require both `|XIU return|` and `|US return|` to clear the 10 bps floor; #20 is unaffected because it operates on the 5-day return.

### Q: After ADR-0011, what is the primary ranking key Delphi uses to pick the leader?
- **Domains:** decision-engine, technical-indicators
- **Source:** ADR-0011

**A:** `DirectionEdge + RScomp` — the raw relative-strength composite is added at equal weight to the model-implied `P(up10) − P(down10)`. `DirectionEdge` alone is the secondary tiebreaker, and the engine `CompositeScore` is the final fallback. Missing RS values default to 0.

### Q: Why does ADR-0011 use raw `RScomp` instead of the volatility-normalized `CompZ` for the additive ranking?
- **Domains:** decision-engine, math-statistics
- **Source:** ADR-0011

**A:** `RScomp` and `DirectionEdge` already share a comparable scale (~±0.2 typical), so a plain sum approximates equal influence without re-scaling. `CompZ` is in units of σ (~±2), so adding it directly would silently over-weight RS by ~10× and break the "equal weight" intent.

### Q: Why did the first sector-index backfill attempt skip every symbol despite finding no historical bars?
- **Domains:** data-pipeline, decision-engine
- **Source:** ADR-0012

**A:** It used `MAX([Date]) WHERE Symbol = …` as the resume cursor, but Hermes's daily snapshot path had already seeded yesterday's date for each `^TT*` symbol. The cursor reported "up-to-date" and the multi-year gap before the earliest stored bar stayed invisible. The fix is coverage-shape gating: trigger `[FULL]` when `count < 100` or `earliest > start + 30d`.

### Q: What latent SQL bug in `SectorIndexRepository.UpsertAsync` did the backfill work expose?
- **Domains:** data-pipeline
- **Source:** ADR-0012

**A:** The `SqlDbType.Decimal` parameters for `Price`, `PriceChange`, and `PercentChange` were created without `Precision`/`Scale`, which defaults to `(18, 0)`. Values were being truncated to integers despite the column being `DECIMAL(18, 4)`. Fixed by explicitly setting `Precision = 18, Scale = 4` on all three parameters.

### Q: In TraderVI, what is a "lens" and what three things define it?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0013

**A:** A lens is a self-contained way of viewing the universe, expressed as a `(thesis → gate stack → ranking key)` triple. Two lenses can share market-level inputs and per-symbol scoring yet produce different shortlists because they gate and rank differently.

### Q: In the multi-lens architecture, what is computed once and shared, versus what may differ per lens?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0013

**A:** Per-symbol scoring (composite, probabilities, `DirectionEdge`) and all market-level inputs (regime, breadth, Granville, RS) are computed once and shared. Only the gate stack (`TradePipeline`) and the ranking key (`PrimaryKey`) differ per lens.

### Q: Which lens drives the executed pick, which is journaled, and how are they distinguished in the DB?
- **Domains:** decision-engine, architecture
- **Source:** ADR-0013

**A:** Continuations is executed (B1) and emits dossiers/sizing; Breakouts is journaled only (B3) and writes picks without dossiers. They are distinguished by the `[Lens]` column on `dbo.DailyPick` (`'Continuation'` vs `'Breakout'`); read APIs default to `'Continuation'`.

### Q: Why was "one shared pipeline with a mode flag" rejected for serving breakout vs continuation theses?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0013

**A:** It re-creates the "tune one gate to do two jobs" anti-pattern, branches scoring/ranking with conditionals, and loses per-thesis attribution. A future third view would need another branch instead of just a new `LensDefinition`.

### Q: Which two patterns must both fire for `TrendConfirmationGate` to pass, and why is `Trend10` excluded?
- **Domains:** decision-engine, technical-indicators
- **Source:** ADR-0014

**A:** `Trend30` (multi-week uptrend) and `MaCrossover` (10/30 MA cross) must both be present. `Trend10` is excluded because it flips on routine pullbacks even while a name is still leading, so requiring it would reject healthy continuation candidates.

### Q: How does the Continuations ranking key differ from the Breakouts (ADR-0011) key, and why?
- **Domains:** decision-engine, technical-indicators
- **Source:** ADR-0014

**A:** Breakouts ranks by `DirectionEdge + RScomp` (equal-weight sum). Continuations ranks RS-first (`primaryKey = RScomp`) with `DirectionEdge` as confirmation, because the continuation thesis is *about* realized leadership, so RS should lead rather than be averaged 1:1 with forward probability.

### Q: In the Continuations lens, what role does breakout probability still play?
- **Domains:** decision-engine
- **Source:** ADR-0014

**A:** It is demoted to a soft composite input only — it no longer gates. The setup gate for this lens is `TrendConfirmationGate`, not the breakout `SetupGate`.



### Q: What two database rows does a single `buy` in TradeManager create, and what does each capture?
- **Domains:** architecture, risk-management
- **Source:** ADR-0015

**A:** A `TradeLog` row (the fill: symbol, BUY, shares, price, amount, commission 0) and an `ActivePosition` row (open risk: entry price, cost basis, `StopLossPrice` = entry x 0.90, `WarningPrice` = entry x 0.92). The sell side later realizes P&L on the position and closes it.

### Q: Why is live Wealthsimple routing not wired, and what runs instead in ghost mode?
- **Domains:** market-microstructure, architecture
- **Source:** ADR-0015

**A:** `WSTrade.PlaceOrder` needs a Wealthsimple `security_id`, which cannot yet be resolved from a ticker. Ghost mode (the default) prints a `[GHOST] Simulated Wealthsimple ...` line and only writes the database; non-ghost mode warns and still logs so the book stays accurate.

### Q: Which Delphi-linkage fields does ADR-0015 leave null, and what does that postpone?
- **Domains:** decision-engine, architecture
- **Source:** ADR-0015

**A:** `EntryComposite`, `StrategyVersionId`, and `OriginalPickId` are left null (manual entry only). That postpones model-vs-discretion attribution - comparing actual fills against Delphi's hypothetical pick for the same day.

### Q: Why is On-Balance Volume (OBV) kept out of the Granville #1–#56 plug-in framework?
- **Domains:** technical-indicators, decision-engine
- **Source:** ADR-0016

**A:** The Granville #1–#56 indicators are market-wide and read once per day. OBV is
*per-symbol* and *cumulative* (its value is anchor-relative, only the breakout shape matters).
Forcing it into that framework would break the framework's cohesion, so OBV lives as a
separate per-symbol indicator with its own table (`dbo.SymbolObv`) and classifier.

### Q: How does OBV influence the daily picks, and why isn't it in the engine composite?
- **Domains:** decision-engine, technical-indicators
- **Source:** ADR-0016

**A:** OBV is a **soft additive tilt** on each lens's ranking key (mirroring the RS pattern,
ADR-0011): `+ObvSignalWeight` when the field trend is Rising, `−ObvSignalWeight` when Falling,
0 otherwise. It is injected via `TradeDecisionEngine.ObvTilts`. It stays out of the engine
composite because that composite is built from **ML model roles** with registry weights; OBV
is a rule-based indicator, so the ranking-key seam (the same one RS uses) is the right place.

### Q: Why is pruning `dbo.SymbolObv`'s tail safe, and how does Hermes continue OBV across a gap?
- **Domains:** data-pipeline, time-series
- **Source:** ADR-0016

**A:** The running cumulative is already baked into each retained row, so deleting old rows
never changes the newer values — the newest retained row stays the anchor. Hermes
`UpdateObvAsync` reads that last stored `(date, obv)` via `GetLatestAsync`, seeds
`CalculateOBV` with it plus the prior close, and extends over every newer session, filling
multi-day gaps in one pass.

### Q: Why are active model names, data freshness, and current priorities kept out of root `AGENTS.md`?
- **Domains:** architecture
- **Source:** ADR-0017

**A:** They change more frequently than durable working rules. Root `AGENTS.md` controls how agents
work safely; `Docs/project-status.md` records what exists now, and `Docs/roadmap.md` records what
comes next. This avoids turning the instruction prompt into a stale second architecture document.

### Q: Why does Sandbox have its own nested `AGENTS.md`?
- **Domains:** architecture
- **Source:** ADR-0017

**A:** Codex layers instructions from the repository root toward the working directory. A nested
file applies the probe contract and side-effect rules only to Sandbox work while preserving the
root safety and validation rules, avoiding irrelevant probe detail in every other task.

### Q: Why is TraderDB.sqlproj built but never published under ADR-0018?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0018

**A:** The build validates that canonical schema definitions compile against SQL Server 2019. Publishing would broadly reconcile unresolved project/database drift and could rebuild or drop unrelated objects, so each live change is instead an individually reviewed manual migration.

### Q: What recovery window does TraderDB's SIMPLE model provide?
- **Domains:** data-pipeline, risk-management
- **Source:** ADR-0018

**A:** Recovery reaches the end of the newest successful full backup. Changes after that backup—such as later Delphi, Hercules, TraderVI, or Oracle writes—are exposed until the next full backup because SIMPLE recovery has no transaction-log backups.

### Q: Why does SQL Server write locally before a backup is copied to OneDrive?
- **Domains:** architecture, risk-management
- **Source:** ADR-0018

**A:** SQL Server writes as its service account while OneDrive syncs as the interactive user. Writing and checksum-verifying locally first prevents permission coupling and keeps OneDrive from treating an incomplete database-backup stream as a finished off-machine copy.

### Q: How does Hermes distinguish a successful data update with a failed backup from a fully protected run?
- **Domains:** data-pipeline, risk-management
- **Source:** ADR-0018

**A:** The completed database update remains in place, but Hermes prints a prominent backup failure and exits with code `2`. A OneDrive filename is published only after a temporary copy matches the verified staging backup's SHA-256; if copying fails, the staging backup is retained for manual recovery.

### Q: Why does Delphi reject stale history before model evaluation instead of using an `ITradeGate`?
- **Domains:** decision-engine, risk-management
- **Source:** ADR-0019

**A:** Freshness is an input-integrity invariant shared by every lens. If checked in a lens gate, relative strength, OBV, and model features would already have consumed stale bars. Pre-scoring exclusion ensures every downstream comparison uses the same completed TSX session.

### Q: How do DataAudit and Delphi's history-freshness eligibility rule differ?
- **Domains:** data-pipeline, decision-engine
- **Source:** ADR-0019

**A:** DataAudit is an independent read-only diagnostic for the whole local universe, including classification, mapping, bar integrity, and freshness. Delphi's rule is a narrow runtime defense that prevents a session-mismatched symbol from entering recommendation scoring even when DataAudit was not run first.

### Q: What exact event creates an official paper-calibration run?
- **Domains:** architecture, data-pipeline, decision-engine
- **Source:** ADR-0020

**A:** The successful append-only transaction that writes the complete `OfficialPaper` run, every model-evaluated candidate, and both lens evaluations after point-in-time inputs are known. The mutable `DailyPick` refresh is a separate operational write and neither creates nor rewrites that evidence identity.

### Q: Why does Athena use two different starting prices for prediction and tradeable outcomes?
- **Domains:** math-statistics, market-microstructure
- **Source:** ADR-0021

**A:** Model labels were trained from the observation-session close, so calibration must reproduce that contract. A real recommendation cannot fill at the same close that produced it, so paper execution starts at the first eligible session open after both the observation and run time.

### Q: What evidence is required before removing a safety gate?
- **Domains:** decision-engine, math-statistics, risk-management
- **Source:** ADR-0022

**A:** At least 120 matured official cohorts, a forty-cohort untouched forward window, adequate regime coverage, cohort-aware confidence bounds for improvement, and bounds showing no unacceptable lower-tail or drawdown deterioration—followed by explicit human approval and immutable version/ADR/code records.

### Q: Why can an opening move not automatically veto Delphi's completed-session recommendation?
- **Domains:** decision-engine, market-microstructure, risk-management
- **Source:** ADR-0023

**A:** The move may be new information, price discovery, or temporary execution noise. Opening direction must first prove incremental value as a versioned challenger; only input-integrity or execution-safety failures may be immediate hard exclusions.

### Q: Why are intraday wave trading and multi-day swing trading scored separately?
- **Domains:** architecture, market-microstructure, risk-management
- **Source:** ADR-0023

**A:** They use different holding periods, entry and exit rules, costs, risks, and opportunities. Combining them would conceal which policy produced the result and make Delphi impossible to tune honestly.

### Q: Why does Athena count prediction cohorts by `MarketDataAsOf` rather than Delphi run date?
- **Domains:** architecture, data-pipeline, decision-engine, math-statistics
- **Source:** ADR-0024

**A:** Weekend runs and deliberate reruns can use the same completed market session. They remain separate audit records, but counting them as independent cohorts would exaggerate the evidence available for tuning.

### Q: What is the difference between Athena's completion and usable coverage?
- **Domains:** data-pipeline, math-statistics
- **Source:** ADR-0024

**A:** Completion counts every terminal outcome, including invalid ones. Usable coverage counts only valid and degraded terminal outcomes; invalid and pending rows cannot support the reported performance score.

### Q: Why is `SwingMarkToMarket3` not the final Delphi swing exit policy?
- **Domains:** architecture, math-statistics, risk-management
- **Source:** ADR-0025

**A:** It records the value of a published pick at the first three session closes without applying a stop, target, trailing rule, or forced sale. It measures selector potential while the actual trade-management policy remains an open, separately versioned decision.

### Q: How does Athena treat a published pick that has no symbol bar in its first three eligible XIU sessions?
- **Domains:** market-microstructure, math-statistics
- **Source:** ADR-0025

**A:** It records terminal `NoEntry`, which counts as a valid execution observation but not as zero return. Return reports exclude it from averages and show the no-entry rate separately.
