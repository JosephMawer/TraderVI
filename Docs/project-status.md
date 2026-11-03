# TraderVI Project Status

Last reconciled **November 3, 2026**. This page records the latest dated evidence and its limits;
it is not a live health check or authority to trade. [Roadmap](roadmap.md) owns priority;
[execution checklist](strategy-alignment-implementation-checklist.md) and
[Live checklist](delphi-live-implementation-checklist.md) own remaining acceptance work.
Design and authority are governed by [design rules](design-rules.md) and applicable ADRs.

## Operating state and immediate risks

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
  managed holdings. The separate Paper recovery restriction remains.
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
- **Paper recovery and inputs:** The Paper recovery restriction retained after the October 30 Real
  recovery was still the sole unresolved incident at 21:24 Toronto on November 2. At 15:12 seven
  Paper/dependent-Research workers reported blocked. At 21:25 five had reported blocked since restart.
  [Current read-only diagnosis](reviews/wealthsimple-auth-and-engine-diagnosis-20261102.md).
  A separate Paper-input failure was traced to missing verified holding correspondence in
  the service wiring; repair is planned in issue #24, not established by that diagnosis. The Real
  recovery does not clear Paper restrictions. [Diagnosis](project-status-history-20261101.md#october-30--paper-inputs-cause-confirmed-without-restart-10181020-toronto),
  [roadmap](roadmap.md).
- **Collector and minute data:** The independent collector runs separately from the trading engine.
  Migration 057 and the latest-minute collector were installed November 1; closed-session checks and
  a native SQL/mock-HTTP package check passed. The first market-open provider response under this
  policy, coverage/capacity and venue qualification remain unverified. The Real strategy remains
  five-minute; the one-minute publisher is staged, and financial adoption requires separate review.
  [Evidence](project-status-history-20261101.md#november-1--latest-minute-collector-running-trading-engine-retained),
  [acceptance work](strategy-alignment-implementation-checklist.md#independent-collector-and-recovered-confirmation--october-27-overnight).
- **Nightly:** The task was changed November 1 to 06:30 Toronto local time daily and selects verified
  existing outputs for Hermes, DataAudit, Delphi and Athena. Successful Windows Release builds register
  per-app outputs for future selection. Preflight and offline tests passed; the first scheduled run
  under this configuration remains pending. Selected output directories are runtime dependencies and
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
protection, Paper recovery and symbol resolution, complete market-data/valuation coverage, native
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
