# Delphi Live

- **Status:** Handoff-ready V1 design source; not yet an accepted ADR and not implementation or operational authorization
- **Date:** 2026-09-05
- **Domains:** architecture, data-pipeline, decision-engine, machine-learning, market-microstructure, risk-management, technical-indicators
- **Related:** ADR-0020 through ADR-0027, ADR-0030, ADR-0033, ADR-0040, ADR-0045, ADR-0051; `Docs/design-rules.md`; `Docs/oracle-rules.md`

## Purpose

The immediate problem is turning Delphi's once-daily recommendation into a five-minute advisory view without losing the daily swing thesis. The parent problem is learning which daily candidates develop real intraday strength, which stagnate or weaken, and whether acting on those changes improves selection and entry timing. The root goal remains aggressive short-term TSX opportunity selection with capital preservation first.

This note records the design dialogue. It is a concept and decision record, not an accepted ADR, implementation plan, database migration, or authorization to change operational behavior.

## System boundary

Delphi Live is a proposed real-time extension of Delphi, not logic hidden inside Shadow.

```text
Daily and five-minute market evidence
                   |
                   v
Delphi daily scan -> frozen daily watchlist and Daily Setup Quality
                   |
                   v
Delphi Live -> five-minute Live Momentum, ranking, states, and alerts
                   |
          +--------+---------+
          |                  |
          v                  v
Delphi Live Shadow      Immutable evidence
(paper challenger)            |
                              v
                         calibration
                              |
                              v
                    optional future model
```

Responsibilities remain separate:

- **Delphi** performs the full-universe daily evaluation and supplies the frozen daily thesis.
- **Delphi Live** observes the frozen watchlist every five minutes and updates live judgments, rankings, and recommendations.
- **Shadow** simulates what a specified recommendation and portfolio policy would have done. Existing frozen-daily Shadow portfolios remain unchanged; separately versioned Delphi Live Shadow portfolios are challengers.
- **Calibration/Athena** measures daily selection and the incremental value of live changes as separate products.
- **Hercules** may later train a model challenger from prepared evidence. Machine learning is optional and must earn its place.
- **Oracle/DotLLM** may later explain persisted deterministic decisions. It never calculates scores, supplies missing reasons, changes rankings, triggers a veto, or authorizes a trade.

These components may initially share the WPF host and the five-minute evidence collector, but shared hosting does not merge their responsibilities.

## Runtime and host boundary

Delphi Live's engine is a shared-Core workflow, not WPF application logic. Calculation, state transitions, scheduling decisions, policy evaluation, persistence, idempotency, and portfolio actions must have no dependency on WPF controls, view models, dispatcher types, or process lifetime. WPF is V1's first host and operator UI: it starts the shared workflow, displays its durable state, and sends explicit operator commands through defined application interfaces.

The design must allow a later console process, Windows service, or scheduled host to run that same Core workflow against the same contracts and durable state without copying or rewriting trading logic. Market data, TSX clock/calendar, persistence, policy assignment, and operator notification boundaries must therefore be injected interfaces. A durable store-backed single-instance lease applies across every present and future host so WPF and a standalone process can never monitor the same Delphi Live system concurrently.

The initial Operational Champion is installed inactive. Explicit operator activation is durable, audited, and effective no earlier than the next regular-session boundary; policy identity and role assignments cannot change during a session. Once enabled, the WPF host automatically starts the monitor for the regular session at 09:30 when WPF is running. WPF closing stops the V1 host; unattended operation is deferred, and any resulting session coverage gap is reported rather than hidden.

Launching WPF after 09:30 or restarting it during the session starts monitoring at the next eligible scheduled cycle. Delphi Live reloads the frozen watchlist identity, holdings, active protection floors, portfolio guards, policy assignments, and the same identities for pending protective sells. A pending Buy expires as `BuyRestartExpired`.

Any process-continuity gap clears operational rolling-family buffers and unfinished ordinary confirmation. Previously persisted pre-gap bars remain valid historical evidence but cannot be joined across the gap for a new rolling live action. Newly returned bars for intervals missed while the host was absent are `LateResearchOnly`. Persistence, Directional Volume, rolling Price Movement, and the prior-range reference remature only from fresh consecutive on-time post-resume observations. Because the first fresh bar supplies an endpoint rather than a prior fresh close, the first fully mature four-family evaluation occurs on the fifth fresh completed bar. If it is strong, it may start confirmation; the next fresh strong result may complete it. The earliest ordinary new entry is therefore normally the sixth fresh completed bar after resume.

Session VWAP is the exception because it is a cumulative fact rather than a rolling confirmation sequence. It may be reconstructed after restart only from a complete 09:30-to-current path whose every bar was originally persisted on time; if the host missed any required interval, later historical retrieval cannot repair VWAP for operational use that session. Quote-based hard-loss, active profit-floor, and pending-sell protection resumes immediately and does not wait for ordinary-family rematurity.

The runtime never fabricates missed bars, pretends it made earlier decisions, or overlaps two monitor cycles to catch up. Genuine finalized earlier bars returned by the provider may be retained as late research evidence with their actual receipt time, but cannot create retrospective recommendations, confirmation, fills, or portfolio actions. An incomplete session is visibly labelled and cannot qualify as a clean engineering-shakedown or promotion cohort.

## Collection capacity, priority, and retention

Each completed five-minute interval creates one durable collection-cycle identity. Required symbols are deduplicated across the frozen watchlist, all Delphi Live policy portfolios, and XIU; each symbol is requested and normalized once, and every assigned policy evaluates the same canonical observation. A challenger never creates a second provider request for evidence the champion already collected.

A collection cycle has until the next scheduled five-minute cycle begins. Two collection cycles may never overlap. Work still unfinished at that boundary is cancelled where the provider permits; a response received afterward is marked `LateResearchOnly` with its request and receipt times. It may be retained for source diagnostics and research, but it cannot repair the operational miss, restore Data Confidence, confirm a sequence, change a ranking, create or cancel an action, or produce a retrospective fill.

When source capacity is constrained, work proceeds in this deterministic order:

1. pending protective-sell retries and holding-protection quote checks;
2. five-minute evidence for held symbols;
3. XIU evidence;
4. active unheld candidates in the Operational Champion's current live-ranking order; and
5. quiet or dismissed candidates in the same deterministic full live order defined below, including the rule that an exact live tie places today's frozen candidates before non-reselected `SessionCarryCandidate` symbols.

The complete live tie-break contract, ending with ticker, makes each class stable even when a symbol has no current daily rank. The order protects capital first and preserves the shared benchmark before spending remaining capacity on new-entry research. It does not remove a lower-priority symbol from the frozen observation set. Any required symbol not completed by the cycle deadline receives a persisted per-symbol miss such as `CycleDeadlineExceeded`, follows the accepted Data Confidence rules, and is attempted normally in the next cycle.

A repeated response for the same symbol, session, provider, and completed interval references the existing canonical observation and cannot be counted or acted upon twice. A conflicting duplicate is retained as a source anomaly and cannot overwrite evidence already used for a decision.

V1 applies no automatic purge or roll-up to its normalized five-minute facts, source-quality metadata, family judgments, policy decisions, actions, fills, or outcomes. They remain available for replay, audit, calibration, and later model preparation. Any future retention limit must be a separately reviewed policy and must preserve the immutable decision audit; provider licensing constraints must be checked before deciding whether unnormalized payload bodies may also be retained. Source throughput is measured during engineering shakedown rather than assumed from the design.

## Two-stage advisory flow

1. Delphi performs one full-universe daily scan.
2. The union of Delphi's 25 Continuation picks and 25 Breakout picks is frozen for that regular session.
3. Delphi Live deduplicates that union and observes each unique watchlist symbol, plus tracked holdings, once per completed five-minute interval.
4. Daily Setup Quality remains visible and unchanged for attribution.
5. Live Momentum Quality, recommendation state, and live rank may change every five minutes.
6. An unheld candidate without at least a current positive nudge moves to quiet observation: it leaves the active recommendation list and positive alerts but remains collected and diagnostically ordered through the close.
7. Existing holdings are not sold merely because their relative rank falls. Entries use live rankings; exits remain governed by separately defined risk, profit-protection, safety-veto, and combined-live-weakening rules.

Each candidate retains whether Continuation, Breakout, or both selected it, together with its daily rank and Daily Setup Quality under each source lens. Deduplication is a collection rule only: it must not erase lens attribution or cause a symbol selected by both lenses to be counted twice as independent market evidence. The frozen set may contain up to fifty unique symbols and may be smaller when the lists overlap.

In V1, **Daily Setup Quality** is a structured frozen profile rather than a newly invented scalar: source-lens membership, eligibility and publication facts, each lens's rank and ranking key, the common Delphi composite, daily strategy identity, and daily reason/gate evidence. The UI may summarize that profile, but it cannot average Continuation and Breakout or silently turn the profile into another score. Live Momentum Quality remains the separate current intraday judgment.

### Frozen daily source

At the 09:30 session boundary, Delphi Live freezes the newest `Valid` `OfficialPaper` Delphi run for that recommendation date that was durably created no later than 09:30 Toronto time. Selection order is descending `CreatedUtc`, then `StartedUtc`, then stable run identity, matching the existing System Shadow convention. The selected run, its daily `StrategyVersionId`, and every published Continuation/Breakout row used by the union are persisted with the Delphi Live session. A later same-day Delphi rerun cannot rewrite that session.

The run's `MarketDataAsOf` must be the immediately preceding canonical completed XIU session; otherwise it is not fresh enough to freeze. If no qualifying run existed by 09:30, record `NoValidDelphiRun`, create no unheld watchlist, and take no new Delphi Live risk that day. Existing Delphi Live holdings and pending exits continue to receive collection and protection. A late WPF start applies the same historical 09:30 availability cutoff, so a run created after the market opened cannot be selected with hindsight.

## Regular-session timing

All session boundaries use the official TSX calendar in `America/Toronto` time. Delphi Live's scheduled bar endpoints are 09:35, 09:40, and every five minutes thereafter through 16:00. The V1 collection attempt begins two minutes after each endpoint—09:37 through 16:02—using the safe offset already established by ADR-0045. The offset is a versioned collector setting to be verified during shakedown, not permission to consume a forming bar. A checkpoint such as `09:50` always names the completed bar's market-end time; its decision occurs only after that exact bar has been received and persisted, normally around 09:52.

Each required symbol receives one primary bar request in that cycle. The response must contain the exact completed five-minute interval, positive OHLC values, `Low <= min(Open, Close)`, `High >= max(Open, Close)`, non-negative volume, and a receipt time after the interval ended. A missing exact bar is `NoCompletedBar`; an older newest bar is `StaleNoNewBar`; a forming bar is ignored; an identical duplicate is idempotent; and a conflicting completed duplicate is invalid under ADR-0030. None may be replaced by the nearest interval. Provider retries or chunking, if required by the client, remain inside the same cycle identity and deadline and cannot start an overlapping cycle.

This design explicitly refines ADR-0045 only by adding the Delphi Live/shared-evidence collection extension beginning at 09:37. It does not move the existing 09:47 first-safe poll or decision boundary for Operator Ghost, existing System Shadow, or `DelayedIntradaySwingV1`, and it does not turn any accepted fifteen-minute policy into a five-minute policy. A shared WPF collector may persist the additional exact five-minute bars once, while each existing policy continues to evaluate only its own accepted intervals and first-safe time. The final 16:00 bar may be received at 16:02 for evidence and closing valuation, but no new Buy may result and a protective sell without a usable regular-session quote becomes or remains `ExitPendingOvernight`.

Ordinary combined Live Momentum may first create an actionable entry after the four contiguous opening observations are complete and received, no earlier than 09:50. Before then the candidate is `WarmingUp`; incomplete windows do not cast substitute votes. New buy decisions and their usable immediate quote fills are permitted from 09:50 inclusive until 15:45 exclusive. At 15:45, any unfilled buy expires and no new position may open.

Candidate ranking, evidence collection, holding protection, and protective sell decisions continue through the regular-session close. An exit that cannot obtain a usable regular-session quote follows the accepted pending and `ExitPendingOvernight` rules.

### Multi-session rollover

Each new session's frozen daily union replaces the prior session's **unheld** candidate set. An unheld symbol absent from the new list leaves active monitoring, while all of its prior evidence remains immutable. Open Delphi Live holdings and pending exits remain in the observation set regardless of whether the new daily scan selects them.

If an open holding is selected again, the new Daily Setup Quality, source lenses, and ranks are attached as a new session thesis; they never overwrite the daily thesis and evidence that caused the original entry. If it is not selected again, record `HeldNotReselected` for explanation, but do not sell merely because of that absence. The position remains governed by live market evidence, profit and loss protection, and safety rules.

Ordinary rolling intraday state and confirmation reset to `WarmingUp` at every session boundary; yesterday's observations cannot confirm today's entry or ordinary combined-weakening decision. From 09:30 until ordinary four-family scoring becomes available, open holdings receive fresh quote-based opening safety checks so capital protection does not wait for the twenty-minute window.

During that warm-up period, only four sell paths may act: an already-pending protective sell may obtain its fill; a valid positive bid at least five percent below the position's average purchase price creates an immediate full `WarmupHardLoss5Pct` exit decision; a valid positive bid at or below an already-active versioned profit-protection or trailing floor creates an immediate full `WarmupProfitFloorBreach` exit decision; or a completed five-minute bar may create the provisional `FastDownside10Pct` exit defined below. The quote or completed bar that proves a new breach is persisted as decision evidence but cannot also be its fill. After persisting the decision, Delphi Live requests a fresh quote and applies the normal causal sell-fill contract. Ordinary `LiveWeakeningExit` remains unavailable until the four signal families are mature, and `HeldNotReselected` never creates an exit.

### Holding ownership and observation scope

The mandatory Delphi Live observation set contains the frozen daily union; every open holding exposed by TraderVI's tracked-position view, regardless of Real, Operator Ghost, existing System Shadow, or Delphi Live ownership; every pending Delphi Live exit; and any symbol that a Delphi Live policy sold earlier that session but may still re-enter. Collection is deduplicated across those sources. A sold, non-reselected carried symbol remains a `SessionCarryCandidate` through that close so the accepted same-day recovery rule is actually possible; it leaves the set after the session unless selected again or held again.

Existing frozen-daily System Shadow, Operator Ghost, and Real positions keep their current controllers and ledgers. When their symbols are already collected by the shared evidence service, Delphi Live may display its independent advisory interpretation, but it cannot create, cancel, fill, or mutate their orders or positions. In particular, a Real holding may receive an advisory warning only. A Delphi Live exit rule has action authority solely over the position owned by the evaluating Delphi Live Shadow policy.

## Live Momentum evidence families

Delphi Live V1 begins as a transparent rule-based monitor and evidence collector, not a newly trained intraday model. It preserves raw values and a separate judgment for four signal families:

1. **Persistence** — whether the stock repeatedly improves or weakens over recent completed five-minute observations.
2. **Price movement** — how far and how quickly price moves over multiple intraday horizons and relative to XIU.
3. **Volume support** — whether observed trading activity supports the price move, using an appropriate time-of-day comparison.
4. **Price structure** — whether price holds useful intraday reference levels such as VWAP, the previous close, recent highs, and established support.

The same raw five-minute facts and family judgments are computed once per symbol regardless of source lens. In V1, lens emphasis is explanation-only: it cannot add a vote, change Live Momentum Quality, alter an action state, create a rank bonus, or change the underlying observations. A Continuation explanation may highlight persistence and holding structure, while a Breakout explanation may highlight price expansion and volume participation. A symbol selected by both lenses retains one evidence set and shows both daily source ranks and both thesis explanations; the readings are not averaged and dual membership is not treated as extra strength.

The provisional persistence measure uses the most recent four completed five-minute observations, a rolling twenty-minute window. For the opening 09:30–09:35 bar, both the stock and XIU interval returns are `(BarClose / BarOpen) - 1`. For every later bar, each return is `(CurrentBarClose / ImmediatelyPrecedingContiguousBarClose) - 1` over the same timestamps. Non-positive or missing endpoints, a missing XIU match, or a gap in the four-observation path makes Persistence `NotMature` or `Unavailable`; it never substitutes the prior session's close for the opening anchor.

Each valid interval contributes:

- `+1` when the stock return is greater than zero and strictly greater than XIU's matching return.
- `-1` when the stock return is less than zero and strictly less than XIU's matching return.
- `0` in every other valid mixed case, including an exactly unchanged stock close or equal stock/XIU returns.

The score therefore ranges from `-4` to `+4`. It never accumulates for the entire session. Actual percentage movement remains separate so four tiny increases cannot masquerade as a powerful move.

The provisional Persistence state preserves more information than a three-way label:

- `+3` or `+4`: `Supportive`;
- `+2`: `PositiveLeaning`;
- `-1`, `0`, or `+1`: `Neutral`;
- `-2`: `NegativeLeaning`; and
- `-3` or `-4`: `Weakening`.

The exact integer remains visible, so `+4` may rank ahead of `+3` and `-4` remains more severe than `-3`. `PositiveLeaning` and `NegativeLeaning` are explanation and within-state tie-breaking evidence only: neither counts as a full Supportive or Weakening vote in the four-family agreement rule, changes the safety path, or overrides broad family agreement.

### Price Movement measurements

For every completed observation with bar-end timestamp `T`, Delphi Live records the stock's exact percentage return over these windows:

- rolling twenty minutes;
- rolling one hour;
- rolling two hours;
- rolling three hours; and
- since the previous completed TSX session's close.

For a rolling horizon `H`, the end is the current bar close at `T`. When `T - H` equals the 09:30 session open, the start is the opening 09:30–09:35 bar's open; after that, the start is the exact close of the completed bar whose end timestamp is `T - H`. Thus the first twenty-minute result at 09:50 is `09:50 close / 09:30 open - 1`, the next at 09:55 is `09:55 close / 09:35 close - 1`, and the first one-hour result at 10:30 is `10:30 close / 09:30 open - 1`. Two- and three-hour windows follow the identical rule. Before the exact start exists, the window is `NotMature`.

XIU uses the identical start and end timestamps and the same open-versus-close convention. The stock's excess return is its window return minus XIU's matching return. Volume Support's twenty-minute price-sign check uses this exact same stock window rather than a separately calculated return. The previous-close return alone is `CurrentBarClose / PreviousCanonicalSessionClose - 1` and remains non-voting context.

The twenty-minute window measures immediate acceleration, while the one-hour window tests whether that move is being sustained. The longer windows preserve same-day context and evidence for later calibration and opportunity discovery.

Every endpoint must come from completed, causally available observations. A window that has not yet accumulated enough same-session history is `NotMature`; it is not silently shortened and does not count as Neutral, Weakening, missing data, or a negative vote.

Price Movement uses a volatility-adjusted noise boundary rather than applying one fixed percentage to every stock. For daily session `i`, classic `TrueRange[i] = max(High[i] - Low[i], abs(High[i] - Close[i-1]), abs(Low[i] - Close[i-1]))` and `TrueRangePct[i] = TrueRange[i] / Close[i-1]`. V1's action-affecting `MedianTrueRangePct10` sorts the ten percentages from the ten completed canonical TSX sessions immediately preceding the live session and averages the fifth and sixth values. It therefore requires eleven positive, structurally valid, non-duplicate stock daily bars aligned to eleven contiguous canonical XIU sessions; a missing or duplicate aligned session makes the ruler unavailable rather than compressing the sample. The five-, fourteen-, and twenty-session variants use the same formula and require six, fifteen, and twenty-one aligned bars respectively.

The ruler is computed only from point-in-time daily bars and frozen before live evaluation with its source-through session. The raw percentage and XIU-relative percentage remain visible. This normalization affects only whether a live move is meaningful; it does not change the fixed opportunity thresholds, including the five-percent research target.

Delphi Live also retains a multi-horizon volatility profile rather than one blended volatility number. Its layers are rolling one-hour and two-hour intraday volatility, current-session-to-date volatility, and frozen trailing three-, five-, and ten-session daily volatility. The windows remain separately stored and support named short-versus-long comparisons such as `Expanding`, `Normal`, or `Contracting`; overlapping windows do not each become an independent vote. Volatility measures activity, not direction: expansion during a positive XIU-relative move may support momentum, while expansion during a negative move may strengthen a danger explanation. In V1, these profile layers are explanatory and research evidence only; only the frozen ten-session ruler affects the Price Movement judgment. Exact profile calculations and categorical boundaries remain open.

The ten-session baseline is a provisional V1 champion, not a proven optimum and not a consequence of the one-to-five-session holding target. A fourteen-session median is its single promotion challenger because it is steadier and closest to TraderVI's existing ATR14 convention. Five- and twenty-session medians are retained as diagnostics. Only the ten-session version affects V1; every version is calculated from the same point-in-time history, frozen with its source-through date and definition version, and attached to the resulting decision and outcomes. Promotion requires predeclared comparison on untouched one-, three-, and five-session evidence; historical decisions are never recomputed under a later winner.

The provisional V1 window hierarchy prevents nested returns from becoming multiple votes:

- Before twenty minutes matures, Price Movement is `NotMature`; the previous-close return remains visible but cannot create a family judgment alone.
- From twenty minutes until one hour matures, the twenty-minute result supplies an explicitly labelled `20mOnly` provisional judgment.
- Once both windows mature, a meaningful twenty-minute or one-hour direction carries the family judgment when the other window is Neutral or agrees.
- Meaningful opposite twenty-minute and one-hour directions produce `NeutralConflict`, unless a separately defined safety veto applies.
- Two-hour, three-hour, and previous-close returns remain context labelled `Aligned`, `Mixed`, or `Opposed`; they do not add votes or change the V1 Price Movement category.

This keeps magnitude separate from Persistence: several tiny increases may make Persistence supportive while Price Movement remains Neutral, and one large move may make Price Movement supportive while Persistence remains Neutral.

Each mature twenty-minute or one-hour window first receives a direction class before the numeric noise threshold is applied:

- a rising stock that outperforms XIU is `AlignedUp` and may become Supportive when the move is large enough;
- a falling stock that underperforms XIU is `AlignedDown` and may become Weakening when the move is large enough;
- a rising stock that lags XIU is `RisingButLagging` and remains Neutral;
- a falling stock that outperforms XIU is `FallingButOutperforming` and remains Neutral for momentum, never Supportive; and
- a flat or immaterial stock/relative result is `MixedOrFlat` and remains Neutral.

An absolute decline still enters the independent safety-veto evaluation even when the stock is outperforming a worse XIU market. Relative strength therefore cannot disguise a real loss. The named mixed states remain visible so Neutral never means "nothing was observed."

For the provisional V1 raw-price noise boundary, `RawMoveUnits = stock window return / MedianTrueRangePct10`:

- `AlignedUp` becomes Supportive when `RawMoveUnits >= +0.25`.
- `AlignedDown` becomes Weakening when `RawMoveUnits <= -0.25`.
- A smaller aligned move is `Neutral` with reason `RawMoveBelowThreshold`.
- The same threshold applies to the twenty-minute and one-hour windows; reaching it sooner already captures greater speed.
- A missing, non-positive, or invalid frozen ruler produces `BaselineUnavailable` and no Price Movement vote, never division by zero or a Neutral market judgment.

The value `0.25` is an explicit starting hypothesis, not an evidence-derived optimum: it means the stock has travelled one quarter of its recent typical full-session range. It was chosen because it is easy to explain, rejects tiny moves, remains reachable intraday, and provides one stable baseline to evaluate. The threshold stays symmetric so ordinary Price Movement remains comparable in both directions; faster asymmetric loss handling belongs to the separate safety-veto policy.

The complete predeclared raw-move comparison set is symmetric `0.15 / 0.25 / 0.35`. Only `0.25` controls V1. The `0.15` challenger represents earlier but potentially noisier recognition; `0.35` represents later but more selective recognition. Both challengers are calculated from the same causal snapshots and stored as counterfactual research results, but cannot alter live state, alerts, ranks, collection, or safety behavior. No additional threshold may be added after inspecting these results without starting a new, separately versioned comparison.

Relative agreement uses the same frozen ruler: `ExcessUnits = (stock return - XIU return) / MedianTrueRangePct10`. A window is `RelativeUp` at or above `+0.05`, `RelativeDown` at or below `-0.05`, and `ExcessWithinDeadband` between those boundaries. A missing or invalid stock return, XIU return, or frozen ruler produces `RelativeBaselineUnavailable` and no relative vote; it never falls back to zero.

The complete predeclared relative-deadband comparison set is symmetric `0.025 / 0.05 / 0.10`. Only `0.05` controls V1; `0.025` and `0.10` are research-only counterfactuals. For example, when a stock's normal daily range is 4%, the operational `0.05` boundary requires it to lead or lag XIU by at least 0.20 percentage points before relative direction counts.

For each mature twenty-minute or one-hour window, raw movement and relative movement are jointly required:

- the window is Supportive only when `RawMoveUnits >= +0.25` **and** `ExcessUnits >= +0.05`;
- it is Weakening only when `RawMoveUnits <= -0.25` **and** `ExcessUnits <= -0.05`;
- meeting only the raw threshold is Neutral with reason `RawMoveWithoutRelativeAgreement`;
- meeting only the relative threshold is Neutral with reason `RelativeMoveWithoutRawAgreement`;
- opposing meaningful raw and relative directions are Neutral with reason `RawRelativeConflict`; and
- missing either required calculation makes the window `Unavailable`, not Neutral.

Thus a stock cannot become Price Movement Supportive merely by beating a falling XIU while its own move is too small or negative, and it cannot become Weakening merely by lagging XIU while its own price has not fallen enough.

### Volume Support evidence constraint

TraderVI's stored five-minute history is selection-dependent: collection follows the changing daily Delphi watchlist and tracked holdings rather than retaining continuous per-symbol intraday history for the whole universe. A symbol may therefore have a complete session on one selected day, no observations on many unselected days, and another complete session later.

V1 must not require a twenty-session per-symbol same-clock-slot baseline, interpret unobserved sessions as zero volume, or treat the irregular selected sample as continuous history. Same-clock-slot relative volume and same-time cumulative pace remain desirable future measurements only after a prospective evidence contract supplies adequate comparable history. The provisional V1 Volume Support calculation and its opening-bar and continuity rules are defined below.

The provisional V1 fallback is a rolling twenty-minute directional-volume balance over four contiguous completed five-minute bars:

`DirectionalVolumeBalance20 = sum(DirectionSign * BarVolume) / sum(BarVolume)`

For the opening 09:30–09:35 bar, `DirectionSign` is `+1` when its close is above its open, `-1` when below, and `0` when equal. For each later bar it compares that close with the immediately preceding contiguous same-session close using the same signs. The result ranges from `-1` to `+1` and describes whether more observed volume accompanied rising or falling intervals; it is not true buyer-versus-seller order flow. Fewer than four contiguous bars, any missing scheduled bar, an invalid comparison endpoint, or zero total volume produces `Unavailable` and no family vote rather than compressing the window. Using the opening bar's open allows four valid observations to mature at 09:50 without borrowing the prior session's close.

The provisional full-vote boundary is symmetric `+0.10 / -0.10`:

- `DirectionalVolumeBalance20 >= +0.10` together with a positive twenty-minute price return is `Supportive`.
- `DirectionalVolumeBalance20 <= -0.10` together with a negative twenty-minute price return is `Weakening`.
- A balance inside the boundary is `Neutral` with reason `DirectionalVolumeWithinDeadband`.
- A meaningful balance whose sign conflicts with the twenty-minute price return is `Neutral` with reason `VolumePriceConflict`.

With no flat-bar volume, `0.10` corresponds to approximately 55% of volume on rising intervals versus 45% on falling intervals. It is an intentionally sensitive starting hypothesis, not an evidence-derived optimum. Volume Support still cannot act alone: it remains one family inside the broad-agreement and confirmation rules, while verified downside continues through the independent safety path.

Current cumulative volume divided by the median full-session volume from the prior twenty completed daily bars may be shown separately as `FullDayVolumeFraction20`. It means only "fraction of a typical full day's volume observed so far"; without comparable time-of-day history it is not called volume pace, does not classify Volume Support, and cannot affect a recommendation or rank.

### Price Structure references

Price Structure V1 records three separately visible references against the latest completed five-minute close:

1. **Previous completed-session close** — whether current price is holding above or below the stock's last causally available daily close.
2. **Session-to-date bar-derived VWAP** — whether current price is above or below the approximate volume-weighted average price represented by every completed regular-session five-minute bar received from the open through the decision time.
3. **Prior rolling twenty-minute range** — whether current price is above the high, inside the range, or below the low of the four contiguous completed five-minute bars immediately preceding the current decision bar.

The three relationships remain separate rather than being averaged. The current bar is excluded from the prior range so it cannot define and break its own boundary. An unavailable previous close, incomplete session path, missing range bar, zero-volume VWAP path, or immature range makes only the affected reference `Unavailable`; it is never replaced with a nearby value or an assumed Neutral reading. The level buffer and family-combination rule are the provisional V1 definitions below.

For bar-derived VWAP, each valid completed regular-session bar uses `TypicalPrice = (High + Low + Close) / 3`. The session value is `sum(TypicalPrice * BarVolume) / sum(BarVolume)` from the 09:30 opening bar through the current completed bar. Only completed bars participate. The full scheduled path must be present, each OHLC value and volume must be valid, and cumulative volume must be positive; otherwise the VWAP reference is `Unavailable`. A zero-volume bar contributes no weight but does not invent volume or price.

Price Structure applies a provisional symmetric buffer using the same frozen volatility ruler:

`StructureDistanceUnits = ((CurrentClose / ReferencePrice) - 1) / MedianTrueRangePct10`

For the previous close and session VWAP, a distance at or above `+0.05` is `Above`, a distance at or below `-0.05` is `Below`, and a smaller distance is `AtLevel`. For the prior twenty-minute range, the current close must exceed the prior high by `+0.05` range units to be `Breakout` or fall below the prior low by `-0.05` range units to be `Breakdown`; otherwise it is `InsideOrAtRange`. If a stock's frozen typical daily range is 4%, the buffer is approximately 0.20% of price. This is a provisional anti-flicker boundary, not a proven optimum.

`Above` and `Breakout` are bullish reference states; `Below` and `Breakdown` are bearish; `AtLevel` and `InsideOrAtRange` are neutral. Price Structure combines the available reference states without averaging them:

- at least two bullish and none bearish is `Supportive`;
- one bullish and none bearish is `PositiveLeaning`;
- no directional reference is `Neutral`;
- at least one bullish and at least one bearish is `NeutralConflict`;
- one bearish and none bullish is `NegativeLeaning`; and
- at least two bearish and none bullish is `Weakening`.

`PositiveLeaning`, `NegativeLeaning`, and `NeutralConflict` do not cast a full family vote. Three agreeing directional references may order ahead of two, but Price Structure remains only one family vote. At least two of the three reference states must be available before Price Structure may be classified; with fewer than two it is `Unavailable` and casts no vote. An unavailable reference is neither Neutral nor bearish, and any source failure still affects the separate Data Confidence state.

Broad agreement determines Live Momentum Quality. Let `S` be the number of full Supportive family votes and `W` the number of full Weakening family votes; Neutral, Leaning, Conflict, NotMature, and Unavailable family results cast neither full vote. These are every possible combined state:

| S | W | Live Momentum state | Entry or exit meaning |
|---:|---:|---|---|
| 4 | 0 | `Strong` / `FourOfFour` | Highest entry-eligible tier |
| 3 | 0 | `Strong` / `CleanThree` | Entry-eligible tier |
| 3 | 1 | `StrongWithConflict` | Lowest entry-eligible tier; a safety veto still wins |
| 2 | 0 | `PositiveNudge` | Non-actionable positive nudge |
| 2 | 1 | `PositiveNudgeWithConflict` | Lower positive nudge; non-actionable |
| 2 | 2 | `MixedConflict` | Balanced full-vote conflict; no entry or ordinary exit |
| 0–1 | 0–1 | `Neutral` with `SupportTilt`, `WeakTilt`, or `Conflict` detail when applicable | No entry or ordinary exit |
| 1 | 2 | `NegativeNudgeWithConflict` | Warning and lower rank; no ordinary exit |
| 0 | 2 | `NegativeNudge` | Warning and lower rank; no ordinary exit |
| 1 | 3 | `WeakWithConflict` | Blocks entry; eligible for confirmed weakening exit |
| 0 | 3 | `Weak` | Blocks entry; eligible for confirmed weakening exit |
| 0 | 4 | `VeryWeak` | Blocks entry; immediate weakening exit for a held position |

An ordinary family-level Weakening vote is not a safety veto. Two Weakening votes never sell a holding by themselves, and two Supportive votes never authorize an entry by themselves.

- A second consecutive valid `WeakWithConflict`, `Weak`, or `VeryWeak` observation produces `Dismissed` for active new-entry ranking, while quiet evidence collection continues through the close.
- For a position owned by a Delphi Live Shadow portfolio, the first valid `VeryWeak` observation issues a full deterministic `LiveWeakeningExit`. A `WeakWithConflict` or `Weak` observation issues that full exit only when the next completed five-minute observation is also valid and remains in any of those three strong-weakening states. Missing or invalid evidence cannot confirm the exit. This is a dedicated, versioned risk rule—not a relative-rank sale and not an escape from the exit policy.
- Individual-family Leaning states and Unavailable families do not count as Supportive or Weakening votes, and the denominator remains four rather than shrinking to the available count.
- A hard safety veto retains authority regardless of the ordinary family counts.

Daily Setup Quality and Live Momentum Quality are never collapsed into an unexplained number. Their raw facts, family judgments, combined state, and contribution to the final live ordering remain visible and separately calibratable.

### Provisional daily/live ordering

V1 gives every observed candidate a deterministic total order, including candidates that cannot currently enter. The descending Live Momentum order is: `Strong/FourOfFour`, `Strong/CleanThree`, `StrongWithConflict`, `PositiveNudge`, `PositiveNudgeWithConflict`, `Neutral/SupportTilt`, plain `Neutral`, `Neutral/Conflict`, `Neutral/WeakTilt`, `MixedConflict`, `NegativeNudgeWithConflict`, `NegativeNudge`, `WeakWithConflict`, `Weak`, then `VeryWeak`.

Within the same combined state, compare the count of PositiveLeaning family results descending, the count of NegativeLeaning results ascending, and the exact Persistence score descending. These are transparent tie-breaks, not another weighted vote. Daily Setup Quality then breaks remaining ties: use the best numerical frozen rank among the source lenses that selected the symbol, then the higher common Delphi composite, then ticker. A dual-selected symbol retains and displays both ranks but receives no extra bonus and the ranks are not averaged. Raw percentage, volume, and structure measurements remain visible and determine their own family judgments, but V1 does not invent another blended numeric score from them.

A `SessionCarryCandidate` that is absent from today's frozen union has no current Daily Setup Quality, lens rank, or common Delphi composite. It may outrank a currently selected symbol through a stronger combined Live Momentum state or the live tie-breaks above, but when all those live comparisons tie it sorts after every current-session frozen candidate; ties among such carry candidates resolve by ticker. Its original entry thesis remains explanation evidence only and is never reused as today's rank. It does not appear in a lens-specific Daily-versus-Live Top 5 diagnostic unless today's frozen list selected it for that lens.

For a lens-specific diagnostic, the daily tie-break uses only that lens's own frozen rank; it never borrows the other lens's better rank. Ranking never changes an existing holding's exit authority.

## Recommendation states and confirmation

For entry confirmation, `EntryEligibleStrong` means any of the three otherwise permitted supportive combinations: four-of-four Supportive, clean three-of-four `Strong`, or three Supportive plus one ordinary Weakening as `StrongWithConflict`. Movement among those three strength tiers does not break confirmation.

Recommendation lifecycle is per policy and separate from Live Momentum Quality:

- `WarmingUp` — required current-session families have not matured.
- `Watching` — the symbol is being evaluated but has no live entry confirmation or pending action.
- `Emerging` — the first valid `EntryEligibleStrong` observation has started confirmation.
- `EntryEligible` — at least two consecutive valid strong observations have confirmed the symbol, but no Buy is currently pending. It remains eligible only while each new scheduled evaluation is valid and `EntryEligibleStrong`; a full portfolio leaves it ranked in this state rather than enlarging another position.
- `BuyPending` — one persisted Buy decision is inside its accepted quote-attempt window.
- `Held` — this policy owns an open Delphi Live Shadow position and no sell is pending.
- `ExitPending` — one idempotent protective sell exists, including `ExitPendingOvernight`.
- `Dismissed` — an unheld symbol has the accepted confirmed-weakening sequence and is removed from active entry ranking while collection continues.

`Closed` is a durable position event, not a terminal candidate state. After a completed exit, an otherwise in-scope symbol returns to `Watching` with no reusable confirmation and may follow the accepted fresh-evidence re-entry path. A candidate may have different lifecycle states under different policies, while all policies reference the same market observation.

Presentation and collection priority use a separate `Active` versus `Quiet` flag. During warm-up, frozen candidates remain visibly `WarmingUp` in frozen daily order. After maturity, an unheld candidate is `Active` only while it is `Emerging`, `EntryEligible`, or `BuyPending`, or its current Live Momentum state is `PositiveNudge`, `PositiveNudgeWithConflict`, or any entry-eligible Strong tier. An unheld `Watching` candidate whose current state is Neutral, Mixed, or negative is `Quiet`; `Dismissed` is also Quiet. A non-dismissed Quiet candidate returns to Active immediately when a current positive-nudge or Strong state appears, while a Dismissed candidate retains the stricter two-Strong recovery rule. Held and ExitPending symbols always keep the highest protective collection priority but do not compete in the new-entry list.

The full deterministic order remains available for diagnostics and quiet-collection priority. Only the Active subset is shown as the live opportunity ranking or may create positive alerts, so a straggler does not crowd the operational list merely because evidence collection continues.

The state progression is:

- A first valid `EntryEligibleStrong` observation produces an informational `Emerging` state with reason `StrongConfirmationStarted`.
- The immediately following scheduled five-minute evaluation completes confirmation when it is also valid and `EntryEligibleStrong`, even if its strength tier differs. It produces `EntryEligible` with reason `StrongConfirmationCompleted`; if it wins an available portfolio slot and every separate safety, Data Confidence, cutoff, and causal-fill gate passes, a persisted Buy decision moves it to `BuyPending`.
- A missing or invalid scheduled observation, any result outside `EntryEligibleStrong`, a non-Normal Data Confidence state, or a safety veto breaks an unfinished positive streak with reason `StrongConfirmationBroken`. Observations on opposite sides of that interruption cannot be joined.
- A first valid combined three-family Weakening observation produces `Weak`, or `WeakWithConflict` when the fourth family is Supportive; four-family agreement produces `VeryWeak`. All three states block new entries immediately.
- A second consecutive valid strong-weakening observation produces `Dismissed` for active new-entry ranking, even if the two observations move among `WeakWithConflict`, `Weak`, and `VeryWeak`.
- A quietly monitored unheld `Dismissed` candidate may recover in the same session, but only from two entirely fresh consecutive valid `EntryEligibleStrong` observations. The first records `DismissedRecoveryStarted`; the second records `DismissedRecoveryCompleted` and may restore entry eligibility. Any intervening interruption leaves it `Dismissed` and requires a new recovery sequence.
- When the candidate is already held by that Delphi Live Shadow portfolio, the first `VeryWeak` observation or the second consecutive strong-weakening observation also issues a full `LiveWeakeningExit`.

Missing or invalid evidence cannot confirm either direction. The accepted pending-Buy, runtime-restart, exit, and recovery rules below complete these lifecycle transitions. Every transition preserves its previous state, new state, timestamp, source evidence, policy identity, and reason code.

## Causal Delphi Live Shadow fill observation

Delphi Live does not have to wait for the next five-minute bar to observe an actionable price. After an entry or exit decision has first been persisted with its decision time, it may immediately make a fresh TMX quote request for that symbol. The resulting Shadow action may use only a value first received after the decision existed.

The quote observation must preserve the decision time, request-start time, receipt time, symbol, raw `price`, `bid`, and `ask`, the selected field and rule, and the provider/source-contract version. The current quote client exposes those price fields but does not expose a provider event or last-trade timestamp, so the evidence must be described as the **first price observed by TraderVI after the decision**, not as a guaranteed exact-market-time fill.

For a Shadow market sell, V1 uses a valid positive `bid`; for a Shadow market buy, it uses a valid positive `ask`. These are the prices currently offered on the side needed by the action and are not commissions. If the required side is absent but `price` is valid and positive, the system may use `price` as an explicitly lower-confidence `EstimatedFill`, preserving that distinction in the portfolio ledger and scorecard. An `EstimatedFill` updates the V1 Shadow portfolio and remains included in strategy evaluation and promotion evidence; it is not silently discarded because its price may be slightly approximate.

The official scorecard includes every valid fill, including `EstimatedFill`. Beside it, a diagnostic view reports results using only bid/ask fills, the count and percentage of estimated fills, and the difference between the two views. The diagnostic does not remove trades or independently control promotion; it exposes whether approximate prices materially influence the reported result.

A one-minute bar that overlaps the decision time is not a safe substitute: it may still be forming, and a later request could return a revised close, high, or low containing post-decision information. When the first immediate quote has no usable side-specific field or fallback `price`, make two additional bounded attempts within sixty seconds and persist every attempt.

After all three attempts fail, a new-buy action expires with `BuyQuoteUnavailableExpired` and may be reconsidered only from a fresh later five-minute evaluation. Quote failure alone does not erase an otherwise unbroken confirmed-Strong state: if the next valid completed evaluation is still `EntryEligibleStrong` and every current gate passes, it may create a new decision with `BuyRetryFreshObservation`. The new action has its own identity and quote attempts; the expired action is never revived.

A pending Buy exists for at most sixty seconds and is cancelled sooner if newly available state makes entry impermissible. Persist the applicable reason: `BuyCancelledSignal`, `BuyCancelledSafety`, `BuyCancelledDataConfidence`, `BuyCancelledPortfolio`, `BuyCutoffExpired`, or `BuyRestartExpired`. Signal, safety, or Data Confidence cancellation also breaks positive confirmation and requires two fresh consecutive `EntryEligibleStrong` observations. Portfolio, cutoff, and restart recovery follow their separately defined runtime/session rules. A sell action does not expire while its Delphi Live Shadow position remains open: it stays pending, produces a visible execution-data warning, and retries once on every later monitoring cycle until it obtains a usable quote. Retries must remain idempotent so one pending exit cannot create duplicate sales. No path may invent a price or fall back to a more favourable retrospective value.

A pending protective sell is durable. At the regular-session close it becomes `ExitPendingOvernight`, remains visibly associated with the open Shadow position, and is never silently cancelled. On an application restart, the same pending-exit identity is reloaded rather than recreated. Quote attempts resume during the next eligible regular session, and the first usable post-open quote fills it under the accepted bid-or-`EstimatedFill` rule.

Within one completed evaluation cycle, persist all shared evidence and policy judgments first. Each policy then processes existing or newly triggered protective sells before considering a Buy, and it may spend sale proceeds or reuse a position slot only after the sell fill is durably committed. Remaining entry-eligible candidates are considered in the accepted rank order against the resulting cash, NAV, guards, entry cap, and cutoff. This capital-first ordering is deterministic per policy and cannot make one policy's fill change another policy's portfolio.

## Same-session recovery and churn control

A completed `LiveWeakeningExit` or safety-veto exit does not create a blanket rest-of-day ban. The same Delphi Live policy may buy the symbol again later that session if fresh post-exit evidence independently passes the normal entry confirmation, all safety vetoes are clear, Data Confidence permits action, and no sell remains pending. Evidence and confirmation from before the exit cannot be reused.

The design caps **entries**, not total buy-and-sell actions. V1 permits no more than two completed buy fills in the same symbol for each Delphi Live policy and regular-session date: one initial entry and one possible recovery re-entry. An expired, rejected, or unfilled buy does not consume an entry. After the second entry, the policy records `EntryLimitReached` and continues monitoring and collecting evidence but cannot buy that symbol again that session. A protective sell always remains allowed regardless of the entry count, and each parallel policy owns its own independent count.

This Delphi Live direction does not rewrite the accepted next-five-minute-boundary convention for the existing `DelayedIntradaySwingV1` outcome. Delphi Live requires its own separately named and versioned fill-observation contract.

## Capital preservation and safety vetoes

Capital preservation is the highest-priority invariant. Genuine verified downside evidence may act faster than ordinary positive confirmation. Safety decisions remain separate from ordinary ranking changes.

The accepted safety-veto families are:

1. **Fast downside move** — a separately versioned extreme decline within one completed five-minute bar; V1's provisional rule is defined below.
2. **Confirmed support failure** — loss of an important support level with corroborating selling evidence.
3. **Holding protection** — a separately versioned loss, profit-protection, or portfolio-risk boundary for an existing position.

Delphi Live V1 has no `MarketWideDanger` veto and no blanket entry pause merely because XIU is down. XIU remains a required relative-strength and market-context input inside the normal signal calculations, but it cannot independently block every new Buy or force a holding exit. This intentionally preserves the ability to buy an individually strong stock during a weak broad-market session; stock-specific signals, loss protection, profit protection, and the remaining safety rules still apply.

Persistent downside is not a second safety-veto rule. It is handled by the already-defined `LiveWeakeningExit`, which owns both relevant forms of combined weakness: `BroadImmediateWeakening` means the first valid four-of-four `VeryWeak` observation, while `PersistentWeakeningConfirmed` means a valid `WeakWithConflict` or `Weak` observation followed by another valid observation that remains in any strong-weakening state. Both subconditions create the same single `LiveWeakeningExit`; their distinct detail reason is retained for explanation and calibration.

### Provisional V1 fast downside

The earlier rejection of standalone one-bar fast-downside exit authority was subsequently reversed. V1 defines the bar return as `FastBarReturn = (BarClose / BarOpen) - 1`, using a valid completed regular-session five-minute bar with positive open and close values. `FastBarReturn <= -0.10`—a decline of ten percent or more from that bar's open to its close—may by itself create a full `FastDownside10Pct` exit for a Delphi Live Shadow holding, even when the position remains above its five-percent average-cost loss boundary.

The rule may act on the first completed 09:30–09:35 bar while the system is `WarmingUp`. It intentionally does not call an overnight gap a five-minute return; opening loss exposure remains covered by the always-active five-percent average-cost rule. The completed bar is persisted as decision evidence, the exit decision is persisted next, and only a fresh post-decision quote may supply the simulated fill. The ten-percent threshold and open-to-close definition belong to the immutable policy version and begin as a Shadow hypothesis that can be compared and changed prospectively without rewriting history.

### Confirmed support failure

`ConfirmedSupportFailure` requires all three facts in the same completed five-minute evaluation:

1. the latest close is `Below` session-to-date bar-derived VWAP using the accepted Price Structure buffer;
2. that close is also a `Breakdown` below the prior rolling twenty-minute low using the same buffer; and
3. Volume Support is `Weakening` under the accepted `DirectionalVolumeBalance20` rule.

A level break without weakening volume, or weakening volume without both price breaks, cannot fire this veto. All inputs must be available and causally mature; because the prior range excludes the current bar and requires four earlier bars, this veto cannot normally mature before the evaluation following the first five completed session bars, at approximately 09:55. Once valid, one observation blocks a new entry and creates a full `ConfirmedSupportFailure` exit for a holding in that Delphi Live Shadow portfolio. The completed-bar evidence and decision are persisted before a fresh quote supplies the simulated fill. Real holdings remain advisory-only.

A verified safety veto overrides an otherwise Strong three-of-four Live Momentum result. For an unheld candidate it blocks a new entry. In Delphi Live Shadow it may drive a separately defined protective action. A Real holding receives a human alert only; this concept does not authorize broker activity.

The five-percent average-cost loss boundary is active for the entire lifetime of an open position. It does not deactivate after `WarmingUp`, reset at a session boundary or application restart, wait for four-family maturity, or yield to an otherwise positive ranking. While the position remains open, the boundary persists through overnight carry and acts as soon as a usable regular-session quote is available under the session contract.

A valid positive bid at or below `average purchase price * 0.95` creates an immediate full `HardLoss5Pct` exit decision. If this occurs during `WarmingUp`, also preserve `WarmupHardLoss5Pct` as the phase-specific reason. The triggering quote is persisted as decision evidence but cannot also be the simulated fill; after the decision is stored, a fresh quote supplies the fill under the accepted causal contract. The five-percent value belongs to the immutable policy version so historical decisions remain reproducible.

Any already-active versioned profit-protection floor likewise remains effective while a new session is `WarmingUp`; it does not depend on four-family maturity. Its trigger observation, decision reason, and subsequent fill observation remain separate so the system can truthfully explain both why it sold and which later price it used for the simulated fill.

### Provisional V1 profit protection

Profit protection uses completed regular-session five-minute closes to activate and raise its floor, while a valid current bid determines whether the market has crossed that floor:

1. Before a completed close reaches three percent above the position's average purchase price, no profit-protection floor is active; the separate five-percent hard-loss boundary and other safety rules still apply.
2. When a completed close first reaches at least three percent above average purchase price, activate a break-even floor at the average purchase price.
3. When a completed close first reaches at least five percent above average purchase price, enter trailing mode. Set the floor to the greater of its existing value or two percent below the highest completed five-minute close observed since entry: `max(previous floor, highest completed close since entry * 0.98)`.
4. Recalculate that trailing candidate after each later completed five-minute close. The stored floor may rise but may never fall, and it survives session rollover and application restart while the position remains open. A newly opened or re-entered position starts a fresh peak and profit-protection state; it cannot inherit the prior closed position's floor.
5. A valid positive bid at or below the active floor creates an immediate full `ProfitProtectionFloorBreach` exit decision. Record the triggering bid, average purchase price, highest completed close, activation stage, prior floor, new floor, policy version, reason code, and timestamps. During `WarmingUp`, preserve the more specific `WarmupProfitFloorBreach` phase reason as well.
6. The triggering bid is decision evidence, not the simulated fill. A fresh post-decision quote supplies the fill under the accepted causal contract.

The floor is a sell trigger, not a guaranteed execution price. A later bid or an `EstimatedFill` may produce a fill below it. The three-percent activation level, five-percent trailing activation level, and two-percent trail distance are changeable settings in the immutable Delphi Live policy version and must be evaluated in Shadow evidence rather than silently retuned.

A quote received before a completed bar activated or raised the floor cannot test that new floor. Persist the floor change first, then obtain a fresh quote to test it. If that fresh bid breaches the new floor, persist the exit decision and obtain another fresh post-decision quote for the simulated fill. This ordering prevents an earlier observation from being evaluated under a rule that did not yet exist.

If one observation fires more than one exit rule, Delphi Live creates or retains exactly one idempotent protective-sell intent for the position's full remaining quantity. It persists every rule that fired and its evidence, but it never creates duplicate sell orders for the same exit intent. Additional exit evidence received while that sell remains pending is appended to the same decision history rather than creating another sale.

### Provisional V1 exit-reason precedence

When several exit rules first fire in the same evaluation, V1 labels the highest item in this order as primary and stores every other fired rule as supporting evidence:

1. `HardLoss5Pct`
2. `FastDownside10Pct`
3. `ProfitProtectionFloorBreach`
4. `ConfirmedSupportFailure`
5. `LiveWeakeningExit`

This ordering changes only the explanation, never whether an independently satisfied rule may exit or how many shares are sold. A rule firing alone is primary. Once a sell decision is pending, its original primary reason is immutable; stronger or additional conditions observed later are timestamped as supporting evidence and cannot create a second sale. The ordering belongs to the policy version so it remains visible and changeable without rewriting historical explanations.

Strong combined weakening may also close a position owned by the applicable Delphi Live Shadow portfolio through the distinct, deterministic `LiveWeakeningExit` rule. This single rule satisfies the system's persistent-downside behavior without creating a competing safety-veto exit. It does not alter a current frozen-daily Shadow portfolio, and a Real holding still receives only a human advisory. The persisted reason must distinguish `BroadImmediateWeakening` from `PersistentWeakeningConfirmed` and both from a hard safety-veto exit.

The authority, confirmation timing, causal-fill handling, fresh-evidence same-session re-entry rules, and simultaneous-trigger precedence for `LiveWeakeningExit` are accepted.

## Data confidence is not market weakness

Missing or invalid observations indicate uncertainty about the monitoring system, not bearish information about the stock. Delphi Live records Data Confidence separately from Market Judgment:

- **One consecutive miss:** `Ambiguous`. Do not change the market score; do not create a new Buy from stale evidence.
- **Two consecutive misses:** `Degraded`. Freeze promotions and new entries and show a monitoring warning.
- **Three consecutive misses:** `Monitoring Lost`. Remove the candidate from actionable rankings and urgently alert when it is held.

For this ladder, one per-symbol cycle is a **miss** when either the symbol's exact scheduled five-minute bar or the matching XIU bar is absent, late for operational use, stale, structurally invalid, conflicting, or not durably persisted by the cycle deadline. One source-wide XIU failure therefore creates a visible per-symbol miss for every evaluation that required that benchmark. A legitimate `NotMature` window, a valid zero-volume bar, an unavailable optional diagnostic, a quote/fill failure, or the absence of a valid morning Delphi run does not increment this market-observation counter; those conditions retain their own states and reasons.

Collection attempts continue in every state. The system never invents a price or interprets data failure as a decline. After one, two, or three consecutive misses, one subsequent clean observation resets the consecutive-miss count to zero and restores Data Confidence to `Normal`. A clean observation requires the exact current stock and matching XIU bars to pass the versioned input checks and be persisted on time; it does not require every rolling family to have rematured. A partial response does not qualify.

Confidence recovery does not backfill a missing bar, reuse stale evidence, or shorten a calculation window. Signal families that require contiguous observations remain `NotMature` or `Unavailable` until their own input requirements are satisfied, so restored Data Confidence does not automatically restore an actionable rank or permit a Buy. Holding protection and pending protective sells remain active throughout a confidence outage and its recovery.

## Explainability contract

TraderVI must be able to answer questions such as "Why did you sell that stock?" from durable deterministic facts.

Every consequential Delphi Live decision must persist at least:

- decision identity and timestamp;
- daily Delphi run, candidate, lens, and original rank;
- ruleset and component-definition versions;
- source five-minute bar or observation identities;
- raw values for all four signal families;
- each family judgment and the combined Live Momentum judgment;
- Data Confidence before and after the decision;
- recommendation state before and after the decision;
- every fired rule, veto, and reason code;
- requested advisory or Shadow action; and
- whether an action was completed, rejected, expired, or remained an alert.

If any deterministic rule caused an exit, the record—not an LLM—must identify the exact rule and supporting measurements. It must distinguish a hard safety veto, `LiveWeakeningExit`, loss protection, and profit protection rather than reducing them to a generic sell reason. A future DotLLM integration may translate the record into conversational language, compare the facts, or answer follow-up questions. Under `Docs/oracle-rules.md`, it remains downstream, may cite only persisted dossier facts, and may not invent causality or influence the decision.

An LLM asked to analyze candidates or propose picks would be a different future component, provisionally called an **AI Selector Challenger**, not an expanded DotLLM narrator. It would receive the same frozen point-in-time facts as the deterministic baseline, return a constrained structured decision, and persist its exact model, prompt, input, output, timing, and failure identity. It would begin with Shadow-only recommendations, could not supply missing market facts, change a safety veto, approve itself, or place an order, and would require its own untouched scorecard and human promotion decision. A successful version may later earn authority as the advisory picker, but deterministic safety vetoes always retain final authority. Delphi Live V1 does not depend on this component.

## Changeability and versioning contract

Delphi Live must be implemented as explicit, replaceable policy rather than scattered formulas or magic numbers:

1. **Measurement** computes and persists raw causal facts such as prices, returns, XIU comparisons, volume, volatility baselines, and data quality. It does not know recommendation thresholds.
2. **Window classification** applies one named policy version to raw facts and emits a category plus a stable reason code.
3. **Family combination** combines the four visible family categories without recomputing their measurements.
4. **Recommendation and safety** consume the family results and independent veto results through separately versioned rules.

All horizons, thresholds, confirmation counts, categorical boundaries, and precedence rules belong to one reviewable Delphi Live policy definition. Every decision persists the exact policy version, source-through timestamps, raw facts, derived facts, family judgments, and reason codes used. A behavior change creates a new policy version and preserves the old results; configuration is never changed in place and historical decisions are never silently reclassified.

Delphi Live has its own immutable policy identity, separate from but linked to Delphi's daily strategy identity. Every live decision records both: the daily `StrategyVersionId` explains why the symbol entered the frozen watchlist, while the `DelphiLivePolicyVersionId` explains how intraday evidence was interpreted. Either may advance without pretending the other changed, and scorecards can group or compare them independently.

V1 uses a limited hybrid policy definition. Code owns formulas, precedence, state transitions, missing-data behavior, validation rules, and stable reason-code meanings. Immutable stored policy versions own the named numeric thresholds, horizons, confirmation counts, volatility rulers, and categorical boundaries accepted by that code version. The active policy is validated and frozen at the session boundary; there are no mid-session overrides or in-place edits. A threshold-only successor creates a new stored policy version, while a formula, precedence, or reason-semantic change also requires new code identity. Unknown fields, unsupported evaluator versions, invalid ranges, or contradictory values fail closed rather than falling back to current defaults.

### Parallel policy roles

Delphi Live may evaluate multiple immutable policy versions against the same causal evidence, subject to a strict cap of one champion and no more than two active non-champion Shadow versions:

- **Operational Champion:** exactly one assigned version. It is the only version allowed to control normal Delphi Live recommendations, live rankings, and operator alerts. It has no authority to place a broker order.
- **Active Shadow Challenger:** an experimental contender. Up to two may run when there is no active Shadow Baseline; each produces its own judgments and simulated results but cannot alter the champion's output or operational state.
- **Shadow Baseline:** the former champion retained after a promotion. It is not a new contender, but it occupies one of the two non-champion Shadow slots and has the same lack of operational authority.
- **Research Counterfactual:** an additional predeclared calculation retained for analysis only. It has no live ranking, alert, recommendation, or paper-portfolio authority and does not occupy an Active Shadow Challenger slot.

Initial V1 activation assigns only the Operational Champion. Challenger slots begin empty; calculating the already predeclared counterfactual facts does not activate a challenger. Starting a portfolio-bearing comparison later requires its own explicit, audited experiment assignment effective at a session boundary.

Each completed five-minute observation is collected and normalized once, then referenced by every assigned evaluator. Within one active experiment, only one named threshold family may differ between the champion and challengers; every other setting, watchlist input, safety veto, and Data Confidence rule remains identical. This prevents a result from becoming impossible to attribute and avoids a combinatorial set of threshold mixtures.

The ten-versus-fourteen-session volatility ruler, raw-move boundary, and relative-deadband variants are three different hypothesis families. Only one family may own active Shadow Challenger portfolios at a time. Values from the other families may still be calculated as `ResearchCounterfactual` diagnostics, but they cannot be combined into the active challenger or influence its decisions. A later family begins only through a new experiment identity after the current comparison and any required former-champion baseline period end.

Every policy version keeps a separate immutable decision stream, family judgments, reason codes, and scorecard identity. When a capital-constrained paper portfolio is attached, its cash, positions, orders, and performance ledger are independent and are never summed with another portfolio. Delphi Live V1 uses the single-portfolio contract below rather than multiplying every policy into separate Top 3 and Top 5 variants.

A separately recorded **Policy Assignment** gives a policy version its role and effective session. Assignments are frozen for the session; promotion is human-approved and takes effect no earlier than the next session. Promotion never edits earlier results. The former champion automatically becomes the Shadow Baseline for the next thirty clean completed trading sessions, providing a direct post-promotion comparison and rollback reference. Its historical record remains available after its active baseline assignment ends.

While that thirty-session Shadow Baseline assignment is active, a different threshold family may not begin an experiment. The remaining Shadow slot may stay empty or test another predeclared value from the same threshold family; this preserves one-variable attribution across every simultaneously active comparison.

### Aligned comparison inception

Promotion evidence never compares a newly created cash-only challenger with the Operational Champion's older, already-invested portfolio. Each active `PolicyComparisonExperiment` identifies one champion version, one hypothesis family, up to two challenger versions, a common effective session, and a common immutable notional starting capital. At that next session boundary it creates a cash-only comparison portfolio run for the champion and for each challenger, all with zero positions. The champion comparison run is a Shadow control; it is distinct from and cannot mutate the continuing operational champion portfolio.

Every comparison run consumes the same market observations but owns independent cash, positions, action state, and ledger entries. Only these aligned runs and market sessions meeting the shared coverage mask enter the paired promotion test. An operational portfolio's longer history remains visible but cannot substitute for an aligned control. After a promotion, the operational portfolio carries its existing positions into the next session under the newly assigned policy; promotion never invents liquidation or entry fills. A new thirty-session comparison then starts aligned cash-only runs for the new champion and former-champion Shadow Baseline.

Implementations must keep these stages deterministic and independently testable with table-driven boundary cases. Diagnostics and human summaries must expose every new input, family result, veto, and policy version in accordance with `Docs/design-rules.md`.

Existing ADR-0030 five-minute market bars remain shared canonical facts keyed by symbol, interval, and market-event time when their source contract is compatible. Delphi Live policy identity belongs on its evaluation, decision, and portfolio records rather than creating competing copies of the same market bar. The implementation ADR must add or version storage contracts where the current single-policy poll or four-portfolio System Shadow schema cannot represent Delphi Live; it may not reinterpret existing rows or loosen existing portfolio controllers in place.

### Delphi Live Shadow portfolio V1

The initial Operational Champion cannot activate without an explicit positive starting-capital amount and currency persisted with its portfolio generation. The operator may copy the same immutable TFSA comparison-capital snapshot already used to seed System Shadow or enter a new explicitly labelled simulation amount; Delphi Live never reads or moves broker cash and has no silent default. Activation starts the champion cash-only at the next session boundary, and that amount is the first session-return denominator.

After activation, V1 permits no deposit or withdrawal in either the continuing Operational Champion portfolio or any comparison-only run because its return and drawdown calculations are not cash-flow adjusted. Never infer a capital change from an account snapshot. Reject a requested capital change with `CapitalChangeUnsupportedV1`; if comparison state was nevertheless changed, invalidate the experiment from that event forward and restart fresh aligned cash-only runs at a later session boundary under a new experiment identity. Supporting later capital changes requires a separately accepted cash-flow-adjusted accounting contract and cannot reinterpret this generation's history.

Each assigned policy receives one independent role portfolio: the Operational Champion has the continuing operational Shadow portfolio, while a challenger or baseline has a non-operational Shadow portfolio. An aligned experiment may additionally create the comparison-only champion control run defined above. No portfolio shares cash, positions, or orders with another, and their values must never be summed as though the alternatives were simultaneously achievable.

V1 permits at most five concurrent distinct holdings and one open position per symbol. Each new position targets up to twenty percent of that portfolio's current net asset value, limited by its available cash; the system never borrows or forces a purchase merely to fill all five slots. If fewer than five candidates qualify, the remainder stays in cash. Vacancies are offered in the accepted live-ranking order.

There are no add-on purchases or automatic rebalancing in V1. The twenty-percent value is an entry target, not a command to sell a winner merely because appreciation later raises its portfolio share. These rules belong to the separately versioned Delphi Live policy and do not alter the existing Continuation/Breakout Top 3 or Top 5 Shadow portfolios.

V1 uses whole virtual shares. For a selected Buy fill, `TargetNotional = min(20% * CurrentNav, AvailableCash)` and `Quantity = floor(TargetNotional / FillPrice)`. A quantity below one records `InsufficientCashForOneShare` and leaves the slot in cash; fractional shares, borrowing, and overspending are not allowed.

Portfolio valuation is mark-to-market evidence, not an executable sale:

- **Opening NAV:** cash plus each carried share quantity multiplied by that session's exact 09:30–09:35 bar open. It becomes available only after those opening bars are complete and persisted.
- **Checkpoint NAV:** cash plus each open quantity multiplied by its exact completed five-minute close for the same checkpoint.
- **Closing NAV:** cash plus each open quantity multiplied by the exact 16:00 completed-bar close.
- **Daily portfolio return:** `ClosingNav / PreviousClosingNav - 1`; the first comparison session uses immutable starting capital as its denominator.
- **Drawdown:** the percentage decline of each complete checkpoint NAV from the highest earlier complete checkpoint NAV in that same portfolio run, beginning with starting capital.

Every held symbol must have the exact aligned mark. The system never carries a prior price forward to authorize risk. Until Opening NAV is complete, or whenever current Checkpoint NAV is unavailable, new buys are blocked with `PortfolioNavUnavailable` while exits continue. A missing closing mark makes that portfolio session unusable for paired-return and drawdown evidence and prevents it from being a clean cohort. Display code may show a clearly labelled last-known estimate, but it cannot drive sizing, a guard, or promotion evidence.

Dividends, splits, and other corporate-action accounting remain unsupported in V1, consistent with System Shadow. If a known or suspected action affects an open position or outcome path, record `CorporateActionUnsupported`, block the affected performance evidence and promotion cohort, and require later review; never auto-adjust cash, quantity, cost basis, or history from an inferred event.

Each Delphi Live portfolio independently freezes its regular-session Opening NAV under the rule above. A complete Checkpoint NAV decline of three percent or more from that value activates `DailyBuyingPaused` for the rest of the session: pending buys expire and new buys, re-entries, and rotations are blocked, while every protective exit remains enabled. The pause resets for the next regular session.

A decline of ten percent or more from that portfolio's highest completed-session closing value activates `CapitalReviewRequired`. This blocks new risk across sessions and restarts but never blocks exits. Only an explicit human resume with a durable reason may clear it; resume re-arms the guard from the reviewed current portfolio value. The three- and ten-percent values are provisional, versioned V1 thresholds inherited for comparability with existing System Shadow behavior.

## Opportunity-discovery evidence

The research workstream records the exact maximum price gain and elapsed trading time from causal five-minute decision points. It can later ask how frequently thresholds such as 1%, 2%, 3%, 5%, 10%, and 15% were reached over five-minute, hourly, same-session, and multi-session horizons.

### Live observation outcome anchor

`LiveObservationOutcomeV1` is a research label, not a trade or assumed fill. Each expected symbol evaluation at a canonical completed five-minute checkpoint has one outcome anchored to that bar's close and the time TraderVI first received the completed bar. The anchor bar describes information already known; none of its earlier high, low, or movement may be counted as a future outcome. Future measurement begins with the next completed five-minute interval.

For every usable anchor, record raw symbol return, matching raw XIU return, stock-minus-XIU excess return, maximum favourable movement, signed maximum adverse movement, and the first reach of each fixed positive opportunity threshold `1% / 2% / 3% / 5% / 10% / 15%`. The exact return remains stored even when no threshold is reached.

Let `P0` be the anchor close and `X0` the matching XIU close. At an exact horizon endpoint `h`, `RawReturn[h] = Price[h] / P0 - 1`, `XiuReturn[h] = XiuPrice[h] / X0 - 1`, and `ExcessReturn[h] = RawReturn[h] - XiuReturn[h]`. Over the subsequent path through `h`, `MFE[h] = max(0, max(FutureHigh / P0 - 1))` and `MAE[h] = min(0, min(FutureLow / P0 - 1))`. The anchor bar is excluded from both paths. A threshold `t` is first hit when a later valid high is at least `P0 * (1 + t)`; ties use the earliest valid interval or, for daily evidence, the earliest session ordinal.

The V1 horizons are:

- twenty, sixty, one-hundred-twenty, and one-hundred-eighty regular-session minutes after the anchor, but only when the exact five-minute endpoint fits in the same session;
- Session 1 close, meaning the anchor date's regular-session close;
- Session 3 close, meaning the second canonical XIU session after the anchor date; and
- Session 5 close, meaning the fourth canonical XIU session after the anchor date.

A same-session path uses only the subsequent canonical five-minute bars. A same-session threshold hit records the first five-minute interval in which the high proves the level was reached. Later-session paths combine the valid remaining-anchor-day five-minute path with aligned daily OHLC bars for later sessions when continuous five-minute coverage is unavailable. Later daily bars may identify only the first session ordinal in which a level was reached, never an invented intraday time. The anchor date's daily high or low is never used because it includes prices observed before the live signal. If a later daily bar proves both favourable and adverse levels in one session, their intraday ordering is `SameSessionUnknown`.

An intraday horizon that cannot fit before the close is `NotApplicable`, not failed evidence. Session 1 is also `NotApplicable` when the anchor is already the 16:00 close because no future same-session path exists. A missing exact endpoint invalidates only that endpoint return. A gap anywhere in a required path makes path-dependent maximum movement and threshold timing unusable through the affected horizon, without destroying a separately valid exact closing return. A future horizon that has not matured is `Pending`; no nearby bar, date, or forward fill may substitute. Exact later-received bars may mature a research label with their receipt provenance, but they never repair operational coverage, make an incomplete host session clean, or rewrite the live decision.

The intended future event study will count one continuous price move as one event and retain its five-minute observations to determine when it first became recognizable. V1 deliberately does not guess the volatility-adjusted reversal boundary needed to perform that grouping. `LiveObservationOutcomeV1` therefore preserves checkpoint labels but never treats overlapping checkpoints as independent evidence; scorecards aggregate them inside their market-session cohort. Event grouping remains a later, separately versioned research derivation over the retained facts.

Every OHLC input must be positive and satisfy `Low <= min(Open, Close)` and `High >= max(Open, Close)`; invalid or conflicting data cannot contribute. Known or suspected splits and unsupported corporate actions mark the affected path `CorporateActionUnsupported` and exclude it from performance or threshold claims rather than interpreting a mechanical price discontinuity as momentum. V1 does not invent an adjustment when a trustworthy point-in-time adjustment identity is unavailable.

Research opportunity returns use observed price changes with zero commission or fee deduction, as accepted in the dialogue. Existing immutable Athena and Shadow definitions are not rewritten; any Delphi Live outcome contract is separately named and versioned.

Evidence is classified into four logical baskets:

1. **Model-grade** — passed historical TraderVI eligibility and has trustworthy evidence.
2. **Near-eligible** — missed one adjustable eligibility rule but otherwise has trustworthy evidence.
3. **Out-of-scope but valid** — reliable evidence for a security clearly outside current operational scope.
4. **Unusable** — incomplete, inconsistent, or invalid evidence retained for audit but excluded from learning claims.

Model-grade and near-eligible evidence may both participate in future experimental training. An eligible-only baseline and combined challenger must be evaluated separately; the combined model must beat the eligible-only model on untouched model-grade evidence before it can become primary. Training on near-eligible evidence does not authorize trading near-eligible securities.

## Calibration and future machine learning

Calibration keeps the two system stages distinct:

- **Daily Delphi scorecard:** Did the frozen daily list contain and correctly order later opportunities?
- **Delphi Live scorecard:** Did live promotion, reranking, blocking, and demotion improve on the frozen daily view?
- **Component scorecards:** Did Persistence, Price Movement, Volume Support, and Price Structure each add stable information?
- **Raw-move threshold scorecard:** How did the frozen `0.15 / 0.25 / 0.35` variants differ in trigger frequency and timing, later same-session and one-/three-/five-session raw and XIU-relative returns, opportunity capture, and downside?
- **Relative-deadband scorecard:** How did the frozen `0.025 / 0.05 / 0.10` variants differ in near-zero direction flips, agreement with absolute movement, trigger timing, later returns, and downside?
- **Safety scorecard:** Did each veto avoid losses without rejecting too many later winners?
- **Data-confidence scorecard:** Did degraded monitoring states prevent unsafe decisions without being misreported as market weakness?

The existing accepted Daily Delphi/Athena outcome definitions remain unchanged. Delphi Live adds a separate checkpoint-ranking comparison so it can answer whether live information improved the daily ordering without rewriting the daily scorecard or pretending a research mark was a fill.

At every scheduled entry-window bar endpoint from 09:50 through 15:40 inclusive, whether or not any candidate qualifies, build two equal-weight diagnostic baskets separately for Continuation and Breakout:

1. the frozen Daily Top 5 for that lens, unchanged throughout the session; and
2. the Live Top 5 drawn only from that lens's candidates whose Operational Champion evidence has completed the two-observation strong confirmation and remains currently `EntryEligibleStrong`, with Data Confidence `Normal` and no active safety veto, using the accepted live-state order and that lens's own frozen rank for its daily tie-break. This pre-portfolio research fact is named `ConfirmedLiveEligible`.

Both baskets use the same timestamp and `LiveObservationOutcomeV1` anchors, with eligibility snapshotted before portfolio actions from that checkpoint are processed. The frozen basket ignores live signals because it is the control. Portfolio cash, current holdings, position limits, entry counts, and portfolio loss guards do not filter either research basket. If either basket has fewer than five eligible published symbols, unused equal-weight slots remain cash with zero return; they are not silently removed and the remaining names are not enlarged. Average symbols within a checkpoint, checkpoints within their market session, and then give each market-session cohort equal weight. This is a ranking diagnostic, not another executable portfolio and not the Operational Champion's combined cross-lens portfolio.

Actual Delphi Live policy performance remains a separate capital-constrained scorecard. Its profits, losses, NAV, exits, and drawdown come only from the accepted quote-based or explicitly tagged `EstimatedFill` Shadow ledger. A completed-bar close used by `LiveObservationOutcomeV1` is never presented as an executable trade price.

### Outcome coverage

Collection coverage begins with every scheduled symbol/checkpoint slot for the frozen deduplicated watchlist, carried holdings, and one separate XIU benchmark slot per checkpoint; deprioritization, host downtime, failed collection, and missing bars never make an expected slot disappear. XIU has benchmark-coverage records and is not given a stock-style `LiveObservationOutcomeV1`. Each outcome metric reports valid, degraded, invalid, pending, and not-applicable counts. Matching XIU evidence is required for an excess-return metric, but missing XIU does not invalidate a separately complete raw symbol-return metric.

For a particular metric, structurally `NotApplicable` anchors are reported but excluded from its applicable denominator. Every other expected applicable anchor remains in the denominator: `Pending` reduces completion coverage, `Invalid` reduces usable coverage, and `Valid` plus explicitly enumerated `Degraded` results are usable. `Valid` means every exact endpoint or path element required by that metric passed its source and structural checks. `Degraded` means an explicitly versioned, lower-confidence input allowed by the metric contract—such as an `EstimatedFill` in the portfolio view—remained mathematically usable; an unknown defect is `Invalid`, never degraded by default.

Following ADR-0024, a matured metric is `Ready` only at one-hundred-percent usable coverage, `Degraded` from ninety-five percent inclusive to below one hundred percent, and `Blocked` below ninety-five percent. Counts and failure reasons remain visible even while performance fields are blocked. A clean engineering-shakedown or promotion session additionally requires one hundred percent usable operational stock and XIU collection slots, stable policy identities, reconstructible decisions and fills, no overlapping cycle, and no WPF host-coverage gap, even if some research labels are reconstructed later.

Champion and challenger promotion uses one predeclared eligibility mask applied identically to both policies and only same-session paired portfolio returns. `NoAction`, a live cash slot, a veto, an entry limit, or a quote failure that leaves cash is an observed policy result rather than missing evidence and cannot be dropped merely because it hurts performance. Only a session whose portfolio state or closing NAV cannot be reconstructed is unusable for that paired return, and the exclusion remains visible in coverage.

The initial threshold evaluation uses a `10 + 30 + 30` rollout, counting one completed TSX market-session date as one cohort. Symbols, repeated five-minute observations, overlapping windows, dual-lens membership, and reruns never create additional independent cohorts.

1. **Engineering shakedown — 10 clean cohorts:** validate causal timing, window maturity, missing-data behavior, persistence, replay, and reporting. These cohorts are permanently excluded from threshold-performance claims. A material measurement defect invalidates affected cohorts and restarts the clean sequence.
2. **Discovery — 30 additional matured paired cohorts:** after shakedown, explicitly activate one named-family experiment with new aligned cash-only champion-control and challenger portfolio runs. Each contender must accumulate the same thirty eligible paired sessions. Select at most one already-predeclared immutable challenger from that family; variants from other families remain non-portfolio research counterfactuals. Discovery results may guide selection but cannot themselves prove promotion.
3. **Untouched confirmation — 30 additional matured cohorts:** freeze the selected challenger and start new aligned cash-only champion/challenger comparison runs at the next session boundary. Test without adding variants, changing metrics, or revising thresholds.
4. **Human review:** a challenger that clears every evidence and risk gate becomes eligible for review, never automatically active. Failure is reported as `NotProvenRetainV1`.

Each discovery and untouched performance cohort must be portfolio-evaluable for both aligned policies, mature through the five-session research-outcome horizon, and meet the accepted coverage contract. The thirty paired discovery plus thirty fresh paired untouched cohorts provide the sixty portfolio-evaluable performance cohorts required by ADR-0022 for an ordinary low-risk promotion proposal; the primary improvement interval and downside pass/fail use only the untouched runs. Review still requires the predeclared regime coverage, cohort-aware uncertainty, capital-protection guardrails, and explicit human approval. The app must display the current phase and must not present an experimental threshold as trusted merely because it is running successfully.

The first baseline is the transparent Delphi Live rule set. A future ML model is only a challenger. It must use point-in-time inputs, preserve the four component facts for diagnosis, and outperform the rule baseline on untouched future evidence without weakening capital protection. Failure to add stable value means the rule-based system remains active.

### Champion-versus-challenger promotion score

The primary promotion comparison is the paired difference in daily Delphi Live Shadow portfolio return: compare champion and challenger on the same eligible untouched market-session cohorts, after applying each policy to its aligned independent comparison portfolio. Discovery results cannot enter this primary estimate after helping choose the challenger.

V1 calculates the 95% interval with a deterministic paired moving-block bootstrap over consecutive session-return differences: five-session blocks, ten-thousand resamples, a two-sided percentile interval, and a pseudorandom seed derived from the immutable experiment identity. The complete method, seed, eligible dates, and software/code identity are persisted. "Reliably better" means the interval's lower bound is greater than zero; a higher point estimate alone is insufficient. A later statistical method requires a new predeclared experiment contract and cannot be chosen after seeing results.

Capital preservation is a hard companion requirement. Over the untouched comparison window, the challenger may not have a worse maximum checkpoint portfolio drawdown or a worse average return across its own worst ten percent of eligible session returns than the champion. For `N` eligible sessions, the tail contains the lowest `max(1, ceiling(0.10 * N))` returns for each policy. Reports still show total return, trade counts, win rate, exposure, turnover, no-fill rate, `EstimatedFill` rate, and exits by reason, but none can compensate for failing the primary improvement test or either downside guardrail.

Regime coverage reuses the immutable daily Delphi regime for each session: `Bearish` when both accepted benchmarks are bearish, `Bullish` when either is in its accepted uptrend, and `Mixed` otherwise. ADR-0022's minimum total-cohort, untouched-window, and regime-count requirements remain mandatory; if the initial thirty untouched sessions do not contain the required regime coverage or a conclusive interval, the challenger remains unproven and the untouched run continues without changing policy. Passing makes it eligible for human review only; it never promotes itself.

## Decision record

### Accepted

- Evolve Delphi toward a five-minute advisory view that remains informed by shorter one-to-three-day swings.
- Use a full-universe daily scan followed by a focused intraday watchlist.
- Freeze the deduplicated union of Delphi's 25 Continuation and 25 Breakout picks, collect each unique symbol once, and preserve every source-lens membership and daily rank.
- At 09:30, freeze the newest valid audited `OfficialPaper` run for that date that was created by the boundary and uses the immediately preceding canonical XIU session. Ignore later reruns; when none qualifies, record `NoValidDelphiRun`, take no new risk, and continue protecting holdings.
- Freeze watchlist membership for the session while allowing live ranks and recommendations to change.
- Place the Delphi Live engine and all behavior in a WPF-independent shared-Core workflow. Use TraderVI.WPF only as V1's host and UI, behind injected runtime interfaces, so a later standalone host can reuse the same logic, durable state, and store-backed single-instance lease without a rewrite.
- Install the initial Operational Champion inactive and require explicit audited activation effective at the next session boundary. When enabled and WPF is running, start monitoring automatically at 09:30; V1 stops when WPF closes and does not claim unattended coverage.
- On late start or restart, begin at the next scheduled cycle without retrospective actions. Restore holdings, protection, portfolio and policy state, and pending sells; expire pending buys; resume quote safety immediately; clear operational rolling buffers, use five fresh completed bars for the first fully mature four-family evaluation, and require a sixth fresh Strong observation to complete entry confirmation. Reuse session VWAP only when every required bar was originally persisted on time.
- Deduplicate each five-minute collection cycle across all policies and never overlap cycles. At capacity, process pending protective work and holdings first, XIU next, active candidates after that, and quiet candidates last; persist every missed deadline instead of silently shrinking the frozen set.
- Treat late responses as research-only and duplicate responses as idempotent. They cannot repair an operational miss or create a second decision. Retain V1's normalized evidence and complete decision audit without automatic purge or roll-up, subject to a later explicit retention policy and provider licensing for original payload bodies.
- Schedule exact five-minute bar endpoints from 09:35 through 16:00 and begin V1 collection two minutes after each endpoint. A checkpoint names market time, while decisions occur only after receipt and persistence; reject forming, stale, structurally invalid, missing, and conflicting bars without changing existing fifteen-minute policy semantics.
- Observe completed five-minute evidence; do not use sub-five-minute evidence in V1.
- Collect regular-session evidence from 09:30 through 16:00 Toronto time, permit ordinary live entry actions no earlier than 09:50, and require both a new-buy decision and its fill before the 15:45 cutoff. Continue rankings, evidence collection, and protective exits through the close.
- Replace prior-day unheld candidates with the new frozen daily union, but continue observing every open holding and pending exit. Preserve the original entry thesis, attach any new selection as separate session evidence, record `HeldNotReselected` without forcing a sale, reset ordinary rolling confirmation at the session boundary, and run opening safety checks on holdings during warm-up.
- During `WarmingUp`, allow an existing pending protective sell to fill, issue a full exit when a valid bid is at least five percent below average purchase price, and honor any already-active versioned profit-protection or trailing floor. Persist the triggering quote as decision evidence and obtain a fresh post-decision quote for the simulated fill. Do not permit an ordinary `LiveWeakeningExit` before four-family maturity, and never sell merely because a holding was not reselected.
- Keep the five-percent hard-loss boundary active for the full lifetime of every open Delphi Live position, including after warm-up, across session rollover, and after application restart. A valid bid at or below ninety-five percent of average purchase price creates a full `HardLoss5Pct` exit decision regardless of positive signals; preserve a warm-up phase reason when applicable and use only a fresh post-decision quote for the fill.
- Use completed five-minute closes to activate and ratchet profit protection, but use a valid current bid to detect a breach. A breach creates a full deterministic exit decision, and only a fresh post-decision quote may provide its simulated fill. Persist the activation, every upward floor change, the breach evidence, and the exact policy version so the exit can be explained and replayed.
- When multiple exit rules fire together, create or retain only one idempotent full-position protective sell and preserve every fired rule. Use a versioned primary-reason order for simultaneous triggers; once a sell is pending, retain its original primary reason and append later triggers only as timestamped supporting evidence.
- Record exact twenty-minute, one-hour, two-hour, three-hour, and previous-close returns, together with XIU returns and stock-minus-XIU excess returns over the same timestamps; an immature window casts no vote.
- Judge Price Movement against each stock's own causally available recent volatility rather than one fixed percentage for every symbol, while preserving raw returns and the separate fixed opportunity targets.
- Define every intraday return with exact market-time endpoints: the first window starts at the 09:30 opening price, later rolling windows start at the exact boundary bar close, and stock/XIU use identical timestamps. Define `MedianTrueRangePct10` from classic True Range over eleven contiguous aligned daily bars, using the average of the fifth and sixth sorted percentages.
- Classify a window as potentially Supportive only when the stock rises and outperforms XIU, and as potentially Weakening only when it falls and underperforms XIU. Preserve rising-but-lagging and falling-but-outperforming as named Neutral states, while sending every meaningful absolute decline through the independent safety path.
- Retain separate rolling one-hour, two-hour, session-to-date, three-session, five-session, and ten-session volatility layers. Describe named short-versus-long relationships as `Expanding`, `Normal`, or `Contracting`; do not average the layers or count overlapping windows as separate votes, and never treat volatility alone as bullish or bearish.
- Do not base V1 Volume Support on per-symbol same-clock five-minute history because collection follows a changing selected watchlist and the resulting history is irregular. Never convert unobserved intervals or sessions into zero volume.
- For the opening `DirectionalVolumeBalance20` observation, compare the first completed bar's close with its open; later bars compare close with the immediately preceding contiguous same-session close. This permits four valid observations to mature at 09:50 without using the previous session.
- Freeze `0.15 / 0.25 / 0.35` as the complete raw-move threshold comparison set. Only `0.25` controls V1; the other two are counterfactual research outputs and cannot influence live behavior.
- Freeze `0.025 / 0.05 / 0.10` as the complete stock-minus-XIU relative-deadband comparison set. Only `0.05` controls V1; the other two are counterfactual research outputs and cannot influence live behavior.
- Define each Persistence interval from the completed stock and XIU bar endpoints: opening-bar open to close, then current close versus the immediately preceding contiguous same-session close. Award `+1` only for a positive stock return that strictly beats XIU, `-1` only for a negative return that strictly trails XIU, and `0` for other valid mixed cases; missing continuity makes the window unavailable.
- Require a mature Price Movement window to clear both its raw-move and matching XIU-relative threshold in the same direction. One threshold alone or a direction conflict is Neutral with a named reason; missing either calculation is Unavailable.
- Calculate session-to-date bar-derived VWAP as completed-bar volume-weighted typical price, where `TypicalPrice = (High + Low + Close) / 3`, over a complete valid path from 09:30 through the current bar.
- Let only the frozen ten-session median true-range percentage affect V1 Price Movement. Retain the fourteen-session median as the single promotion challenger and five-/twenty-session medians as diagnostics; all profile layers are explanation and research evidence until untouched outcomes justify a versioned promotion.
- Continue recording every frozen candidate through the close, including demoted stragglers and losers.
- Keep Daily Setup Quality and Live Momentum Quality separate.
- Require broad agreement across four separately recorded live signal families: two Supportive votes provide a non-actionable positive nudge, three may make a confirmed candidate Shadow-entry eligible, and four rank above three. Exactly three Supportive votes plus one ordinary Weakening vote are `StrongWithConflict`: still Shadow-entry eligible after all normal gates, but ranked below clean three-of-four and four-of-four support; a named safety veto still blocks it. Two Weakening votes provide only a negative ranking and warning nudge. Three Weakening votes are `Weak`, except that a remaining Supportive vote makes the result `WeakWithConflict`; four Weakening votes are `VeryWeak`. All three immediately block new entries, `WeakWithConflict` ranks as less dangerous than uncontested `Weak`, `VeryWeak` ranks as most dangerous, and a second consecutive strong-weakening occurrence dismisses the candidate from active new-entry ranking while collection continues through the close. Leaning and Unavailable families are not full votes, and the denominator never shrinks below four.
- Use the exhaustive Supportive/Weakening count table for all remaining mixed combinations, including `PositiveNudgeWithConflict`, `MixedConflict`, `NegativeNudgeWithConflict`, and Neutral tilt details. Compute one deterministic full diagnostic order without blending the families into another weighted score.
- Keep lens emphasis explanation-only in V1. It cannot change family votes, Live Momentum Quality, action state, or ranking strength; a dual-selected symbol receives no automatic bonus and retains two visible thesis explanations over one shared evidence set.
- Keep current frozen-daily Shadow portfolios unchanged and create separate Delphi Live Shadow challengers.
- Let live ranking control new entries and alerts; do not sell an existing holding solely because its relative rank falls. Permit strong combined weakening to close only a position owned by the applicable Delphi Live Shadow portfolio through a separately versioned and explained `LiveWeakeningExit` rule.
- Revise the earlier holding-exit boundary: strong combined live weakening is itself a first-class deterministic risk-exit reason for Delphi Live Shadow because capital preservation takes priority. Relative-rank decline alone remains insufficient, and the revision grants no authority over frozen-daily Shadow or Real holdings.
- Issue a full `LiveWeakeningExit` on the first valid four-of-four `VeryWeak` observation. For `WeakWithConflict` or `Weak`, require the next completed five-minute observation to be valid and remain in any strong-weakening state; missing or invalid evidence cannot confirm the exit.
- Do not require Delphi Live to wait for the next five-minute opening price. Persist the decision first, then request and durably record a fresh TMX quote as the first price TraderVI observed after that decision. Do not claim an exact provider event time that TMX does not supply, and never replace the original observation later with a finalized minute-bar value.
- Use a valid positive TMX `bid` for a Shadow market sell and a valid positive `ask` for a Shadow market buy. If the required field is absent, permit valid `price` as a lower-confidence `EstimatedFill` rather than presenting it as equivalent evidence.
- Report one official scorecard that includes every valid fill, plus a diagnostic bid/ask-only view showing the count and percentage of `EstimatedFill` trades and the performance difference. The diagnostic is transparent context and does not itself exclude trades or control promotion.
- When no usable quote is returned, make at most three total attempts within sixty seconds. After that, expire a new-buy action and require a fresh later five-minute decision; keep a protective sell pending, warn visibly, and retry it once per monitoring cycle until filled. Never create duplicate sales or invent a fill price.
- Cancel a pending Buy within its sixty-second life if current signal, safety, Data Confidence, portfolio, cutoff, or restart rules make entry impermissible, recording the exact cancellation reason. Quote failure alone does not erase valid confirmation: the next valid five-minute observation may create a new Buy decision if it remains entry-eligible, but the expired action itself is never revived.
- Persist an unfilled protective sell across the regular-session close and application restarts under one idempotent identity. Display `ExitPendingOvernight`, resume attempts in the next regular session, and use the first usable post-open quote; never silently cancel the exit.
- Allow a symbol sold by `LiveWeakeningExit` or a safety veto to qualify again later in the same session from entirely fresh evidence. Do not reuse pre-exit confirmation, do not enter while a sell is pending, and never let a churn limit block a protective sell.
- Cap V1 at two completed buy entries per symbol, policy, and regular-session date: the initial entry plus one recovery re-entry. Do not count expired, rejected, or unfilled attempts; after the cap, record `EntryLimitReached`, continue evidence collection, and preserve unlimited protective-exit authority.
- Preserve capital as the highest priority and retain the three safety-veto families above, with persistent downside handled once through `LiveWeakeningExit` rather than duplicated as another veto.
- Do not implement a `MarketWideDanger` stop gate in V1. XIU may inform relative strength and explanations but cannot, merely by declining, pause every new Buy or force a holding exit.
- Use one `LiveWeakeningExit` with two explainable trigger details: `BroadImmediateWeakening` for the first valid four-of-four `VeryWeak` observation, and `PersistentWeakeningConfirmed` when a valid `WeakWithConflict` or `Weak` observation is followed by another valid strong-weakening observation.
- Permit a separately versioned one-bar fast-downside rule to create a full Delphi Live Shadow exit without confirmation from another bar. It may act during opening warm-up, while the exact V1 calculation and threshold remain provisional settings.
- Treat support failure as a safety veto only when the same completed evaluation closes below buffered session VWAP, breaks below the buffered prior twenty-minute low, and has Weakening Volume Support. One fully available three-part confirmation blocks entry or exits the applicable Delphi Live Shadow holding; any missing or disagreeing part prevents the veto.
- Keep Data Confidence separate from Market Judgment and use the one/two/three-miss ladder.
- Restore Data Confidence to `Normal` after one complete clean observation following one, two, or three consecutive misses. Do not backfill the gaps or bypass any signal family's independent continuity and maturity requirements; protective exits remain active.
- Define a Data Confidence miss per symbol as failure to receive and persist either its exact current bar or matching XIU bar by the cycle deadline. Keep legitimate immaturity, optional diagnostics, quote failures, and missing daily runs under their own states rather than incrementing this counter.
- Begin rule-based; defer ML until it can be compared with the rule baseline.
- Require a combined model-grade-plus-near-eligible model to beat an eligible-only model on untouched model-grade evidence.
- Permit a future, successfully validated AI Selector to become the advisory picker only through the normal versioned Shadow, untouched-evidence, and human-promotion process. Deterministic safety vetoes remain final and the AI receives no broker authority.
- Keep Real actions manual and broker-free.
- Observe tracked Real, Operator Ghost, existing System Shadow, and Delphi Live holdings through the shared deduplicated evidence set, but grant Delphi Live action authority only over its own policy's Shadow position. Retain a sold carried candidate through that session so fresh-evidence re-entry remains possible. If it was not selected today, use no stale Daily Setup rank: its stronger live state may still win, but an exact live tie sorts it after today's frozen candidates and then by ticker.
- Keep measurement, classification, family combination, and recommendation/safety policy as separate deterministic stages. Centralize changeable values in an immutable named policy version, persist that version and its reasoned evidence with every decision, and never rewrite historical judgments when policy changes.
- Give Delphi Live its own immutable policy identity linked to the daily Delphi strategy identity, and persist both with every live decision so daily selection and intraday interpretation can evolve and be calibrated independently.
- Evaluate initial thresholds through ten clean engineering-shakedown cohorts, thirty additional discovery cohorts, and thirty additional untouched-confirmation cohorts. Exclude shakedown cohorts from performance claims, count each completed market-session date only once, and require human approval after all ADR-0022 evidence and risk gates pass; promotion is never automatic.
- Require a challenger to show a cohort-aware, statistically credible improvement in paired daily Delphi Live Shadow portfolio return over the champion. It must also have no worse maximum drawdown and no worse average return across its own worst ten percent of eligible sessions; diagnostic wins cannot offset either capital-preservation failure.
- Give each canonical completed five-minute symbol evaluation a separate research outcome anchored to the known bar close, with future movement beginning only in the next interval. Measure twenty-minute, one-/two-/three-hour, and Session 1/3/5 returns, favourable/adverse movement, XIU excess, and first `1% / 2% / 3% / 5% / 10% / 15%` opportunity hits without treating the anchor as a fill.
- Compare frozen Daily Top 5 with contemporaneous Live Top 5 at the same research anchors, separately for Continuation and Breakout and with unused Live slots held as zero-return cash. Keep this diagnostic separate from the actual capital-constrained Shadow scorecard, whose performance uses only persisted fills.
- Use metric-specific coverage with `Ready` at one hundred percent usable, `Degraded` at ninety-five to below one hundred percent, and `Blocked` below ninety-five percent. Never guess through gaps; never let an incomplete WPF-hosted session support promotion; and apply the same predeclared paired-session eligibility rule to champion and challenger.
- Start every promotion comparison with new same-session, equal-capital, cash-only champion-control and challenger portfolio runs. Never compare a new cash challenger with the operational champion's older invested portfolio, and never let comparison portfolios mutate the continuing operational portfolio.
- Require an explicit persisted positive simulation-capital snapshot before initial champion activation; never infer broker cash. Forbid deposits and withdrawals in every active V1 portfolio generation, reject them as `CapitalChangeUnsupportedV1`, and invalidate and restart any comparison experiment whose state was nevertheless changed.
- Use exact aligned opening, checkpoint, and closing marks; whole-share entry quantities; no stale-price carry-forward for sizing or guards; and capital-first sell-before-buy processing. Unsupported corporate actions and unreconstructible closing NAV block affected promotion evidence.
- Use the predeclared paired five-session-block bootstrap, ten-thousand deterministic resamples, and untouched sessions for the primary 95% improvement interval. Reuse persisted Delphi Bullish/Mixed/Bearish regimes, compare worst `max(1, ceiling(10% * N))` session returns, and continue untouched collection when evidence or regime coverage is inconclusive.
- Allow exactly one Operational Champion and no more than two active non-champion Shadow versions. Only the champion may control normal Delphi Live rankings, recommendations, and alerts; challengers and baselines have no operational authority, and only one threshold family may vary within an active comparison.
- After promotion, automatically assign the former champion as the Shadow Baseline for thirty clean completed trading sessions. It occupies one of the two Shadow slots and remains available historically for comparison and rollback after that assignment ends.
- During the thirty-session Shadow Baseline assignment, defer experiments involving a different threshold family. The remaining Shadow slot may be empty or compare another version of the same family.

### Provisional

- Use a rolling four-observation persistence score from `-4` to `+4`: `+3/+4` is Supportive, `+2` is Positive Leaning, `-1/0/+1` is Neutral, `-2` is Negative Leaning, and `-3/-4` is Weakening. Preserve the exact score for ordering within a state.
- Use Persistence's Positive Leaning and Negative Leaning states only for explanation and tie-breaking within the same overall Live Momentum state. They do not cast a full family vote or override safety.
- Use rolling twenty-minute `DirectionalVolumeBalance20` as the provisional V1 Volume Support measurement because it requires only four contiguous current-session five-minute bars. Treat it as a directional-volume proxy, not order flow; missing continuity or zero total volume produces Unavailable.
- Use symmetric `+0.10 / -0.10` provisional full-vote boundaries for `DirectionalVolumeBalance20`, requiring the twenty-minute price-return sign to agree. Treat a smaller balance or a volume/price sign conflict as Neutral with a distinct reason code.
- Display `FullDayVolumeFraction20` only as non-voting context. It is not same-time volume pace and cannot affect rankings or recommendations.
- Use the previous completed-session close, session-to-date bar-derived VWAP, and prior rolling twenty-minute high/low as Price Structure V1's three separately visible references. Exclude the current bar from its comparison range and never substitute through missing evidence.
- Apply a provisional symmetric `0.05` frozen-range-unit buffer to every Price Structure reference. A smaller cross remains `AtLevel` or `InsideOrAtRange` so insignificant movement cannot repeatedly flip the state.
- Combine Price Structure with a no-conflict rule: at least two bullish and none bearish is Supportive; one bullish and none bearish is Positive Leaning; all neutral is Neutral; mixed bullish and bearish is Neutral Conflict; one bearish and none bullish is Negative Leaning; and at least two bearish and none bullish is Weakening. Leaning and Conflict cast no full family vote.
- Require at least two available Price Structure references before classifying the family. With fewer than two it is Unavailable and casts no vote; unavailable evidence never becomes Neutral or bearish, and Data Confidence remains separate.
- Use one valid `EntryEligibleStrong` observation for informational `Emerging` and two immediately consecutive valid `EntryEligibleStrong` observations for actionable `Buy`, allowing the strength tier to change within the eligible group. Break the streak on missing, invalid, non-strong, non-Normal-confidence, or vetoed evidence. Allow a `Dismissed` candidate to recover only through the same two-observation sequence using entirely fresh evidence.
- Keep per-policy recommendation lifecycle separate from market quality, and keep operational `Active` versus `Quiet` presentation separate from both. After maturity, unheld Neutral, Mixed, negative, and Dismissed candidates remain quietly collected but do not crowd the live opportunity list; a fresh positive nudge may reactivate a non-dismissed candidate.
- Include tracked holdings in the intraday observation set.
- Keep the frozen daily and Delphi Live Shadow policies as paired baseline/challenger comparisons.
- For V1 entry ordering, place four-of-four live support ahead of clean three-of-four `Strong`, place clean `Strong` ahead of `StrongWithConflict`, and use Daily Setup Quality only within the same live-state group. Keep both qualities visible and separately calibratable.
- Within one live-state group, use the symbol's best numerical frozen source-lens rank, then the higher common Delphi composite, then ticker. Preserve every source rank, do not average them, and award no separate dual-selection bonus.
- Give each assigned Delphi Live policy an independent role portfolio with at most five concurrent distinct holdings and a twenty-percent-of-current-NAV entry target per holding; permit only the additional aligned champion-control run required for a fair experiment. Keep unused capital as cash, prohibit borrowing, add-ons, and automatic rebalancing, and do not alter the existing daily Top 3/Top 5 Shadow portfolios.
- Inherit the provisional portfolio guards used by System Shadow: a three-percent loss from session-opening NAV pauses all new risk for the rest of that session, and a ten-percent drawdown from highest closing NAV requires explicit human review before new risk resumes. Pending buys expire when either guard activates; protective exits always continue.
- Activate a break-even profit floor when a completed five-minute close reaches three percent above average purchase price. When a completed close reaches five percent above average purchase price, trail two percent below the highest completed five-minute close observed since entry. Never lower the floor, and retain it across sessions and restarts until the position closes.
- Allow an `EstimatedFill` to update Delphi Live Shadow cash, positions, and returns and to remain in V1 strategy and promotion evidence. Preserve the tag and raw quote fields so its prevalence and effect can be measured, and revisit this provisional compromise when actual automated-broker fills exist.
- Store outcome opportunity thresholds and exact elapsed time without choosing one profit horizon in advance.
- Use `MedianTrueRangePct10` as the provisional V1 volatility ruler, calculated from the prior ten completed sessions and frozen before live evaluation.
- Let either the twenty-minute or one-hour move carry a provisional Price Movement direction when the other is Neutral or agrees; meaningful disagreement is `NeutralConflict`, and longer windows remain non-voting context.
- Use a symmetric `0.05 ExcessUnits` deadband for the provisional twenty-minute and one-hour XIU-relative comparison; differences inside the band are neutral rather than directional.
- Use a limited hybrid policy boundary: code owns formulas and reason semantics, immutable stored policy versions own validated numeric settings, and activation occurs only between sessions with no in-place edits or fallback defaults.
- Use symmetric `RawMoveUnits` thresholds of `+0.25` and `-0.25` for the provisional twenty-minute and one-hour Price Movement rule; smaller aligned moves remain Neutral, and independent safety vetoes retain authority to react faster to losses.
- Define provisional `FastBarReturn` as completed regular-session five-minute bar open-to-close return. A value at or below `-10%` creates a full `FastDownside10Pct` exit, including on the completed 09:30–09:35 opening bar; use the bar only as decision evidence and a fresh post-decision quote for the fill.
- Use this provisional primary-reason order for rules first firing together: `HardLoss5Pct`, `FastDownside10Pct`, `ProfitProtectionFloorBreach`, `ConfirmedSupportFailure`, then `LiveWeakeningExit`. Preserve all lower-priority reasons as supporting evidence; the order affects explanation only.

### Superseded

- The initial rejection of standalone sell authority for one unusually severe completed five-minute decline was reversed. The replacement threshold is a completed five-minute return at or below `-10%`.
- Treating persistent downside as its own safety-veto family was replaced by the existing `LiveWeakeningExit`. Its immediate broad-weakness and confirmed persistent-weakness subconditions retain separate explanation details but create one exit rule and one sell intent.
- The earlier proposed `MarketWideDanger` safety-veto family and a provisional XIU `-2%` blanket Buy pause were rejected. XIU remains normal relative-strength context, not a V1 market-wide stop gate.

### Deferred

- A full-universe continuous intraday scanner that adds symbols not present in the frozen watchlist.
- Rank-driven selling and aggressive intraday portfolio rotation. A deterministic `LiveWeakeningExit` based on strong combined evidence is capital protection, not rank-driven selling.
- A trained Delphi Live model.
- DotLLM narration and conversational decision Q&A.
- A separately versioned AI Selector Challenger that proposes Shadow-only picks from the same point-in-time facts. It remains distinct from DotLLM narration; a successful version may later compete for advisory-picker authority, but never for authority over deterministic safety, its own promotion, or broker actions.
- Any automated broker integration or order placement. A future Wealthsimple market-order integration may reuse the bid/ask evidence contract, but requires its own design, safety review, explicit authorization, and actual broker-fill reconciliation.
- Per-symbol same-clock-slot relative volume or same-time cumulative-volume pace until prospective five-minute coverage is sufficient and its comparison contract is explicitly accepted.
- Any future evidence-backed market-wide stop gate; V1 deliberately has none.
- Deposits or withdrawals within an active portfolio generation until a cash-flow-adjusted return, unit-value, drawdown, and comparison contract is explicitly accepted.
- Exact continuous-move grouping and reversal boundaries, exact intraday threshold timing on later sessions, continuous multi-day five-minute reconstruction, hypothetical executable fills for every research observation, and richer volatility/event taxonomies. V1 retains the underlying evidence without claiming these derivations.

### Open — required for the V1 implementation handoff

- None. Independent final consistency and handoff reviews completed on 2026-09-05 without a remaining material contradiction or builder-blocking behavioral ambiguity.

### Open — non-blocking future or research choices

- May a future evidence-backed version add an explicit named lens-specific requirement, such as requiring Volume Support for a Breakout action?
- Which causal calculations and categorical boundaries should later govern the explanatory multi-horizon volatility profile?
- What exact volatility-adjusted rule should later group raw observations into one derived price-move event, if that derivation is deferred while immutable five-minute evidence is retained?
- What structured input/output, model-and-prompt versioning, reproducibility, latency, failure, and evaluation contract must a future AI Selector satisfy before beginning Shadow comparison?

## Build-readiness checkpoint

The core Delphi Live trading behavior and scorecard contract are specified sufficiently for an implementation ADR. The final consistency and scope review is complete. Before substantive implementation, a fresh authorized session must reconcile this frozen design source into the required ADR, review aids, and immutable strategy, policy, evidence, and dossier identities. This session intentionally leaves those shared ADR, index, and review files untouched because other work is concurrently modifying the documentation tree.

This document plus the repository agent guide and authoritative design records is the V1 implementation handoff. It does not by itself authorize a build, test run, database migration, external market call, model training, deployment, broker action, or operational activation. Implementation should remain phased, and every consequential operation still requires the authority described by the repository guide.

## Acceptance criteria before implementation

The final audit confirmed these implementation-ADR prerequisites:

- watchlist identity, source-lens attribution, dual-lens handling, and session timing are unambiguous;
- all component inputs, thresholds, states, and reason codes are specified;
- data-confidence and market-risk behavior cannot be confused;
- every recommendation and safety action has a causal evidence and fill-time contract;
- existing frozen Shadow and official calibration evidence remain unchanged;
- daily and live incremental scorecards are defined;
- champion, challenger, and counterfactual role assignments are session-frozen, capped, attributable, and unable to overwrite one another;
- source capacity and incomplete-session behavior are understood; and
- no LLM output is required for calculation, causality, scoring, or action.

## Recommended design sequence

1. Reconcile the whole draft and translate the accepted design into the required ADR and review aids without changing its behavior.
2. Implement shared Core contracts, deterministic measurements and policy evaluation, then durable evidence and Shadow state, then the WPF host and diagnostics in reviewable phases.
3. Run focused tests before any separately authorized migration, external-source validation, operational activation, model training, deployment, or broker work.

## Review questions

1. Why must Delphi Live continue collecting evidence for candidates it has operationally dismissed?
2. Why are Data Confidence and Market Judgment separate states?
3. What deterministic record must exist before DotLLM may answer why a recommendation changed or a Shadow position exited?
