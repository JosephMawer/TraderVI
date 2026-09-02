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

### Q: What do MFE and MAE tell us about a long Delphi recommendation?
- **Domains:** math-statistics, risk-management
- **Source:** ADR-0026

**A:** MFE is the largest unrealized gain reached from the raw entry open; MAE is the largest unrealized loss and is stored as zero or a negative return. Together they show available upside and endured downside that the closing return can hide.

### Q: Why does Athena mark some excursion ordering as `SameSessionUnknown`?
- **Domains:** market-microstructure, risk-management
- **Source:** ADR-0026

**A:** A daily OHLC bar shows the session high and low but not which occurred first. Guessing the order could make a future target-and-stop policy look executable when the data cannot prove it.

### Q: How does Athena stop repeated official Delphi runs from overweighting one market session?
- **Domains:** architecture, math-statistics
- **Source:** ADR-0027

**A:** It first averages recommendations within each run, then averages runs within the shared `MarketDataAsOf` cohort, and finally gives each cohort equal weight. Reruns remain auditable but do not create extra independent cohorts.

### Q: Why are Continuation and Breakout tradeability results not combined?
- **Domains:** decision-engine, risk-management
- **Source:** ADR-0027

**A:** The lenses express different theses with different gates and rankings. Separate reports show which selection process produced the return, path risk, or no-entry result; combining them would hide the behavior Delphi needs to tune.

### Q: Why can ADR-0028's 10% and 20% loss levels not guarantee those sale prices?
- **Domains:** data-sources, market-microstructure, risk-management
- **Source:** ADR-0028

**A:** TMX evidence is delayed and the monitor is advisory, so the market may move further before the crossing is observed and a manual sale is possible. Paper evaluation must use a price available after detection rather than awarding the earlier threshold price.

### Q: What prevents the original Delphi recommendation from bypassing ADR-0028's 10% loss alert?
- **Domains:** decision-engine, risk-management
- **Source:** ADR-0028

**A:** The exception requires the newest valid OfficialPaper run that started after entry and was durably created before the decision bar. That exact run must publish the same symbol through Breakout with probability at least 60%, direction edge at least 10%, and down probability below 35%. A newer run that omits the symbol prevents fallback to older evidence; missing or original-entry evidence cannot qualify, and no signal bypasses the 20% alert.

### Q: Why must TMX intraday requests leave `freq` unset?
- **Domains:** data-sources, market-microstructure
- **Source:** ADR-0028

**A:** TMX's current chart code uses `interval` plus Unix `startDateTime` and `endDateTime` for intraday data. Sending the obsolete `freq = "minute"` combination produced daily fallback bars; omitting it returned valid 15-minute sessions.

### Q: Why does a TMX intraday batch record receipt time separately from the bar timestamp?
- **Domains:** data-sources, market-microstructure, risk-management
- **Source:** ADR-0028

**A:** The bar timestamp describes when market activity occurred, while receipt time describes when TraderVI could first act on that evidence. Without both, a delayed paper policy could be credited with information it did not yet have.

### Q: Why must a TMX consumer distinguish the newest returned bar from the newest completed bar?
- **Domains:** data-sources, market-microstructure, risk-management
- **Source:** ADR-0028

**A:** TMX includes the currently forming interval, and the market-hours probe observed that snapshot changing before completion. A mutable forming bar may be displayed for diagnostics but cannot be treated as final policy evidence.

### Q: Does TMX offering one-minute bars mean TraderVI should poll every minute?
- **Domains:** architecture, data-sources, risk-management
- **Source:** ADR-0028

**A:** No. Bar resolution controls how precisely the observed path is represented; polling cadence controls how often TraderVI asks for updates. The confirmed v1 cadence remains fifteen minutes. Completed five-minute storage is accepted because it remained gap-free and reproduced all nine comparable fifteen-minute bars exactly; one-minute evidence showed gaps.

### Q: Why is an intraday ghost-entry pilot not an official Athena tradeable outcome?
- **Domains:** architecture, market-microstructure, risk-management
- **Source:** ADR-0029

**A:** ADR-0021's official outcome uses the first eligible session open. A user-timed intraday entry has a different opportunity and fill convention, so mixing it into the official scorecard would make the evidence incomparable. The pilot tests the workflow but earns no promotion evidence.

### Q: Why may ADR-0029 observe a forming bar for entry but never use one for an exit-policy decision?
- **Domains:** market-microstructure, risk-management
- **Source:** ADR-0029

**A:** The user chooses the pilot entry at the current delayed observation, whose exact event and receipt state are recorded. An exit rule must be reproducible, so it waits for a completed direct fifteen-minute bar. Completed five-minute bars are retained later for finer evidence and exact aggregation checks, not substituted when a source slot is absent.

### Q: Why does ADR-0030 store both a poll observation and completed market bars?
- **Domains:** architecture, data-pipeline, market-microstructure
- **Source:** ADR-0030

**A:** Bars record what happened in the market; the poll observation records when and how TraderVI asked for and received that evidence, including empty or failed polls. Both are required to reconstruct a delayed decision without look-ahead.

### Q: What happens when TMX returns different OHLCV for an already stored completed intraday bar?
- **Domains:** data-pipeline, data-sources, risk-management
- **Source:** ADR-0030

**A:** TraderVI keeps the first completed evidence unchanged, marks the later poll invalid with a bounded conflict code, and surfaces the disagreement. It never overwrites the audit trail or selects the more favourable version.

### Q: Why does an automatic ghost exit use a new five-minute observation after the policy alert?
- **Domains:** market-microstructure, risk-management
- **Source:** ADR-0031

**A:** The trail or stop may have crossed before delayed evidence reached TraderVI. A later TMX receipt provides a price observed after detection; using the earlier threshold would award the paper trade a fill the system could not prove was available.

### Q: Why is the live WPF dashboard not allowed to implement its own exit rules?
- **Domains:** architecture, risk-management
- **Source:** ADR-0032

**A:** The console and GUI must use one shared, tested monitor. Keeping policy and persistence in Core prevents display code from creating a second trading behavior and lets either host reproduce the same result.

### Q: Does applying migration 012 make intraday monitoring durable by itself?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0030, ADR-0032

**A:** No. The migration creates the ledger. The shared collector must still fetch, validate, and append each poll before the dashboard can display durable receipt and completed-bar history.

### Q: Where should behavior live when both a CLI and a WPF tab expose the same capability?
- **Domains:** architecture
- **Source:** ADR-0033

**A:** In one host-neutral shared workflow returning structured results. The CLI owns text and exit codes; WPF owns presentation and interaction.

### Q: Does opening TraderVI automatically run Data Audit, Delphi, Hermes, or Athena?
- **Domains:** architecture, risk-management
- **Source:** ADR-0033

**A:** No. Tabs expose status and deliberate controls; opening the shell is not authorization to launch consequential workflows. The first Data Audit tab runs only when its read-only button is pressed.

### Q: What does opening or refreshing the Delphi tab do?
- **Domains:** architecture, risk-management
- **Source:** ADR-0034

**A:** It only reads and displays the latest persisted Continuation and Breakout recommendations. It does not evaluate symbols or write SQL.

### Q: Which component turns Delphi recommendations into paper buy, hold, and sell actions?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0034

**A:** The paper controller or Trade Manager. Delphi publishes the daily thesis; the intraday paper workflow manages positions under the accepted entry and exit policy.

### Q: Why does the Delphi workspace use a typed presentation snapshot instead of parsing its console report?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0035

**A:** The snapshot is a stable contract made from the same evaluated facts as the report. Parsing spacing and prose would make ordinary wording changes capable of breaking the GUI.

### Q: What is the difference between Delphi Overview and Full Report?
- **Domains:** architecture
- **Source:** ADR-0035

**A:** Overview answers what Delphi recommends and why at a glance. Full Report preserves the structured summary and detailed diagnostic text for copying and investigation.

### Q: Why does Project Docs resolve a local link against both the repository boundary and the current catalog?
- **Domains:** architecture
- **Source:** ADR-0036

**A:** Canonical root checking blocks path traversal, while catalog membership limits in-tab navigation to deliberately discovered Markdown and applies the same directory exclusions used during browsing.

### Q: Can Project Docs open a web page merely by loading or refreshing Markdown?
- **Domains:** architecture
- **Source:** ADR-0036

**A:** No. Only an explicit click on an HTTP(S) hyperlink may open the system browser; discovery, filtering, selection, rendering, and refresh are local and passive.

### Q: Why does an operator-confirmed paper entry require the actual fill price?
- **Domains:** architecture, market-microstructure
- **Source:** ADR-0037

**A:** A later TMX quote is not the operator's execution. Requiring the actual fill preserves faithful book cost, P/L, and exit evidence while avoiding an invented price.

### Q: What happens when a Breakout pick is manually added to Paper Trading?
- **Domains:** risk-management, decision-engine
- **Source:** ADR-0037

**A:** It is linked to its exact saved pick and monitored, but it is durably labelled exploratory. Continuation remains the production recommendation lens, and no selection automatically changes Delphi.

### Q: How can a model rank useful candidates while still being badly calibrated?
- **Domains:** machine-learning, math-statistics
- **Source:** ADR-0038

**A:** Its higher probabilities may correctly order events above non-events, producing useful AUC, while the probability values are systematically too high or low. Reliability buckets, Brier score, and expected calibration error test confidence honesty separately from ordering.

### Q: Why does the official prediction scorecard average candidates, then reruns, then market-session cohorts?
- **Domains:** architecture, math-statistics
- **Source:** ADR-0038

**A:** Candidates from one run share market exposure, and deliberate reruns over the same completed session are not independent evidence. Nested weighting keeps every run visible while giving each distinct `MarketDataAsOf` cohort equal final weight.

### Q: Why can a favourable diagnostic slice not automatically increase a Delphi signal's weight?
- **Domains:** decision-engine, machine-learning, math-statistics
- **Source:** ADR-0038

**A:** A slice is an association that may reflect correlated signals, regime concentration, repeated testing, or a small sample. It can justify a versioned challenger hypothesis, but only forward evidence and human approval may change the champion.

### Q: What does a TraderVI `REAL` row establish?
- **Domains:** architecture, market-microstructure, risk-management
- **Source:** ADR-0039

**A:** It records a broker fill reported by the operator, with an account label. It does not establish that TraderVI sent or verified the order, and it gives TraderVI no broker authority.

### Q: Why can an exit policy automatically close a Ghost row but not a Real row?
- **Domains:** architecture, risk-management
- **Source:** ADR-0039

**A:** A Ghost fill is simulated under TraderVI's accepted paper policy. A Real sale is true only after it occurs at the broker, so the dashboard must keep the signal open until the operator records the actual fill.

### Q: Why are Trading-tab P/L and Scorecards-tab calibration kept separate?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0039

**A:** Trading P/L depends on operator timing, execution mode, and fills. Official scorecards test immutable Delphi predictions against defined outcomes; mixing the ledgers would confound model calibration.

### Q: Which price does the delayed-intraday outcome receive after an exit alert?
- **Domains:** market-microstructure, math-statistics
- **Source:** ADR-0040

**A:** The raw fill is the open of the first five-minute bar beginning at or after the recorded detection time. The earlier policy threshold is never awarded as a fill.

### Q: Is the delayed-intraday outcome's 25-basis-point adjustment a Wealthsimple commission?
- **Domains:** market-microstructure, math-statistics
- **Source:** ADR-0040

**A:** No. The raw result is explicitly zero-commission. The separate 25-basis-point-per-side result is a conservative spread/slippage sensitivity used to test whether an apparent edge survives execution friction.

### Q: When does a delayed-intraday evidence gap become invalid rather than pending?
- **Domains:** architecture, data-pipeline, math-statistics
- **Source:** ADR-0040

**A:** It becomes invalid only when later evidence proves a required policy or exact fill bar was skipped, or when receipt order conflicts with event order. With no later proof, an apparent gap at the end of the available evidence remains pending.

### Q: Why can Athena not use the next later five-minute bar when the exact expected fill bar is missing?
- **Domains:** data-pipeline, market-microstructure
- **Source:** ADR-0040

**A:** That substitution would hide a data gap and choose a different price after seeing what evidence survived. Athena records the proven gap as invalid so every valid fill follows one reconstructible convention.

### Q: Why can relative-strength histories not be aligned by their minimum list length?
- **Domains:** decision-engine, data-pipeline, time-series
- **Source:** ADR-0041

**A:** Length contains no session identity. A short recent sector series and a long stock/XIU series can have the same selected count while representing different dates, and an interior gap can shift every later position. ADR-0041 aligns exact endpoints to canonical XIU sessions instead.

### Q: What does TraderVI do when an exact relative-strength endpoint is missing?
- **Domains:** decision-engine, technical-indicators, time-series
- **Source:** ADR-0041

**A:** The affected metric remains null and coverage reports the missing or stale-tail session. TraderVI does not clip, compress, forward-fill, or substitute another date, and pre-/post-correction official evidence keeps a visible code and strategy boundary.

### Q: Why are pre-ADR-0041 official runs excluded from the active strategy scorecard rather than marked invalid?
- **Domains:** decision-engine, math-statistics
- **Source:** ADR-0042

**A:** They faithfully record the earlier implementation, so they are valid historical evidence. They are excluded because combining them with the corrected strategy would describe two implementations as one unchanged system.

### Q: What changes when `v3.1-rs-date-aligned` is activated?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0042

**A:** The strategy and code identity changes, and active comparative cohorts restart under that identity. Thresholds, models, gates, ranking formulas, and execution policy are cloned unchanged.

### Q: How does TraderVI distinguish genuine zero active breadth from unavailable movers data?
- **Domains:** data-pipeline, data-sources, technical-indicators
- **Source:** ADR-0043

**A:** A genuine zero has a positive reported basket with equal advancing and declining counts. Unavailable data stores all three mover fields as null; `0/0/0` is never treated as a market reading.

### Q: Why can leadership not filter out missing mover rows and calculate over the remaining observations?
- **Domains:** decision-engine, technical-indicators
- **Source:** ADR-0043

**A:** Filtering would compress time across gaps and make stale observations appear consecutive. Leadership requires the newest 12 sessions to have contiguous mover coverage, otherwise the affected indicators remain neutral/no-data.

### Q: Why does the leadership-missingness correction start a new strategy identity?
- **Domains:** decision-engine, data-pipeline
- **Source:** ADR-0043

**A:** Preventing fabricated zero and falling votes can change Granville scores or gates. A new identity keeps pre- and post-correction evidence attributable even though thresholds, weights, models, and execution policy are unchanged.
