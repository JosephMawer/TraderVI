# TraderVI System Design Reference

This document describes the current state of the TraderVI trading system — what's implemented, what was tried and rejected, and where things are headed.

For agent-enforced implementation rules, see `Docs/design-rules.md`. For the dated operational
snapshot and active priorities, see `Docs/project-status.md` and `Docs/roadmap.md`.

## AI Momentum learning and proposal boundary (ADR-0087)

Accepted target composition: portfolios own cash/holdings/history; engines supply versioned
signals/rules; AI proposes discretionary actions; one shared deterministic risk authority enforces
approved limits; one execution path rechecks current permission and submits orders. Each portfolio
has an explicit risk configuration and independent state. AI continuous rounds replace its engine's
daily-pause calculation; ordinary portfolios retain existing behavior. Stock stops, overall review
holds and account/data/order controls persist. Engine switches cannot reset risk or loosen the profile.
Extract only this needed boundary, reusing existing workflows. Protective exits do not wait for AI.
The offline round core below starts this work; shared engine/execution integration remains pending.
Saved holds require their established release rules.

`Core.Trader.Risk` now supplies `PortfolioRiskAssessment` and explicit profile identifiers, plus
`AiPortfolioRiskPolicy` for immutable AI round transitions. Complete current no-flow valuations
advance peaks and protection independently of model calls; closure and fresh re-entry evidence
are tied to the same portfolio/round, and exhausted sequences use `ITsxSessionCalendar`.
Independent blocks and a missing fresh AI review suppress allowance without cancelling closure.
The assessment is not executable permission. Native input qualification, cash-flow adjustment, full
account/position controls and adapters to existing engines remain pending.
`AiPortfolioRiskJournal` supplies a private local account/portfolio/mode-scoped transition history,
exclusive writer, explicit initialization, revision checks and idempotent command retries. Recovery
replays policy transitions and rejects inconsistent state or missing/corrupt/pending storage.
Writes flush before replacement/return; recovery restores round/sequence identity and peaks without
renewing AI review or execution authority. The host must block new risk on failure and still recover
independent protections. Full-history replay/rewrite is bounded at 32 MB; protected storage, retention,
backup rollback detection and production host integration remain unqualified. A checksum is not
tamper-proof storage. Existing overall holds, stock stops and broker/order history stay separate.

`BrokerAiPortfolioRiskMonitor` connects existing reconciled broker book/no-flow contracts to the
local journal offline. It validates scope, exact-time holding prices, uninterrupted flow coverage
and configured evidence age, derives portfolio value and retains canonical source provenance.
Rejected input blocks new allowance without erasing a latched closure; duplicate evidence does not
extend expiry. Initialization is explicit and storage errors propagate. The producer must still
prove native book/flow completeness; no production feed or freshness duration is qualified.

The broker and paper adapters share `AiPortfolioRiskObservationWriter`. Delphi Live paper consumes
its complete checkpoint plus matching current cash/positions/fills; post-checkpoint fills wait for
a later complete mark. Daily Shadow consumes an explicitly coherent full book and actual five-minute
bars matching every held symbol's saved price. Its stored timestamp can mean bar start or fill time;
the overview's freshest timestamp is insufficient. Both preserve saved source holds, capital and
generation history, with strict paper-account scope and source provenance. `PaperAiPortfolioRiskReader`
connects the existing Delphi Live snapshot reader and a new Daily Shadow read-only serializable SQL
read of the full book, assigned/fixed comparison rules and saved bars. It checks age after I/O and
propagates read failures/cancellation without resetting risk. Shadow evidence identity and times come
from saved content, not the read clock. SQL execution/concurrency and operating AI paper-account
integration remain unverified; ordinary engines are unchanged.

Source-only: `AiLearningAwareSupervisor` reads the shared private `AiLearningJournal`, records a
pending review and invokes `AiPortfolioReviewer` with a version-2 packet. Each scoped, evaluated lesson
gets an explicit assessment. `AiPortfolioExecutionCoordinator` rereads the saved decision and consults
current learning before preparing a proposal; changed context requires another review. Both expose
periodic consultation hooks with persisted last-consultation times. The budgeted API client remains
behind `ILlmClient`; routine learning consultation is local and incurs no model call.

There is no runtime host, automatic outcome evaluator, complete decision-packet adapter or order transport on
this path yet. The existing broker workflow and independent protection remain separate. A learning
check grants no execution authority. [ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md) defines
storage, provenance and operational prerequisites; project status owns validation evidence.

Planned learning production runs once after each trading session, reviewing completed trades and
blocked alternatives with sufficient follow-up evidence and leaving inconclusive cases pending.
Publish only qualified evaluated lessons for later decisions; supervisor/coordinator consultation
of saved lessons continues intraday. The review shares the monthly budget and cannot change hard
boundaries. Outcome evaluation, scheduling and restart qualification remain pending (ADR-0087).

Planned stock-action boundary: reuse engine hard-stop/trailing-profit settings, allow earlier sells
or tighter stops, and reject any loosening/removal in application validation and execution rechecks.
A separate non-executable record captures blocked alternatives and evidence for later learning;
actual and simulated outcomes stay distinct, and lessons cannot rewrite protection. Existing source
rejects hard-rule portfolio overrides and journals rejections; stock-action integration and outcome
comparison remain unimplemented (ADR-0087).

Planned AI switching reuses the existing handover contracts: retain mutually permitted holdings
and their protection, confirm/reconcile required exits before replacement buys, and preserve actual
costs/history plus the same round peak/reference/limit, attempts and review holds. Transfer protection
ownership once at the recorded activation boundary and log the switch reason/versions/outcome.
An all-cash transition does not reset risk. AI replacement and eligibility mapping remain pending.

Planned attention model (September 27): three scheduled rule consults coexist with ordinary Delphi
Live intraday monitoring and budgeted AI reviews of its evidence. The operator requests adaptive
AI attention frequency; a host-validated next-review proposal is the recommended MVP, not implemented
composition. Collection/bar timing, independent protective checks and paid model review cadence are
distinct. Accepted scheduled AI review bounds are one to fifteen minutes during market hours, subject
to budget. Initial earlier-review hooks are new qualifying entry signals, confirmed holding closures
and risk-limit changes to permitted actions. One extensible request path combines events arriving
together and applies the one-minute minimum and shared budget to current and future hooks.
These hooks are not implemented. Faster AI calls do not create fresher source bars.
Independent deterministic protection consumes every fresh verified whole-portfolio valuation,
including one-minute updates when qualified data exists, without waiting for an AI call. The app
owns protection and required collection; slower AI attention cannot throttle them. Failed polls or
repeated cached prices are not fresh evidence. Current five-minute bars alone cannot deliver
minute-level coverage; a suitable source/valuation path must be qualified in Steps 3/7.
All roles share the existing durable spending ledger, with no daily allocations or future-consult
reserves. Keep per-call cost reservations and token limits. An explicit operator allowance extension
must preserve recorded spend; a path above the current below-CAD-100 source limit is not implemented.
The agreed provider/budget fallback pauses
new buys, additions and switches while monitoring and existing exits continue. Recovery requires
a successful fresh review with all other limits satisfied; show the reason in the UI. This fallback
and its detailed timeout/recovery handling are not implemented.

Planned round lifecycle: persist sequence identity and attempt count across sessions/restarts, without
morning refill or renumbering. A carried round 2 stays at 3%; its failure permits round 3 at 1%
only after closure/reconciliation and fresh entry checks. After three failed and reconciled attempts,
even across several days, next-session eligibility permits
a fresh 5% / 3% / 1% sequence only with fresh qualifying entry evidence, AI review and all other
limits satisfied. The round loss fraction and exit trigger trail the highest verified value within
the round, initialized at its opening value. Carry its saved peak/trigger across closures and retain
the independent overall closing-peak reference and operator-controlled review holds.
Record original allocated capital plus each round's actual opening capital and initial trigger.
Later-round forecasts, if displayed, remain estimates; initialize enforced 3%/1% triggers from
actual reconciled capital when those rounds start. Valuation source/collection qualification remains unresolved.
Confirmed external capital flows reuse ADR-0083's matched after/before reference scaling for
effective round and overall drawdown references. Keep original capital/opening records and an
adjustment audit; derive the trigger from the adjusted peak. Preserve round/attempt state, holds
and stock protection. Cash-event source qualification and AI integration remain pending.
The UI must expose session pause, carried round, pending reconciliation and approval hold distinctly,
with attempts, opening/current values, round peak/trigger, next eligible session and unmet restart requirements. Enforcement
and UI remain pending (ADR-0087). Ordinary sales, profitable exits and all-cash periods preserve
the same round/peak/trigger and count. Only a round-limit hit consumes an attempt, including when
already in cash; reconcile before another round. UI attempt counts describe the continuous sequence,
not a new daily allowance. Selling/rebuying grants no success reset.

## Compact Live storage (ADR-0082)

Source now reconstructs Live portfolios from compact roots, immutable content and revision-scoped
item versions. Reports and unchanged observations are stored once; fingerprints preserve exact
historical reconstruction. All SQL portfolio readers/writers use this boundary. Migrations 037/038
are installed; authorized historical conversion, all-snapshot preservation checks and native restart
acceptance are complete. New WPF writes use the compact representation. See
[ADR-0082](adr/0082-compact-live-snapshots-and-backup-retention.md) and
[the maintenance procedure](../Operations/StorageMaintenance/README.md).

## Complete-strategy recommendations

[ADR-0084](adr/0084-external-broker-trade-synchronization.md) adds an atomic projection from validated
broker order aggregates into the Real trade/position ledger. Immutable synchronization receipts retain
the original reviewed baseline plus the expected resulting financial fingerprint. Desktop refresh
checks every five minutes while signed in and updates dependent account/history views after commit.
September 25 joins this projection with the shared broker-account read and strategy controls. A single
scheduler handles manual/post-trade coalescing and failed-read backoff; display and history use the same
completed reconciliation. Linked-account saves retain the moving-price fix; manual review still rechecks facts.
Migration 040 is prepared; installation/native acceptance remain pending. This extends the history-only
ADR-0080 boundary below while preserving original imports, risk state and paper accounts.

[ADR-0081](adr/0081-broker-account-baselines-and-protection-ownership.md) adds initial broker risk
baseline preparation and a reviewed save/recovery workflow in source. The repository reads existing
import/report/recommendation state under the same account fences, preserves known holds/protection,
and prepares one immutable initial record with source provenance. Migrations 035/039/040 are installed;
operational baseline setup and native risk-source qualification remain unfinished. Source/tested
continuation adjusts confirmed capital flows while preserving loss percentages and holds. Protection
transfers at activation with established levels. The baseline subsystem itself sends no orders.

[ADR-0085](adr/0085-portfolio-wide-ongoing-tfsa-management.md) adds an immutable explicit duration to the
existing broker execution approval. The same durable assignment/order history can span checked days
without upgrading old single-session approvals or replenishing the reviewed purchase ceiling. Core
recovery replays missed protection and required closing exits; missing evidence blocks execution.
Separate saved-mode controls reuse the account lease and have no order transport. WPF restores those
controls only after linked-account refresh. Execution-host startup, Delphi Live protection, ownership
roles, replacement and native qualification remain unfinished; no automation is enabled.

[ADR-0080](adr/0080-reviewed-broker-history-import.md) adds an explicit first import review and repeatable
SQL broker history, under the existing account transaction fences. BrokerAccountImportBinding pins the
permanent TFSA identity and legacy ledger fingerprint; BrokerAccountImport preserves complete cumulative
order/holding observations, reviewed legacy references and request receipts. Stable order IDs prevent
double counting within a snapshot; readers must never add totals across snapshots. Existing tracked
positions, individual trade records, account reports and protection memory remain unchanged. Migration
034 is installed; the account's 32-order import and linked native refresh are verified. The engine's source-aware account projection and baseline/protection
work are still required before using these facts for strategy execution.

[ADR-0079](adr/0079-broker-reconciliation-and-market-handover.md) adds an explicit broker reconciliation
workflow. It discovers historical order IDs from the owned TFSA activity feed, validates cumulative
order details, reconstructs shares/costs and compares them with fresh holdings. A final activity-page
checkpoint rejects changes during the read. Previous protected observations and saved manual orders
are checked for missing or changed facts. The shared order gate and encrypted directory lease cover
reads and append-only reconciliation snapshots. Transition review consumes this result and rechecks
the source portfolio and broker snapshot. No SQL position or trading authority is changed.

The first portfolio execution policy is now regular-session whole-share market orders, with required
non-overlap sells reconciled before new buys. Broker-to-strategy account projection, complete cash/risk baselines,
inherited protection, automation qualification and a durable activation/execution coordinator remain.

Manual brokerage execution is now a separate WPF path under
[ADR-0075](adr/0075-manual-market-buy-ticket.md). Reusable selection actions carry immutable context to one
ticket/coordinator. The operator supplies the whole-share quantity; Core rechecks broker facts and
existing pause/financial holds, saves a protected reviewed intent, then calls the one-attempt market-buy
client. Recovery reads cumulative fills by the original request ID. This does not activate a portfolio,
assign automatic exits or copy fills into the tracked-position SQL ledger. Native live order acceptance
and complete-account reconciliation remain unverified/unfinished.

[ADR-0076](adr/0076-wealthsimple-market-sell-protocol.md) extends the broker client with whole-share
market sells. Buy and sell now share transport, scoped cumulative-fill parsing and compatible durable
attempt markers, while checking `buyable` or `sellable` respectively. The buy API and protected manual
order format remain intact. [ADR-0077](adr/0077-manual-holdings-market-sell.md) connects an explicit
whole-holding sell action to fresh quote/holdings/order checks and protected durable sell records.
Both manual workflows share their submission lock and block on unresolved buys or sells. Terminal
sells require matching remaining holdings before releasing the local reservation. The limit ticket
is a non-submitting preview; full account/SQL reconciliation and automatic exits remain separate.

[ADR-0062](adr/0062-complete-strategy-recommendation-promotion.md) implements Portfolios review and
account-specific Trading assignment. Pure family evaluation consumes an immutable complete strategy and
reported Real state; SQL handles fenced, append-only review/publication history. It shares calculations
and source evidence with paper engines, not their accounts or fills. Manual performance approval is the
operator's decision. Broker automation and enforced common evidence gates remain future work.

## Fixed-gate paper comparisons

[ADR-0067](adr/0067-fixed-gate-paper-comparison-pairs.md) adds five prospective account pairs without
changing daily scoring or official outcomes. `PaperComparisonBatch` and `PaperComparisonAccount` pin
the independent capital, profile and portfolio-rule identities. `PaperComparisonSourceRevision` stores
one hashed publication/baseline snapshot for a whole batch. The leased Live source coordinator adopts
that source and all eight Shadow candidate sets in one transaction; Live overlays candidate admission
for each of its two comparison policies while sharing collection. Account cash, orders, lifecycle state
and protection stay in their original engines' separate ledgers. A new publication resets pending entry
qualification prospectively. Comparison activity is excluded from original clean research promotion
cohorts. WPF presents fixed pair identities and aligned observed closing periods; these accounts cannot
be assigned through Shared Settings or promoted through the recommendation review.

## Philosophy

The accepted product direction is [independent strategies to approved live execution](adr/0055-independent-strategies-to-approved-live-execution.md):
prove complete strategies in separate paper accounts, compare evidence fairly, and explicitly select one
for real-account recommendations before adding separately authorized Wealthsimple execution. The
[concept and diagram](concepts/strategy-promotion-and-execution.md) explain these relationships. The
[2026-09-06 static review](reviews/strategy-direction-alignment-20260906.md) identifies implementation gaps;
the existing Delphi Live Operational Champion is a paper-policy role, not a universal production selection.

TraderVI is a **short-term aggressive momentum rotation** system on the TSX. The core idea:

- Use **multiple diverse ML signals** (not one model) to build conviction
- Layer **rule-based safety gates** on top to preserve capital
- Rotate aggressively into the single best opportunity
- Never hold losers — stop-loss overrides all models

## What Is Rule-Based vs What Is ML-Based

### Rule-Based Components

These are hand-coded logic gates that override or modulate the ML signals:

| Gate | What It Does | Implementation |
|------|-------------|----------------|
| **XIU Regime Filter** | Blocks longs when XIU MA50 < MA200 AND 20d return < 0 | `TradeDecisionEngine.ComputeRegime()` |
| **A/D Line Breadth** | Blocks/warns longs when market breadth is deteriorating | `AdvanceDeclineCalculator.BreadthScore()` |
| **Down-Probability Veto** | Blocks longs when P(down ≤ -4%) reaches the configured maximum (default 35%) | `DownProbabilityGate` |
| **Stop-Loss** | Hard exit at -10% drawdown from entry (overrides all) | Planned in Sentinel |
| **Drawdown Warning** | Alert at -5% drawdown | Planned in Sentinel |
| **Rotation Threshold** | Only switch positions if new pick is sufficiently better | Strategy config |
| **Trend Confirmation** | Continuation lens requires Trend30 and MA-crossover confirmation | `TrendConfirmationGate` |
| **History Freshness Eligibility** | Excludes a symbol unless its newest bar matches the canonical XIU session | `HistoryFreshnessEligibility` |

**Key decision**: The A/D Line is maintained as a **rule-based regime indicator**, not a direct ML feature. This was explicitly decided because breadth is best used as a binary/graded gate, not a noisy input to gradient boosting.

### Hybrid Components

| Component | Rule-Based | ML | Status |
|-----------|-----------|-----|--------|
| **Relative Strength** | Ranking signal + future gating | LightGBM feature columns | Ranking active; ML planned after backfill |

### ML Components

These are trained models that produce probability scores:

**Active models (4 profit):**

1. **BreakoutEnhanced** — "Is a breakout above the 20-day high happening?" — Setup probability and the Breakout lens's primary setup gate.

2. **BinaryUp10** — "Will return ≥ +4% in the next 10 days?" — Direction signal.

3. **BinaryDown10** — "Will return ≤ -4% in the next 10 days?" — **Veto signal**. High P(down) blocks longs.

4. **VolExpansionRelative10** — "Will volatility expand significantly?" — Confirmation that a big move is expected.

`RelStrengthCont10_2pct` is retained as a disabled experiment. Relative strength is currently
computed deterministically by Delphi and used directly in lens ranking.

**Trend10 / Trend30 / MaCrossover** are deterministic pattern-presence checks. They are evaluated
directly from price history and are not trained, saved as model artifacts, or loaded from SQL.

**How they combine:**
- `DirectionEdge = P(up) - P(down)` — directional conviction and a ranking component in the Breakout lens.
- `Composite = 0.40×Breakout + 0.25×Up − 0.20×Down + 0.15×VolExp + Granville adjustment`.
- Relative strength and the OBV tilt are added in the lens ranking key, not the ML composite.
- CLX is diagnostic-only in v1.

## Feature Builders

Each ML model uses a **feature builder** to convert a window of daily bars into a float array:

| Builder | Key Features | Models Using It |
|---------|-------------|-----------------|
| **AtrVolatilityBreakout** | Normalized price/volume sequences, ATR%, breakout distance, momentum, SMA distance | BinaryUp10, BinaryDown10, VolExpansion |
| **Enhanced** | Above + RSI, MACD, Bollinger bands, OBV, relative strength vs XIU | BreakoutEnhanced |
| **MarketContext** | XIU momentum, volatility regime, beta, relative perf at multiple horizons, info ratio | RelStrengthCont10_2pct |
| **Direction** | Direction-specific features | All DirUp5 variants (currently disabled) |
| **TrendMomentum** | RSI, MACD signal/histogram, Bollinger %b/width, OBV trend, relative strength | Nested inside Enhanced |

## Tried and Rejected

| Experiment | What We Tried | Why It Failed |
|-----------|---------------|---------------|
| **Regression (ExpectedReturn)** | Predict exact 5d/10d return | R² negative — model worse than predicting the mean |
| **3-Way Classification** | Predict Buy/Hold/Sell | 40% accuracy — couldn't separate three classes |
| **DirUp5 (direction > 0)** | Predict if 5d return is positive | AUC 0.54 — essentially random |
| **Banded Direction** | Only label "meaningful moves" (> 0.5 ATR) | AUC ~0.52 — still random |
| **Setup-Conditional Direction** | Train direction only on breakout bars | AUC 0.52–0.53 — insufficient signal |
| **Meta-Labeling (BreakoutMeta)** | Predict if breakout leads to profit | AUC 0.52 — no discriminating power |
| **Volume-Conditional Meta** | Same but require volume surge | AUC 0.52 |
| **Risk-Adjusted Up/Down** | Label by return/ATR ratio | AUC 0.52–0.53 |
| **Triple Barrier** | Asymmetric profit/stop barriers | AUC 0.51 |
| **MarketContext features on BinaryUp** | Add XIU features to up-probability model | AUC 0.64 (worse than base 0.70) |
| **Enhanced features on BinaryUp** | Use richer feature set | No improvement over AtrVolatilityBreakout |
| **RelStrength 1% threshold** | Lower outperformance bar | AUC 0.60 (2% threshold is 0.65) |

**Lessons learned:**
- Short-horizon direction (5d) is very hard to predict on TSX
- Adding more features doesn't always help — simpler feature sets often win
- Setup-conditional training reduces sample count too much for TSX universe size
- Meta-labeling needs more signal in the base model to work
- Tail-event models (≥ +4%, ≤ -4%) work better than direction (> 0) models

## Current Decision Flow

Before shared and lens-specific gates, Delphi establishes the latest XIU bar as the canonical completed TSX session and removes any symbol whose newest bar is from a different date. This pre-scoring invariant prevents stale bars from reaching relative strength, OBV, models, or ranking (ADR-0019).

`TradeDecisionEngine` then evaluates every pattern and trained profit model exactly once per eligible symbol.
The resulting composite, probabilities, direction edge, pattern facts, and signal list are shared unchanged
across lenses; each lens receives a fresh gate context and applies only its own gate stack and ranking key
(ADR-0013).

Shared gates: regime → A/D breadth → Granville → down-probability veto.

The setup stage then diverges by lens:

- **Continuation (executed):** Trend30 + MA-crossover confirmation → direction gate → composite gate → rank by `RScomp + obvTilt`.
- **Breakout (journaled):** BreakoutEnhanced setup gate → direction gate → composite gate → rank by `DirectionEdge + RScomp + obvTilt`.

Both rank remaining ties by DirectionEdge and composite score. The executed recommendation is sized as a single concentrated ghost/advisory position. CLX is reported but does not affect this flow.

## Tracked-position monitoring flow

The operational Ghost/Real ledger is deliberately separate from official Athena calibration outcomes. `TraderVI.WPF` and the `TraderVI paper-monitor` command call the same Core monitor:

1. Poll TMX for direct completed 15-minute policy bars and append the request receipt/evidence to the ADR-0030 ledger.
2. Replay the deterministic ADR-0028 policy from the tracked entry through the completed evidence.
3. Poll five-minute TMX data after policy evaluation, append that receipt/evidence, and update the active-position snapshot.
4. If the completed policy produces an exit alert for a Ghost position, record a database-only simulated sale at the newest separately observed delayed price through the ADR-0031 serializable transaction. For Real, display a manual-action signal and leave the holding open.
5. Refresh the WPF dashboard from SQL so explicitly labelled Ghost/Real positions, separate P/L, trades, and receipts remain visible after restarts.

Automatic Ghost exits never route to a broker. ADR-0039 adds durable mode/account fields plus an immutable Ghost-to-Real reconciliation audit; all pre-migration rows default to Ghost, and Real fills are operator-reported facts only. The WPF process must remain open for its five-minute collection schedule; the decision policy still consumes completed 15-minute bars, and always-on service hosting is deferred. The tabbed shell contains Trading, Data Audit, Delphi, Scorecards, and Project Docs. The six-view Delphi workspace loads persisted picks and the matching typed presentation snapshot without evaluation; only a confirmed official action invokes its database-writing workflow. Project Docs remains file-only. Scorecards reads official calibration SQL through the same pure calculator as Athena and cannot write outcomes or alter Delphi.

## Delphi Live flow

[ADR-0053](adr/0053-delphi-live-v1.md) adopts the frozen [Delphi Live V1 design](concepts/delphi-live.md).
`DelphiLiveMonitorWorkflow` in Core owns session scheduling, lease recovery, collection, deterministic
evaluation and per-portfolio action sequencing behind injected stores, market data, clock/calendar and
notifications. `DelphiLiveDesktopService` composes the first WPF host; UI selection never determines
whether an activated monitor ticks.

At a regular-session boundary the session repository freezes one eligible daily source and the
published Top-25 union, exact policy assignments and point-in-time daily baselines. The collection
repository stores one expected symbol/checkpoint slot, receipts and canonical five-minute observations
with deadline disposition and process-continuity evidence. Every assigned policy references those same
market facts; each portfolio owns its confirmation, cash, orders, positions and immutable ledger revisions.
Protective sells precede buys and every decision precedes its fresh simulated-fill quote.

Research outcomes and lens-specific Daily/Live Top-5 baskets are separate from executable portfolio
returns. The experiment protocol creates aligned cash-only controls and challengers, records cohort
coverage, and permits a human-approved policy change only at a later session boundary. A promotion
carries the operational positions and preserves their original entry evidence. Additive migrations
022–025 define this storage; applying them and activating monitoring remain separate operational steps.

## Official prediction scorecard flow

Athena joins the immutable `OfficialPaper` candidate population to `PredictionLabels10` version 1 and validates definition, purpose, strategy identity, cohort, candidate, lens, rank, schema, session, and event identity before reporting performance. ADR-0042 scopes every comparative query to the one active strategy carrying `InitialCodeCommit` and `DecisionRef`; earlier identities remain immutable and queryable but are counted as excluded rather than pooled. ADR-0038 averages candidates inside a run, reruns inside `MarketDataAsOf`, and market-session cohorts equally. It produces four model-calibration reports, separate eligible Continuation/Breakout rank reports, and descriptive technical/market slices only after the 95% coverage floor is met. Export-schema-v2 CSV artifacts stamp the same strategy identity and are optional outputs of the pure calculator. Operational ghost positions, actual broker holdings, and manual fills never enter this report.

ADR-0051 adds a second, explicitly separate evidence path: four System-selected Shadow portfolios. The WPF
host freezes the valid 09:30 `OfficialPaper` ranks for each lens and maximum basket size, persists completed
intraday source receipts, records a decision as a pending virtual order, and resolves it only from a later
five-minute bar. A buy may use only its exact immediate fill bar; a missed window cancels the order and
requires current requalification. Positions persist the last consumed fifteen-minute bar start so trailing
state is idempotent and a bar cannot test its earlier low against a stop that the same bar just established.
The Shadow ledger owns its own generations, sessions, candidates, positions, orders, capital snapshots, and
audit events. It never writes Athena outcomes or the operator Ghost/Real position tables, and neither of
those ledgers is treated as a substitute for Shadow performance.

ADR-0039's operational ledger persists `Ghost` or `Real` plus an account label for Real and displays the modes separately. This state never enters the official prediction report. `Real` is not broker identity or verification: no order, balance, import, partial-fill, or broker adapter exists. Migration 013 was applied and verified manually on 2026-08-28; all legacy operational rows were deliberately classified Ghost. A real holding is represented only by a separately confirmed Real entry or audited active-position reconciliation, never by inference from a Ghost lifecycle.

## Granville Market Timing Layer

TraderVI is incrementally implementing Granville's day-to-day market indicators as a **rule-based**
overlay on top of the ML ranking stack.

### Active Granville groups

- **Plurality (#1–#4)** — based on advance/decline breadth vs `XIU`
- **Disparity (#5–#6)** — TSX-adapted real-economy divergence signal
- **Leadership (#7–#10)** — market leadership quality vs directional state
- **Most Active / Features (#11–#14)** — most-active stock behavior versus the tape
- **Weighting (#15–#16)** — narrow-advance warning
- **Genuity (#17–#20)** — confirming US index behavior
- **Light Volume (#25–#28)** — light-volume tape interpreted with leadership quality

### Disparity adaptation for TSX

Granville's original Disparity concept used **Dow Transports vs Dow Industrials**.
On the TSX, that exact structure does not exist, so TraderVI uses a modernized equivalent:

- **Cyclical basket**: `Energy + Industrials + Materials`
- **Benchmark**: `XIU`
- **Timeframes**:
  - 1-day percent change
  - 5-day rolling return

Interpretation:
- cyclical basket weaker than `XIU` → short-term bearish divergence
- cyclical basket stronger than `XIU` → short-term bullish confirmation

`XIU` is intentionally used for now because it is already available in the breadth/Granville pipeline.
A future revision could swap this to the raw TSX 60 index symbol if needed.

### Leadership adaptation for TSX

Granville's Leadership concept asks: **are the most influential stocks still outperforming, or is the
market advance (or decline) being driven by a narrowing base?** In cap-weighted indexes like the TSX,
a few large names can sustain the index while internal participation deteriorates.

TraderVI defines leadership through three complementary layers:

**Layer 1 — New-High/New-Low Breadth** (`NHNL_10`):
Are actual winners still being produced? Computed from stored OHLCV data using a 252-trading-day
lookback via `NewHighLowCalculator`. A healthy advance shows expanding or stable new highs with
contained new lows. Smoothed with EMA-10 to reduce daily noise.

**Layer 2 — Active-Stock Breadth** (`ActiveBreadth_10`):
Where is the urgent capital going? Top-50 stocks by **dollar volume** (price × shares, a better
proxy for capital flow than raw share count) are fetched daily via `GetMarketMoversAsync("dollarvolume")`.
The ratio of advancers to decliners in this basket is smoothed with EMA-10.

**Layer 3 — Large-Cap Relative Strength** (`LargeCapRS_20`):
Are the biggest names outperforming or underperforming the broad market? Measured as the 20-day
return of XIU (TSX 60 proxy) minus the 20-day return of `^TXCE` (S&P/TSX Composite Equal Weight
Index). Strong leaders are fine; **isolated** leaders (TSX 60 up, equal-weight down) are a warning.

**Important**: `^TXCE` is the Composite Equal Weight Index, not `^TXEW` (which is TSX 60 Equal Weight).
Both are defined in `TsxBenchmarkSymbols.cs`.

**State determination** (upswing/downswing):
These are not single up/down days. They represent directional legs defined by the composite
leadership series:
- **Upswing**: ≥ 2 of 3 series rising, none deeply negative (threshold: −0.10)
- **Downswing**: ≥ 2 of 3 series falling, NHNL_10 < 0
- **Indeterminate**: mixed or insufficient data

**Quality determination** (improving/deteriorating):
The 3-point slope consistency of NHNL_10 and ActiveBreadth_10 determines whether leadership
quality is trending up or down.

**Indicator signals**:

| # | Condition | Signal | Interpretation |
|---|-----------|--------|----------------|
| **7** | Quality deteriorates + upswing | Bearish (−1) | Near-term decline likely |
| **8** | Quality deteriorates + downswing | Bullish (+2) | Near-term advance likely (exhaustion) |
| **9** | Quality improves + downswing | StrongBearish (−1) | Decline likely to continue |
| **10** | Quality improves + upswing | StrongBullish (+2) | Advance likely to continue |

**Data pipeline**:
- Hermes stores daily `LeadershipSnapshot` to `[dbo].[LeadershipData]`. A complete, distinct top-50
  movers basket is attached only to a computed session matching the current local date because the TMX
  response has no as-of field; historical, stale-target, malformed, or unavailable mover fields are null.
- Delphi loads 50-day history → `LeadershipCalculator` → `LeadershipIndicators`.
- Graceful degradation (ADR-0043): Leadership #7–#10 and Light Volume #25–#28 require 12 trailing
  contiguous active-breadth observations. Missing rows break coverage instead of being filtered out;
  unavailable and flat layers cast no falling vote and produce explicit neutral/no-data output.

### Composite point range

The `GranvilleComposite` normalizes net Granville points to a `CompositeAdjustment` in
[−0.10, +0.10]. The theoretical raw point range across all active groups:

| Group | Max Bullish | Max Bearish |
|-------|------------|------------|
| Plurality (#1–#4) | +4 | −2 |
| Disparity (#5–#6) | +2 | −2 |
| Leadership (#7–#10) | +4 | −2 |
| Most Active (#11–#14) | +3 | −3 |
| Weighting (#15–#16) | 0 | −1 |
| Genuity (#17–#20) | +4 | −4 |
| Light Volume (#25–#28) | +2 | −1 |
| **Normalization headroom** | **+19** | **−15** |

## Relative Strength Layer

### Design

RS measures how a stock performs relative to its sector and the market across multiple horizons:

| Feature | Formula |
|---------|---------|
| `RS_StockVsSector_{h}d` | `Return(stock, h) - Return(sector_index, h)` |
| `RS_StockVsMarket_{h}d` | `Return(stock, h) - Return(XIU, h)` |
| `RS_SectorVsMarket_{h}d` | `Return(sector_index, h) - Return(XIU, h)` |
| `RS_Z_*` | `(RS_today - mean(RS_20d)) / std(RS_20d)` |

Horizons: 5d, 10d, 20d, 60d.

XIU defines the canonical completed-session sequence. Live stock, sector, and XIU observations retain
their dates, and every return or rolling Z-score uses exact canonical endpoints. Missing or ambiguous
duplicate stock/sector endpoints leave only affected metrics unavailable; duplicate or invalid XIU data in the required
61-session canonical window fails the RS stage (ADR-0041).

### Composite Score
`0.5 × RS_StockVsMarket_10d + 0.3 × RS_StockVsSector_10d + 0.2 × RS_SectorVsMarket_10d`

Weights are initial defaults, intended to be tuned via Hercules feature importance.

### Lifecycle
- **Delphi**: computes live from dated in-memory price observations (does not read saved RS features)
- **Hermes**: backfills historical to `[dbo].[RelativeStrengthFeatures]` (planned — stock-vs-XIU is fully backfillable from 2020; stock-vs-sector only from when sector collection started)
- **Hercules**: reads from DB, includes RS columns as LightGBM features (planned)

### Sector data limitation
`[SectorIndices]` only has data from when Hermes started collecting. For historical backfill:
- Stock vs XIU features are available from 2020 (full DailyBars history)
- Stock vs Sector features are only available from sector collection start date
- Delphi explicitly falls back to XIU only when no sector series is available. In that case
  `RS_StockVsSector` equals `RS_StockVsMarket`, while `RS_SectorVsMarket` is zero; the fallback is
  counted and labelled rather than presented as real sector evidence.

## TSX Sector Map

TraderVI now maintains its own stock → sector-index mapping rather than depending on TMX to expose
direct index membership.

### How it works

For each active TSX stock:
1. Hermes calls `TmxClient.GetQuoteDetailAsync(symbol)`
2. Reads TMX `sector` and `industry`
3. Normalizes the sector using `TsxSectorMap`
4. Stores:
   - stock symbol
   - sector
   - industry
   - mapped TSX sector index symbol
   - last refresh timestamp

### Sector symbols

| Symbol | Sector | Confirmed |
|--------|--------|-----------|
| `^TTEN` | Energy | ✅ TMX Money |
| `^TTFS` | Financials | ✅ |
| `^TTHC` | Health Care | ✅ TMX Money |
| `^TTIN` | Industrials | ✅ |
| `^TTTK` | Technology | ✅ |
| `^TTUT` | Utilities | ✅ |
| `^TTMT` | Materials | ✅ |
| `^TTCD` | Consumer Discretionary | ⚠️ Verify |
| `^TTCS` | Consumer Staples | ⚠️ Verify |
| `^TTRE` | Real Estate | ⚠️ Verify |
| `^TTTS` | Communication Services | ⚠️ Verify |

### Benchmark index symbols

| Symbol | Index | Usage |
|--------|-------|-------|
| `^GSPTSE` | S&P/TSX Composite | Broad-market benchmark |
| `^TX60` | S&P/TSX 60 | Large-cap leadership proxy |
| `^TXCE` | S&P/TSX Composite Equal Weight | Breadth comparison for Leadership indicators |
| `XIU` | iShares S&P/TSX 60 ETF | Tradable TSX 60 proxy (used throughout system) |

**Important**: `^TXCE` ≠ `^TXEW`. TXCE is the **Composite** equal-weight; TXEW is the **TSX 60** equal-weight. For leadership analysis, the Composite equal-weight is correct because we want to compare cap-weighted leadership against the full breadth of the Composite universe.

Defined in `Core/TMX/TsxBenchmarkSymbols.cs`.

### Why this exists

This gives TraderVI a stable internal lookup it controls and enables:
- sector-relative strength
- sector concentration/risk checks
- future Granville leadership/weighting work
- resilience if TMX naming/schema changes

The stock-sector map is refreshed by Hermes only when stale (currently **7 days**), since sector
metadata changes far less often than daily prices.

## Future Direction

The active Now/Next/Later sequence lives in `Docs/roadmap.md`. Keep this document focused on
architecture; do not duplicate volatile priority lists here.

## Data Flow

| Program | Runs | Reads | Writes |
|---------|------|-------|--------|
| **Hermes** | Daily (post-close) | TMX/Yahoo APIs, existing market tables | `[DailyBars]`, `[AdvanceDeclineLine]`, `[SymbolObv]`, `[MarketClimax]`, `[SectorIndices]`, `[StockSectorMap]`, `[LeadershipData]`, `[UsIndexBars]` |
| **Hercules** | Weekly / on-demand | `[DailyBars]`, enabled code registries | model artifacts, `[ModelExperiment]`, `[ModelRegistry]` |
| **Delphi** | Daily (pre-market) | market tables, models, strategy version, OBV/CLX | `[DailyPick]`, `[DecisionDossier]`, `[GranvilleIndicatorLog]`, narrative cleanup, console reports |
| **TraderVI** | Manual or 15-minute console watch | picks/models/positions, delayed TMX bars | ghost positions, `[TradeLog]`, intraday receipts/evidence; no live order placement |
| **TraderVI.WPF** | Interactive tabbed shell; thirty-second Trading refresh, explicit Ghost/Real rows, read-only official Scorecards/Data Audit, six-view saved Delphi workspace, and native Project Docs reader | operational positions/trades, official calibration evidence, shared monitor/audit/Delphi workflows, saved presentation evidence, repository Markdown | Ghost auto-exits and confirmed manual Real reconciliation write SQL; Scorecards, Data Audit, saved-Delphi refresh, and Project Docs do not; a confirmed official Delphi run has Delphi's documented SQL effects; no broker integration |
| **Oracle** | Manual/optional | decision dossiers | external LLM call and `[LlmNarrative]` when configured |
| **Sentinel** | Future always-on/live-risk boundary | durable paper evidence and future explicitly authorized broker state | not implemented; ADR-0031 authorizes ghost exits only |

## Operational daily-source revisions

[ADR-0066](adr/0066-operational-paper-daily-source-adoption.md) separates original opening freezes from current operational revisions. Shadow retains candidate/order identity per source revision. Live overlays an immutable checksum-protected candidate/baseline snapshot. Both serialize adoption and retain protective state. Research reads the original context and excludes source-changed cohorts. Manual migration 032 defines the additive storage; DACPAC remains a reference artifact.
