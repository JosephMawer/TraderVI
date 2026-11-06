> Historical snapshot before the November 5 documentation consolidation. Later evidence may supersede these statements; use [current status](project-status.md).

# TraderVI Project Status

Last reconciled **November 5, 2026**. This page records the latest dated evidence and its limits;
it is not a live health check or authority to trade. [Roadmap](roadmap.md) owns priority;
[execution checklist](strategy-alignment-implementation-checklist.md) and
[Live checklist](delphi-live-implementation-checklist.md) own remaining acceptance work.
Design and authority are governed by [design rules](design-rules.md) and applicable ADRs.

## Operating state and immediate risks

- **November 5 evening restart:** At 21:13 Toronto all three applications were stopped. The last
  collector receipt was 20:26:08; Windows AppX events show a Codex package update attempt stopping
  `CodexSandboxService.OpenAI.Codex` at 20:26:09. The timing strongly suggests termination through
  their shared launch environment, but individual TraderVI process termination causes were not recorded.
  No new TraderVI crash event was found at that time; the 16:58 dashboard binding crash was the earlier,
  already repaired incident. No collector stop request was present and installed package hashes matched.
  On explicit operator instruction, the reviewed collector supervisor, engine and dashboard were started
  at 21:17 (collector 15936, engine 21836, dashboard 13292; supervisor 35164). Read-only engine status at
  21:18 showed all 15 workers with zero consecutive failures; execution reported waiting for regular
  trading hours. The collector reported fresh OutsideSession and the dashboard showed broker connected.
  No binaries, configuration or financial permissions were changed. This verifies startup, not the next
  market session or survival of a future Codex service shutdown. Private launch receipts, local logs and
  Windows-event evidence: `artifacts/startup-20261105-2116/`.

- **November 5 pre-open readiness:** At 09:11 Toronto, the dashboard, engine and collector were
  running with their November 4 starts retained. Fresh read-only native controls showed Wealthsimple
  connected, all 15 workers with zero consecutive failures, zero integrity incidents and execution
  waiting for 09:30. The collector reported fresh OutsideSession. Hash-verified SQL confirmed today's
  valid official Delphi preparation using November 4 data, zero unfinished compact orders and zero
  unresolved integrity incidents. The latest shared publication covered November 4's 16:00 close,
  published at 16:02; yesterday's morning publication stall was not the latest saved state. Collected
  observations reached 15:59 across 40 symbols with zero stored conflicts; this does not prove complete
  per-symbol coverage. Verified archived execution diagnostics were dated November 4 at 16:00 and
  do not establish today's quotes, opening risk evidence or protection. Fresh market-hour Paper/Real
  evaluation remains unverified. No nightly rerun or host restart was needed. Private evidence:
  `artifacts/morning-readiness/2026-11-05/manual-readiness/`.

- **Trading dashboard holdings:** At 14:24 Toronto November 3, independent read-only broker
  checks confirmed two TFSA holdings and matching broker order history. The dashboard showed
  its empty 11:56 snapshot after refresh failed. A tested dashboard change reads holdings and
  balances separately when trade synchronization throws and dates retained holdings when a
  current read is unavailable. Dashboard repair `2476438f` was installed in the standard Release
  folder with a dashboard-only restart. The reopened Trading page showed two holdings and a
  successful account-history update. The engine and collector retained their process IDs.
  The original failing synchronization step has not been identified. The subsequent dashboard
  update `56cf3eb9` was installed and verified on the populated Trading grid. It shows ticker,
  company name, shares held, average cost per share and gain or loss against the remaining book
  cost in both money and percent. The installed grid also shows current price per share
  beside average cost, using the snapshot's position value divided by shares held.
  Wealthsimple supplies no quote time for that value. The gain/loss and return columns show
  positive values in green, negative values in red, and zero or unavailable values in neutral text.
  The separate selected-holding card was removed from the installed dashboard. Trade options
  are on the holding's row menu, and strategy details are on the status cell.
  The November 3 dashboard update replaces the strategy dropdown with the saved approved strategy
  and a **View strategy** link to its source in **Portfolios → Strategy workspace**. Full version
  details are on hover; strategy changes retain **Review for Real**. The dashboard-only update was
  installed with the engine and collector retained. Release build and 255 focused tests passed;
  native accessibility verified the loaded label, link and removed dropdown. Link click verification
  remains pending because desktop automation could not resolve input geometry.
  **Main account** shows buying power below available cash and Refresh Wealthsimple below the
  performance period choice. The single eligible TFSA loads automatically. **Account tools**
  groups broker status with pending orders, manual buys with sells, and reconciliation with import.
  Its broker and manual recommendation sections share one scrolling page with fixed headings and dividers.
  It retains the managed TFSA buying controls, advanced risk setup and manual recommendation
  workflow. Tracked-position pause and resume are on **Recorded trades & monitor**. The November 3
  read-only database check found no saved manual recommendation approvals, batches, operator
  account reports or recommendation-linked purchase reports. Broker synchronization receipts
  were present. The manual workflow decision is deferred in [issue #27](https://github.com/JosephMawer/TraderVI/issues/27).
  The single-line dashboard footer shows engine, collector, execution, history, session-dated daily
  preparation and Wealthsimple status. Daily prep uses the reviewed TSX calendar and nightly receipt.
  The connection control separately flags stale or unloaded holdings. Collector status comes
  from its dated local receipt. The engine and collector
  continued running.
  **Engine** now defaults to flat **Health & issues**, with **Controls** and **History** beside it.
  Findings show explanation, impact, next step, evidence and dates. The November 3 21:56 Toronto
  dashboard-only extension adds per-incident recovery progress, observed checkpoint speed, clearance
  requirements and focused recovery navigation. Copy diagnostic snapshot exports the same structured
  evidence, separating receipt facts from live checks still required by the engine. The engine and
  collector retained their original processes; 75 shared runtime files were unchanged. A final
  scope-compatibility correction was installed at 22:01 Toronto. Release build and 29 focused
  health/recovery/batch tests passed (including all 23 health cases). Existing compiler
  warnings remain; dependency-security auditing was disabled. Native accessibility confirmed the new
  cause/impact, clearance checklist and advancing progress/speed before that final scope correction. Screenshot capture timed out and
  automated navigation clicks failed on desktop geometry/cached targets; pixel layout, review-link
  interaction and clipboard interaction remain unverified.
  [Diagnostic contract and coverage limits](adr/0126-engine-health-and-diagnostic-snapshot.md).

  **November 5 trade-history layout:** Installed dashboard change `276a458f` at 15:01 Toronto, moving **Trade history**
  into its own tab immediately beside **Main account**. The grid fills the tab, with wider columns,
  scrollbars and full reason text on hover; durable poll history uses the vacated monitor width.
  Existing trade data and refresh behavior are unchanged. The staged WPF Release build passed.
  Only the dashboard DLL and symbols were replaced after a normal dashboard close; the engine and
  collector retained their process IDs and start times, and 116 other runtime files retained their
  hashes. Native accessibility confirmed the new adjacent tab. Automated tab clicks were rejected
  after detected user input, and screenshot capture timed out on both attempts, so populated-grid
  interaction and pixel layout remain unverified. Installation receipts and rollback files:
  `artifacts/trade-history-ui-20261105/`.

  **November 5 market-context follow-up:** The operator selected Holdings and sectors with a compact
  benchmark/breadth summary and accepts one- to five-minute updates. At 17:00 Toronto, dashboard
  changes `58c791ab`/`b5d2bc8e` installed the compact summary, sector/exposure panels and repaired
  sector resolution through the existing verified managed-purchase projection. Native accessibility
  confirmed all displayed holdings classified and all 11 sector indices populated. The snapshot is
  fetched once on opening, as requested for this after-hours start; automatic intraday refresh is off.
  Follow-up `09293f17` installed at 17:04:58 Toronto preserves exposure rows across unchanged status
  checks and holdings snapshots, preventing repeated bar recreation. 246 focused tests and the staged
  WPF build passed; engine/collector identities and 116 other runtime-file hashes were retained.
  The first panel installation exposed a read-only progress binding error; the corrected one-way
  binding passed disconnected native rendering at two widths before reinstall. 245 focused tests
  and the staged WPF Release build passed, with existing compiler warnings. Engine/collector process
  identities and 116 other runtime-file hashes were retained. Live screenshot capture timed out;
  full-dashboard pixel layout remains unverified. The 16:32–16:35 read-only TMX check returned all 11 sector indices and
  both benchmarks after correcting the display candidate's Composite symbol to `^TSX`. Four separate
  dated-feed samples report today's 16:00 close. This after-close check does not establish intraday
  delay. The visible snapshot states receipt time and unverified provider timing. Neither background host was restarted. No manual database
  mutation was performed; the dashboard resumed its existing account-refresh workflow.
  [Evidence and selected design](reviews/trading-market-context-20261105.md);
  [remaining acceptance](strategy-alignment-implementation-checklist.md#main-account-market-context--november-5).

- **Real trading:** At 09:58 Toronto November 3, repair build `0e2f6a22` started from the
  standard Release path in build-review mode, connected to the broker. The collector retained
  its process. After approval and separate enable, the 10:01 SQL read verified both owner
  links, both original exit decisions and zero existing sell orders or reservations.
  Normal execution resumed at 10:02. Each exit had one original sell request. Broker history
  agreed with zero holdings and zero pending orders at 10:03, confirmed by a fresh account
  read at 10:05. One exit initially remained blocked because its broker fill timestamp
  preceded local reservation by 85 milliseconds. Broker creation preceded its fill by 242
  milliseconds. At 10:23 the no-submission maintenance process verified the original
  acknowledged journal, terminal broker facts and inventory. SQL confirmed one sell
  request, one managed fill, full quantity and net proceeds applied, a closed position
  and zero active compact restrictions. Repeating the command returned already reconciled.
  The permanent repair `a6110cb0` entered build review at the standard Release path at
  10:24. After operator approval and separate enable, it passed the normal restart account
  check at 10:31:04 Toronto. At 10:34:30 the strategy evaluated 38 fresh symbols from the
  10:30 bar and found no confirmed entry. All 12 owned orders were reconciled with zero
  managed holdings. Paper recovery was separately still restricted at that morning check; its
  November 3 evening completion and clearance are recorded below.
  At 10:57 and 11:02 Toronto two buy selections reached final sizing. In both cases
  cash and the strategy allocation covered a whole share, while the remaining cumulative
  purchase allowance did not. The saved outcome was `InvalidOrderQuantity`. No new order
  was reserved under the cumulative approval then in force. At 11:56 Toronto the standard
  Release dashboard and engine `9d2882a6` opened in build-review mode. The broker was
  connected and the collector retained its process. A read-only reconstruction of original
  settled orders matched cash to the proposed reusable balance. After explicit operator
  approval and separate enable, the normal engine passed the 11:58:41 restart account
  check. At 11:59:28 SQL confirmed the approved build and matching reinvestment terms in
  both records, each of the 12 original orders reconciled once, reusable capital equal
  to cash and zero unfinished orders or positions. The 12:02:46 Real evaluation consumed
  38 fresh symbols from the noon bar and found no confirmed entry. A live buy using
  returned proceeds had not occurred at that check. [Reinvestment terms](adr/0125-reusable-strategy-sale-proceeds.md).
  [Receipt-time contract](adr/0117-compact-trading-current-storage.md#november-3-clarification--protective-sell-receipt-times).
  Both original October 29 buys were fully filled and reconciled. Their original reservation
  revisions identified the same owners still saved on the positions. The first compact transfer
  omitted the per-position link and subsequent approvals preserved that omission. Replaying
  the captured account on the exact Release binary restored both links with no position or
  order writes. The Release suite passed 3,584 tests with 16 opt-in skips, including the new
  two-exit retry regression among the passing tests.
  [Repair contract](adr/0117-compact-trading-current-storage.md#november-3-clarification--original-protection-ownership-across-builds).
  At the November 3 open both remaining holdings triggered protective exits.
  Fresh bids reached the normal engine, but neither sale passed the saved ownership check.
  At 09:30:49 Toronto the broker showed zero pending orders. At 09:31 a read-only comparison
  found neither current position recognized by any of the three saved settings records,
  including the November 2 approval. Both trigger times and floors passed the corresponding
  checks. The operator approved ordinary Release build `124c4408` after the machine restart.
  The engine passed its restart account check at 09:29:43 before the opening block.
  On November 2 at 17:50 Toronto the original completed sell reconciled into managed
  revision 6844 and compact checkpoint 29. SQL readback verified identical financial, holding and
  order fingerprints and the same buying mode. The Trading view restored the saved strategy.
  The operator approved build `cc64462c` and enabled its host. At 21:24 Toronto the normal
  engine was Connected, passed the restart account check and waited for regular trading hours.
  Post-start SQL confirmed revision 6845, compact checkpoint 30 and the two immutable settings
  records. The original sell, remaining holdings and protection, risk references and purchase
  allowance were preserved. The collector stayed running.
  [Cutover evidence](reviews/wealthsimple-auth-and-engine-diagnosis-20261102.md#live-reconciliation-and-build-review).
  [Current diagnosis and evidence](reviews/wealthsimple-auth-and-engine-diagnosis-20261102.md#follow-up-through-1512-toronto--closed-position-guard-confirmed),
  [November 1 installation](project-status-history-20261101.md#november-1--compact-restart-active-for-delphi-live).
- **Protection gap:** On October 30, a managed holding's broker bid reads failed repeatedly, so its
  bid-dependent protection could not be verified even though the host reported Running. The operator
  placed the investigation on hold; reliable automatic selling must not be inferred from saved stops
  or a Running status. [Evidence and limits](project-status-history-20261101.md#october-30--held-position-bid-protection-gap-verified-1003-toronto).
- **Paper recovery and inputs:** The November 3 21:16 Toronto hash-verified read-only check confirmed
  one unresolved Paper restriction inherited from the October 30 13:22:58 startup-continuity incident.
  Real-only verification completed at 14:33 and its 14:41 clearance retained Paper. The operator
  authorized Paper-only recovery and conditional guarded clearance on November 3. Run
  `28f8f730-76ce-471f-94d2-cde1904cef0d` started at 21:30 Toronto on the existing engine.
  It completed at 23:42:09 Toronto with 212,508 verified units across all four groups, after about
  2h12m. At 23:57:44, supported `ClearIntegrityHolds` returned true for only the original Paper
  restriction and this run's matching verification-pending incident, Scope=Paper. The engine's
  current-head, original-incident-boundary, database-identity and immutable-guard checks passed.
  The 23:57:59 readback reports zero unresolved incidents. All five previously blocked Paper/dependent-
  Research workers retried after clearance and completed ticks with zero consecutive failures.
  These outside-session ticks do not prove fresh market-hour Paper activity or repair issue #24.
  Three older failed audit receipts remain visible as incomplete history; they are not new failures
  of this successful pass and were not erased. Private control receipts remain under
  `artifacts/paper-recovery-20261103`; the conditional-clearance follow-up is complete.
  The pass found no verification failure; the original shutdown cause remains unknown. Engine PID
  21140 and collector PID 17980 retained their original November 3 starts without restart. Real
  execution remained enabled and waiting for regular trading hours during verification and clearance.
  The audit worker reported zero failures and continuing five-second batches. Source inspection
  confirms recovery priority, the maximum configured 4,096-step/five-second batch and one-second
  yield; no supported live speed control exists. The installed scheduling and verification checks
  were retained. Observed speed varies with history; this is not proof of maximum possible throughput.
  A 15-second native stack sample identified legacy full-state fingerprint hashing as the dominant
  observed cost. Hash-verified aggregate SQL confirmed a position change ahead of 8,866 quote entries
  in canonical checksum order, which limits the installed prefix-reuse optimization. The compact
  Real restart did not replace this Paper path. No supported live speed adjustment was identified;
  no host restart, priority change or verification bypass occurred.
  [Bottleneck evidence and limits](reviews/paper-recovery-bottleneck-20261103.md).
  November 4: the Paper checkpoint restart/incremental-verification scope was approved for source
  implementation and offline validation, followed by a separately authorized isolated-copy rehearsal.
  Offline checks and six copy-only SQL phases passed. Separately authorized cleanup and installation
  completed November 4 at 01:40 Toronto: matching Release engine/dashboard installed, original recovery
  adopted in 0.742 seconds with zero legacy tail and all three portfolio revisions unchanged. Fresh
  backup/copy verified and synchronized; approximately 33.7 GB of disposable rehearsal files removed.
  Joseph completed exact-build review, approval and resume. Normal engine PID 32368 started at 01:51:41
  Toronto; all three Paper portfolios qualified within 2.967 seconds, with zero pending buys captured
  and zero integrity incidents. The automatic Paper NewHistory pass completed with zero verified units:
  all targets began after their unchanged saved boundaries. No full Paper scan was started.
  Real's restart account check passed; Wealthsimple is connected and approved execution waits for
  regular trading hours. The separate Real/shared new-history pass completed 440 units.
  Collector PID 21964 reports fresh OutsideSession status; it replaced PID 17980 before installation,
  with unchanged verified package/configuration and an unestablished restart cause.
  [Installation and remaining boundary](reviews/paper-restart-copy-rehearsal-20261104.md#live-installation-and-rehearsal-cleanup).
  Nonempty overnight delta and market-time acceptance
  remain in the [owning checklist](strategy-alignment-implementation-checklist.md) under
  [ADR-0127](adr/0127-paper-checkpoint-restart-and-incremental-verification.md).
  [Original restart and clearance evidence](project-status-history-20261101.md#october-30--real-execution-resumed-after-recovery-1446-toronto),
  [prior diagnosis](reviews/wealthsimple-auth-and-engine-diagnosis-20261102.md).
  A separate Paper-input failure was traced to missing verified holding correspondence in
  the service wiring. The November 4 source repair under issue #24 now supplies one resolver to
  market collection, Paper, research and desktop, preserves the compact buy's original position
  identity, and carries the verified correspondence through progress, quotes and protective sales.
  Full offline Core validation: **3,695 passed, 17 native/operational skips, zero failures**. Isolated
  Release WPF/engine builds passed with 257 existing compiler warnings and zero errors; no dependency
  vulnerability warning was emitted, but `--no-restore` did not refresh a security audit. The SQL project
  was not built. [Consumer inventory and validation](reviews/verified-symbol-mapping-20261104.md).
  This repair is **installed and resumed** as of November 4, 15:28 Toronto. At the earlier investigation, the broker buy was filled
  and applied to compact storage while the managed head still marked it unreconciled. Ordinary build
  review requires those records to agree. A separately authorized restored-copy rehearsal passed the
  actual SQL handoff, simulated review and fresh-process reload, preserving all four holdings, original
  fill and protection without a broker client. Rehearsal regression passed **3,695 tests, 18 opt-in skips,
  zero failures**; the three native phases passed separately. The private instance is stopped; its
  33.8 GB of database/backup files remain retained for explicit cleanup. The guarded live original-buy
  maintenance command is now implemented and qualified on a fresh restored copy using the exact Release
  Core: maintenance, review and reload pass. Release regression: **3,707 passed, 18 opt-in skips, zero
  failures**; WPF/engine build passed with two existing CS0168 warnings. The authorized live maintenance
  advanced the managed head once, preserved the original filled receipt and reconciled all 22 managed
  orders; all four open positions have saved protection state and zero compact restrictions. The tested
  package (source `32bd5119`, Core `A6B891CD…7043`) is installed. Joseph approved the exact build and
  resumed it; normal engine 22780 started at 15:28:25 with the broker connected and collector 32356
  unchanged. All three Paper portfolios qualified from saved state within 3.3 seconds, incremental
  verification enabled and no new full scan or integrity incident. Fresh broker bids and matching
  protection were observed for all four holdings. One ordinary protective sale then filled/reconciled;
  all 23 managed orders are settled and the three remaining holdings retain matching protection.
  **New-entry readiness remains blocked:** at 15:29, shared publication was still at the 10:20 bar
  despite fresh 15:28 collector stock/benchmark observations. Real evaluated but waited for fresh inputs,
  also reporting `NetAssetValueUnavailable`; fresh Paper entry evaluation is not established.
  [Live transition evidence](reviews/verified-symbol-mapping-20261104.md#live-transition).
  Prospective new-holding and fresh market-hour Paper/Real acceptance remain open. The Real
  recovery alone did not clear Paper restrictions; the explicit Paper pass and clearance above now do.
  [Diagnosis](project-status-history-20261101.md#october-30--paper-inputs-cause-confirmed-without-restart-10181020-toronto),
  [roadmap](roadmap.md).
- **Collector and minute data:** The independent collector runs separately from the trading engine.
  November 4 morning readiness found it absent, with its last receipt Stopped at 04:30:33 Toronto,
  no surviving supervisor and no deliberate stop marker. No matching Application error event explained
  the exit. Joseph explicitly approved restarting the unchanged hash-verified package at 08:54 under
  its existing bounded supervisor (collector PID 32356, supervisor PID 29944). Engine PID 32368 retained
  its 01:51 start. At 08:58 all 15 engine workers had zero consecutive failures, Paper's three portfolios
  remained qualified, zero integrity incidents were open, and the collector reported fresh OutsideSession
  with today's subscription boundaries. A fresh 09:08 capture retained those engine/collector processes
  and zero worker failures. The collector's exact exit cause remains unknown: its finalizer writes
  Stopped even after a fatal error, and the old supervisor retained no exit details. Windows entered
  connected standby at 03:54, but this does not establish causation; Application and PowerShell logs
  supplied no matching collector failure. The bounded supervisor allows three restarts, but exhaustion
  was not proven. The relaunched supervisor now retains stdout/stderr.
  The history-reading broker v2 helper returned PendingOrdersUnsupportedResponse at 08:56. Its
  64-request cap is the leading explanation: yesterday's successful check consumed 63 requests for
  53 historical orders. An offline characterization reproduced that same pending-read status after
  budget exhaustion without sending the pending request (131 checks passed; two existing CS8625
  compiler warnings). The installed current-account v1 helper succeeded at 09:07 with seven requests
  and zero pending orders. Its public receipt is `report-efec9e5ebeec48ba9ea09ceee11830cf.json`
  under `artifacts/read-only-broker/`. This clears the fresh pending-read evidence gap; the installed
  v2 failure receipt does not expose enough detail to prove its exact cause. The live engine does not
  use that helper cap. A genuine pending-read failure can block order submission, and a collector
  outage can block entries and impair bar-based protection. Today's market-hour inputs/protection
  remain prospective. A read-only five-minute chat watch runs through noon November 4;
  current evidence and already-reported findings are in `artifacts/morning-readiness/2026-11-04/engine-watch/`.
  Migration 057 and the latest-minute collector were installed November 1; closed-session checks and
  a native SQL/mock-HTTP package check passed. The first market-open provider response under this
  policy, coverage/capacity and venue qualification remain unverified. The Real strategy remains
  five-minute; the one-minute publisher is staged, and financial adoption requires separate review.
  [Evidence](project-status-history-20261101.md#november-1--latest-minute-collector-running-trading-engine-retained),
  [acceptance work](strategy-alignment-implementation-checklist.md#independent-collector-and-recovered-confirmation--october-27-overnight).
- **Nightly:** The task was changed November 1 to 06:30 Toronto local time daily and selects verified
  existing outputs for Hermes, DataAudit, Delphi and Athena. Successful Windows Release builds register
  per-app outputs for future selection. November 5's scheduled 06:30–06:37 run completed all four
  stages, overall Attention. Delphi succeeded; Hermes reported individual-source warnings, DataAudit
  retained one error/ten warnings with no new, escalated or resolved findings, and Athena reported two
  invalid prediction outcomes. SQL verified one valid November 5 official preparation using November 4
  market data. At 09:10, Windows reported the task Ready, zero missed runs and next occurrence November 6
  at 06:30. The requested conditional catch-up was unnecessary; no rerun occurred. Private receipt,
  log and SQL evidence are retained in the November 5 manual-readiness directory above.
  November 4 inspection confirmed the scheduled 06:30–06:37 run
  completed all four stages for November 4, overall Attention; Windows recorded zero missed runs and
  the next occurrence November 5 at 06:30. At Joseph's request, one preflight-verified forced rerun ran
  08:55–08:57 with the existing outputs. Delphi and Athena succeeded; Hermes still reported two missing
  daily-bar sources, and DataAudit retained one error/ten warnings (no new or resolved findings).
  That date's valid Delphi preparation uses November 3 market data. Athena's earlier one invalid prediction
  outcome had MissingSymbolSession; zero newly invalid outcomes on rerun does not repair that record.
  No models were trained, outputs rebuilt, engine restarted or order/permission settings changed by
  the rerun. Receipts/log pointers are retained in the morning-watch evidence directory above.
  Selected output directories are runtime dependencies and
  must be protected. [ADR-0122](adr/0122-existing-output-nightly-execution.md),
  [configuration and evidence](project-status-history-20261101.md#november-1--nightly-reuses-existing-outputs-at-0630).

## Evidence and development

- **Edge passkey sign-in:** On November 2, the operator's desktop Edge offered its Microsoft Password
  Manager passkey while TraderVI's embedded browser did not; the attempted phone route also reported
  no available passkeys. An operator-selected companion-extension route is implemented in source,
  handing a completed regular-Edge session to existing native verification through a paired local pipe.
  Seventy focused authentication/handoff tests, extension protocol/event checks, the real helper's offline
  pipe/framing/receipt check and a WPF build passed.
  The build retained existing compiler warnings; NuGet security auditing was disabled. The full SQL-containing
  solution was not built. The per-user native helper was registered November 2 against a hash-checked
  copy in `artifacts/edge-passkey-20261102/native-host`. The operator confirmed the enabled extension
  with the expected ID, then authorized the desktop switch. At 14:29 Toronto on November 2, the updated
  Release desktop was open and responsive as PID 26768 from `artifacts/edge-passkey-20261102/desktop-runtime`;
  selected application hashes and the copied collector-routing file matched their source. The old desktop
  closed gracefully; engine PID 22452 and collector PID 1452 remained running without replacement or restart.
  The original installed output remains available. Process continuity alone does not prove trading
  or account connectivity.
  A follow-on usability correction opens the extension's connection page before Wealthsimple, removing
  the toolbar timing step and dependence on the provider preserving a URL pairing fragment. Extension
  protocol/event/connection-page checks and the Release build passed, with existing compiler warnings.
  At 14:36 Toronto, the refined desktop was open/responding as PID 9432 from
  `artifacts/edge-passkey-20261102/desktop-runtime-v2`, with copied collector routing and unchanged engine
  PID 22452 / collector PID 1452. The operator subsequently confirmed successful login through this
  refined flow; engine connection status independently reported Connected at 14:47 Toronto.
  This verifies initial passkey handoff/native authentication; subsequent live refresh remains unverified.
  [Auth summary and separate engine diagnosis](reviews/wealthsimple-auth-and-engine-diagnosis-20261102.md).
  [ADR-0123](adr/0123-edge-passkey-session-handoff.md),
  [setup and limits](../Tools/Wealthsimple.EdgeBridge/README.md).

- **Prediction scorecards:** The November 3 implementation grades ten-session predictions once due and
  labels supported scores provisional below 95% coverage. The November 3 read-only aggregate found
  9,281 stored usable outcomes / 9,284 due (99.97%), 3 invalid, 0 due pending and 2,715 waiting for
  ten later XIU sessions (latest stored benchmark date October 30). Athena's 55 tests passed and the
  desktop compiled in isolation. The November 3 nightly preflight selected the registered
  `Athena/bin/Release/net10.0/Athena.dll` from `a6465c19` and verified all four applications.
  At 00:32 Toronto November 3 the updated desktop reopened from its ordinary Release path. The live
  Scorecards tab showed **Ready** for all four models and 99.97% overall coverage, with the same
  graded, due, waiting and invalid counts. WPF loaded its private calibration DLL. The engine and
  collector retained their processes and start times. The first scheduled report from the new Athena
  build remains pending. [Installed desktop layout](running.md#build-output-folders).
  Delayed-intraday outcomes have a separate evidence gap. [November 1 baseline](project-status-history-20261101.md#november-1--athena-prediction-scorecards-and-the-ten-session-window),
  [reporting contract](adr/0124-due-prediction-scorecard.md).
- **Paper protection:** Progressive individual-profit protection was installed and enabled October 18
  across all 15 then-active paper portfolios, with existing holding protection preserved. The off-session
  verification did not establish regular-session bid touches or continuous quote freshness.
  [Installation evidence](project-status-history-20261101.md#reference-map-and-maintenance),
  [ADR-0090](adr/0090-progressive-individual-profit-trailing.md).
- **Nightly application separation:** DataAudit, Hermes and Athena build independently of Core under
  ADRs [0119](adr/0119-independent-data-audit-build.md), [0120](adr/0120-independent-hermes-build.md)
  and [0121](adr/0121-independent-athena-build.md). November 1 builds and relevant tests passed,
  including 53 Athena and 3,545 Core tests in the Athena checkpoint (16 operational checks skipped).
  These are source/build checks, not proof that the running desktop or trading engine was replaced.
  Dependency security auditing was disabled; existing compiler warnings remain, and the full
  SQL-containing solution was not built. [Detailed validation](project-status-history-20261101.md#november-1--athena-builds-independently-of-core).
- **Desktop Delphi:** The official-run button and automatic daily run after strategy assignment were
  removed in source; result views and refresh remain. The WPF build and 23 focused tests passed
  November 1. This change was not installed at that checkpoint. [ADR-0118](adr/0118-delphi-desktop-results-only.md),
  [validation](project-status-history-20261101.md#november-1--desktop-daily-delphi-execution-removed-from-source).
- **Storage and backups:** Receipt compression migration 056 was installed November 1 after a verified,
  synchronized backup; independent checks preserved logical data and released 2.51 GiB for reuse within
  SQL. First market-open use and the next backup size were not measured. A separate test-database restore
  drill remains outstanding. [Conversion evidence](reviews/sql-cleanup-inventory-20261031.md#production-conversion--november-1).
  Authorized retired-nightly and intermediate-build cleanups also completed November 1; selected outputs
  and running hosts were checked afterward. [Cleanup record](project-status-history-20261101.md#november-1--retired-nightly-copies-removed).

## Remaining qualification

The roadmap and checklists retain the details and order. Key boundaries are reliable held-position
protection, the offline-validated Paper restart redesign's separate installation acceptance, symbol resolution, complete market-data/valuation coverage, native
restart and order reconciliation, and comparable mature paper evidence before broader strategy
adoption. AI-managed portfolio components have source/tested pieces but are not an activated operating
portfolio; valuation, cash-event, integration and host qualification remain. A reviewed short-session
calendar and carried-holding protection are needed before **December 24, 2026**. Dependency advisories,
schema drift, sustained memory capacity and a restore drill remain separate reliability work.
[Open questions](reviews/open-questions.md) and the relevant checklists retain decisions and dependencies.

## History and maintenance

The complete former status, including its dated operational receipts and superseded statements, is
preserved in [status history through November 1](project-status-history-20261101.md); the earlier
[September 22 archive](project-status-history-20260922.md) remains available. Historical claims do not
renew operational verification. Update the affected current item when evidence changes, retaining its
date and limits; keep detailed incidents in linked reviews/history and acceptance criteria in their
checklist. Avoid adding a chronological entry for every checkpoint to this page.


## Prior ADR index metadata (November 5 snapshot)

Historical index annotations, preserved for context; linked ADRs own decisions and current status owns operational evidence.

| # | Title | Historical index annotation | Domains |
|---|---|---|---|
| [0127](adr/0127-paper-checkpoint-restart-and-incremental-verification.md) | Paper checkpoint restart and incremental overnight verification | Approved implementation; not installed | architecture, data-pipeline |
| [0126](adr/0126-engine-health-and-diagnostic-snapshot.md) | Flat Engine health and shared human/AI diagnostic records | Accepted | user-interface, architecture |
| [0124](adr/0124-due-prediction-scorecard.md) | Grade due ten-session predictions and label partial coverage | Accepted | math-statistics, data-pipeline |
| [0125](adr/0125-reusable-strategy-sale-proceeds.md) | Reuse reconciled strategy sale proceeds under explicit ongoing approval | Accepted | risk-management, architecture |
| [0123](adr/0123-edge-passkey-session-handoff.md) | Existing Edge passkeys through a paired session handoff | Installed; initial native handoff confirmed November 2; live refresh pending | architecture |
| [0122](adr/0122-existing-output-nightly-execution.md) | Reuse existing nightly outputs at 06:30 | Installed; first scheduled execution pending | architecture, data-pipeline |
| [0121](adr/0121-independent-athena-build.md) | Athena builds independently of Core | Accepted; source/build validated, not installed | architecture, data-pipeline |
| [0120](adr/0120-independent-hermes-build.md) | Hermes builds independently of Core | Accepted; source/build validated, not installed | architecture, data-pipeline |
| [0119](adr/0119-independent-data-audit-build.md) | DataAudit builds independently of Core | Accepted; source/build validated, not installed | architecture, data-pipeline |
| [0118](adr/0118-delphi-desktop-results-only.md) | Delphi desktop results only | Accepted; source implemented, not installed | architecture, user-interface |
| [0117](adr/0117-compact-trading-current-storage.md) | Compact trading current-state storage | Accepted first disconnected increment; not installed | architecture, data-pipeline |
| [0116](adr/0116-temporary-paper-continuation-during-reboot-recovery.md) | Temporary Paper continuation during reboot recovery | Installed; run subsequently cleared by native proof | architecture, data-pipeline |
| [0115](adr/0115-exact-audit-prefix-hash-reuse.md) | Exact audit prefix hash reuse | Accepted; installed, recovery acceptance pending | architecture, data-pipeline |
| [0114](adr/0114-explicit-nightly-preparation-date.md) | Explicit nightly preparation date | Accepted | architecture, data-pipeline |
| [0113](adr/0113-nightly-data-audit-report.md) | Nightly data-quality audit and report | Accepted | architecture, data-pipeline, user-interface |
| [0112](adr/0112-repository-documentation-outside-desktop.md) | Read documentation and architecture on GitHub | Accepted; supersedes ADR-0036 | architecture, user-interface |
| [0110](adr/0110-nightly-isolated-symbol-warnings.md) | Continue nightly work after isolated ticker-source failures | Operational recovery verified October27; ticker warnings preserved, all stages complete | architecture, data-pipeline |
| [0109](adr/0109-explicit-ongoing-delphi-live-management.md) | Explicit ongoing Delphi Live management | Installed; ongoing approval saved October27; native lifecycle qualification pending | architecture, risk-management, user-interface, market-microstructure |
| [0108](adr/0108-strategy-workspace-and-independent-account-evidence.md) | Strategy workspace and independent account evidence | Installed October 24; offline verification complete; desktop visual review pending | user-interface |
| [0107](adr/0107-build-review-within-existing-dated-permission.md) | Build review within existing dated permission | Implemented, tested, installed; operator approval/enable and fresh evaluation verified October 23 | architecture, risk-management |
| [0106](adr/0106-exact-selected-session-risk-receipt-storage.md) | Exact selected-session risk receipt storage | Installed a090757f; original risk event recovered into operator-approved October 23 permission | architecture, risk-management |
| [0103](adr/0103-real-execution-independent-of-paper-research.md) | Real execution independent of paper research | Independence, verification/audits and dashboard-close lifecycle accepted; staged delivery targets tonight, feasibility unverified | architecture, data-pipeline |
| [0105](adr/0105-reviewed-selected-session-continuation.md) | Reviewed selected-session continuation | Installed; October 23 continuation approved and host separately enabled by operator | architecture, risk-management, user-interface |
| [0104](adr/0104-scoped-recovery-and-sequential-history-audits.md) | Scoped recovery and sequential history audits | Accepted; source implemented, native qualification pending; real trading remains disabled | architecture, data-pipeline |
| [0102](adr/0102-bounded-immutable-live-evaluation-save-recovery.md) | Bounded immutable Live evaluation-save recovery | Accepted SQL-only fix; native cause/capacity unverified | architecture, data-pipeline, risk-management |
| [0101](adr/0101-october22-portfolio-rules-only-review.md) | October 22 Portfolio rules only review | Accepted preparation only; operator approval/enable separate | architecture, risk-management, user-interface |
| [0100](adr/0100-live-entry-and-order-decision-traces.md) | Live entry and order decision traces | Accepted October 21; October 23 rejected-buy price comparison amendment | architecture, decision-engine, risk-management |
| [0099](adr/0099-exact-report-sharing-in-live-writes.md) | Exact report sharing in Live snapshot writes | Accepted October 21; offline validation in progress | architecture, data-pipeline |
| [0098](adr/0098-verified-snapshot-read-reuse-and-build-review.md) | Verified snapshot read reuse and dated build review | Accepted October 21; native acceptance pending | architecture, risk-management |
| [0097](adr/0097-reviewed-october21-portfolio-rules-only-source-switch.md) | Reviewed October 21 Portfolio rules only source switch | Accepted October 21; explicit operator approval/enable required | architecture, risk-management, decision-engine, user-interface |
| [0096](adr/0096-pre-open-october21-approval-and-armed-host.md) | Pre-open October 21 approval and armed execution host | Accepted October 21 | architecture, risk-management |
| [0095](adr/0095-current-readiness-without-daily-verification-replay.md) | Current approval without replaying completed broker verification | Accepted October 20; current-readiness continuation | architecture, risk-management |
| [0094](adr/0094-real-tfsa-execution-status-presentation.md) | Real TFSA execution and buying permission presentation | Scope accepted; detailed design draft for review before code | architecture, user-interface |
| [0093](adr/0093-narrow-read-only-broker-checks.md) | Narrowly approved broker-read helper | Accepted; installed and native read verified October 18 | architecture, data-sources |
| [0092](adr/0092-scoped-supervised-delphi-live-tfsa-session.md) | First supervised Delphi Live TFSA scope | Operator scope accepted; Core tested, not installed/activated; original qualifications retained | architecture, risk-management, market-microstructure |
| [0091](adr/0091-local-read-only-database-checks.md) | Fixed local read-only database checks | Accepted; installed and verified October 18 | architecture, data-pipeline |
| [0090](adr/0090-progressive-individual-profit-trailing.md) | Progressive trailing protection for individual winners | Accepted; installed/enabled for 15 active paper portfolios October 18; off-session preservation verified | risk-management, decision-engine |
| [0089](adr/0089-tmx-session-context-and-supplementary-records.md) | Stable TMX context, supplementary records and causal clock waits | Installed; observed-end/stale-source follow-up's first scheduled checks/current marks verified | data-pipeline, data-sources, time-series |
| [0088](adr/0088-source-symbol-aliases-for-monitoring.md) | Source symbol aliases for monitoring | Installed; symbol resolution and comparison-source adoption verified | architecture, data-sources |
| [0087](adr/0087-ai-momentum-portfolio-boundaries.md) | AI Momentum mandate and enforced protection | Budgeted proposals and learning consultation in source; evaluation, host integration and protection parameters pending | architecture, llm, risk-management |
| [0085](adr/0085-portfolio-wide-ongoing-tfsa-management.md) | Portfolio-wide ongoing TFSA management | Ongoing Core recovery and saved TFSA controls source/tested; native qualification, family coverage and execution host unfinished | architecture, risk-management, user-interface |
| [0086](adr/0086-supervised-broker-strategy-session.md) | Durable supervised broker strategy session | Source/offline tests and installed storage; launch NO-GO pending native qualification and rollout | architecture, risk-management, market-microstructure |
| [0083](adr/0083-opening-account-values-and-shared-engine-hosting.md) | Opening account values and shared engine hosting | Continuation and orchestration tested; native source qualification pending | architecture, data-sources |
| [0084](adr/0084-external-broker-trade-synchronization.md) | External broker trade synchronization | Installed; resolved-order native acceptance verified | architecture, data-sources, risk-management, user-interface |
| [0078](adr/0078-isolated-latest-source-nightly-builds.md) | Isolated latest-source nightly builds | Superseded by ADR-0122 for nightly preparation | architecture, data-pipeline |
| [0079](adr/0079-broker-reconciliation-and-market-handover.md) | Broker reconciliation and first market-order handover | Reconciliation implemented; execution pending | architecture, risk-management, market-microstructure, user-interface |
| [0080](adr/0080-reviewed-broker-history-import.md) | Reviewed broker history import | Implemented; migration and native linked refresh verified | architecture, risk-management, user-interface |
| [0082](adr/0082-compact-live-snapshots-and-backup-retention.md) | Compact Live snapshots and verified backup retention | Installed and verified September 17; bounded history reads remain | architecture, data-pipeline |
| [0081](adr/0081-broker-account-baselines-and-protection-ownership.md) | Broker account baselines and protection ownership | Initial setup/store implemented; migration 035 installed; operational baseline not established | architecture, risk-management |
| [0069](adr/0069-direct-wealthsimple-authentication.md) | Direct Wealthsimple authentication | Accepted; isolated browser-session handoff verified | architecture |
| [0070](adr/0070-wealthsimple-desktop-sign-in.md) | Wealthsimple desktop sign-in and session status | Implemented; embedded login and live refresh validation pending | architecture |
| [0071](adr/0071-native-wealthsimple-login.md) | Native Wealthsimple sign-in | Implemented; operator login confirmed | architecture |
| [0075](adr/0075-manual-market-buy-ticket.md) | Shared manual market-buy ticket and recoverable orders | Implemented; offline validation; native live order unverified | architecture, user-interface, risk-management |
| [0074](adr/0074-wealthsimple-market-buy-protocol.md) | Wealthsimple market-buy protocol and attempt recovery | Client implemented; WPF manual integration in ADR-0075 | architecture, risk-management, market-microstructure |
| [0073](adr/0073-permanent-tfsa-and-whole-share-execution.md) | Permanent TFSA and whole-share execution | Transition preview implemented; activation unfinished | architecture, risk-management, market-microstructure |
| [0072](adr/0072-wealthsimple-tfsa-account-display.md) | Read-only Wealthsimple TFSA landing page | Implemented; live account reads verified | architecture |
| [0068](adr/0068-main-account-trading-overview.md) | Main-account Trading overview | Accepted; workspace implemented; navigation follow-up verified offline | user-interface |
| [0067](adr/0067-fixed-gate-paper-comparison-pairs.md) | Fixed-gate paper comparison pairs | Accepted; implementation pending | architecture, decision-engine, risk-management, user-interface |
| [0063](adr/0063-historical-practice-environment-direction.md) | Historical practice environment direction | Accepted; first milestone implemented in source | architecture, data-pipeline, user-interface, machine-learning |
| [0061](adr/0061-portfolio-comparison-workspace.md) | Portfolio comparison workspace | Accepted navigation; descriptive display implemented | user-interface, architecture |
| [0001](adr/0001-granville-plugin-architecture.md) | Granville indicator plug-in architecture | Accepted | architecture, technical-indicators |
| [0002](adr/0002-xiu-as-benchmark-index.md) | XIU as the system benchmark index | Accepted | finance-fundamentals, decision-engine |
| [0003](adr/0003-weighting-indicator-narrow-advance.md) | Weighting indicator (Granville #15â€“#16) â€” narrow-advance warning gate | Accepted | technical-indicators, decision-engine, math-statistics, finance-fundamentals |
| [0004](adr/0004-genuity-us-confirming-indices.md) | Genuity indicators (Granville #17â€“#20) â€” US confirming-index source & staleness gate | Accepted | technical-indicators, decision-engine, data-sources, finance-fundamentals |
| [0005](adr/0005-defer-granville-dullness-21-22.md) | Defer Granville Dullness indicators (#21 and #22) | Accepted | technical-indicators, decision-engine, data-pipeline, math-statistics |
| [0006](adr/0006-granville-light-volume-25-28.md) | Granville Light Volume indicators (#25â€“#28) â€” tape Ã— leadership-quality | Accepted | technical-indicators, decision-engine, market-microstructure |
| [0007](adr/0007-liquidity-floor-universe-filter.md) | Liquidity floor on Delphi's universe filter (price â‰¥ $1, 20d vol â‰¥ 50k) | Accepted | decision-engine, market-microstructure, risk-management, data-pipeline |
| [0008](adr/0008-genuity-19-magnitude-tolerance-band.md) | Genuity #19 magnitude-ratio Â±5% tolerance buffer (refines ADR-0004) | Accepted | technical-indicators, decision-engine, math-statistics |
| [0009](adr/0009-exclude-leveraged-inverse-etps-from-delphi-universe.md) | Exclude leveraged/inverse ETPs from Delphi's ranking universe | Accepted | decision-engine, data-pipeline, risk-management, market-microstructure |
| [0010](adr/0010-rs-z-score-composite-additive.md) | RS Z-score composite (`CompositeScoreZ`) â€” additive only, no ranking change | Accepted | technical-indicators, decision-engine, math-statistics |
| [0011](adr/0011-rs-equal-weighted-with-direction-edge-in-ranking.md) | RS equal-weighted with `DirectionEdge` in Delphi pick ranking | Accepted (scoped to Breakouts lens by ADR-0013/0014) | decision-engine, technical-indicators, math-statistics |
| [0012](adr/0012-sector-index-historical-backfill.md) | Sector-index historical backfill from TMX `getTimeSeriesData` | Accepted | data-pipeline, technical-indicators, decision-engine |
| [0013](adr/0013-multi-lens-decision-architecture.md) | Multi-lens decision architecture (lens = thesis Ã— gate stack Ã— ranking key) | Accepted | architecture, decision-engine |
| [0014](adr/0014-continuations-lens-trend-confirmation.md) | Continuations lens â€” trend-confirmation gate + RS-primary ranking (executed) | Accepted | decision-engine, technical-indicators, risk-management |
| [0015](adr/0015-trade-logging-ghost-execution-and-position-lifecycle.md) | Manual trade logging with ghost execution and position lifecycle | Accepted | architecture, risk-management, market-microstructure |
| [0016](adr/0016-obv-per-symbol-soft-ranking-signal.md) | On-Balance Volume as a per-symbol soft ranking signal | Accepted | decision-engine, technical-indicators, data-pipeline, time-series |
| [0017](adr/0017-codex-native-instructions-and-project-status.md) | Codex-native instructions and explicit project-status structure | Accepted | architecture |
| [0018](adr/0018-manual-migrations-and-simple-recovery-backups.md) | Manual database migrations and SIMPLE-recovery backups | Accepted | architecture, data-pipeline, risk-management |
| [0019](adr/0019-delphi-strict-history-freshness-eligibility.md) | Delphi strict history-freshness eligibility | Accepted | decision-engine, data-pipeline, risk-management |
| [0020](adr/0020-immutable-calibration-evidence-ledger.md) | Immutable calibration evidence ledger | Accepted | architecture, data-pipeline, decision-engine, machine-learning |
| [0021](adr/0021-calibration-outcome-and-paper-execution-contract.md) | Calibration outcome and paper-execution contract | Accepted | architecture, math-statistics, market-microstructure, risk-management |
| [0022](adr/0022-champion-challenger-evidence-and-promotion.md) | Champion/challenger evidence and promotion | Accepted | decision-engine, machine-learning, math-statistics, risk-management |
| [0023](adr/0023-primary-swing-policy-and-experimental-opening-confirmation.md) | Primary swing policy and experimental opening confirmation | Accepted | architecture, decision-engine, market-microstructure, risk-management |
| [0024](adr/0024-coverage-scorecard-and-market-session-cohorts.md) | Coverage scorecard and market-session cohort identity | Accepted | architecture, data-pipeline, decision-engine, math-statistics |
| [0025](adr/0025-three-session-swing-mark-to-market-outcome.md) | Three-session swing mark-to-market outcome | Accepted | architecture, math-statistics, market-microstructure, risk-management |
| [0026](adr/0026-three-session-swing-excursion-measures.md) | Three-session swing excursion measures | Accepted | architecture, math-statistics, market-microstructure, risk-management |
| [0027](adr/0027-lens-tradeability-scorecards-and-cohort-aggregation.md) | Lens tradeability scorecards and cohort aggregation | Accepted | architecture, decision-engine, math-statistics, risk-management |
| [0028](adr/0028-delayed-intraday-swing-monitor-and-exit-policy.md) | Delayed intraday swing monitor and exit policy | Accepted | architecture, data-sources, market-microstructure, risk-management |
| [0029](adr/0029-intraday-ghost-entry-pilot.md) | Intraday ghost-entry pilot | Accepted | architecture, market-microstructure, risk-management |
| [0030](adr/0030-intraday-evidence-and-poll-audit-ledger.md) | Intraday evidence and poll-audit ledger | Accepted | architecture, data-pipeline, data-sources, market-microstructure, risk-management |
| [0031](adr/0031-automatic-ghost-exit-execution.md) | Automatic policy-triggered ghost exit execution | Accepted | architecture, market-microstructure, risk-management |
| [0032](adr/0032-live-paper-trading-wpf-dashboard.md) | Live paper-trading WPF dashboard | Accepted | architecture, data-pipeline, risk-management |
| [0033](adr/0033-tabbed-desktop-shell-and-shared-workflows.md) | Tabbed desktop shell and shared application workflows | Accepted | architecture, data-pipeline, risk-management |
| [0034](adr/0034-delphi-shared-workflow-and-desktop-tab.md) | Delphi shared workflow and desktop tab | Accepted | architecture, data-pipeline, decision-engine, risk-management |
| [0035](adr/0035-delphi-operator-workspace-and-presentation-snapshot.md) | Delphi operator workspace and presentation snapshot | Accepted | architecture, data-pipeline, decision-engine |
| [0036](adr/0036-native-project-documentation-reader.md) | Native read-only project documentation reader | Superseded by ADR-0112 | architecture |
| [0037](adr/0037-operator-confirmed-delphi-paper-entry.md) | Operator-confirmed Delphi pick to paper position | Accepted | architecture, market-microstructure, risk-management |
| [0038](adr/0038-cohort-weighted-official-prediction-scorecards.md) | Cohort-weighted official prediction scorecards | Accepted | architecture, decision-engine, machine-learning, math-statistics |
| [0039](adr/0039-unified-ghost-real-dashboard-and-scorecard-workspace.md) | Unified Ghost/Real dashboard and scorecard workspace | Accepted | architecture, data-pipeline, market-microstructure, risk-management |
| [0040](adr/0040-reconstructible-delayed-intraday-outcome-fill.md) | Reconstructible delayed-intraday outcome fill | Accepted | architecture, data-pipeline, market-microstructure, math-statistics, risk-management |
| [0041](adr/0041-date-aligned-relative-strength-inputs.md) | Date-aligned relative-strength inputs | Accepted | decision-engine, data-pipeline, time-series, technical-indicators |
| [0042](adr/0042-strategy-code-identity-and-scoped-official-evidence.md) | Strategy/code identity and scoped official evidence | Accepted | architecture, data-pipeline, decision-engine, math-statistics |
| [0043](adr/0043-preserve-leadership-source-missingness.md) | Preserve leadership-source missingness | Accepted | data-pipeline, data-sources, decision-engine, technical-indicators |
| [0044](adr/0044-unlinked-real-position-monitoring.md) | Unlinked Real position monitoring | Accepted | architecture, data-pipeline, market-microstructure, risk-management |
| [0045](adr/0045-five-minute-collection-and-nonoverlapping-intraday-chunks.md) | Five-minute collection and non-overlapping intraday chunks | Accepted | architecture, data-pipeline, data-sources, market-microstructure, risk-management |
| [0046](adr/0046-guarded-nightly-operations-runner.md) | Guarded nightly operations runner | Accepted | architecture, data-pipeline, decision-engine, risk-management |
| [0047](adr/0047-discretionary-real-entry-from-non-buy-row.md) | Discretionary Real entry from a non-Buy Delphi row | Accepted | architecture, decision-engine, market-microstructure, risk-management |
| [0048](adr/0048-consequential-operational-transaction-boundaries.md) | Consequential operational transaction boundaries | Accepted | architecture, data-pipeline, risk-management |
| [0049](adr/0049-canonical-database-write-contracts.md) | Canonical database write contracts | Accepted | architecture, data-pipeline, risk-management |
| [0050](adr/0050-build-only-ssdt-ci-boundary.md) | Build-only SSDT CI boundary | Accepted | architecture, data-pipeline, risk-management |
| [0051](adr/0051-system-shadow-portfolios.md) | System-selected Shadow V1 portfolios | Accepted | architecture, data-pipeline, decision-engine, market-microstructure, risk-management |
| [0052](adr/0052-working-tree-state-is-provenance-not-evidence-quality.md) | Working-tree state is provenance, not evidence quality | Accepted | architecture, data-pipeline, decision-engine |
| [0053](adr/0053-delphi-live-v1.md) | Delphi Live V1 | Accepted | architecture, data-pipeline, decision-engine, machine-learning, market-microstructure, risk-management, technical-indicators |
| [0054](adr/0054-delphi-live-preview-and-historical-replay.md) | Delphi Live preview and historical replay | Accepted (exploratory execution estimates) | architecture, data-pipeline, market-microstructure |
| [0055](adr/0055-independent-strategies-to-approved-live-execution.md) | Independent strategies to approved live execution | Accepted direction; implementation incomplete | architecture, decision-engine, risk-management, market-microstructure |
| [0056](adr/0056-dated-profit-model-input-contract.md) | Dated profit-model input contract | Accepted; operationally adopted | architecture, machine-learning, data-pipeline, decision-engine |
| [0057](adr/0057-preserved-model-sets-and-explicit-selection.md) | Preserved model sets and explicit selection | Accepted; supported daily workflow validated | architecture, machine-learning, data-pipeline, decision-engine |
| [0058](adr/0058-daily-ingestion-and-observed-benchmark-confirmation.md) | Daily ingestion and observed benchmark confirmation | Accepted; implemented and operationally verified | architecture, data-pipeline, decision-engine |
| [0059](adr/0059-delphi-model-and-threshold-settings.md) | Delphi model-set and threshold settings | Implemented in source; operational selection pending | architecture, decision-engine, machine-learning, user-interface |
| [0060](adr/0060-central-settings-and-scoped-configuration.md) | Central settings and scoped configuration | Accepted direction; broader implementation pending | architecture, user-interface, decision-engine |
