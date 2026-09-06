# ADR-0053: Delphi Live V1

- **Status:** Accepted
- **Date:** 2026-09-05
- **Domains:** architecture, data-pipeline, decision-engine, machine-learning, market-microstructure, risk-management, technical-indicators
- **Related:** ADR-0020 through ADR-0027, ADR-0030, ADR-0033, ADR-0040, ADR-0045, ADR-0051; `Docs/concepts/delphi-live.md`; `Docs/design-rules.md`; `Docs/oracle-rules.md`

## Context

The immediate problem is turning Delphi's once-daily recommendation into a five-minute advisory view without losing the daily swing thesis. The parent problem is learning which daily candidates develop real intraday strength, which stagnate or weaken, and whether acting on those changes improves selection and entry timing. The root goal remains aggressive short-term TSX opportunity selection with capital preservation first.

The completed design dialogue is frozen in `Docs/concepts/delphi-live.md`. It contains the exact V1 measurements, classifications, thresholds, state transitions, safety rules, causal fill contract, portfolio accounting, scorecards, coverage rules, and promotion protocol. Re-expressing all of those rules independently in a shorter ADR would create two subtly different specifications.

## Decision

Accept `Docs/concepts/delphi-live.md`, dated 2026-09-05, as the normative detailed design for Delphi Live V1 without changing its specified behavior.

The concept's **Accepted** and **Provisional** decision-record sections are operative V1 behavior. Its **Superseded** section is historical context and is not operative. Its **Deferred** and non-blocking open sections remain outside V1. If a summary in this ADR is less precise than the frozen source, the frozen source governs. Changing any action-affecting rule in the frozen source requires a successor ADR and prospective policy identity; do not edit history to make a new rule appear to have governed earlier evidence.

### Product and authority boundary

1. Keep Delphi's full-universe daily evaluation, Delphi Live's five-minute interpretation, Shadow execution, Athena calibration, future Hercules training, and optional DotLLM narration as separate responsibilities.
2. Implement the engine as a WPF-independent Core workflow behind injected market-data, TSX calendar/clock, persistence, policy-assignment, lease, and notification interfaces. WPF is the first host and operator surface, not the owner of policy behavior.
3. Install the Operational Champion inactive. Activation requires an explicit positive simulation-capital snapshot, is audited, and takes effect no earlier than the next regular-session boundary. There is no broker adapter or Real-order authority.
4. Use one durable store-backed lease across all present and future hosts. V1 runs only while WPF is open; host gaps are visible and disqualify a session from clean shakedown or promotion evidence.
5. Do not alter the existing frozen-daily System Shadow, Operator Ghost, Real, or Athena ledgers. Shared collection may observe their symbols, but a Delphi Live policy may mutate only its own Shadow portfolio.

### Frozen source and collection

1. At the 09:30 Toronto boundary, freeze the newest `Valid` `OfficialPaper` Delphi run for that recommendation date that was durably created by 09:30 and whose `MarketDataAsOf` is the immediately preceding canonical XIU session. Persist the run, daily strategy, candidates, publication facts, source lenses, ranks, ranking keys, common composite, and gate/reason evidence. A later rerun cannot rewrite the session.
2. Freeze the deduplicated union of the published Continuation Top 25 and Breakout Top 25. Preserve both lens theses and ranks for dual-selected symbols over one canonical symbol observation; dual selection never creates a second independent piece of market evidence.
3. Observe that union, all tracked holdings, all Delphi Live holdings and pending exits, and same-session carry candidates. A non-reselected carry candidate receives no stale current-day rank.
4. Create one non-overlapping collection cycle for each scheduled five-minute endpoint from 09:35 through 16:00, beginning collection two minutes after the endpoint. Deduplicate symbol/XIU requests across policies and persist expected slots, attempts, deadlines, receipts, misses, late-research-only responses, and duplicate/conflict outcomes.
5. Preserve existing fifteen-minute policy timing. The earlier 09:37 Delphi Live collection extension does not move ADR-0045's 09:47 first-safe decision boundary for Operator Ghost, existing System Shadow, or `DelayedIntradaySwingV1`.
6. On restart or another process-continuity gap, expire pending Buys, retain pending protective sells, clear operational rolling buffers and unfinished ordinary confirmation, and prohibit retrospective actions. Ordinary families remature from fresh consecutive on-time evidence; quote protection resumes immediately.

### Deterministic evidence and judgment

Implement four separate signal families exactly as frozen:

1. **Persistence** uses four contiguous five-minute stock/XIU interval comparisons and retains its exact `-4..+4` score.
2. **Price Movement** records exact twenty-minute, one-, two-, and three-hour, previous-close, and matching XIU returns. Only the twenty-minute and one-hour windows vote. The V1 volatility ruler is frozen `MedianTrueRangePct10`; action thresholds are symmetric `RawMoveUnits = +/-0.25` and `ExcessUnits = +/-0.05` with the exact frozen agreement, conflict, missingness, and counterfactual rules.
3. **Volume Support** uses four contiguous bars to calculate `DirectionalVolumeBalance20`; `+/-0.10` plus an agreeing twenty-minute price sign supplies the only V1 vote. `FullDayVolumeFraction20` is non-voting context.
4. **Price Structure** keeps previous close, complete-path session VWAP, and the prior four-bar twenty-minute range separate. It uses the frozen `0.05` range-unit buffer, requires at least two available references, and applies the accepted no-conflict combination table.

Measurement, window classification, family combination, and recommendation/safety policy are separate deterministic stages. Missing or unavailable input never becomes zero, Neutral, or Weakening. Leaning states remain explanatory/tie-breaking evidence and do not become full votes.

Combine Supportive and Weakening family votes through the exhaustive frozen table. Preserve `Strong/FourOfFour`, `Strong/CleanThree`, `StrongWithConflict`, both Positive Nudge states, Neutral tilts/conflict, `MixedConflict`, both Negative Nudge states, `WeakWithConflict`, `Weak`, and `VeryWeak` exactly. Do not shrink the denominator or invent a blended score.

Use the frozen total order: live state, PositiveLeaning count descending, NegativeLeaning count ascending, then Persistence score descending. When those live comparisons tie, current-session frozen candidates precede carry candidates that were not reselected. Among current-session candidates, compare best numerical frozen source-lens rank, common Delphi composite descending, then ticker; carry candidates tie by ticker without reusing an earlier daily thesis. A lens-specific diagnostic uses only that lens's frozen rank. Lens emphasis is explanation-only and dual selection earns no bonus.

### Recommendation, safety, and causal action

1. Keep Live Momentum, recommendation lifecycle, and `Active`/`Quiet` presentation state separate. Shared policy observations do not share portfolio lifecycle or ownership: the continuing champion and its same-policy experiment control confirm, hold, exit and re-enter independently.
2. One valid `EntryEligibleStrong` observation starts `Emerging`; the immediately following valid strong observation completes `EntryEligible`. Missing/invalid evidence, non-Normal Data Confidence, a veto, or a non-strong result breaks the sequence. A dismissed candidate recovers only through two entirely fresh valid strong observations.
3. Permit both Buy decisions and their usable immediate quote fills only from 09:50 inclusive until 15:45 exclusive; every unfilled Buy expires at 15:45. Persist a decision before requesting a fresh quote. Buy uses positive ask; sell uses positive bid; valid positive `price` is an explicitly tagged `EstimatedFill`. Use at most three attempts inside sixty seconds. A Buy then expires; a protective sell remains idempotently pending and may become `ExitPendingOvernight`.
4. Process and durably commit protective sells before Buys in each policy cycle. A sale slot or proceeds are not reusable before its fill commit.
5. Permit at most two completed entries per symbol, policy, and session. Same-session re-entry requires entirely fresh post-exit confirmation and cannot occur while a sell remains pending.
6. Keep the five-percent average-cost hard loss active for the holding's entire lifetime. Apply the frozen completed-bar `FastDownside10Pct`, three-part `ConfirmedSupportFailure`, profit-floor/trailing rules, and `LiveWeakeningExit` timing. Rank decline alone never sells.
7. One `VeryWeak` observation creates `LiveWeakeningExit/BroadImmediateWeakening`; `WeakWithConflict` or `Weak` requires the next valid strong-weakening observation and records `PersistentWeakeningConfirmed`.
8. When rules first fire together, label the primary reason in this order: `HardLoss5Pct`, `FastDownside10Pct`, `ProfitProtectionFloorBreach`, `ConfirmedSupportFailure`, `LiveWeakeningExit`. Preserve all supporting triggers under one full-position sell identity.
9. Keep Data Confidence separate from market judgment: one consecutive miss is `Ambiguous`, two `Degraded`, and three `MonitoringLost`; one later clean exact stock/XIU observation restores `Normal` without repairing rolling continuity. Protection and pending sells remain active.
10. Implement no V1 `MarketWideDanger` gate. XIU supplies normal relative context and cannot independently pause every Buy or force an exit.

### Policy, portfolio, evidence, and dossier identity

Use these stable V1 identities in code and durable records:

| Concern | V1 identity | Purpose |
|---|---|---|
| Live policy | `DelphiLivePolicyV1` / definition schema 1 | Immutable numeric settings and the supported evaluator contract |
| Evaluator | `DelphiLiveEvaluatorV1` | Formula, state-transition, precedence, missingness, and reason semantics |
| Collector | `IntradayEvidenceCollectorV3` / source contract 1 | 09:37 five-minute shared collection with cycle deadlines and priority |
| Decision dossier | `DelphiLiveDecisionDossierV1` / schema 1 | Complete deterministic explanation and causal references |
| Quote fill | `DelphiLiveQuoteFillV1` | First post-decision observed ask/bid or tagged fallback price |
| Shadow portfolio | `DelphiLiveShadowPortfolioV1` | Independent cash, positions, actions, guards, and marks |
| Research outcome | `LiveObservationOutcomeV1` | Post-anchor opportunity path beginning in the next interval |
| Ranking diagnostic | `DelphiLiveDailyVsLiveTop5V1` | Frozen Daily Top 5 versus contemporaneous confirmed Live Top 5 |
| Promotion protocol | `DelphiLivePromotionV1` | `10 + 30 + 30` phases and the frozen paired comparison method |

The exact daily `StrategyVersionId` is inherited from the frozen official run and is persisted beside the separate `DelphiLivePolicyVersionId`; Delphi Live does not create or pretend to replace the daily strategy. Stored policy definitions contain all accepted numeric settings and reject unknown evaluator versions, unknown fields, invalid ranges, or contradictory values rather than using fallback defaults.

The V1 policy definition includes, at minimum, the four five-minute intervals / twenty-minute windows; `10`-session true-range ruler; `0.25` raw movement; `0.05` excess and structure buffers; `0.10` directional-volume threshold; two-observation entry/weakening confirmation; five-percent hard loss; ten-percent fast-bar decline; three-/five-/two-percent profit-floor settings; five holdings; twenty-percent NAV target; two same-session entries; three-percent daily buying pause; ten-percent capital review; three quote attempts in sixty seconds; 09:50 entry start; 15:45 cutoff; the primary-reason order; and all predeclared research variants.

Every consequential decision stores the applicable daily and live identities plus exact source timestamps and IDs, raw and derived facts, all family judgments, confidence and lifecycle transitions, every fired rule/reason, requested action, and terminal action state. Quote-only protection can lack a current-session daily attribution; the holding's original entry attribution remains intact. DotLLM may later translate only this persisted dossier and may not calculate or supply causality.

### Portfolio and calibration boundary

1. Give each assigned live policy one independent five-slot, cash-constrained, whole-share portfolio with a twenty-percent-of-current-NAV entry target, no borrowing, add-ons, automatic rebalancing, deposits, or withdrawals. The continuing champion may also have one distinct aligned champion-control portfolio for an experiment.
2. Mark portfolios only with exact aligned opening, checkpoint, and closing evidence; never carry a stale price into sizing, a guard, or promotion. Apply the frozen three-percent daily buying pause and ten-percent capital-review guard while protective exits continue.
3. Activate break-even protection at a completed close three percent above average cost; enter trailing mode at five percent; trail two percent below the highest completed five-minute close and never lower the floor.
4. Keep the capital-constrained fill scorecard separate from `LiveObservationOutcomeV1` and from the lens-specific frozen Daily Top 5 versus confirmed Live Top 5 diagnostic.
5. Give every expected candidate/checkpoint and XIU benchmark slot visible metric-specific coverage. `Ready` requires 100 percent usable, `Degraded` requires at least 95 percent, and `Blocked` is below 95 percent. A clean shakedown/promotion session additionally requires complete operational coverage, stable identities, reconstructible actions, no overlap, and no host gap.
6. Use ten clean engineering-shakedown sessions, thirty additional discovery sessions, and thirty additional untouched-confirmation sessions. Shakedown is never performance evidence. Start each comparison with same-session, equal-capital, cash-only champion-control and challenger runs.
7. Permit exactly one Operational Champion and at most two active non-champion Shadow versions. Vary only one threshold family in an experiment. A former champion becomes the thirty-clean-session Shadow Baseline after human-approved promotion.
8. The untouched promotion test uses paired daily portfolio returns, deterministic ten-thousand-resample five-session moving-block bootstrap, a 95 percent interval whose lower bound must exceed zero, no worse maximum checkpoint drawdown, no worse mean return over each policy's own worst `max(1, ceiling(10% * N))` sessions, and ADR-0022 regime/evidence gates. Promotion is never automatic.

## Implementation sequence

Implement in reviewable, non-activating phases:

1. **Governance and Core contracts:** this ADR, review aids, stable identities, validated immutable settings, raw facts, judgments, dossiers, and host-neutral interfaces.
2. **Pure deterministic behavior:** measurements, family classifiers, combined state, ranking, Data Confidence, recommendation transitions, safety rules, quote-fill selection, and portfolio arithmetic with table-driven boundary tests.
3. **Durable evidence and Shadow state:** additive canonical schema, manual migration script, repositories, idempotency, lease, sessions, assignments, cycles, decisions, fills, portfolios, marks, outcomes, and coverage. Building or parsing the migration is allowed; applying it is not.
4. **Shared workflow:** scheduling/priority orchestration, restart recovery, frozen-run selection, shared evidence evaluation, sell-before-buy processing, and read-only snapshot/report builders.
5. **WPF host and diagnostics:** inactive-by-default operator surface, explicit next-session activation, status/coverage/warnings, rankings, family facts, dossiers, portfolio state, experiment phase, and report completeness.
6. **Operational rollout:** separately reviewed migration, external-source shakedown, activation, and cohort collection only with explicit authorization. No part of source implementation grants that authorization.

Each phase must preserve unrelated work and may advance only after focused tests/builds pass or its limitations are reported. Existing accepted evidence and portfolio rows remain immutable.

## Alternatives considered

- **Put live rules inside WPF or System Shadow.** Rejected because host lifetime and presentation must not own policy semantics, and existing frozen-daily Shadow behavior must remain unchanged.
- **Poll or score the full universe continuously.** Deferred because V1 deliberately freezes the daily Top-25 union plus holdings and carry candidates.
- **Collapse Daily Setup and Live Momentum into one score.** Rejected because it destroys attribution and invites unreviewed weighting.
- **Fill from the signal bar or a later convenient bar.** Rejected because neither price was first observed after the decision under the accepted contract.
- **Treat missing evidence as bearish or shrink the family denominator.** Rejected because source uncertainty is not market weakness.
- **Let an LLM calculate, select, veto, or trade.** Deferred/rejected for V1; deterministic persisted evidence remains authoritative.
- **Activate while implementing.** Rejected because schema rollout, market collection, simulation capital, and operational monitoring each require separate explicit authorization.

## Consequences

Delphi Live gains an auditable daily-informed intraday layer whose decisions can be replayed and challenged without rewriting daily Delphi, existing Shadow, Real/Ghost operations, or Athena. The explicit stages and immutable identities make threshold changes measurable and keep future hosts or models replaceable.

The cost is a substantial durable evidence surface, stricter source-capacity requirements, multiple independent portfolio ledgers, and visible incomplete sessions whenever WPF or the provider misses a cycle. The first ten clean sessions validate engineering only, so trusted performance conclusions will take materially longer than implementation.

Would tell us this decision is wrong: the provider cannot reliably complete the frozen expected set inside non-overlapping cycles; causal decisions/fills cannot be reconstructed after restart; the rule baseline cannot produce stable complete cohorts; or the capital-preservation rules systematically worsen untouched downside. Those findings justify a prospective successor, never silent V1 retuning.

## Review questions

1. Why must a dismissed candidate remain in the evidence set through the close?
2. Why are Data Confidence and Market Judgment separate, and what does one clean observation restore?
3. Which deterministic record must exist before DotLLM may explain a recommendation or exit?
4. Why must a new challenger start beside a cash-only champion control rather than compare with the continuing operational portfolio?
