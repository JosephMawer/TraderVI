# TraderVI Project Status

Last reconciled **November 4, 2026**. This page records the latest dated evidence and its limits;
it is not a live health check or authority to trade. [Roadmap](roadmap.md) owns priority;
[execution checklist](strategy-alignment-implementation-checklist.md) and
[Live checklist](delphi-live-implementation-checklist.md) own remaining acceptance work.
Design and authority are governed by [design rules](design-rules.md) and applicable ADRs.

## Operating state and immediate risks

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
  This repair is **not installed**. At the last November 4 investigation, the broker buy was filled
  and applied to compact storage while the managed head still marked it unreconciled. Ordinary build
  review requires those records to agree. A separately authorized restored-copy rehearsal passed the
  actual SQL handoff, simulated review and fresh-process reload, preserving all four holdings, original
  fill and protection without a broker client. Rehearsal regression passed **3,695 tests, 18 opt-in skips,
  zero failures**; the three native phases passed separately. The private instance is stopped; its
  33.8 GB of database/backup files remain retained for explicit cleanup. The guarded live original-buy
  maintenance command is now implemented and qualified on a fresh restored copy using the exact Release
  Core: maintenance, review and reload pass. Release regression: **3,707 passed, 18 opt-in skips, zero
  failures**; WPF/engine build passed with two existing CS0168 warnings. Installation is now authorized
  and being prepared; exact-build approval and separate resume remain operator actions.
  Native protection and fresh market-hour Paper/Real
  acceptance remain open. The Real
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
  per-app outputs for future selection. November 4 inspection confirmed the scheduled 06:30–06:37 run
  completed all four stages for November 4, overall Attention; Windows recorded zero missed runs and
  the next occurrence November 5 at 06:30. At Joseph's request, one preflight-verified forced rerun ran
  08:55–08:57 with the existing outputs. Delphi and Athena succeeded; Hermes still reported two missing
  daily-bar sources, and DataAudit retained one error/ten warnings (no new or resolved findings).
  Today's valid Delphi preparation uses November 3 market data. Athena's earlier one invalid prediction
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
