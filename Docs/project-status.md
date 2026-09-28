# TraderVI Project Status

**Current record:** September 28, 2026. Faster prices now combine with verified cash and all holdings for both paper-account families in source/tests. The reader rejects changing accounts and missing, stale or differently timed prices, and retains source evidence. Durable risk-journal integration and native qualification are next; no operating freshness policy or AI loop is enabled. Earlier raw-response/trade checks support quiet trading as the explanation for sampled minute gaps, not exhaustive market coverage. Existing saved five-minute prices still have multi-minute delay and collection gaps. Current cash-only Shadow verification is built/tested; the alias repair remains installed with source adoption recovered. The consolidated ten-step checklist tracks the three delivery milestones without duplicate AI task lists.
Desktop controls and saved account refreshes were observed September 25; operational evidence retains
its original dates below.

## Executive summary

TraderVI runs independent paper strategies and has a connected Wealthsimple TFSA with saved
broker history. **Automatic real strategy trading is off; September 23 launch is NO-GO.** The operator
selected the ungated Breakout three-position portfolio as an initial review candidate, then clarified
that any portfolio must be promotable with ongoing management until pause or switch (ADR-0085).
Broker projection, setup UI, synchronization, protection handover and durable supervised execution
are combined with the main-checkout reliability fixes and tested offline. Storage migrations
035/039/040 are installed; their verification dates remain below.
Qualified native risk evidence, portfolio-wide ongoing management, native order
qualification and supervised host rollout remain unfinished. The root goal is one deliberately approved complete strategy
managing the permanent TFSA while paper and real histories remain independent.

Use the [roadmap](roadmap.md) for priorities and the [implementation checklist](strategy-alignment-implementation-checklist.md)
for dependencies and next actions. [Design rules](design-rules.md) own behavior; this page records
what has actually been implemented, installed or observed.

**Delivery checkpoints, September 28:** the existing ten-step checklist now groups delivery into a
[visible AI reviewer, complete tested paper trader, and supervised live trading](strategy-alignment-implementation-checklist.md#delivery-milestones).
All three are pending; the reviewer is the next visible target and Step 3 remains the immediate work.
Duplicate AI/learning task lists are consolidated into that sequence; completed price investigations
link to the dated evidence below. This planning clarification changes no implementation, validation
result or operational readiness.

## Latest operational evidence

| Area | Last evidence (Toronto) | Verified result and limit |
|---|---|---|
| Missing-minute source/trade diagnosis | Sep 28, 14:12 and 14:21 Toronto | Authorized raw-response probes for five held symbols plus XIU. Final sample covers 09:30–14:20: 542/1,740 requested minute slots absent at source, zero null/incomplete rows; the client filter caused none of these gaps. All 207 sparse five-minute groups match summed minute volume; all 130 complete groups match OHLCV. Of 150 recent trades, 105 fall within the completed window across 24 minutes; all have bars. Nineteen comparable trade-minute volumes match; 32 absent minutes lie between reported trade times. Latest gaps for five targets remain absent on narrow rerequest. This supports no reported trading in sampled gaps, not exhaustive venue coverage or production qualification. Final run: 23 requests/attempts, held-symbol set unchanged, no SQL/account/risk/order writes or restart. Evidence: `artifacts/ai-risk-values-20260928/missing-minute-trades-native.log`; first raw-only run: `missing-minute-native.log`. |
| Held-stock one-minute source | Sep 28, 13:25–13:29 Toronto | Authorized read-only probe sampled five held symbols plus XIU across five polls, then compared intervals and retried gaps: 44 requests/transport attempts. Latest-minute held coverage was 3/5, 4/5, 3/5, 4/5, 4/5; held price ages were 10.067–370.298 seconds after bar end. Zero revisions/disappearances across 188 repeated completed-bar comparisons. Four held symbols retained historical gaps; their first missing minutes remained absent on both broad and narrow rerequests. All seven sparse five-minute groups had matching summed minute volumes; all five complete groups matched OHLCV exactly. This suggests, but does not establish, no-trade minutes. Final held-symbol set unchanged; account consistency and production freshness remain unqualified. No SQL/account/risk/order writes or app restart. Evidence: `artifacts/ai-risk-values-20260928/held-minute-native.log`. |
| Candidate one-minute price source | Sep 28, 13:13–13:17 Toronto | Authorized read-only XIU probe completed five requests/transport attempts, one per minute. Latest completed prices were 10.069–10.582 seconds old at receipt; all four subsequent polls advanced exactly one minute. Zero revisions/disappearances across 38 repeated completed-bar comparisons. Two requested historical minutes were absent throughout (8/10 completed minutes initially, 12/14 finally); cause is not established. Forming bars were excluded. This small benchmark sample does not qualify held stocks, continuous age bounds, publication latency before the poll, or production freshness limits. No SQL/account/risk/order writes or app restart. Evidence: `artifacts/ai-risk-values-20260928/one-minute-native.log`. |
| AI stock-price freshness | Sep 28, SQL 12:40 and 12:56; account reads 12:55 | At 12:40, successful V3 five-minute receipts in the preceding hour arrived 144.722–158.396 seconds after bar end. The 12:55 risk-reader check reconciles all 15 books; two invested Shadow values are 338.3–338.4 seconds old and one Live value is 339.5 seconds old. None is within 60 seconds; this is a diagnostic comparison, not a newly approved production limit. Current sources do not establish one-minute coverage. The 12:56 window includes 378 usable mature Live slots, 23 without completed bars and nine failed slots; one cycle was cancelled with completion code `Failed`, leaving eight unfinished slots. Its underlying exception is not established by these receipts. The session retains `Blocked`/`HostGapObserved`; twelve V2 fifteen-minute receipts also report `CompletedBarConflict`. No external collection, SQL writes or restart. Evidence: `artifacts/ai-risk-values-20260928/price-freshness-*`. |
| AI paper risk-value readers | Sep 28, native reads 11:47 | All 12 Shadow and three Live books map and reconcile cash/holdings/prices; none is missing. Ten Shadow cash-only books carry current transaction verification receipts; two invested Shadow books and one invested Live book retain price/checkpoint evidence. All 15 meet the diagnostic ten-minute age/session check. Altered cash, wrong account, missing/incomplete marks and expired-value allowance are rejected in memory. This is a bounded reader check, not production feed or recurring host qualification. No SQL/journal writes, AI calls, broker calls or app restart were performed. Evidence: `artifacts/ai-risk-values-20260928/cash-verification-native.log`. |
| Live symbol-alias repair / comparison adoption | Sep 28, installation 11:17; SQL through 11:22; native readers 11:23 | Authorized migration 041 installed after a checksum-verified 1,121.07 MB COPY_ONLY backup and SHA-256-matching local copy in the OneDrive folder; cloud synchronization and a restore drill were not verified. Before/after counts and content hashes match across eight checked account/history tables. Restarted PID 32308 is responsive and loaded Core/WPF hashes match source build `8106499a`. Today's session froze successfully, canonical tracked membership exists while the original position symbol remains unchanged, all eight comparison Shadow sessions are Active with adopted sources, and all three Live ledgers have today's session. The common source became effective at 11:20; a fresh Live cycle completed at 11:22:26 and saved evaluations/ledger updates. Coverage remains Blocked with HostGapObserved; missed intervals were not backfilled as valid observations. The final native AI reader test passes for 12 Shadow and three Live contexts; none grants a selectable proposal. Private receipts: `artifacts/symbol-alias-install-20260928/`; earlier diagnosis: `artifacts/ai-source-investigation-20260928/`. |
| Nightly pipeline | Sep 24, completed 00:36:48; SQL checked 09:01 | Scheduled current-source build and all three stages completed. Hermes inserted 483 daily bars with zero collection failures; Delphi published Valid using Sep 23 data. Athena wrote 864 valid outcomes and four invalid older `v3.0` 20-session outcomes (`MissingSymbolSession`), producing Attention/exit 2. No recovery rerun was needed. |
| Delphi inputs | Sep 24, 09:01 | Daily identity is `v3.4-observed-benchmarks`. Publication: 211 candidates, 422 lens rows, zero source-observation mismatches, 25 Hold rows per lens blocked by Breadth. All 483 active symbols, XIU/SPY and checked derived series reach Sep 23. ALYA is excluded for an invalid older feature bar; fresh endpoint coverage does not prove complete historical inputs. Publication is not buy eligibility. |
| Broker connection/history | Sep 25, screenshot 16:40:55; SQL 17:02:22 | Operator confirms the integrated Strategy management panel in the normal checkout. The screen reports successful account/history and trade synchronization. Eight recent SQL imports from 16:18–16:57 each have a trade-sync receipt, spaced about 5m30s apart. This bounded sample supports recurring persistence, not uninterrupted coverage, broker rate limits or cash-event qualification. |
| Strategy controls and setup | Sep 25, SQL 17:02:22 | One account binding; zero baselines, continuations or managed-session revisions. All three risk/session immutable triggers remain enabled. The screenshot's no-approved-strategy state and disabled stop/resume buttons agree with storage. The account setup dialog and recovery of a populated real assignment remain unverified. |
| External-trade synchronization | Sep 24, migration 10:41; corrected desktop 10:59; repeat SQL 11:01; UI 11:02 | Authorized migration 040 installed after a fresh checksum-verified COPY_ONLY backup and SHA-256-matching local mirror. Financial records were preserved by migration. Corrected Core/WPF loaded with matching hashes; fresh broker reconciliation saved the reported exit exactly once and closed its tracked position. Later automatic refresh retained identical trade counts; the UI reports successful synchronization and current recorded holdings exclude the exited symbol. Original trades, binding and account reports retain exact hashes. One Live lease resumed. Evidence: `artifacts/broker-sync-20260924/`; this does not establish continuous coverage, cloud backup durability or complete cash-event/risk inputs. |
| Storage and desktop | Sep 17, installed and observed | Compact snapshot migrations 037/038 and UI-worker fix installed. All 4,472 historical/current snapshots verified; subsequent compact writes observed. Full-history reconstruction still consumes background CPU/memory. |
| WPF memory investigation | Sep 24, 09:08:49–09:10:45 pre-install sample | Previous Release PID 24492 responded in all 24 samples: 2.58–3.75 GB working set and 3.25–4.90 GB private committed memory (decimal GB). Full-history JSON expansion and separate display snapshots were identified as likely contributors; no runtime allocation/heap attribution was captured. Superseded by the authorized installation below. Private evidence: `artifacts/wpf-memory-20260924/`. |
| WPF memory-fix installation | Sep 24, restarted 09:30:32; SQL checked 09:32:09; process sampled through 09:33:14 | Normal close of PID 24492, five backed-up/replaced binaries with matching SHA-256, and one new Release PID 31052 loading the installed Core/WPF modules. All 24 post-restart samples responded: 0.66–1.33 GB working set and 0.57–1.32 GB private committed memory. This short fresh-process sample is not a matched workload or sustained memory bound. One renewed Live lease, nine new compact revisions/events and three updated portfolios confirm runtime resumption. Session is Monitoring with Blocked coverage and a host gap; uninterrupted collection is not established. Evidence: `artifacts/wpf-memory-20260924/installation.md`. |
| Database allocation | Sep 23, 23:55 verified | User-authorized one-time file shrink reclaimed 54.88 GiB. Data file is 16 GiB with about 6.40 GiB used; log is 1.008 GiB. Full integrity checks before/after found zero errors; all 89 application tables retained their exact row counts (1,210,867 total). Database remains ONLINE, SIMPLE, CHECKSUM, with automatic shrink off. |
| Build storage cleanup | Sep 23, 21:53 verified | User-authorized one-time removal of 11,514 old compiled files reclaimed 29.66 GiB. Artifacts now total about 7.03 GiB. All 23,438 retained inventory files have unchanged sizes/timestamps; 48 model/archive and source-manifest hashes match. Latest two nightly builds, package cache, recent work and worktree retained. |
| Backups | Sep 24, nightly receipt; hashes checked 09:03 | Hermes created a 1,091,843,072-byte full backup and recorded successful SQL checksum verification; independent morning SHA-256 reads match both local files and the runner receipt. Routine retention removed the Sep 22 standard backup in both locations. The separate Sep 23 pre-shrink COPY_ONLY checkpoint remains. Its earlier IN_SYNC observation does not establish cloud synchronization of today's backup; independent download and test restore remain unverified. |
| Delphi Live | Sep 23, 00:21:18 read of Sep 22 session | A responding WPF process is open (started Sep 22, 22:16:53). The last three Live cycles, through the 16:00 bar, each had 40/40 usable receipts; final collection completed at 16:02:12. All three portfolios saved complete 16:00 values. No current collection lease at this pre-open check; last stored heartbeat Sep 22, 16:06:34. Source does not acquire the collection lease before the open. Today's market-session operation is not yet verified. |
| System Shadow | Sep 22, 09:59 | All 12 sessions adopted that day's publication; all 13 open positions advanced to the bar ending 09:55. Not rechecked in the Sep 23 Live-only inspection. |
| Live limitations | Sep 23, 00:21 read of Sep 22 session | Session remains Incomplete/Blocked with a host gap; one comparison portfolio still lacks opening NAV (net asset value), despite a complete closing value. No clean full-session qualification. The tracked 15-minute conflict last observed Sep 22 at 09:59 was not rechecked. |
| Broker risk/session storage | Sep 22, 23:34:11 | Authorized migrations 035/039/040 installed after a fresh verified backup/copy. Three empty tables, three enabled immutable triggers, eight trusted foreign keys, three trusted checks and six native zero-row update/delete rejections verified. Linked binding/import and report/promotion counts and hashes unchanged. No baseline/session created. |

Private evidence directories: `artifacts/nightly-readiness-20260924/`, `artifacts/nightly-readiness-20260922/`, `artifacts/app-health-20260922/`,
`artifacts/broker-import-20260916/`, `artifacts/live-storage-20260917/`, and `artifacts/ui-freeze-20260917/`.
Detailed dated accounts remain in the [status history](project-status-history-20260922.md#executive-summary).

Storage inventory: private `artifacts/disk-audit-20260923/`. Subsequent SQL maintenance evidence is in
`artifacts/sql-storage-reclaim-20260923/report.md`: verified backup, physical file shrink, full integrity
checks and exact per-table counts. About 9.60 GiB remains reusable inside the data file for growth.
No application records/schema were deleted or changed by the maintenance scripts; row counts and
integrity checks do not constitute an independent byte-for-byte comparison or a restore drill.
Cleanup evidence: `tradervi-cleanup-verification.json` and `tradervi-build-cleanup.md` in that private
directory. Old diagnostic/nightly compiled output is now incomplete and must be rebuilt before reuse;
source snapshots, manifests, logs, captures, model files and current development outputs were retained.
Recurring build/artifact retention remains [open](reviews/open-questions.md#nightly-build-retention-adr-0078-2026-09-14).
The separately authorized one-time SQL shrink is complete; automatic shrinking remains disabled.
No application/build/test workflow, model operation, service restart or market/broker request accompanied
these storage operations. SQL backup/checkpoint/file maintenance ran only in the later database step.

## Implemented versus operational

| Capability | Current boundary |
|---|---|
| Paper strategies and manual recommendation selection | Implemented; migrations through the comparison/selection milestones are installed. Recommendation approval under ADR-0062 does not submit broker orders. |
| Broker history | Migration 034 installed; permanent account link and native refresh verified. Existing paper/legacy records remain independent. |
| Initial real-account risk references | Preparation/save/recovery and an account setup screen implemented in the integrated checkout. Migration 035 installed; no initial operational baseline. Native setup-dialog review and baseline/continuation repository recovery remain unverified. |
| Daily risk continuation | Boundary valuation/references/holds tested. Broker orchestration calls the existing continuation workflow. Migration 039 installed; qualified native source and operational verification absent. |
| Cash-flow continuation | Accepted percentage preservation implemented/tested. Broker projection adopts the continued reference and preserves holds. Complete event/effective timing and matched native books remain unqualified; the default source blocks new risk. |
| Broker inputs and execution | Exact comparison profile, real position cycles/costs, synchronization, preserved protection, immutable approval/order intent and restart recovery implemented/tested. Production composition reuses existing clients, journal and gates. Migration 040 installed. No qualified execution host, native mutation evidence or automation activation. |
| Portfolio-wide ongoing promotion | Sep 23 source/tested: explicit Ongoing approval retains assignment identity across checked Daily Shadow sessions, continued references, protection and original orders. Existing one-day approvals still expire; cumulative purchase ceilings never refill automatically. Recovery reads missed prior-day bars and preserves closing-exit obligations; missing required closing evidence blocks further orders. Role separation, replacement, Delphi Live adapter and continuation after in-app manual fills remain unfinished. Unsupported families are rejected before approval can trigger exits. |
| Strategy stop/restart control | Sep 23 source/tested: saved purchase control plus TFSA panel display/buttons. Sep 25 native panel/no-assignment state observed in the normal checkout. Startup and recurring linked-account refresh restore saved strategy/mode in tests; populated real-assignment recovery remains unverified. Stale reads, logout, changed assignment and uncertain saves cannot enable controls. The panel starts no execution loop. Current build identity is checked before all managed orders; Market sell all remains under ADR-0077. No activation. |
| Broker background synchronization | Sep 25 source/tested: shared account/history read plus atomic resolved-trade projection, five-minute cadence, manual/post-trade refresh, coalescing and 5/10/20/30-minute failure backoff. Native combined refresh/persistence observed over eight imports about 5m30s apart. The moving-market-value fix and dependent-view updates are retained. Broader rate, failure, partial-order and restart coverage remain unqualified. |
| Hosting | Nightly 00:30 schedule and 07:00 supervision were installed previously; Sep 22 required recovery. Intraday monitoring depends on the desktop/console host. Standalone pre-session hosting is future work, with one active owner. |
| External broker trade synchronization | Migration 040 and corrected desktop installed and natively verified Sep 24 under ADR-0084. Resolved orders project atomically into real trades/positions with immutable receipts and dependent-view refresh. Reported exit, historical completion, repeat-refresh duplicate prevention and current-holdings presentation verified. Polling requires the app running and signed in; working partial orders and unsupported/corrected facts remain blocked. |
| WPF snapshot memory | Sep 24 direct typed reads, shared report strings and streaming fingerprint checks implemented/tested under ADR-0082, installed with explicit restart authority at 09:30:32. Loaded-binary hashes and resumed compact writes verified. Full lifetime collections/repeated reads and sustained memory/capacity qualification remain (S4). |

## Verified development baseline

September 25 integration: **1,729 Core tests passed in Release**. The combined WPF build passes
with 261 existing compiler warnings and zero errors; the SQL project builds separately with Visual
Studio SSDT. No full-solution health claim or fresh dependency-security audit. Source changes combine
`a8aae80` (strategy work) and `7faa6eb` (main-checkout reliability/broker synchronization).
The root command builds its own checkout; separate worktrees are not included until merged.

The shared refresh keeps ADR-0084's single reconciled automatic save and atomic trade projection,
with ADR-0085's five-minute scheduling, cancellation and saved strategy controls. Tests cover moving
market values, concurrent ledger changes, terminal partial fills/replays, failed reads, changed logins,
coalesced refreshes, stopped buying, ongoing recovery and original uncertain order IDs. The September
24 memory reconstruction and stale-ledger recommendation fixes remain in the combined source.

Evidence in the isolated worktree: `artifacts/launch-validation/integrated-tests.log`,
`integrated-wpf.log` and `integrated-sql.log`. The [activation review](reviews/supervised-session-20260923.md)
and [normal source-run instructions](running.md#build-and-run-the-local-desktop-checkout) distinguish
source integration from a fresh launch and native account verification. Earlier 1,702-test strategy
and 1,622-test external-trade checkpoints remain dated evidence, not proof of this combined runtime.
Memory/native synchronization evidence remains under the root checkout's
`artifacts/wpf-memory-20260924/` and `artifacts/broker-sync-20260924/`.

Migrations retain their exact applied contents, including the two distinct full filenames ending in
040: `20260923_040_AddBrokerManagedSession.sql` and `20260924_040_AddBrokerTradeSync.sql`.
No migration, broker request/order, application restart or automation activation ran during integration.
The managed-session ADR is now [0086](adr/0086-supervised-broker-strategy-session.md), resolving its
independent numbering collision with external-trade ADR-0084 without changing either decision.
Storage 035/039 and managed-session 040 installation evidence remains September 22 at 23:34;
the older main-checkout statements that 035/039 were uninstalled were stale branch summaries.
The September 25 isolated SQL managed-session rehearsal now verifies precise readback, competing
leases, interrupted append rollback, saved buying mode, original-order recovery and partial exits
before purchases. This narrow increment passed 71 focused offline cases and one opt-in SQL case;
the full-suite/build evidence above belongs to the earlier integration. Baseline/continuation
repository rehearsal, qualified risk evidence and operational setup remain unverified. Scope and
reproduction instructions are in the [activation review](reviews/supervised-session-20260923.md#native-desktop-and-isolated-sql-recovery--september-25).

## Known gaps and risks

**September 27 implementation checkpoint:** Step 2's core framework is agreed and sufficient to
build. Portfolio scope is now agreed: all current strategy portfolios and their gate/position-count
variants, expanding as additional portfolios are built and integrated (ADR-0087). Catalogue mapping
is implemented offline; native mapping and adapter qualification remain implementation work. The operator deliberately defers lesson criteria
until needed for Step 6; continue delivery without reopening that topic now (ADR-0087).
Operational evidence dates remain unchanged.
Step 4 has begun with `Core/Trader/Risk` and
`AiPortfolioRiskPolicyTests`: shared assessment/profile contracts, continuous round transitions,
trailing peaks/allowance, failure/closure and calendar-aware fresh-entry restart. Independent
holds and unavailable AI suppress new allowance while failed-round closure remains required.

`AiPortfolioRiskJournal` now persists accepted commands and states with account/portfolio/paper-live
scope, single-writer ownership and explicit initialization. Replay-validated recovery preserves round
identities, peaks, allowance inputs and closure/sequence pauses; stale/conflicting commands and
missing/corrupt/interrupted history cannot reset them. Existing overall holds and stock stops remain
outside this journal. No production directory or host was configured.

Step 3 has three offline source adapters: `BrokerAiPortfolioRiskMonitor`,
`DelphiLiveAiPortfolioRiskMonitor` and `SystemShadowAiPortfolioRiskMonitor`. A shared observation
writer qualifies input, persists the transition and assesses risk. Rejected readings suppress buying
without erasing a required closure; retries do not append history or renew freshness. Source evidence
and exact account/generation/policy/gate identities are retained. Paper guards add independent blocks;
ordinary engines and their ledgers are unchanged. Paper and broker journals reject crossed scope.

Delphi Live uses its latest complete checkpoint matching current holdings, cash and whole-share fills;
opening-only, incomplete and post-checkpoint trade books are rejected. Such trades require a later
complete checkpoint. Daily Shadow requires a coherent complete book plus identified exact five-minute
source bars: stored `LastPriceEventUtc` denotes a bar start for monitor updates but a fill timestamp
during fills. Neither that field nor the overview's freshest holding time alone establishes a current
whole-portfolio value. Fixed paper capital reconciles to fills or full position costs/realized results;
capital/generation substitution and rewritten completed history are rejected.

`PaperAiPortfolioRiskReader` now connects saved paper records to those checks through read-only
interfaces. Delphi Live reuses its complete saved snapshot reader. Daily Shadow's new repository
method reads generation, portfolio, session, all open/closed positions, assigned or fixed comparison
rules, and existing source bars in one read-only serializable SQL transaction. It requires matching
saved settings and bars, declines mixed-time books and post-bar fills (including additions), and
retains stable evidence identity and saved timestamps across reads. Missing/invalid records block
new risk; source errors and cancellation propagate without initializing/resetting a round. Freshness
is checked after I/O. No host is scheduled and no market collection is introduced.

`AiPortfolioCatalogRepository` now reads the maintained Shadow/Delphi Live accounts, including fixed
gate variants, with exact daily/model/policy/implementation identities. It reads definitions only;
no model files or candidate account balances are included. Missing/inconsistent definitions remain
visible as unavailable choices. There is no fixed 15-account list. `AiPortfolioPacketBuilder` combines
these choices with a fresh accepted observation from the managed account's current journal. It
preserves source evidence and risk history, requires matching fresh engine context for selectable
choices, bounds packet expiry by its inputs, and accepts no unknown active assignment or conflicting
identities. Missing AI review can prompt a proposal; other saved holds cannot be bypassed. Packets
use the existing reviewer/learning contract and confer no execution authority.

September 28: `AiShadowDecisionRepository` and `AiShadowDecisionContextReader` connect saved Breakout/
Continuation sources to review packets. A SELECT-only transaction reads the adopted publication,
exact assigned/fixed policy, current-revision candidate decisions and a caller-bounded daily history.
The adapter verifies daily/model/policy/gate identity and the reviewed preceding session. Published
rankings and first-failure gate traces remain unchanged; fixed bypass is separately labelled.
Known trading gates and entry/rotation guidance are discretionary; data/account/protection boundaries
and unknown gates cannot be waived. Saved decision times bound expiry; rereads and later performance
records cannot renew it. Missing/stale/partial or data-blocked decisions cannot create selectable
context. No admitted/evaluated candidates also remain unavailable in this first adapter.
Daily paper returns reuse `PortfolioDailyPerformance`, retaining nulls/conflicts and disclosing that
history can span earlier policies. These are candidate-account observations, never managed funds,
stock-action authority or post-closure entry confirmation. Shadow raw-bar/conflict requalification,
production history/freshness settings and runtime composition remain open.

`AiLiveDecisionRepository` and `AiLiveDecisionContextReader` now add Delphi Live saved context through
one SELECT-only transaction: exact account/generation/revision, validated stored policy and daily model
identities, ordinary or comparison source adoption, current process-continuity epoch, recent checkpoint
history and bounded daily performance. Existing source checksum/snapshot readers are reused. Conflicted
or invalid latest checkpoints remain visible; no fallback to an older valid result. Epoch/source changes,
missing account confirmation, data-confidence failures and stale market endpoints prevent selection.
The account's own lifecycle is labelled separately from shared signal judgments and never transferred
to the managed account. Candidate funds, holdings and shared-account safety state are excluded from the
review evidence. Live marks reuse the existing daily-return calculation, including incomplete latest
marks. This reads and describes saved state; it does not recompute confirmation, collect prices, mutate
accounts or authorize managed-stock actions. Broader native SQL locking/mapping, original bar requalification,
source coverage/completeness, production freshness/history settings and host composition remain unverified.

Validation with .NET SDK 10.0.401: **416 offline tests passed, zero failures/skips** on September 28:
the prior 407 AI/risk/paper/performance cases plus nine legacy Shadow policy regressions.
Coverage includes both paper families and gate variants, every-holding freshness, source/quantity/cash
conflicts, saved holds, cash exits, next-session continuity, restart, source-family substitution and
unavailable storage. Reader tests use synthetic saved books/fake read interfaces and cover pinned
rules, stable retries, cash history, slow reads, cancellation and missing/mixed evidence. Core and the
test project compiled into isolated `artifacts/ai-risk-20260927/build`; evidence is
`artifacts/ai-risk-20260927/native-reader-regressions.log` (earlier logs remain in the same directory).
Command: `dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --no-restore --verbosity quiet --artifacts-path artifacts/ai-risk-20260927/build -p:NuGetAudit=false --filter 'FullyQualifiedName~AiPortfolio|FullyQualifiedName~AiShadowDecisionContext|FullyQualifiedName~AiLiveDecisionContext|FullyQualifiedName~AiLearning|FullyQualifiedName~AiMomentum|FullyQualifiedName~DelphiLivePortfolioAccountTests|FullyQualifiedName~DelphiLiveActionWorkflowTests|FullyQualifiedName~SystemShadowPolicyTests|FullyQualifiedName~PaperGateComparisonTests|FullyQualifiedName~PortfolioDailyPerformance|FullyQualifiedName~DelphiLiveEvaluationEngineTests|FullyQualifiedName~DelphiLiveLifecycle|FullyQualifiedName~DelphiLiveRanking'`.
Release desktop build: `dotnet build TraderVI.WPF/TraderVI.WPF.csproj -c Release --no-restore --artifacts-path artifacts/ai-catalog-20260927/build -p:NuGetAudit=false --verbosity quiet` succeeded with zero errors and 260 existing CS8632/CS0168 warnings;
log: `artifacts/ai-risk-20260927/wpf-native-readers-build.log` (September 28 rebuild). The runnable output is under
`artifacts/ai-catalog-20260927/build/bin/TraderVI.WPF/release/`. The normal checkout's running files
were not replaced or restarted. The normal source launch in `running.md` rebuilds the
committed checkout after closing the old app. A build does not activate the unfinished AI host.
Package-security auditing was disabled; there is no fresh vulnerability assessment. The full Core
suite and SQL/full-solution build were not run. Only the separately authorized SELECT-only native test
below accessed SQL; no writes, app launch/restart, model, market or broker operation ran.

**Native reader checks, September 28:** the initial 10:37 check validated all 15 catalogue definitions,
but loaded only four Shadow and three old Live contexts; eight Shadow comparisons had no adopted source.
After the separately authorized repair/restart and fresh checkpoint, the 11:23 check loads all 12 Shadow
and three Live contexts with no missing sources. All 15 remain non-proposable under the diagnostic
ten-minute age setting; a simulated after-close clock also rejects selection. This setting is not a
production freshness decision. The initial check found and corrected rejection
of legacy Shadow policies: absent original assignments use frozen V1, and unversioned comparisons must
match their pinned V1 settings. Explicit broken assignments never fall back. No saved policy changed.
Recorded sample: catalogue 1.77 seconds, Shadow contexts 0.17 seconds, Live contexts 1.52 seconds; these are
single-run observations, not load/concurrency qualification. Evidence: `artifacts/ai-risk-20260927/native-readers.log`;
final repeat after the test's post-read clock correction also passes in `native-readers-final.log` in that directory.
The test is skipped without explicit opt-in (verified separately); see [running](running.md#ai-saved-record-reader-check).

**Paper risk-value qualification, September 28:** the separate Shadow saved-risk-book path now reuses
the decision reader's legacy policy identity: original unassigned books keep frozen V1, fixed legacy
comparisons retain their pinned V1, and incomplete/wrong-family explicit assignments still reject.
The risk adapter validates that legacy comparison identity without requiring an artificial database
version or rewriting saved settings. The existing pure valuation functions are now internally callable
for read-only native checks without constructing a journal or granting runtime permission.

`AiRiskReadersSqlTests` passes against all 15 saved portfolios. It independently totals saved holding
marks, compares the adapter's source-bar/checkpoint valuation, and exercises cash/account/incomplete-price
rejection and expiry. The risk policy calculates only in memory; no production round is initialized.
The earlier 11:35 check found five temporally usable books and ten rejected Shadow books. Shadow's
unchanged cash-only books had no current saved update time. The reader now records SQL time after the
complete serializable read, with explicit cash-only provenance, fixed-capital/closed-history reconciliation
and no ledger edits. Historical reads and invested books retain their saved valuation times. Receipt
replay expires normally; new verified cash reads preserve round history, peaks and holds (ADR-0087).
At 11:47, all 15 books meet the diagnostic ten-minute age/session check: ten verified cash-only Shadow
books, two invested Shadow books and three Live books (one invested). This is not an approved production
age, external feed accuracy or one-minute coverage. Family read samples: Shadow 184 ms, Live 1,070 ms;
single serial samples do not qualify lock duration
or concurrent load. Opt-in instructions and side effects: [running](running.md#ai-paper-risk-value-check).

Validation: **193 focused offline tests pass**, with the native test skipped by default; the separately
enabled native test passes. Isolated Release WPF build passes with zero errors and 260 existing
CS8632/CS0168 warnings. SDK 10.0.401; no fresh dependency-security audit (`NuGetAudit=false`), full Core
suite or full-solution/SQL rebuild was performed. No schema changed. Evidence: private
`artifacts/ai-risk-values-20260928/` (`cash-verification-tests.log`, `cash-verification-native.log`,
`cash-verification-wpf-build.log`; earlier baseline: `native-final.log`).
Changes are compiled/tested in source; this increment did not replace/restart the normal desktop or
connect the unfinished AI runtime. No external service, database mutation or production risk-journal write ran.
Stock-price timing and bounded faster-source samples are now measured below; next qualify timestamped faster observations and wire recurring risk observations into the AI paper host;
broker cash events remain a separate dependency. Preserve the tested stale-input rejection.
Transaction concurrency/lock duration and full source coverage remain unqualified.

**Stock-price freshness check, September 28:** the read-only diagnostic
`Operations/Read-AiPriceFreshness.sql` measures recent poll/bar receipt delays and mature Live slot/cycle
outcomes against today's saved reviewed-session bounds. The opt-in native risk-reader test now also
reports invested valuation ages without printing symbols, amounts or account IDs. These bounded
samples measure existing records; they do not prove continuous coverage or all scheduled slots existed.
Delphi Live uses five-minute bars and starts collecting two minutes after bar end; the observed
roughly 2.4–2.6-minute receipt delay is consistent with that schedule. Shared V2 five-minute poll
receipts also include repeated older prices, up to about 13 minutes old at receipt in the 12:40 sample.
Neither collection cadence nor a recent download timestamp proves a new market price.
The TMX client accepts one-minute requests, but saved evidence validation/storage accepts only five-
and fifteen-minute bars, and both paper risk readers require five-minute evidence. The existing quote
adapter exposes request/receipt time without a market event timestamp. Neither is a qualified faster
valuation source. The August 26 one-minute comparison already recorded gaps (calibration checklist);
it is historical evidence, not current held-symbol qualification. The new one-minute samples below
measure current benchmark and held-stock availability. Investigate missing bars, the failed Live cycle and V2
completed-bar conflicts before claiming reliable operation; no ordinary engine timing or price rules changed.
Validation: 40 focused reader tests pass; the opt-in native check passes separately and SQL diagnostics
execute successfully. Core/test compilation has existing CS8632 warnings. No new WPF/full-solution
build or dependency-security audit was needed/run for this diagnostics/documentation-only increment.

**Candidate one-minute source, September 28:** the bounded `tmx-one-minute-freshness` probe uses the
existing client and reviewed calendar, with five XIU requests at one-minute spacing and explicit
timeouts/retry bounds. It reports missing head/interior/tail minutes, completed-bar revisions and
disappearances, excludes forming/out-of-window bars and stops on provider failure. The authorized
13:13–13:17 sample shows advancing prices about 10–11 seconds old with five transport attempts and no
completed-bar changes. Two historical minutes remain absent; the probe cannot distinguish no-trade
minutes from missing provider data. Ages are measured at polls, not a guarantee that the saved price
stays under a minute old between polls. This is an initial benchmark screen, not a selected operating
freshness policy or permission to replace engine bars. Held-stock follow-up is recorded below;
the later raw-response/trade check narrows the cause, while production source qualification remains pending.
Four offline diagnostic tests pass; Sandbox build succeeds with 261 existing CS8632 warnings. The
sandboxed restore could not reach NuGet; the authorized restore/build succeeded. No dependency-security
audit (`NuGetAudit=false`), full Core suite, WPF/full-solution build, database operation, AI/broker call,
order or app restart ran. Receipts and validation: `artifacts/ai-risk-values-20260928/one-minute-*`.

**Held-stock source follow-up, September 28:** `tmx-held-price-freshness` reads the saved paper-held
symbol set through existing repositories and adds XIU. It bounds targets, requests, retries and time,
anonymizes output, and compares complete one-minute groups with five-minute bars without filling gaps.
The authorized 13:25–13:29 sample above demonstrates uneven coverage: no poll had the latest requested
minute for all five held stocks. One price remained unchanged until its age reached 370.298 seconds,
then advanced. At the final poll, four held symbols lacked 5, 3, 9 and 8 of the 14 requested minutes;
the other held symbol and XIU had all 14. The first missing minute for each affected symbol was not
recovered by either rerequest. Matching sparse-group volumes suggest no trades in omitted minutes,
but cannot establish that meaning: the client also filters rows with incomplete/null OHLCV fields.
The later raw-response/trade check below distinguishes the missing rows and supports quiet trading;
an old price must still retain its actual timestamp. No faster source or freshness policy is enabled.
The final reread confirms only the held-symbol set, not every intervening account/quantity change.
Nine focused offline diagnostic tests pass (five held-stock and four benchmark cases). Sandbox build
passes with three existing CS8632 warnings; test compilation also reports existing CS8632 warnings.
No fresh dependency-security audit (`NuGetAudit=false`), full Core suite or WPF/full-solution build ran.
No database mutation, AI/broker call, order or app restart ran. Evidence and validation:
`artifacts/ai-risk-values-20260928/held-minute-*`.

**Missing-minute diagnosis, September 28:** `tmx-missing-minute` uses diagnostic TMX methods that
preserve unfiltered response points alongside validated bars, plus the public quote
page's `getCompanyMostRecentTrades` query (25 rows). Counts distinguish omitted rows from all-null or
partially-null rows without treating unknown volume as zero. The ordinary batch reader now rejects a
missing/null data payload instead of allowing it to look like a successful empty read; an explicit empty
list remains a valid empty response. Complete-bar mapping and validation remain in place. Trade timestamps require explicit offsets;
the oldest listed minute is excluded from volume comparison because the row cap may truncate it.
The 14:12 raw-only run completed 17 requests/attempts. Its 200 sparse five-minute groups all matched
volume. The final 14:21 run and trade comparisons are recorded above; the active stock's latest 25 trades
all fell after the comparison cutoff, so that target has no completed trade overlap in this sample.
These results support no reported trades during sampled gaps, not an independent guarantee of every
venue/trade or a method of detecting a silently frozen feed. Provider omissions also occur at five-minute
resolution. Keep market-event time, successful receipt time and missing/failed refresh state distinct;
do not fill gaps, reset price age, loosen stale-input rejection or change ordinary engine evidence.
The price adapter and combined-account reader below implement source boundaries;
durable risk integration and operating qualification remain required.
The public [TMX last-sale explanation](https://www.blog.tmx.com/money/tmx) describes real-time quotes
and charts but does not establish the gap contract. Schema introspection was unavailable; the recent-trade
query was read from TMX's public quote-page JavaScript, then checked on XIU and in the bounded probe.
Thirty-seven focused TMX tests pass, including ten new diagnostic cases. The final incremental Sandbox
build passes with zero warnings/errors; Core/test compilation reports existing CS8632 warnings. No dependency
security audit (`NuGetAudit=false`), full Core suite or WPF/full-solution build ran. No database mutation,
AI/broker call, order, deployment or app restart ran. Private validation: `missing-minute-tests.log`,
`missing-minute-build.log` in `artifacts/ai-risk-values-20260928/`.

**One-minute price adapter, September 28 (source/tested only):** `TmxAiRiskPriceSource` connects the
existing TMX client to a typed AI price-read contract. It requests up to 32 explicit canonical symbols
inside the reviewed current session, with one bounded request per symbol and no constructor-time
collection. Price age, receipt age and request timeout must be supplied explicitly; no production
freshness values were selected. It preserves each completed one-minute bar and its request/receipt
times, excludes forming bars, rejects mismatched/duplicate/invalid evidence and has no cached fallback
after a failed refresh. Re-reading an old bar cannot extend its price expiry. Missing/failed/stale
symbols make the full price set unusable; the set is checked after all I/O and at subsequent use time.
Caller cancellation propagates; per-request deadlines return unavailable evidence without provider
details. Empty symbol sets cannot stand in for a verified cash-only account.
The adapter alone does not revalue a portfolio or write the risk journal. The combined reader below
now joins prices to the owned account; durable conflict/history checks and qualification remain
before scheduling it. Existing saved-price readers still require their original five-minute book evidence;
no account balance, engine mark, risk reference or hold was replaced.
Validation: 56 focused tests pass (16 new price-source cases and 40 saved-paper-reader cases), including
old-price rereads, slow multi-symbol collection, missing/failed responses, cancellation, deadlines,
forming bars and incorrect source metadata. Core/test compilation succeeds with existing CS8632
warnings. No fresh dependency-security audit (`NuGetAudit=false`), full Core suite, WPF/full-solution
build, native collection, SQL operation, AI/broker call or app restart ran in this increment.
Evidence: `artifacts/ai-risk-values-20260928/minute-price-source-tests.log`.

**Combined faster paper valuation, September 28 (source/tested only):** `PaperAiFastValuationReader`
uses the actual Shadow and Delphi Live account-reader interfaces and `TmxAiRiskPriceSource`.
It reads the complete owned book before collection and again afterwards, reuses existing fixed-capital
cash/holding-history validation and rejects changed books, including revision, trade, settings or hold
changes. Shadow exposes a price-independent account read from its existing serializable transaction;
its ordinary saved-price reader is unchanged. The combined value is cash plus all held shares at actual
completed one-minute closes. Every held price must be fresh and end at the same minute; trades after
that boundary reject the value. Quiet stocks with mismatched latest minutes remain unavailable.
Cash-only accounts need no market request and must reconcile complete closed history. Saved holds,
both account reads and original price/receipt times are retained in hashed evidence. Expiry is the
earliest account/price/receipt/session bound, rechecked after I/O; receipt time cannot renew market time.
The result is a preparation record, not a risk permission or persisted journal transition. Existing
rounds/peaks, account balances, stops and engine marks are untouched. Next implement durable source
conflict detection and history continuity/handover before assessing combined values through the risk
journal, then qualify native timing/concurrency and operating freshness. No production age limits
were selected and no schedule or application wiring was enabled.
Validation: 163 focused tests pass, including 34 new combined-reader cases and regressions for the
price adapter, saved readers and both paper risk monitors. Cases cover arithmetic, account ownership,
missing history/prices, stale/mixed times, changed accounts and intervening trades, slow reads,
cash-only closed history, saved holds, unsupported corporate actions, cancellation and storage failure.
Core/test compilation succeeds; existing CS8632 compiler warnings remain. No fresh dependency-security
audit (`NuGetAudit=false`), full Core suite or WPF/full-solution build ran. No native TMX/SQL operation,
database mutation, AI/broker call, order, deployment or app restart ran in this increment.
Evidence: `artifacts/ai-risk-values-20260928/fast-account-tests.log`.

No native feed or freshness duration is qualified, and current five-minute bars do not prove
one-minute coverage. Catalogue, packet construction and both family decision contexts are source-implemented.
Before repair, the read-only investigation identified those eight accounts as both fixed gate profiles of
Breakout Top 3/5 and Continuation Top 3/5. The missing record was the common comparison source, not today's
Delphi publication. `BrokerTradeProjection.ListingSymbol` preserves Canadian broker suffixes in new
tracked records; the former `DelphiLiveHoldingSource` passed them into session membership, while
`NormalizeObservedHoldings` only trimmed/uppercased. `FreezeSql` then violated the canonical `Symbols`
foreign key and rolled back the session. Comparison adoption runs later in the same monitor tick and
was never reached. This also prevented all three Live accounts from obtaining today's monitoring context.
The four original Shadow accounts use their independent adoption path; all today's published Top 25
rows in both lenses fail Breadth, which explains their zero admitted candidates separately from the bug.
The September 28 repair now resolves Real observation symbols using an explicit source-scoped alias
table ([ADR-0088](adr/0088-source-symbol-aliases-for-monitoring.md)). Ownership IDs and action flags
are preserved; unknown/conflicting references fail clearly. Account records and foreign keys are unchanged.
Read-only saved broker metadata confirms CAD/XTSE equity listing evidence. Migration 041
rechecked that evidence and the canonical target before creating the table and its one reviewed alias.
Validation: 51 focused alias, session, monitor and broker-projection tests pass using SDK 10.0.401;
isolated Release WPF build passes with 260 existing CS8632/CS0168 warnings; Visual Studio MSBuild/SSDT
schema build passes without warnings. NuGet auditing was disabled; no fresh dependency-security
assessment or full-solution build is claimed. Logs: private `artifacts/ai-source-investigation-20260928/alias-*`.
The operator subsequently authorized backup, this exact migration and the updated app restart.
Installation/native adoption evidence is in the operational row above. The original positions, trades,
imports, bindings, sync receipts, account reports, Live ledger roots and Shadow positions retained
matching counts/hashes across migration while WPF was stopped. The restarted normal host resumed its
existing workflows. Missing coverage remains recorded; no trading activation or historical rewrite was made.
Early native reader checks rejected the newly adopted source before its 11:20 effective boundary,
then four ungated Shadow contexts whose saved MissingEvidence checks predated that boundary. After
fresh checkpoint processing, the final SELECT-only test passed: all 12 Shadow and three Live contexts
loaded, none missing, with current/after-close rejection checks intact and zero selectable proposals.
The interim failed checks remain in private logs; no guard was relaxed or saved timestamp changed.
The subsequent native valuation-reader check and its remaining limits are recorded above.
Remaining native mapping/concurrency, prompt size/API costs and account/price/cash-event producers need qualification
before host integration. Candidate/managed histories are not interchangeable.
Production storage,
retention/performance, backups and host recovery are unqualified. Full history is replayed/rewritten
with a 32 MB cap; checksums are not tamper-proof or proof against old backups between restarts.
Native collection and actual cash-flow adjustment, overall-guard and stock-action integration,
ordinary-engine replacement and the execution caller remain pending. These tests establish adapter
and round behavior, not native data qualification or live readiness.

September 26 design discussion: the operator requested an AI-managed portfolio and Monday,
September 28 live adoption, with a mandate/discretion/boundaries framework and decision logging.
The first pass is AI Momentum in paper and intended live use; AI Explorer is explicitly deferred.
Discretionary rules, enforced boundaries, declining risk capacity and no loosening stops to prolong
losses are agreed ([ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md)). The selected engine's
per-stock hard-stop/trailing-profit settings are now retained: AI may tighten or sell earlier,
never loosen/remove an established stop. Desired relaxations must be logged as blocked learning
hypotheses for later actual-versus-simulated outcome comparison. Application code must enforce
the boundary; learning cannot change it. Source inspection confirms existing portfolio-review
hard-rule rejection and rejected-review journaling, but stock-stop actions and alternative-outcome
evaluation are not implemented. Concrete rule mapping is implementation work; lesson criteria are
deliberately deferred until relevant to learning implementation. Portfolio scope is settled above.
The operator sets an initial AI budget below CAD 100
monthly and requests portfolio selection/switching plus self-improvement. API-first Oracle adaptation
is selected for the first source increment; subscription-backed Codex remains an alternative.
Initial switching behavior is now agreed: keep mutually permitted holdings and stops, close others,
and confirm/reconcile required sales before replacements. Preserve round peak/reference/limit,
attempts and review holds across the switch, including all-cash transitions, and log the reason.
This reuses existing handover contracts; AI replacement, executable compatibility mapping and adapter
qualification remain unfinished (ADR-0087; Step 5). Catalogue choices now exist offline; no assignment or execution changed.
The budgeted Responses client, durable private call journal and versioned portfolio-proposal reviewer
are implemented/tested offline. They have no production composition or trading authority. Full
supervision, automatic outcome evaluation, saved engine-context adapters and paper/live integration remain unfinished.
September 26 clarification: both the supervisor and execution coordinator must periodically consult
evaluated learning. Source now connects the learning journal to the supervisor and execution-preparation
coordinator, with versioned lessons, per-lesson assessments and restart-safe consultation intervals.
One post-session learning review is now agreed: completed trades and blocked alternatives with
sufficient follow-up evidence, evidence-backed helpful/harmful findings, and inconclusive cases
retained pending. Evaluated lessons feed subsequent decisions; saved-lesson consultation continues
intraday. All paid learning shares the monthly allowance (ADR-0087; Steps 6/7).
Production scheduling, lesson qualification and broker integration are not implemented. Protective
handling has no dependency on this new path. See ADR-0087 for precise limits.
The [ten-step delivery sequence](strategy-alignment-implementation-checklist.md#ai-momentum-delivery-sequence)
groups commits `720c780a` and `07928730` as Step 1, complete offline. Step 2's core framework is agreed
with portfolio scope settled and lesson criteria deferred; Step 4's offline risk core is in progress.
Step 3 has broker and paper valuation adapters offline; the other delivery steps remain pending.
The operator requests this same list for future "progress" queries and an MVP focus
without tangents or unnecessary complexity. September 26: the operator accepts a 7% hard daily
buying pause for AI Momentum and the same default for new Delphi Live/System Shadow templates.
Source templates and settings help are updated; frozen V1, saved strategies and active assignments
retain their settings. September 27: the operator accepts the gradual per-stock allocation scale
in ADR-0087, covering initial buys and additions together without scale-triggered forced sales.
The later September 27 clarification replaces the AI daily framing and session-minimum lock with
continuous allowance reduction/recovery across days, without a calendar reset (ADR-0087).
The operator clarifies that both starting balance and recorded portfolio peak must be tracked, with
continued monitoring and intended automatic re-entry after market recovery. Source inspection confirms
existing position trailing floors and separate portfolio guards; the new AI allowance/re-entry path
is not implemented. Round references and confirmed cash-flow adjustment are now settled below;
valuation/cash-event source qualification and integration remain open (ADR-0087).
September 27 follow-up accepts 5% / 3% / 1% cycle-loss thresholds, replacing the earlier AI 7%
pause, with new buying stopped for the day after three failed rounds. ADR-0087 owns the decision
and comparison assumptions. Failed-round closure is now agreed: close AI-managed holdings, confirm
and reconcile sales, then monitor for fresh entries before another permitted round. Partial or
uncertain exits cannot start a new round. Re-entry now requires a fresh qualifying signal from the
selected engine after confirmed closure, with no additional fixed delay; the AI can take it or wait.
After three failed, closed/reconciled attempts, a fresh 5% / 3% / 1% sequence is eligible next trading
session with fresh entry evidence and AI review, subject to all other limits. Carried unfinished
rounds retain their number, limit/reference and peak. The operator accepts carrying the whole
sequence and attempt count across days: round 2 remains round 2 at 3%, and its failure advances
to round 3 at 1% after required checks. No morning refill or renumbering; the third failure may
occur days after the first. The 10% approval hold persists. UI must distinguish these states
and show attempts, active limit/reference, next eligible session and remaining restart requirements
(ADR-0087; Step 8), with attempts labelled for the sequence. The operator also accepts keeping the
same round/peak/limit and count through ordinary sales, taking profits or holding only cash.
Only a round-limit hit consumes an attempt; there is no success reset. No UI or enforcement changed.
This increment is documentation only, checked by diff review.
Confirmed deposits/withdrawals now preserve measured loss percentages under ADR-0083's matched
before/after reference scaling, applied to AI round protection and overall drawdown. Original capital
and round-opening history remain recorded; attempts, holds and per-stock stops do not reset.
Read-only inspection confirms existing broker continuation arithmetic; AI integration and native
cash-event source qualification remain pending. No new runtime validation or operational action ran.
The operator agrees to shared risk authority with one explicit risk configuration per portfolio:
AI continuous rounds replace its engine daily-pause calculation; ordinary portfolios retain their
rules. Existing holds, stock protection and account/order checks persist. Read-only inspection
finds family-specific guard calls in `TradingRecommendationEngine`, common broker workflow checks,
and an AI coordinator that only prepares proposals. Focused extraction and single-path execution
integration remain pending; no runtime implementation or new tests accompany this design decision.
Proportional sizing within each round is agreed, including recovery before its hard limit.
The operator confirms capturing original allocated capital and calculating each round's initial
threshold from its actual verified opening capital. Later-round previews are estimates; the 3%/1%
thresholds use actual reconciled capital when those rounds start. The operator now accepts independent
threshold checks on every fresh verified portfolio valuation, including qualified one-minute updates,
without an AI call. This supersedes five-minute-only checks; slower AI reviews cannot slow protection.
Source/collection qualification remains pending; current five-minute bars do not prove minute-level
coverage. No feed, enforcement, runtime or UI changed; this is documentation agreement only.
The operator now accepts trailing each round's highest verified value, initialized at its opening
value, for both the 5% / 3% / 1% trigger and buying-allowance loss fraction. Price declines cannot
lower that trigger; hitting it commits the round to closure. UI must show the saved round peak,
trigger, opening/current values and remaining room. The separate 10% decline from highest recorded
closing value still triggers a cumulative buying pause requiring operator approval,
with protective exits continuing. Explicit UI distance to this and other thresholds is required
(ADR-0087; checklist Step 8). Unfinished rounds now explicitly carry their references, limits and
protection across market closures. A new trading session requires consultation of fresh engine entry
guidance. The operator now accepts session-start, mid-session and pre-close consults alongside
ongoing intraday Delphi Live monitoring and AI receipt of its data. The operator confirms one minute
as the fastest AI review interval and fifteen minutes as the slowest during market hours, subject to
the shared budget. Provider unavailability or exhausted budget now has an agreed fallback: pause
new buys, additions and portfolio switches, retain monitoring/stops/engine exits, and resume only
after a successful fresh AI review with all other limits satisfied. The UI must show the reason.
Initial earlier-review triggers are agreed: a new qualifying entry signal, a confirmed holding
closure, or a risk-limit change to permitted actions. Combine events arriving together and retain
the one-minute minimum and shared budget; more hooks may be added later through the same path.
The operator rejects daily budget allocations and reserves for later consults: use the monthly
allowance as needed, then apply the accepted pause or explicitly authorize more allowance/funding.
The initial budget is unchanged. Source inspection confirms the CAD 90 default and a validator
rejecting CAD 100 or more; the journal also retains the month's lowest ceiling. An explicit manual
extension path is not implemented (ADR-0087).
Cadence ownership is agreed: AI review attention is separate from app-owned protection and required
collection. Source qualification and detailed timeout/recovery handling remain open.
Source inspection distinguishes the 30-second host tick from five-minute signal bars/two-minute
collection offset (ADR-0087). Valuation/cash-event qualification, AI adjustment integration, event integration and budget-aware
scheduling remain unfinished. No source changes or
new operational validation accompany this decision.
Existing engine daily templates are unchanged. Documentation was checked; no new runtime tests or operational actions ran
for this decision. The source-validation evidence below retains its original scope.
No numerical limit has been operationally activated by this change.
Validation: 14 engine-settings cases pass, including six added boundary/preservation cases; the
full Core suite passes **1,798 tests, one SQL opt-in skipped, zero failures**. The desktop builds
to an isolated artifact directory with zero errors and two existing CS0168 compiler warnings;
the focused test build also reports existing CS8632 warnings. Separate dependency-security warnings
NU1902/NU1903/NU1904 remain. Logs: `artifacts/daily-buy-pause-focused-tests.log`,
`artifacts/daily-buy-pause-core-tests.log`, `artifacts/daily-buy-pause-desktop-build.log`.
No app launch, SQL mutation, API request, broker operation or installation ran.
September 26 local configuration check confirms Oracle has a saved API key and OpenAI provider
selection. Secret contents were not displayed; remote authentication, Astra access and account credit
balance remain unverified. No paid request ran.
The operator authorizes adapting the existing Oracle client, whose Astra integration is unvalidated.
Oracle Phase 2 narration and a May manual run are documented historically. September 26, 21:07 Toronto
read-only inspection found no Oracle process or scheduled task directly naming it; the repository's
nightly stages are Hermes, Delphi and Athena. No Oracle database history was queried.

September 26 source validation: .NET SDK 10.0.401; 63 synthetic AI cases (40 client/proposal plus
23 learning cases) included in the full Core result of **1,792 passed, one skipped, zero failed**.
The skipped test requires explicit SQL-database opt-in. Oracle builds with zero warnings/errors.
Commands: `dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --no-restore --verbosity quiet -p:NuGetAudit=false`
and `dotnet build Oracle/Oracle.csproj --no-restore --verbosity quiet -p:NuGetAudit=false`.
Local ignored logs: `artifacts/ai-learning-core-tests.log`, `artifacts/ai-learning-oracle-build.log`.
After final publication-time hardening, all 23 learning cases also pass in
`artifacts/ai-learning-final-tests.log`.
Learning validation includes per-consumer/restart cadence, paper/live and version scope, required
assessments, pending/rejected review recovery, changed/expired learning, concurrent publication,
tampered/lost/interrupted storage and preparation without model calls. Tests use synthetic facts only.
The test build emits existing compiler warnings, principally nullable annotations, plus separate
NU1902/NU1903/NU1904 dependency-security warnings for System.Data.SqlClient 4.8.1,
System.DirectoryServices.Protocols 5.0.0, System.Drawing.Common 5.0.0 and
System.Security.Cryptography.Xml 5.0.0. This is not full-solution/SQL-project validation.
No paid model request, production journal initialization, database mutation, app launch or broker
operation ran. Default working budget is CAD 90; reviewed expiring FX/tax bounds, billing qualification,
protected storage and shared host composition remain required. See ADR-0087 for enforcement limits.
No operating AI portfolio is connected or activated, and
operational evidence dates above are unchanged. See the
[proposal and next decision](reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026).

Current launch blockers are the Live opening/conflict findings and the real-account boundaries above.
Research still needs comparable complete-session evidence, historical/leadership continuity and mature
outcomes; no dependable strategy winner is established. Broader backlog: RS feature backfill, registry
and universe hygiene, bounded account-history reads, schema drift, dependency remediation, and reviewed
short-session/calendar handling before **December 24, 2026**. The [roadmap](roadmap.md) retains their order.

## Reference map and maintenance

Programs, side effects and commands: [running guide](running.md). Architecture:
[system design](system-design.md). Models, signals, gates and price definitions:
[design rules](design-rules.md). Original measurements, old snapshots and superseded summaries:
[preserved status history](project-status-history-20260922.md).

Replace the affected current row after new evidence; preserve its verification date until that evidence
changes. Put long run/build narratives in linked reports or dated history. Keep next steps in the
checklist and priority changes in the roadmap, then reconcile their short links/status labels.
The earlier September 22 bounded documentation pass changed navigation only; this later execution
increment adds source/test evidence and explicitly dated native read-only verification.
