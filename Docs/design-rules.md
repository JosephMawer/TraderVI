# Design Rules (TraderVI)

> This file is referenced by the repository-root `AGENTS.md`.
> Rules here are authoritative for decision-engine, model, feature, and indicator changes.

## Manual recommendation approval

[ADR-0062](adr/0062-complete-strategy-recommendation-promotion.md) permits explicit human approval of a
complete strategy despite disclosed performance-evidence gaps in recommendation-only mode. Do not add
statistical promotion gates to this mode. Preserve existing internal Live research/untouched rules,
Athena evidence, operational pause/empty requirements and financial risk checks. Enforced common evidence
eligibility is deferred to Wealthsimple live trading/automation. A recommendation never creates a fill.

## Planned real TFSA execution

[ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md) permits a separately composed AI Momentum
reviewer to propose existing portfolio versions and explain discretionary exceptions. Its current
contract has no assignment/order/protection mutation interface. Hard-rule overrides are rejected;
existing engine scores, gates and protections remain authoritative until an explicit integration
contract is implemented. Version-2 reviews consult scoped evaluated lessons and explain each use or
disregard. Execution preparation rechecks the saved review against current learning; this is not order
authority. Learning cannot rewrite policy or block independent protection. Numerical protection,
handover and automatic lesson qualification remain unresolved.

[ADR-0084](adr/0084-external-broker-trade-synchronization.md) adds source-aware projection of resolved
broker orders into the Real ledger, atomically with imports and immutable receipts. Preserve original
binding/matched trades and verify the last committed ledger before each continuation. Whole-share
partial sales retain identity; close/reopen starts a new lifecycle. Cumulative orders remain explicitly
labelled aggregates. This supersedes ADR-0080's history-only boundary when migration 040 is installed;
it does not complete risk baselines, cash-event qualification or enable strategy execution.

[ADR-0081](adr/0081-broker-account-baselines-and-protection-ownership.md) permits first risk-reference
setup from verified current account value where no earlier applicable reference exists, with explicit
provenance and preservation of known references/holds. A setup observation is not a historical session
open or lifetime closing high. Retained holdings keep their current protection owner through required
exits/reconciliation and transfer at activation with established protective levels. Missing protection
starts from real cost and fresh observed evidence, never paper state or invented historical highs.
Purchase availability is capped by the lower of fresh broker CAD cash/buying power, strategy sizing
and reservations; unfinished sales supply no funds. Confirmed deposits/withdrawals adjust loss references;
unexplained cash movements pause new buys. Initial setup/store and operator UI are implemented in
source; storage 035/039/040 is installed, while operational setup/UI rollout and engine cutover remain.
Daily/flow continuation is tested offline;
source qualification, operational integration and actual protection handover remain unfinished.
No strategy is activated and no execution is enabled. Confirmed cash movements preserve the measured
loss percentage by scaling both references from matched before/after books (ADR-0083, September 22);
they never clear existing holds. A feed occurrence time or net-deposit delta is not sufficient proof.
September 21 adds offline boundary valuation and append-only daily continuation for positively
reconciled intervals without capital flows (ADR-0083). Daily Shadow keeps its saved-book price
definition; Delphi Live keeps first-bar Open/exact final-bar Close. A broker chart timestamp alone
cannot change those definitions. Confirmed-flow adjustment and native integration remain open;
migration 039 is installed without an operational baseline. No new late-start buy-pause rule is authorized.

[ADR-0085](adr/0085-portfolio-wide-ongoing-tfsa-management.md) accepts any portfolio with ongoing
assignment until pause/switch. September 23 accepts manual purchases through TraderVI staying outside
the active strategy by default, plus explicit purchase-time or later management handover. The operator
explicitly permits manually adopted extras under existing sell/protection rules, separate from the
strategy's normal automatic position slots. Breakout 3 continues managing its own three positions;
extras neither consume those slots nor receive automatic additions/replacements. Keep the manual
exception identifiable and all holdings in actual account valuation; automatic buying limits, account-wide
risk rules and paper attribution do not change implicitly. September 23 also accepts a strategy control
that stops new automatic purchases while ordinary sell/protection rules continue, plus the existing
manual Market sell all row action. Restore the same approved strategy and saved buying mode after
restart only after fresh account/order checks; stopped buying stays stopped and risk history persists.
Core now saves a distinct purchase-only control; the supervised-session Paused phase still blocks
automatic exits too. Existing one-session approvals still expire; new explicitly scoped ongoing
approvals retain their exact strategy, protection, cumulative purchase ceiling and original orders
across checked days. Only verified risk continuation clears the prior daily hold. Source/tested TFSA
controls restore the saved selection after linked-account refresh but start no execution loop.
Native qualification, execution-host rollout, Delphi Live handover, switching and manual ownership
remain unfinished. No new order
or automation authority follows from these decisions.

[ADR-0080](adr/0080-reviewed-broker-history-import.md) stores reviewed broker observations in separate
SQL history. `CanImport` is not strategy readiness or execution authority. Do not sum cumulative orders
across observations or reinterpret a last-fill timestamp as every individual fill. Legacy position IDs,
financial records and risk/protection state remain preserved until an explicit source-aware projection
and baseline design is implemented. Initial matching is reviewed; ambiguous or changed facts block import.

[ADR-0079](adr/0079-broker-reconciliation-and-market-handover.md) records the September 14 first-version
choice: regular-session whole-share market orders; promotion initiates required non-overlap exits and
waits for confirmed fills and reconciliation before new buys. Retain overlaps and actual cost/history.
This supersedes the earlier unfinalized limit preference for the first portfolio execution version.
The implemented reconciliation and transition preview grant no order authority and do not replace
SQL positions, risk baselines, research eligibility or ADR-0062's recommendation-only approval gates.

[ADR-0075](adr/0075-manual-market-buy-ticket.md) now enables a separate operator-reviewed manual
market-buy ticket in WPF. Manual quantity comes from the operator, not a portfolio. Preserve saved
source/direction without rewriting gates or assigning strategy attribution. Fresh broker checks,
applicable real-account holds, durable intent/recovery and explicit final submission remain mandatory.
This manual path does not enable strategy activation or automatic selling. The separate September 14
portfolio market-order decision is recorded in ADR-0079.

[ADR-0077](adr/0077-manual-holdings-market-sell.md) adds a separately authorized manual sell-all action
on the broker holdings row. It pins the entire displayed whole-share quantity, verifies fresh matching
holdings/session/orders, and saves protected intent before one transmission. This explicit sell-only
choice does not remove buy review or authorize portfolio exits. Buying pauses/cash limits do not
block manual exits. Uncertain sell outcomes and unconfirmed holdings reconciliation block new manual
orders on either side. Limit placement remains disabled pending a verified broker contract.

[ADR-0073](adr/0073-permanent-tfsa-and-whole-share-execution.md) records the accepted design for the
future broker workflow: the permanent self-directed TFSA uses one reviewed primary strategy while
paper accounts continue independently. TraderVI-originated orders must use positive whole-share
quantities. Multiples of ten are a soft purchase preference; rounding must not increase risk or prevent
closing another whole-share quantity. Preserve exact broker observations, including fractional
exceptions, during reconciliation. The accepted promotion design retains overlapping holdings and
transfers their management without selling solely to rebuy. Preserve actual cost basis and history;
strategy handover is not a new fill. This section records design intent; it does not enable orders,
change historical paper sizing, or supersede current runtime pause/empty checks. The future retained-
holdings activation condition and transition details remain in the linked draft.

## Rule-Based vs ML-Based Components

### Fixed-gate paper comparisons

[ADR-0067](adr/0067-fixed-gate-paper-comparison-pairs.md) defines five fresh Normal/Portfolio rules only
pairs, each account with CAD 1,000 and a lifetime-fixed admission profile. The experimental profile
bypasses daily entry gates only within the existing published ranks. Preserve ranking, universe/input
validity, portfolio entry/sizing/exit/risk rules, causal fills and original official/Athena evidence.
New gate experiments need fresh pairs; no global or account edit may silently switch an existing
comparison's gate profile. Existing accounts and research promotion roles remain separate. Migration
033 and `PaperComparisonRepository` implement immutable batch/account/source records. Five pairs were
activated September 9. Shared Settings cannot reassign their pinned portfolio rules; a daily strategy
change pauses incompatible new entries until a separately reviewed pair is available. A session sharing
comparison collection is excluded from the original Live clean promotion cohorts.

### Strategy identity and adoption direction

[ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md#daily-buying-pause--september-26-decision)
sets new Delphi Live/System Shadow daily buying-pause templates to 7%. Preserve
frozen V1 settings, saved policy identities, existing holds and explicit assignment. AI enforcement
integration remains pending; changing a template does not activate it on an account.
The September 27 AI Momentum allocation scale is agreed in ADR-0087: reduce the combined per-stock
buying allowance linearly toward the 7% pause, including additions. It must not create a fresh
allowance per order or force liquidation solely because the allowance shrank. The later continuous
recovery amendment replaces the AI daily framing and session-minimum lock: allowance follows losses
and verified recovery across days without a calendar reset. The loss reference, resumption details
and interaction with existing holds remain unresolved. Implementation is pending; this design does
not change existing engine execution or authorize resetting loss history.

[ADR-0059](adr/0059-delphi-model-and-threshold-settings.md) exposes preserved daily model-set selection
and eight persisted decision-gate settings. A threshold edit creates a new strategy version; it must not
mutate an existing version's evidence identity. Preserve the source input-policy dispatch and immutable
model metadata. Save Version never changes assignments. Explicit Assign is serialized with daily
evaluation and starts an official reevaluation under the new selection. UI configuration is not proof of
performance or cross-strategy/account promotion.

[ADR-0060](adr/0060-central-settings-and-scoped-configuration.md) governs central settings and whole-target
reassignment. Its revised operating contract requires the affected family to be paused and empty before
editing, saving or assigning trading rules. Daily shared models/gates require all dependent families paused
and empty. Sell is separate and requires eligible execution evidence; assignment cannot liquidate or erase
pending orders. Fence workers through publication/fills. Preserve completed history, capital and existing
loss/capital-review holds. Resume explicitly and reject incompatible frozen daily source evidence for buys.
Manual Live assignments and operator interventions pause research promotion; a fresh comparison requires
separate review. Migration 027 provides versions/assignments; 028 adds durable pauses and exit requests.

[ADR-0055](adr/0055-independent-strategies-to-approved-live-execution.md) makes the complete strategy the
unit of evidence and adoption: selection, entry, exit, sizing, risk, model/policy identity and execution
assumptions. Preserve independent paper accounts and explicit human promotion to recommendation mode.
Training success must not be treated as strategy approval. Broker execution requires a separate authority
and actual-account reconciliation. Existing ADR-0022 and frozen Delphi Live V1 evidence/behavior rules
remain in force; common comparison and promotion across families still require a reviewed design.

### Rule-Based (non-ML) — DO NOT feed into feature vectors

| Component | Purpose | Where |
|-----------|---------|-------|
| **Advance-Decline Line** | Market breadth / regime gate | `AdvanceDeclineCalculator` → `BreadthScore` |
| **Granville 56 Indicators** | Market condition scoring / soft gate + composite adjustment | `Core.Indicators.Granville.*` → `GranvilleComposite` → `GranvilleGate` |
| **XIU Regime Filter** | TSX benchmark uptrend / 20d return gate | `TradeDecisionEngine.ComputeRegime` |
| **SPY Regime Filter** | S&P 500 cross-market confirmation | `TradeDecisionEngine.ComputeRegime` |
| **Stop-Loss (-10%)** | Current operational hard-exit baseline; ADR-0028 separately tests a tightly qualified paper-only exception with an absolute -20% alert | `TradeDecisionEngine` / Sentinel (planned) |
| **Drawdown Warning (-5%)** | Alert / tighter monitoring | Sentinel (planned) |
| **Down-Probability Veto** | Block longs when P(down) reaches the configured maximum (default 35%) | `DownProbabilityGate` |
| **Down-Probability Penalty** | P(down) reduces the role-weighted composite continuously | `TradeDecisionEngine.ComputeCompositeFromRoles` |
| **Rotation Threshold** | Prevent churn; require sufficient edge delta | `PositionSizer` / future Sentinel |
| **Trend Confirmation** | Continuation lens requires both Trend30 and MA-crossover confirmation | `TrendConfirmationGate` |
| **History Freshness Eligibility** | Exclude symbols whose newest daily bar does not match Delphi's canonical XIU session | `HistoryFreshnessEligibility` (ADR-0019) |

These components act as **gates or modifiers** on the ML-driven ranking. They are never encoded as ML features unless explicitly revisited.

History freshness is a pre-scoring universe invariant rather than an `ITradeGate`: it must run before relative strength, OBV, model evaluation, and lens-specific gate stacks consume a symbol's bars. The tolerance is exactly zero completed XIU sessions and is not a tunable strategy parameter.

### Hybrid Components (Rule-Based + ML)

| Component | Rule-Based Usage | ML Usage | Where |
|-----------|-----------------|----------|-------|
| **Relative Strength** | Ranking signal via `CompositeScore`; future gating (deferred) | Feature columns for LightGBM training (planned) | `Core.RelativeStrength.*` |

RS is explicitly designed to serve both roles. The rule-based ranking is active now; ML integration requires Hercules retraining after historical backfill.

Enhanced already contains separate stock-versus-XIU return features. Their corrected dated input path
is governed by [ADR-0056](adr/0056-dated-profit-model-input-contract.md): identical training/prediction
calculations, exact required session alignment, explicit stock exclusions and rejection of unusable shared
XIU input. Reuse requires proven model compatibility and a new strategy identity. The corrected path adds
no deterministic gates, Granville indicators or database RS ranking values to the ML vector. The active
`v3.4-observed-benchmarks` uses the reviewed v3.3 replacements through the immutable stored assignment in
[ADR-0057](adr/0057-preserved-model-sets-and-explicit-selection.md). Training creates separate candidates;
selection requires a reviewed transition. Preserved legacy inputs remain explicitly unverified.

[ADR-0058](adr/0058-daily-ingestion-and-observed-benchmark-confirmation.md) separately requires observed
XIU and SPY histories for the daily regime filter. An unavailable, invalid or stale shared benchmark
stops new evaluation with a reason. This deterministic input check preserves complete-data trend/return
formulas and the ML vectors; it does not substitute Genuity's S&P 500 index for SPY or change Delphi Live V1.

### Granville's 56 Day-to-Day Indicators

Market-level rule-based indicators from Granville's "A Strategy of Daily Stock Market Timing."
Each category is implemented as an `IGranvilleIndicatorGroup` in `Core/Indicators/Granville/`.

| Category | Indicators | Status | Implementation |
|----------|-----------|--------|----------------|
| **Plurality** | #1–#4 | ✅ Active | `PluralityIndicators.cs` |
| **Disparity** | #5–#6 | ✅ Active | `DisparityIndicators.cs` |
| **Leadership** | #7–#10 | ✅ Active | `LeadershipIndicators.cs` |
| **Features / Most Active** | #11–#14 | ✅ Active | `MostActiveIndicators.cs` |
| **Weighting** | #15–#16 | ✅ Active | `WeightingIndicators.cs` (ADR-0003) |
| **Genuity** | #17–#20 | ✅ Active | `GenuityIndicators.cs` (ADR-0004/0008) |
| Dullness | #21–#22 | ⏸ Deferred | ADR-0005; #21 was anti-predictive and #22 had insufficient samples on 2020–2026 XIU |
| Overdueness | #23–#24 | 🔲 Planned | — |
| **Light Volume** | #25–#28 | ✅ Active | `LightVolumeIndicators.cs` (ADR-0006) |
| Heavy Volume | — | 🔲 Planned | — |
| Reversals | — | 🔲 Planned | — |
| Gold Indicator | — | 🔲 Planned | — |
| 3-Day Rule | — | 🔲 Planned | — |
| Churning | — | 🔲 Planned | — |
| News | — | 🔲 Planned | — |
| Erratic Price Movement | — | 🔲 Planned | — |
| General Motors Indicator | — | 🔲 Planned | — |
| The Closing | — | 🔲 Planned | — |
| Odd Lots | — | 🔲 Planned | — |
| Rebounds and Declines | — | 🔲 Planned | — |
| Highs and Lows | — | 🔲 Planned | — |

### Disparity adaptation for TSX

Granville's original Disparity logic compares **transportation average vs industrial average**.
That mapping does not transfer cleanly to the TSX because there is no dedicated TSX transportation
sub-index equivalent to the Dow Transports.

**TraderVI adaptation**:
- Use a **cyclical basket** of TSX sector indices: **Energy + Industrials + Materials**
- Compare that basket to **XIU** as the benchmark proxy
- Evaluate on:
  - **1-day percent change**
  - **5-day rolling return**

**Signals**:
- **Disparity #5**: cyclical basket underperforms `XIU` → near-term decline signal
- **Disparity #6**: cyclical basket outperforms `XIU` → near-term advance signal

**Important implementation note**:
- `XIU` is used for now instead of the raw TSX 60 index symbol because `XIU` is already stored and
  used throughout the system. This is a deliberate simplification and should be documented in code comments.
- Basket weighting is currently **equal-weighted**. This may later be upgraded to market-cap weighting.

### Leadership adaptation for TSX

Granville's Leadership indicators ask: **which stocks or groups are doing the heavy lifting for the
market?** Breadth asks "how many stocks are participating"; Leadership asks "are the most influential,
strongest, most sponsored stocks still outperforming?"

Leadership becomes a warning sign when it narrows — a few large names can keep a cap-weighted index
rising while internal participation deteriorates.

**Three measurement layers**:

| Layer | Metric | Formula | Source |
|-------|--------|---------|--------|
| **New-High/New-Low breadth** | Are winners still being produced? | `NHNL_10 = EMA10((NewHighs − NewLows) / Issues)` | Computed from stored OHLCV via `NewHighLowCalculator` (252-day lookback) |
| **Active-stock breadth** | Where is urgent capital going? | `ActiveBreadth_10 = EMA10((AdvancersTopN − DeclinersTopN) / N)` | Top-50 by dollar volume via `TmxClient.GetMarketMoversAsync("dollarvolume")` |
| **Large-cap relative strength** | Are leaders isolated or confirmed? | `LargeCapRS_20 = Return(TSX60, 20) − Return(CompositeEW, 20)` | `XIU` (TSX 60 proxy) vs `^TXCE` (S&P/TSX Composite Equal Weight Index) |

**Leadership state** (upswing / downswing):
- **Upswing**: ≥ 2 of 3 series rising AND none deeply negative (< −0.10)
- **Downswing**: ≥ 2 of 3 series falling AND NHNL_10 < 0
- **Indeterminate**: mixed signals or insufficient data

**Leadership quality** (improving / deteriorating):
- Determined by slope consistency (3-point trend) in the EMA-smoothed NHNL and Active Breadth series

**Signals**:
- **Leadership #7**: quality deteriorates + upswing → bearish (near-term decline likely)
- **Leadership #8**: quality deteriorates + downswing → bullish (near-term advance likely)
- **Leadership #9**: quality improves + downswing → strongly bearish (decline likely to continue)
- **Leadership #10**: quality improves + upswing → strongly bullish (advance likely to continue)

**Benchmark symbols**:
- `XIU` — iShares S&P/TSX 60 ETF (tradable TSX 60 proxy, already in system)
- `^TXCE` — S&P/TSX Composite Equal Weight Index (NOT `^TXEW`, which is TSX 60 Equal Weight)
- Defined in `TsxBenchmarkSymbols` (`Core/TMX/TsxBenchmarkSymbols.cs`)

**Data pipeline**:
- **Hermes** computes and stores daily `LeadershipSnapshot` to `[dbo].[LeadershipData]`
  - NHNL: computed from stored `[DailyBars]` (fully backfillable)
  - Active breadth: same-session only from a complete, distinct top-50 TMX movers response; the undated
    response is never assigned to an earlier session
  - Benchmark closes: XIU from stored bars, `^TXCE` from TMX API
- **Delphi** loads recent 50 days from `LeadershipRepository` into `GranvilleMarketContext.LeadershipHistory`
- `LeadershipCalculator` computes EMA-smoothed series, determines state and quality
- `LeadershipIndicators` maps (quality × state) to indicator signals #7–#10

**Graceful degradation (ADR-0043)**: Active breadth is one atomic nullable observation. All three mover
counts are null when the source is unavailable; a genuine neutral reading has a positive reported basket
whose advancing and declining counts are equal. Leadership and Light Volume require the newest 12
sessions (`EMA10 + two slope observations`) to contain contiguous valid active-breadth observations.
Never filter across a gap, turn unavailable data into numeric zero, or count unavailable/flat inputs as
falling. Insufficient mover coverage emits an explicit neutral/no-data result even when enough NHNL rows
exist. Historical backfill days store NHNL only.

### TSX sector map rules

Sector membership is **not** inferred from a TMX "index membership" endpoint.
Instead, TraderVI owns an internal stock → sector-index mapping:

1. Pull stock metadata from `TmxClient.GetQuoteDetailAsync()`
2. Read TMX `sector` / `industry`
3. Normalize the sector string through `TsxSectorMap`
4. Store the result in `[dbo].[StockSectorMap]`

This mapping is authoritative for app logic such as:
- sector-relative strength
- sector concentration checks
- future Granville leadership/weighting work
- fallback sector aggregation if TMX index symbols change

Hermes refreshes the stock-sector map on a **staleness schedule** rather than every daily run.

### Relative Strength rules

RS features are computed from three axes:
- **Stock vs Sector** — stock return minus sector index return
- **Stock vs Market** — stock return minus XIU return
- **Sector vs Market** — sector index return minus XIU return

Across horizons: **5d, 10d, 20d, 60d**

Plus volatility-normalized Z-scores: `RS_Z = (RS_today - mean(RS_20d)) / std(RS_20d)`

**Session alignment (ADR-0041):** XIU defines the canonical completed-session sequence. Stock and
sector inputs retain their dates, and each return/Z-score uses exact canonical endpoints. A missing or
duplicated stock/sector endpoint leaves only the affected metric null and is surfaced as degraded
coverage; never select a duplicate by source order, align by minimum list length, compress gaps,
forward-fill, or substitute a nearby session. The full current
feature set requires 61 canonical sessions (a 60-session return needs 61 endpoints). Duplicate XIU
sessions or invalid XIU closes inside that required canonical window fail Delphi's RS stage because
the shared session reference itself is not safe to degrade independently per symbol.

**Lifecycle**:
- **Delphi** computes live for today's inference (does NOT read from DB)
- **Hermes** backfills historical values to `[dbo].[RelativeStrengthFeatures]` for Hercules training
- **Hercules** pulls from DB and includes as LightGBM feature columns (planned)

**Composite score**: `0.5 × RS_StockVsMarket_10d + 0.3 × RS_StockVsSector_10d + 0.2 × RS_SectorVsMarket_10d`
- Weights are initial defaults, intended to be tuned via Hercules feature importance analysis.

**Backfill note**: Stock-vs-market RS is fully backfillable (XIU history exists since 2020). Stock-vs-sector RS is only backfillable from when Hermes started collecting `[SectorIndices]`.

### Decision score and ranking

The ML composite is role-weighted from the four active profit models. The Granville adjustment
is then added and clamped at zero; its range is [−0.10, +0.10] and it is normalized across all
implemented groups (#1–#20 and #25–#28).

Ranking is lens-specific and deliberately separate from the ML composite:

- Continuation: `RScomp + obvTilt`
- Breakout: `DirectionEdge + RScomp + obvTilt`

CLX remains diagnostic-only and must not affect a gate or ranking key without a new decision.

### Delphi reporting rules

Delphi outputs two structured reports via `Core.Runtime.DelphiReportBuilder`:

| Report | Method | Audience | Content |
|--------|--------|----------|---------|
| **Diagnostic** | `BuildDiagnostic()` | Developer/agent log analysis | Raw values for every data source: regime flags, A/D line stats, sector index table, Granville per-indicator breakdown, universe stats, per-pick signals + gate traces |
| **Summary** | `BuildSummary()` | Human operator (pre-market review) | Regime label, breadth interpretation, sector leaders/laggards, Granville net signal, final recommendation with composite/edge/allocation |

**Mandatory update rule**: Any new signal, gate, indicator, or data source added to Delphi's evaluation pipeline **must** be reflected in both `BuildDiagnostic()` and `BuildSummary()`. This includes:
- New ML models (add probabilities to diagnostic picks table + summary recommendation)
- New rule-based gates (add to diagnostic gate trace + summary if blocking)
- New market-level indicators (add raw values to diagnostic + interpretation to summary)
- New data sources consumed by the engine (add to diagnostic universe stats)

The `DelphiReportBuilder` properties should mirror what `Delphi/Program.cs` computes — do not duplicate computation logic in the report builder; it only formats data it receives.

### Rule-Based Pattern Signals (A2 refactor)

Pattern detectors (`Trend10`, `Trend30`, `MaCrossover`, and any detector listed in
`PatternRegistry.All`) are **deterministic rule-based checks**, not ML models.

**Why not ML:**
- The label (`detector.Detect(window)`) is a pure function of the same bars used to build
  the feature vector. An ML model trained this way just approximates the rule it was given.
- Near-perfect training AUC (~0.99–1.00) is expected and uninformative — it does not mean
  the pattern predicts forward returns.
- Forward-return prediction is handled by the active **profit models** (`BinaryUp10`,
  `BinaryDown10`, `BreakoutEnhanced`, and `VolExpansionRelative10`).

**Implementation rules:**
- Pattern detectors are authored as `IPatternDetector` implementations under
  `Core/ML/Engine/Patterns/Detectors/`. (Namespace predates this refactor; future cleanup.)
- At runtime they are wrapped by `RulePatternSignalModel` and evaluated directly —
  no ML.NET, no `.zip` files, no `ModelRegistry` rows.
- `PatternRegistry.All` is the **single source of truth** for which patterns are active;
  toggles live in code (`EnableTrend10`, etc.), not in the database.
- `SignalResult.Score` is binary: `1.0` when present, `0.0` when absent.
- `DelphiBootstrap` silently ignores any legacy `[ModelRegistry]` rows whose `TaskType`
  matches a code-registered pattern, so stale DB rows are harmless.
- `ML.Train` does **not** train pattern models. Hercules emits a "Pattern Presence Report"
  showing the firing rate per pattern across the universe for sanity checking only.

**Do NOT:**
- Add pattern task types to `ProfitModelRegistry`.
- Re-introduce a trainer for any `IPatternDetector`.
- Write pattern `.zip` files to disk or `[ModelRegistry]` rows to the DB.

## Operational paper daily-source refresh

[ADR-0066](adr/0066-operational-paper-daily-source-adoption.md) supersedes the permanent 09:30 source block for operational System Shadow and Delphi Live. Adopt newer valid current-strategy publications prospectively, retain original freezes and fills, cancel old pending buys, and restart fresh confirmation. Keep historical replay cutoffs and clean-research exclusions. This does not bypass gates or assign strategies.
