# Delphi Live V1 implementation checklist

Frozen behavior: [Delphi Live](concepts/delphi-live.md), accepted by [ADR-0053](adr/0053-delphi-live-v1.md).
This checklist records source implementation, validation and separately authorized migration work;
it does not itself grant operational authorization.

## Reviewable phases

October21 evening SQL-only correction is implemented under
[ADR-0102](adr/0102-bounded-immutable-live-evaluation-save-recovery.md):71focused/2,919full Core pass/
nine native SQL skips with test concurrency disabled, isolated Release and SQL guard/parse checks pass.
Standard Release fromc4d094fd installs21:31Toronto after normal closure/zero-owner checks, matching
Core commit/copies and125output/eight source-output hashes; zero errors. Native stages/recovery/
capacity remain pending. Existing history
cost/admission waits remain; the operator defers a paper-workload switch.
[Current SQL evidence](reviews/delphi-live-sql-reliability-20261021.md).

October 21 capacity follow-up is resumed now by operator request: bounded exact fresh-payload
snapshot reuse passes 2,821 offline Core checks/nine native skips/Debug WPF build. Standard Release
is installed14:02 after normal closure/zero-owner verification. Operator build approval/enable and
first real evaluation are verified14:13; initial20minute continuity and actual mature signals are
verified14:31 with four captured cycles under five minutes. Whole-day capacity/reliability remains
under the existing through-close watch; historical coverage is stillBlocked.
[Correction and remaining acceptance](reviews/delphi-live-snapshot-slowdown-20261021.md),
[ADR-0098](adr/0098-verified-snapshot-read-reuse-and-build-review.md).

| Phase | Source boundary | Status |
|---|---|---|
| 1 | ADR, review aids, immutable identities and validated settings | Reconciled; frozen concept unchanged |
| 2 | Measurements, four families, combined order, lifecycle, protection, quote and portfolio arithmetic | Implemented; focused regression tests passing |
| 3 | Frozen session/source, durable lease, collection slots/receipts, independent portfolio revisions, evaluations | Implemented; offline persistence contracts and SQL syntax validated |
| 4 | Shared Core scheduling, recovery, evaluation and causal action orchestration | Implemented; synthetic host and recovery regression tests passing |
| 5 | Research outcomes/baskets, cohort coverage, experiment phases and human promotion | Implemented; focused protocol and reporting regression tests passing |
| 6 | Inactive WPF host, activation command, independent diagnostics and portfolio views | Implemented; complete Release solution build passing |
| 7 | Migration, source-capacity shakedown and operational activation | Migrations 022–025 and bounded TSX calendar installed on 2026-09-06; capacity shakedown and activation remain outstanding |

The separately authorized [closed-market source trial](reviews/delphi-live-closed-market-trial-20260906.md)
passed nine of nine exact historical bar requests on 2026-09-06. It made no SQL connection or portfolio
action and contributes zero clean cohorts. The live collection and durability trial remains outstanding.

## Source organization

- `Core/Trader/DelphiLive`: host-neutral contracts, deterministic measurements/judgments, policy selection, lifecycle, safety, collection, action and monitor workflows.
- `Core/Db/DelphiLive*`: transactional storage with durable lease fencing, immutable source snapshots, evidence and isolated portfolio history.
- `Core/Runtime/DelphiLiveDesktopService`: initial desktop composition; read-only status and explicit activation are separate entry points.
- `TraderVI.WPF/Views/DelphiLiveView*` and `Viewmodels/DelphiLiveViewModel.cs`: display and operator commands.
- `TraderDB/Migrations/20260905_022_*` through `025_*`: individually reviewed additive migrations, applied on 2026-09-06 after separate authorization and backup.
- `TraderVI.Core.Tests/DelphiLive*`: synthetic/offline regression tests; no market, broker or live-database fixtures.

## Operational prerequisites

October 20 worker/display correction 55e429d5 is installed under explicit approval at 11:03:56,
execution off. Native wakes expose long Live snapshot/protection/action cost; bounded UI timing
improves, while actual setup idle/sustained capacity remain unverified. Serialization follow-up
30775d12 is separately installed under explicit approval at 11:32:14, PID 131204, execution off;
four completed wakes have bounded UI timing but no clear full-cycle speedup. Both prior copies remain
preserved. The two-minute barrier and every SQL/lease/qualification guard remain.
[Evidence and switch procedure](reviews/desktop-paper-responsiveness-20261020.md).

- [x] Isolate remaining desktop monitor CPU continuations, remove duplicate scheduled display reads,
  bound quote rows and add bounded stage/pulse diagnostics; 65 focused/2,660 full offline passes,
  nine native skips/WPF Release pass. Synthetic dispatcher gap 614.6 → 26.3 ms.
- [x] Perform the expressly approved normal corrected-copy switch, preserving the original copy;
  observe two completed dashboard wakes, bounded UI response and one lease/durable progress with
  execution off. No force-stop or financial action; historical host gap remains.
- [x] Measure/test redundant immutable-prefix and normal-commit serialization removal; 61 focused/
  2,664 full offline passes, nine native skips/WPF Release. Prepare a separate follow-up copy.
- [x] Perform the approved normal serialization-copy switch after the prior dashboard finishes;
  verify one owner, all 124 manifest files and execution off. Preserve both previous copies/history.
- [x] Observe four follow-up dashboard wakes (91.23/73.07/217.46/65.05 s), including collection/actions;
  five UI applies 19.98–41.70 ms, no recorded pulse gap over two seconds through 11:42:00. One hash-
  verified read confirms a held lease and durable progress; full-cycle speedup is not established.
- [ ] **Deferred by operator:** investigate remaining history cost and implement the simplest safe
  fix later; preserve history/both previous copies, execution off, separate validation before any
  separately coordinated switch.
- [ ] Qualify actual setup idle/sustained response/capacity.
  Recapture same-copy proof after any changed build. Native SQL rehearsals remain paused.

October 18: ADR-0090 progressive protection is installed and enabled for all three current Live paper
accounts (and all 12 Shadow accounts). Post-start checks preserve five Live holdings' floors/state,
cash/history, comparison profiles and pending sells. The installed Core hash/host responsiveness are
verified. This weekend check does not observe a regular-session bid-trigger sale; quote investigation
remains deferred. [Operational receipt and limits](project-status-history-20261101.md#reference-map-and-maintenance).

The October 14 full-session comparison qualifies the stable-context/supplementary-record correction
in [ADR-0089](adr/0089-tmx-session-context-and-supplementary-records.md). The opening native trace
also identifies SQL receipt-clock rejection; actual-store-time waiting preserves original timestamps
and strict causal/deadline checks. Both fixes are tested and installed at 09:56 Toronto. Earlier
October 9/13 diagnostic limits remain historical facts; no saved gaps or receipts were rewritten.
See [dated evidence](project-status.md) for counts and validation limits.

- [x] Implement/test wider source context, conservative supplementary exclusion and audit metadata.
- [x] Identify the opening monitor exception and test bounded SQL clock waiting/private diagnostics.
- [x] Install the tested Release WPF build under explicit restart authority (October 14).
- [x] Observe three completed scheduled cycles, policy evaluation persistence, complete current Live
  marks and all current Shadow positions with same-day prices after the initial restart (October 14).
- [x] Identify/test the sparse trailing-record refinement using the authorized four-request comparison.
- [x] Install the sparse refinement and verify its first scheduled collection/evaluations, complete
  current portfolio marks, zero CollectionFailed receipts and explicit missing bars (October 14,
  checked 10:28–10:29 Toronto; project status owns counts/limits).
- [ ] Qualify continuing provider capacity, complete invested coverage and a clean future session.
- [x] Investigate the original nine missing bars through saved receipts and the authorized 27-request
  comparison; implement/test observation-time end context and stale-source metadata (October 14).
- [x] Install the request-end follow-up and verify its first scheduled collection, source/logical/
  deadline guards and complete current Live marks (October 14, 11:14–11:15; project status).
- [ ] Qualify continuing sparse-stock availability; unavailable bars and historical gaps remain
  blocked rather than fabricated.

Further provider probes need authorization. Current warnings replace previous-cycle collection errors,
while session interruptions and host gaps remain visible. Per-stock success and completed cycles do
not themselves establish clean-session qualification; exact historical Shadow failures are not all proven.

The September 28 source update uses installed migration 041 (`SymbolAliases`). The broker-alias lookup
and prospective source adoption are natively verified after authorized restart. The missed morning
remains a coverage gap; source adoption does not establish fresh minute-level evidence. It preserves
account history and rejects unresolved references. See
[ADR-0088](adr/0088-source-symbol-aliases-for-monitoring.md) and project status for evidence.

The initial policy and generated schema install inactive. Simulation capital has no default. An explicit positive capital amount, currency and operator reason are required; activation takes effect at the next regular-session boundary.

The host requires a locally supplied, reviewed official TSX calendar snapshot through `TRADERVI_TSX_CALENDAR_PATH`. `ReviewedTsxSessionCalendar` validates a version, source reference, declared date coverage and distinct regular-session dates. It fails outside coverage and never substitutes a weekday guess. The [calendar installation](../Operations/Calendars/README.md) was separately authorized and completed on 2026-09-06: 247 full sessions within 2026-01-01 through 2026-12-23, with the path saved for the Windows user. Eight focused offline tests passed. Runtime does not call a calendar service. Coverage stops before December 24's short session; short-session handling and carried-position protection require review before extension.

The WPF process is the V1 host. A closed or interrupted host leaves visible coverage gaps. Assigned Delphi Live policies share each V3 observation; legacy Ghost and frozen System Shadow retain their existing collectors and accepted timing while sharing compatible canonical facts. Combined provider load must be measured during authorized shakedown.

Start WPF sufficiently before 09:30 Toronto time to establish successful pre-open heartbeats, and keep it open until the final 16:02 collection and closing persistence complete. The eligible official daily run and required daily baselines must already be available before the session freezes; starting Delphi Live does not generate them.

## Integration guarantees covered in source

- Shared market judgments do not share portfolio ownership or confirmation. The continuing champion and its cash-only experiment control can hold, exit and re-enter independently. Only a portfolio's own current holding or same-session completed exit grants carry authority.
- A recent successful pre-open heartbeat whose pending wake spans the regular open can attest to opening liveness; stale heartbeats, missed wake intervals and late starts record a gap even before the first collection. The WPF host supplies its thirty-second cadence. Completed session finalization is idempotent. Restart clears unfinished confirmation, expires buys and retains protective sell identities.
- A source receipt is operational only after a post-commit SQL time check proves the canonical evidence was already durable before the deadline. Provider request/attempt metadata and untouched deadline misses remain visible.
- Every expected research slot keeps its original operational disposition. Later canonical facts can mature research, while conflicts and corporate-action audits exclude affected evidence without rewriting earlier observations or fills.
- Daily portfolio return uses the exact previous canonical closing NAV, or immutable starting capital on inception; it includes overnight movement. Missing prior/closing marks cannot be replaced with an opening mark or an older close.
- Research maturity updates the cohort's original phase. Promotion rechecks the durable untouched evidence at its effective boundary and is cancelled if a later audit invalidates the previously passing result.
- Research refreshes use a completed-review watermark and changed-source dates. Historical reports load for an explicit date range of at most 366 calendar dates, separately from the monitor's periodic refresh. Portfolio history selects immutable revisions before the report cutoff and any generation ending, preserving historical policy identities. The fill diagnostic compares all closed trades with the bid/ask-only closed-trade slice and labels its distinct basis beside official portfolio NAV return.
- Read-only component, threshold, safety and confidence tables retain coverage, timing, forward outcomes, downside and opportunity capture, including missed opportunities. Their research-only comparisons have no action or promotion authority.
- Descriptive portfolio statistics use each generation's own inception and report cutoff, including ended comparison runs. Win rate counts profitable completed full-position trades. Exposure averages exact marked position value/NAV within sessions, then weights sessions equally; gross turnover is total filled buy-plus-sell notional divided by immutable starting capital. The no-fill rate counts requested actions without a fill at the cutoff, with still-pending actions shown separately. These diagnostics do not replace the frozen promotion test.

## Validation record

The original source, build and operational validation, migration 022–025 application, historical
replay and Portfolios integration are preserved in the
[November 2 checklist snapshot](delphi-live-implementation-checklist-history-20261102.md#validation-record).
Those checks retain their dates and limitations; they do not prove current-session coverage.

## Migration application — 2026-09-06

[Application and preservation evidence](delphi-live-implementation-checklist-history-20261102.md#migration-application--2026-09-06).
The applied migration scripts must not be edited or rerun.

## Daily preview and historical replay — 2026-09-06

[Completed historical replay and its limitations](delphi-live-implementation-checklist-history-20261102.md#daily-preview-and-historical-replay--2026-09-06).
Historical estimates are not operational or promotion cohorts.

## Portfolios overview integration — 2026-09-06

[Completed integration and verification](delphi-live-implementation-checklist-history-20261102.md#portfolios-overview-integration--2026-09-06).

## Authorization boundary

The user separately authorized backup and application of migrations 022–025 on 2026-09-06. They are now
applied and must not be edited or rerun. The authorization did not include application/trading activation,
external market calls, model training, model-artifact publication, broker operations, commits, pushes or
pull requests. The subsequent calendar review and installation were separately authorized and completed.
The user then authorized a controlled collection trial; its closed-market historical portion is complete.
Live timing and SQL durability still require an open-session trial. Simulation activation remains a
separate explicit operator action with user-specified capital.

The remaining rollout risks are live SQL concurrency/latency, combined provider capacity, reviewed calendar coverage,
and actual WPF operation through complete sessions. Offline tests and build/parser checks do not establish those
operational facts. The application record below identifies the exact reviewed migration files and their
referenced table definitions.
