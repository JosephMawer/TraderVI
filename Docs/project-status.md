# TraderVI Project Status

**Current record:** September 27, 2026. AI Momentum round continuity and new-session engine consultation agreed; implementation pending.
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

## Latest operational evidence

| Area | Last evidence (Toronto) | Verified result and limit |
|---|---|---|
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

September 26 design discussion: the operator requested an AI-managed portfolio and Monday,
September 28 live adoption, with a mandate/discretion/boundaries framework and decision logging.
The first pass is AI Momentum in paper and intended live use; AI Explorer is explicitly deferred.
Discretionary rules, enforced boundaries, declining risk capacity and no loosening stops to prolong
losses are agreed ([ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md)); exact protection/profit
limits and evaluation objectives remain open. The operator sets an initial AI budget below CAD 100
monthly and requests portfolio selection/switching plus self-improvement. API-first Oracle adaptation
is selected for the first source increment; subscription-backed Codex remains an alternative.
The budgeted Responses client, durable private call journal and versioned portfolio-proposal reviewer
are implemented/tested offline. They have no production composition or trading authority. Full
supervision, automatic outcome evaluation, packet adapters and paper/live integration remain unfinished.
September 26 clarification: both the supervisor and execution coordinator must periodically consult
evaluated learning. Source now connects the learning journal to the supervisor and execution-preparation
coordinator, with versioned lessons, per-lesson assessments and restart-safe consultation intervals.
Production scheduling, lesson qualification and broker integration are not implemented. Protective
handling has no dependency on this new path. See ADR-0087 for precise limits.
The [ten-step delivery sequence](strategy-alignment-implementation-checklist.md#ai-momentum-delivery-sequence)
groups commits `720c780a` and `07928730` as Step 1, complete offline. Step 2 is in progress; steps 3–10
remain pending. The operator requests this same list for future "progress" queries and an MVP focus
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
is not implemented. Exact reference combination/valuation and restart conditions remain open (ADR-0087).
September 27 follow-up accepts 5% / 3% / 1% cycle-loss thresholds, replacing the earlier AI 7%
pause, with new buying stopped for the day after three failed rounds. ADR-0087 owns the decision
and comparison assumptions. Failed-round closure is now agreed: close AI-managed holdings, confirm
and reconcile sales, then monitor for fresh entries before another permitted round. Partial or
uncertain exits cannot start a new round. Re-entry now requires a fresh qualifying signal from the
selected engine after confirmed closure, with no additional fixed delay; the AI can take it or wait.
Proportional sizing within each round is now agreed, including recovery before its hard limit;
reaching the limit commits that round to closure. The operator now accepts retaining the 10% decline
from highest recorded closing value as a cumulative buying pause requiring operator approval,
with protective exits continuing. Explicit UI distance to this and other thresholds is required
(ADR-0087; checklist Step 8). Unfinished rounds now explicitly carry their references, limits and
protection across market closures. A new trading session requires consultation of fresh engine entry
guidance; additional rule-review checkpoints are requested but not yet selected. Reference combination,
daily retry-counter renewal and exact review cadence remain open. No source changes or
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
