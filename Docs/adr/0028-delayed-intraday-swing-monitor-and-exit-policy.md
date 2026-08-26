# ADR-0028: Delayed intraday swing monitor and exit policy

- **Status:** Accepted
- **Date:** 2026-08-25
- **Domains:** architecture, data-sources, market-microstructure, risk-management
- **Related:** ADR-0015, ADR-0021, ADR-0022, ADR-0023, ADR-0025, ADR-0026, ADR-0027

## Context

The immediate problem is deciding how a Delphi recommendation should be managed after a manual or paper purchase. The parent problem is measuring whether an executable exit policy captures Delphi's short-term upside while limiting losses. The root goal is to tune Delphi from honest evidence under human strategic control without pretending delayed data or a paper fill provides live stop protection.

ADR-0023 selected a roughly three-to-five-session swing as the primary direction and kept a distinct intraday wave-trading strategy separate. The user has clarified that the swing position itself must still be watched throughout every session: it may be sold a few hours after entry when a profitable move reverses, carried overnight while healthy, and normally closed within one trading week. This is intraday **management of a daily-selected swing**, not a new intraday entry thesis.

TraderVI already has `TmxClient.GetIntradayTimeSeriesAsync`, which can request minute/hour OHLCV intervals from the TMX Money GraphQL endpoint, but no production collector, persistence contract, or Sentinel monitor uses it. TMX Money states that its market information is generally delayed by at least fifteen minutes and is not intended as trading data. The user has explicitly chosen to work within that limitation for a local, advisory-only first version.

**Post-decision source finding (2026-08-25):** the authorized read-only `tmx-xiu-intraday` probe requested 15-minute XIU bars over 2-, 14-, and 90-calendar-day windows. All three calls returned the same seven one-per-session bars timestamped at 4:00 p.m. Toronto time, spanning 2026-08-17 through 2026-08-25; the two-day call even returned dates before its requested start. The response was structurally valid daily OHLCV but did not satisfy the intraday interval or window contract. Therefore the existing method name and request shape are not evidence that usable intraday data is currently available. Persistence and polling remain blocked until a corrected request produces actual intraday bars and the probe passes.

**Current request-contract finding (2026-08-26):** inspection of the GraphQL query in the JavaScript bundle loaded by TMX Money's current XIU quote page confirmed that intraday requests send `interval`, `startDateTime`, and `endDateTime`, with Unix-second bounds rounded down to the minute. They leave `freq` unset; `freq` is used for daily and longer aggregations. The page requests intraday history in five-calendar-day chunks. The failed probe had sent the obsolete `freq = "minute"` combination, which explains the daily fallback. Correcting the existing client to match the observed request contract and rejecting obvious daily fallback responses is within this ADR's source-validation milestone; it does not require a separate strategy decision.

The corrected read-only probe then returned 52 bars across two sessions for the two-day window and 260 bars across ten sessions for the fourteen-day window. Every full session contained 26 gap-free bars from 9:30 a.m. through 3:45 p.m. Toronto time, confirming 15-minute bar-start timestamps; duplicate, alignment, OHLC, and volume checks all passed. A ninety-day request was capped at 754 bars: it returned the oldest 29 sessions from May 28 through July 8 rather than continuing to the current session. Short rolling requests are therefore suitable for monitor/storage design, while any historical load must use bounded chunks and deduplicate boundaries.

**Market-hours findings (2026-08-26):** after correcting the workstation clock, an authorized opening-window comparison returned gap-free XIU bars at all three tested resolutions: 23 one-minute bars from 9:30 through 9:52, five five-minute bars from 9:30 through 9:50, and two fifteen-minute bars from 9:30 through 9:45. At the 9:52 receipt, the newest completed events were 9:51, 9:45, and 9:30 respectively. Every response also contained a newer still-forming bar. A separate five-poll sequence ran from 9:53:59 through 10:54:00 Toronto time. The newest completed 15-minute event advanced exactly once per poll from 9:30 through 10:30, all five calls completed without a surfaced transport failure, and the prior poll's forming snapshot was revised before it became the next completed bar. Because these polls occurred about nine minutes after each interval boundary, they prove availability within that bound rather than the earliest possible publication time.

A later comparison narrowed completed five- and fifteen-minute availability to within two minutes of an interval boundary. Both remained gap-free. The one-minute response existed but contained 16 missing minute slots by 11:51, with observed two- and three-minute gaps. One-minute capability is therefore confirmed, but continuous one-minute coverage is not. All nine comparable completed fifteen-minute bars matched exactly when reconstructed from their three five-minute OHLCV bars. This is evidence for completed five-minute bars as the proposed version-1 storage resolution, aggregated deterministically for the accepted fifteen-minute policy, while the confirmed fifteen-minute polling cadence remains unchanged; the storage-resolution choice still requires explicit approval.

## Decision

### Confirmed direction

Keep Delphi's completed-session recommendation process unchanged. After a manual or ghost entry, manage the position through a separate advisory monitor that polls the existing TMX intraday method every **15 minutes** during regular TSX trading hours. Request the newest completed 15-minute OHLCV evidence from TMX's minute-capable intraday source; do not poll minute by minute.

The 15-minute value is the monitor's polling cadence, not a claim that TMX publishes only exact 15-minute updates. A poll may receive no new completed bar, especially outside trading hours or during a source delay; a repeated event timestamp is a no-op rather than new evidence. Market-hours probing must verify how quickly the corrected endpoint exposes a newly completed interval before scheduling or persistence is considered ready.

Treat every source bar as delayed evidence. Preserve its market-event timestamp separately from the UTC time TraderVI received it, calculate and display its age, and never call an alert a guaranteed stop fill. The monitor emits advice and paper decisions only. It does not place an order, silently close a real position, or change Delphi's ranking.

### Accepted source-client operational defaults

- Preserve the simple bar-list API as a compatibility wrapper, while making a timestamped intraday batch the evidence-bearing API. A batch records the requested window, fetch start, receipt time, transport attempt count, request count, and validated bars.
- Expose the newest completed bar separately from the newest returned bar. TMX returns a still-forming interval and the market-hours probe observed it changing before completion; consumers must not silently treat that mutable snapshot as final evidence.
- Retry only transient transport failures: timeouts, HTTP 408, HTTP 429, and HTTP 5xx responses. Make at most three total attempts with cancellation-aware one-second and two-second delays. Do not retry GraphQL/application errors as though they were network failures.
- Reject duplicate timestamps, timestamps outside the requested window, obvious daily fallback, unsupported or misaligned intervals, non-UTC event timestamps, non-positive OHLC values, invalid OHLC ranges, and negative volume.
- Expose wide intraday history through an explicit chunked method using five-calendar-day request windows, a short cancellation-aware pause between chunks, and exact duplicate removal at chunk boundaries. Conflicting duplicate bars are an error rather than an arbitrary last-write-wins choice.
- Describe quote snapshots as current rather than guaranteed real-time data. Actual freshness comes from provider event timestamps where available and from measured receipt behavior, not from a method name or comment.

Allow a same-session exit. The ordinary holding target is no more than five completed trading sessions, not a rule to wait five sessions before examining the position. A profitable, healthy position may continue beyond session five under the trailing rule, but session ten is the absolute time limit for the initial policy.

Use two loss levels measured from the raw entry price:

- At a 10% decline, issue an exit alert unless a later, latest-available valid `OfficialPaper` Delphi run for the same symbol still publishes the Breakout lens and has `BreakoutProbability >= 0.60`, `DirectionEdge >= 0.10`, and `DownProbability < 0.35`. The original entry recommendation never qualifies as fresh evidence. Missing, invalid, stale, or non-published evidence cannot grant the exception.
- At a 20% decline, always issue an exit alert. No Delphi signal can bypass it.

The 20% rule is absolute policy intent, not a price guarantee. Because the source and manual response are delayed, the observable or achievable sale price may be worse than either threshold. Paper evaluation must use the first price that was actually available after the delayed decision, including gaps and further movement; it must never award the threshold price retrospectively.

### Initial paper-policy defaults

The following values are accepted as **version-1 challenger defaults for implementation and measurement**, not as proven or promoted production thresholds:

- Model round-trip friction as 25 basis points per side, consistent with ADR-0021.
- Arm profit protection as soon as a completed 15-minute close is above the cost-aware break-even exit price.
- Maintain a high-water mark from completed 15-minute closes. The high-water mark never decreases.
- Set the trailing level to the greater of the cost-aware break-even exit price and 5% below the high-water close. The trailing level never decreases.
- Test a bar against the trailing level established before that bar. Update the high-water mark and trailing level only after processing the bar, so one OHLC bar is never given a favourable guessed high/low order.
- At the closing bar of session five, exit a position that is not profitable after modeled costs. Continue a profitable position under the trailing rule.
- Exit every remaining position at the closing bar of session ten.
- Flag data older than 45 minutes at receipt as late/degraded while still surfacing any risk alert. This diagnostic default does not make delayed data current and does not suppress a capital-risk warning.

Every exit-policy result must identify the rule, threshold, source-bar event time, receipt/detection time, data age, relevant fresh-Delphi evidence, and whether the result is an alert, paper exit, or later manually recorded fill. The paper-policy definition is immutable; changing polling frequency, source delay, costs, thresholds, trailing basis, session limits, signal exception, or fill convention creates a new version.

The first implementation milestone is a deterministic pure policy engine and fixtures. Intraday persistence, scheduling, alert delivery, historical outcome calculation, and position-ledger integration follow as separately reviewable changes. No TMX request, database write, or live position action is required to validate the pure engine.

### Relationship to prior decisions

This ADR refines ADR-0023: intraday monitoring of an already-open swing position belongs to the primary swing-management policy. A separate strategy whose entry and exit thesis is an intraday wave remains a separately scored challenger.

It also creates a new paper-policy challenger under ADR-0022. It does not retroactively change `SwingMarkToMarket3`, `SwingExcursion3`, the existing operational `ActivePosition.StopLossPrice`, or any previously stored outcome.

## Alternatives considered

- **Evaluate only after five sessions.** Rejected because it cannot capture a same-day breakout/reversal and misunderstands five sessions as a waiting period rather than an ordinary maximum.
- **Poll every minute.** Rejected because it is needlessly aggressive for this strategy, increases noise and request load, and does not turn TMX Money into an execution-quality feed. The endpoint's ability to return one-minute bars is an evidence-resolution capability, not a decision to poll every minute.
- **Poll hourly.** Rejected for version 1 because a total observation lag of roughly fifteen to seventy-five minutes is too weak for the intended same-day profit and loss monitoring.
- **Treat the TMX threshold price as the fill.** Rejected because the threshold may have been crossed before the delayed bar was received; using it would manufacture an unavailable execution.
- **Let the original Delphi recommendation bypass the 10% stop.** Rejected because a stale thesis is not new evidence after a material adverse move.
- **Combine the monitored swing with a pure intraday-wave strategy.** Rejected because different entry theses and opportunity sets still require separate scoreboards.

## Consequences

- TraderVI can express the user's intended short-term behavior without changing Delphi's daily selector.
- Fifteen-minute polling reduces noise but necessarily gives back part of fast moves and cannot enforce exact stop prices.
- The pure policy can be tested without market, database, brokerage, or scheduling side effects.
- Later collection must preserve event and receipt times; a table containing only a price timestamp cannot reproduce delayed decisions.
- TMX returns a mutable, still-forming bar at the end of each response. Persistence and policy evaluation must distinguish it from completed evidence rather than assuming the newest timestamp is final.
- The obsolete `freq = "minute"` request produced daily fallback data; the corrected no-`freq` request produces structurally valid 15-minute evidence. The client now rejects obvious daily fallback responses instead of silently accepting them.
- TMX caps wide intraday responses, so the monitor must use short rolling windows and any backfill must use bounded chunks with boundary deduplication.
- The conditional 10% exception increases downside exposure and must remain a paper challenger until the stronger ADR-0022 promotion contract is satisfied.
- The existing TMX Money endpoint is a user-accepted limitation for local advisory research; delay, availability, and terms remain operational risks and must be visible in documentation.

## Review questions

1. Why can a 10% or 20% threshold not guarantee the realized sale price with delayed polling?
2. What makes a Delphi breakout signal fresh enough to test the 10% exception?
3. Why does the trailing level use prior completed bars instead of the current bar's new high?
4. How does intraday management of a swing differ from a separate intraday-wave entry strategy?
