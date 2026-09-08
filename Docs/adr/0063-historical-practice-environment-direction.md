# ADR-0063: Historical practice environment direction

- **Status:** Accepted; first daily/Shadow practice milestone implemented in source (2026-09-08)
- **Date:** 2026-09-07
- **Domains:** architecture, data-pipeline, user-interface, machine-learning
- **Related:** ADR-0051, ADR-0053, ADR-0054, ADR-0055, ADR-0056, ADR-0057, ADR-0060, ADR-0062

## Context

The immediate problem is reproducing historical sessions and controlled failures. The parent problem
is insufficient evidence about complete-strategy performance and operational reliability. The root
outcome is a trustworthy basis for deciding which strategies deserve real capital.

The operator accepted a practice environment around shared Core workflows, followed by desktop and
connection testing. They want historical Delphi picks from stored daily data, real historical TMX
intraday observations, and WPF playback with a clear Practice label. They separately requested that
historical TMX access be exercised before continuing the harness design.

## Decision

1. Reuse existing Core calculations and workflows with controlled time, inputs and isolated storage.
   Do not expand ADR-0054's independent one-session account loop into another trading engine.
2. Prepare historical inputs separately from replay. Use stored daily data where sufficient and the
   existing TMX client during authorized acquisition. Freeze local datasets for reproducible offline runs.
3. Add WPF viewing/control after the runner works. Display Practice and simulated date/time continuously.
   The label complements storage isolation; it cannot provide it. A local server is optional for later
   transport/host tests, not a prerequisite.
4. Keep historical estimates, injected-failure results and observed live reliability distinct. Replay
   creates no official Athena/OfficialPaper evidence, clean forward cohort or automatic promotion.
   ADR-0062 manual recommendation authority and internal Live research rules remain unchanged.
5. The first practice version permits exploratory runs using today's preserved model set, including
   sessions before those models were trained. Pin the exact model artifacts and strategy/policy settings
   for each run; do not follow later operational assignment changes. Display Practice / Exploratory in
   the UI and reports, identifying when a model postdates the replay or its training history is unverified.
   This explores today's system on historical prices; it is not an honest then-available forecast.
   Historical market-input cutoffs and causal fills still apply. No new training is required or authorized.
6. The first working milestone is historical daily Delphi feeding all four independent System Shadow
   portfolios: Continuation Top-3/Top-5 and Breakout Top-3/Top-5. Recompute each session's daily picks
   from historically bounded market inputs, using the fixed exploratory model set; carry each account's
   holdings, cash, pending actions and risk state across sessions under its production policy.
   Delphi Live follows this milestone. Its earlier proposed first-slot workflow proof is superseded
   as the milestone order, though it may remain a useful focused engineering test.
7. Use the most recent 20 consecutive completed TSX sessions as the starter test/trial, with recorded
   input coverage and additional available earlier history for indicator warm-up. At this design
   snapshot the reviewed calendar resolves that target to August 10 through September 4, 2026.
   Choose dates by calendar rather than returns or completeness, disclose gaps and do not skip sessions.
   This starter size is an engineering/exploratory scope, not sufficient proof of strategy performance.
8. Each of the four starter Shadow portfolios begins with CAD 1,000 cash, no holdings and no pending
   orders. There are no deposits or withdrawals during the 20-session trial. Each amount belongs to
   one independent alternative account; it is not divided among portfolios or combined into shared
   wealth. Initial account value is CAD 1,000. Existing whole-share sizing and family risk rules apply.
9. Missing historical market data is part of the normal trial. The operator rejected the proposed
   complete-data prerequisite: do not block startup, abort the trial, replace dates/symbols or remove
   gaps just to obtain a complete baseline. Present available and unavailable inputs through the same
   contracts the production workflows consume, advance simulated time, and exercise their existing
   skip/wait/protection/recovery behavior. This permits incomplete datasets, not fabricated data/fills
   or disabled production safety rules. Record gaps, effects and failures; report unknown/stale valuation
   honestly. Deliberately injected failures remain identified separately from source gaps already present.
10. The operator subsequently authorized implementation on September 8. The first milestone now uses
   historical inputs in the shared daily workflow, an environment/store boundary in the shared Shadow
   controller, pinned file packages, isolated accounts, diagnostic checkpoints and WPF recorded playback.
   Acquisition remains an explicit separate command. No broad acquisition, training, migration,
   assignment, operational application launch, commit or push ran during implementation.

## Alternatives and consequences

Extending ADR-0054 alone would retain a parallel account loop and miss production recovery. Starting with
a WPF-facing server would exercise transport but leave time and direct repository dependencies unresolved.
The Core foundation reduces duplicated policy logic; daily Delphi, Shadow and the tracked monitor still
need explicit environment boundaries. Daily Delphi and Shadow now have those boundaries; the tracked
monitor and real SQL clock/lease testing remain later work.

The proposed requirement to wait for complete historical inputs was rejected because missing data is
itself a condition the system must withstand. Coverage inspection is diagnostic and does not gate replay.
Successful replay completion and complete performance measurement remain different facts; engine crashes
or accounting violations must remain visible test failures rather than being swallowed as normal gaps.

Historical access passed for two symbols at three sampled dates. That does not establish universal
coverage, the retention limit, quote history, corporate-action semantics or model eligibility. See the
[running draft and probe results](../concepts/historical-practice-environment-draft.md).

Implementation contracts, explicit preparation commands, validation and remaining limits are recorded in
[Historical practice — first implementation](../concepts/historical-practice-implementation.md). The first
version checkpoints at session boundaries and replays a failed run from the start into a new directory;
automatic resume and real SQL recovery are not claimed. The WPF tab reads saved frames only.

## Review questions

1. Why separate historical acquisition from replay?
2. Why is a Practice label insufficient to isolate operational accounts?
3. Why can real historical prices combined with today's model produce biased historical results?
