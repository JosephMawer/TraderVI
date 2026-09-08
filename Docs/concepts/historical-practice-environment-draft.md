# Historical practice environment — design draft

- **Status:** Design history; the operator authorized implementation on September 8. The first daily/Shadow
  milestone and recorded WPF playback are implemented in source. See the
  [implementation contracts and run instructions](historical-practice-implementation.md) and
  [ADR-0063](../adr/0063-historical-practice-environment-direction.md). Remaining provisional ideas below
  are later work, not claims of implemented behavior.
- **Date:** 2026-09-07 (probe completed September 8 UTC)
- **Domains:** architecture, data-pipeline, user-interface, machine-learning

## Problem and accepted direction

Immediate problem: replay historical sessions and failures. Parent problem: establish complete-strategy
and operational evidence. Root outcome: a trustworthy basis for deciding which strategies deserve capital.

**Accepted:** shared Core runner with controlled time and isolated storage; separate saved historical
datasets; historical Delphi picks from stored daily inputs once every cutoff is safe; actual historical
TMX intraday observations; eventual WPF viewing/control with Practice and simulated date/time displayed.
Start without WPF; a local server is optional later for transport testing. The operator requested the
bounded source-access check below. The first harness and WPF recorded-playback workspace are now implemented.

**Accepted first model mode:** the operator approved exploratory runs with today's preserved models.
Capture the exact model IDs/hashes and strategy/policy settings when preparing a run and keep them fixed
through playback and reruns. Later operational model assignments cannot alter a prepared run. Show
Practice / Exploratory and the model/replay dates; label later-trained models accordingly and missing
training provenance as unverified. The result describes today's system on historical prices, not a
forecast made with only then-available training information. This exception permits later-trained models,
not future market observations, fabricated inputs or retrospective fills. No training is authorized.

**Accepted first working milestone:** historical daily Delphi feeds all four independent System Shadow
portfolios (Continuation Top-3/Top-5 and Breakout Top-3/Top-5), followed by Delphi Live in a later milestone.
Recompute both daily lists for each simulated session using bounded market inputs and the pinned model set.
Each Shadow account preserves its production candidate/eligibility contract and carries its own cash,
holdings, pending actions and risk state between sessions. Implementation was subsequently authorized.

**Accepted starter period:** the most recent 20 consecutive completed TSX sessions, explicitly a starter
test/trial that includes missing historical observations. The installed reviewed calendar resolves the
current target to **August 10–September 4, 2026**, inclusive (20 sessions). Capture these dates in the
prepared dataset rather than moving an existing run forward as new days pass. Earlier lookback history
is needed to calculate the first session's indicators; it is separate from the measured 20-session account
path. Calendar dates have been verified locally; daily and full intraday coverage have not been audited.

**Accepted starter accounts:** Continuation Top-3, Continuation Top-5, Breakout Top-3 and Breakout Top-5
each begin with **CAD 1,000 cash**, zero holdings and zero pending orders. No deposits or withdrawals
occur during the trial. Each has its own full CAD 1,000 and initial account value; balances are neither
pooled nor divided among strategies. Earlier indicator warm-up does not create pre-trial trades or holdings.

**Accepted missing-data behavior:** the normal trial runs with whatever historical market inputs are
available, preserving missing observations. No complete-data prerequisite, skipped sessions or replacement
dates/symbols to make the run appear complete. Simulated time continues, and production workflows own their
response to unavailable inputs, including blocked decisions or continued protective monitoring. The purpose
is to discover whether the system withstands these conditions as it should in live operation. Gaps remain
diagnostic facts; they are not permission to fabricate inputs/fills, weaken policy checks or declare unknown
account values known. Additional injected failures are still labelled by their scenario provenance.

## Verified historical-access probe

`Sandbox/Probes/TmxHistoricalIntervalAccessProbe.cs`, selector `tmx-historical-interval-access`, calls the
existing `TmxClient.GetIntradayTimeSeriesBatchAsync`. No SQL, quotes, models or account path. Bounds:
12 sequential logical requests, up to three transport attempts each, 20 seconds per request, four minutes
overall, one second between requests, stop on first exception; empty/partial responses remain findings.

| Toronto session | Symbols | Five-minute bars per symbol | Fifteen-minute bars per symbol | Exact 5-to-15 aggregation per symbol |
|---|---|---|---|---|
| 2026-09-04 | XIU, RY | 78/78 | 26/26 | 26/26 |
| 2026-08-04 | XIU, RY | 78/78 | 26/26 | 26/26 |
| 2026-03-04 | XIU, RY | 78/78 | 26/26 | 26/26 |

All 12 requests succeeded on their first attempt: 468 five-minute bars, 156 fifteen-minute bars,
zero missing slots/excluded boundary rows, 156 exact OHLCV aggregation matches. Spacing was exactly
five/fifteen minutes. March used UTC-5, August/September UTC-4 under the current mapper. Elapsed time
was 12.66 seconds including pacing; that is not a full-watchlist/live-load capacity benchmark.

Private normalized sources/report:
`artifacts/tmx-historical-interval-access/20260908-032206-3036d24f2dbc4bb4bac17fb915e357fe/`.
Report SHA-256: `990AD3BC0928F9CF96D678D879129F5D04C9ADD36E606A333D0719460851CF24`.
All 12 source hashes were independently verified. Report captures calendar and loaded Core/probe assembly
hashes. Source baseline: `f72fca5` plus probe and existing working changes. Prices remain in ignored files.
An initial sandboxed first-request `HttpRequestException` returned no bars; its report is preserved.
Approved execution outside the network sandbox produced the successful result above.

Validation: SDK 10.0.400; focused Sandbox `--no-restore` build to isolated output passed, with three existing
CS8632 warnings in other probes. No dependency-security advisory appeared in this incremental build;
existing project advisories remain unresolved. No full solution build/test suite or database operation ran.

**Established:** sampled historical five-/fifteen-minute access, including a date about six months earlier.
**Unverified:** all-symbol/older coverage, retention boundary, caps/pagination beyond one session, raw null-row
reasons, independent provider timestamp/revision semantics, historical receipts/quotes, adjustment identity,
corporate actions and delisted securities. The client filters null OHLCV before this probe sees results.

## Reuse and constraints verified in source

- ADR-0054 reuses Live policy calculations but has a separate one-session account loop, fixed V1 policy,
  assumed two-minute availability, minute-close bid proxies and cost-free next-minute-open fills.
- Live monitor/collection/action workflows already accept source, clock and store interfaces. Tests cover
  timing, duplicate prevention, restart behavior and capacity with simulated request/storage durations.
- Daily Delphi, Shadow and Operator Ghost still construct dependencies directly and use real time.
  Delphi's recommendation-date option does not bound every supporting input. Daily calculations need
  OHLCV, benchmarks, breadth, sector/universe membership, OBV and other indicators, not closes alone.
- Real-time cancellation timers and SQL `SYSUTCDATETIME()` lease/receipt checks need explicit treatment.
  Preserve production SQL authority; injecting `UtcNow` alone cannot make the database replay historical time.
- The reviewed calendar lists session dates but hard-codes full-session bounds. Short sessions need
  explicit hours and compatible policy handling; uncovered dates must not become weekday guesses.
- Reuse ADR-0062 strategy/model/policy identities and family protection. Its recommendation workflow
  does not reproduce the paper fill lifecycle. Harness work need not wait for all promotion work.

## Provisional contracts for discussion

**Dataset:** immutable manifests/checksums, coverage/exclusion reasons, source revision/adjustment identity,
historical universe/sector membership and full benchmark/supporting-input lookbacks. Acquisition time is
separate from event time and assumed availability. Never fabricate missing observations.

Accepted replacement for the rejected completeness gate: coverage inspection records present/missing/invalid
inputs and expected slots without blocking the normal run. At each simulated request, return the available
as-of facts or the appropriate unavailable/error result through the production input contract. A missing
historical row alone does not establish an actual historical timeout or host outage; preserve the unknown
cause and use named scenarios for deliberately simulated failures. The policy cannot inspect future gaps.

Missing daily inputs may prevent that session's Delphi publication under its existing rules. Record that
result and continue the simulated schedule, allowing Shadow's no-daily-source/protection paths to run.
Missing intraday observations exercise the existing entry, pending-fill and recovery rules; do not fill
from a convenient later price or clear safety holds. A lack of supported valuation affects the affected
metric, not permission to continue the timeline. Preserve known cash/holdings; label stale marks and leave
unsupported valuations/returns unavailable rather than reporting zero or complete data.

Unexpected engine exceptions, duplicate actions or accounting violations are reproducible test failures,
not ordinary missingness to suppress. Recovery should follow production behavior; the harness must not
silently repair state to keep a run looking successful. Detailed failure/restart mechanics remain open.

**Time and execution:** record bar start/end, availability, request, receipt, durable decision, economic
fill and recording times. Stable event ordering, IDs and seeds support deterministic reruns. Playback
pause stops simulated time; a simulated host outage advances it. Speed/step must preserve decisions.
Keep each family's fill contract: Shadow's strictly later five-minute open and friction; Live's fresh
post-decision quotes. Missing quote history requires a separately named estimated fill model with explicit
cost, latency and sampling assumptions. Bars cannot prove unknown intrabar order or provide a past fill.

**Accounts:** all four Shadow lens/Top-3/Top-5 accounts; Live's deduplicated union of two Top-25 lists and
its own operational/research policies. Preserve carried holdings/pending exits, re-entry, rotation and
risk holds. Operator Ghost needs a reproducible selection/action script or recorded historical actions.

Accepted starter capital: initialize each of the four Shadow accounts with CAD 1,000 cash and initial
account value, zero holdings and zero pending orders, with no deposits/withdrawals during the trial.
Preserve whole-share sizing, unspent cash and family risk rules. Each account owns its full independent
amount. No current operational holdings/cash are copied, changed or treated as simulation capital events.

**Isolation:** replay has no operational credentials/provider adapter, only explicit simulation repositories,
run/account identities and private output paths. Real SQL tests require a separate database/identity with
no operational access, startup identity checks and no production fallback. Model loading is read-only and
hash-pinned; optional future training remains separate and candidate-only. A UI badge is not this boundary.

**Evidence:** distinguish historical estimates, injected failures and observed live operation. The accepted
first mode permits fixed newer models only as labelled exploration. Honest historical forecasts require eligible
preserved models and prior training/selection information. Walk-forward means training on earlier data,
then testing later periods; label endpoints, preprocessing and selection must obey that cutoff. Preserve
overlap with dates used to design/tune strategies. Replay cannot manufacture untouched or forward cohorts.

**WPF:** proposed Practice workspace with permanent label, simulated date/time, Play/Pause/Step/speed,
candidate explanations, pending actions, holdings/cash and traces. Opening it reads practice data. Use the
runner's state; hide later results at earlier frames. Dataset preparation is separate. Process boundary
and detailed navigation remain open.

## Phases and acceptance — scope accepted, implementation details provisional

1. Source-access probe: completed. Inventory the selected period's available daily/intraday inputs and
   model provenance; preserve gaps as part of the normal trial rather than a startup blocker. Broader
   acquisition remains separately scoped and authorized.
2. Accepted first working milestone: historical daily Delphi feeding all four System Shadow accounts
   over the accepted 20-session starter trial. Use fixed current models under the accepted exploratory label.
   Shadow's existing five-/fifteen-minute contracts match the sampled data without needing a new quote proxy.
   This prioritizes the operator's daily-picks-to-account path; it is not authorization to implement it.
3. Add Live policy comparisons and scripted Operator Ghost after defining their execution/input contracts.
4. Exercise real isolated SQL transactions/recovery, competing hosts and optional HTTP/host-load tests.
   A simulated repository cannot prove SQL atomicity; fake durations cannot prove provider capacity.
5. Add WPF viewing/control over the proven runner.

Recommendation history: the initial provisional first slice was a two-session Live-only workflow proof
using frozen daily inputs. Following successful historical bar access and agreement to exploratory current
models, the operator accepted daily Delphi plus all four Shadow accounts as the first working milestone,
with Delphi Live afterward. The Live-only proof remains useful as a focused engineering test; it is no
longer the proposed first account milestone.

Missing-data decision history: the proposal to require complete inputs before the ordinary trial and
reserve gaps for separate tests was rejected. The accepted trial includes naturally missing data; only
additional artificial failures need a separate scenario identity. No run is selected for completeness.

Acceptance: future-data edits leave earlier decisions unchanged; fills obey family causality; reruns and
playback pause/resume preserve economic results; host-outage recovery obeys production rules; no duplicate
actions/cross-account writes; cash and holdings reconcile; protective exits survive pause/restart; missing
marks and gaps remain visible; missing market data does not gate startup or advance the replay past
affected sessions; recovery when observations resume uses fresh evidence under production rules;
no automatic official evidence/promotion effects. Preserve reproducible
scenario traces. Simulated SQL time remains an explicit unresolved integration design.

Reports: return, drawdown, exposure, turnover/costs, trade/session counts, coverage/exclusions, reasons,
missed opportunities, market-condition slices and fill/cost/latency sensitivity. Compare common dates,
capital and disclosed execution assumptions against cash/benchmark/simple reference policies; predeclare
component-removal studies. Keep losses/abandoned trials, avoid a universal winner score, and do not count
overlapping observations as independent sessions.

## Open and deferred

- First model mode is resolved: allow clearly labelled exploration with today's preserved models. Exact
  artifact selection/provenance still needs verification; no models have been loaded or trained here.
- First study period is resolved: latest 20 consecutive completed TSX sessions as a starter trial;
  intended dates August 10–September 4, 2026. Non-blocking coverage inventory remains open. Load available prior history
  for indicator warm-up; disclose missingness without silently skipping/replacing sessions. This is not
  a statistically sufficient performance claim or authorization for more TMX requests.
- Starter capital is resolved: CAD 1,000 per independent account, cash-only, no initial pending orders,
  and no deposits/withdrawals during the trial.
- Missing-data handling is resolved: include natural gaps in the ordinary trial, continue simulated time,
  and test production responses. Do not require complete history or skip affected sessions. Report gaps
  and unsupported metrics; distinguish additional artificial failures from source missingness.
- Daily-input availability/missingness mapping, historical universe and comparative fill model.
- Exact simulation/SQL time authority, isolated database provisioning and durable checkpoints.
- First milestone order is resolved: daily Delphi plus all four Shadow portfolios, then Delphi Live.
  WPF process boundary and timing of later UI/integration work remain open.
- Broader retention/coverage probes, historical quotes/corporate actions and walk-forward training need
  separately scoped work. Successful access is not authorization for further acquisition.

## Review questions

1. What does historical price access establish, and what still requires live observation?
2. Which model and input dates determine whether a replay is an honest historical forecast?
3. Why does a playback pause differ from a simulated application outage?
4. Why should the normal practice trial include missing data, and how is an engine failure distinguished?
