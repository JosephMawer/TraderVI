# TraderVI Project Status

**Current record:** September 24, 2026. Operational evidence is dated per row below;
the latest read-only nightly check does not reverify intraday or broker behavior.

## Executive summary

TraderVI runs independent paper strategies and has a connected Wealthsimple TFSA with saved
broker history. **Automatic real strategy trading is off.** The strategy engine still uses its
legacy reported-account inputs; broker-backed risk setup, synchronization, protection handover
and execution remain unfinished. The root goal is one deliberately approved complete strategy
managing the permanent TFSA while paper and real histories remain independent.

Use the [roadmap](roadmap.md) for priorities and the [implementation checklist](strategy-alignment-implementation-checklist.md)
for dependencies and next actions. [Design rules](design-rules.md) own behavior; this page records
what has actually been implemented, installed or observed.

## Latest operational evidence

| Area | Last evidence (Toronto) | Verified result and limit |
|---|---|---|
| Nightly pipeline | Sep 24, completed 00:36:48; SQL checked 09:01 | Scheduled current-source build and all three stages completed. Hermes inserted 483 daily bars with zero collection failures; Delphi published Valid using Sep 23 data. Athena wrote 864 valid outcomes and four invalid older `v3.0` 20-session outcomes (`MissingSymbolSession`), producing Attention/exit 2. No recovery rerun was needed. |
| Delphi inputs | Sep 24, 09:01 | Daily identity is `v3.4-observed-benchmarks`. Publication: 211 candidates, 422 lens rows, zero source-observation mismatches, 25 Hold rows per lens blocked by Breadth. All 483 active symbols, XIU/SPY and checked derived series reach Sep 23. ALYA is excluded for an invalid older feature bar; fresh endpoint coverage does not prove complete historical inputs. Publication is not buy eligibility. |
| Live and Shadow | Sep 22, 09:59 | One responding Release WPF host and one advancing Live lease. Latest two Live cycles each had 40/40 usable receipts; three portfolios saved complete 09:55 values. All 12 Shadow sessions adopted today's publication; all 13 open positions advanced to the bar ending 09:55. |
| Live limitations | Sep 22, 09:59 | Held comparison account lacks opening NAV (net asset value): today's return is unavailable and existing entry rules block new buys. Coverage remains Blocked with a host gap and earlier missing/failed receipts. One tracked symbol's 15-minute bar conflicted on three polls. Latest-cycle recovery does not establish uninterrupted coverage. |
| Broker connection/history | Sep 16, 22:06 | Native TFSA reads and linked history refresh verified: 32 orders already saved, no current import blocker. This does not prove complete cash-event history or strategy execution readiness. |
| External-trade synchronization | Sep 24, 09:56 read-only SQL check | An operator-reported external sale was absent from both the saved trade ledger and latest broker import; the tracked real position remained active. Latest saved import was still Sep 16, 22:08 Toronto, matching the latest local reconciliation-file timestamp. No broker request or financial-record change ran. Operator-triggered import and separate legacy position tracking do not provide automatic external-trade synchronization; checklist item 4 remains open. Private trade details omitted. |
| Storage and desktop | Sep 17, installed and observed | Compact snapshot migrations 037/038 and UI-worker fix installed. All 4,472 historical/current snapshots verified; subsequent compact writes observed. Full-history reconstruction still consumes background CPU/memory. |
| WPF memory investigation | Sep 24, 09:08:49–09:10:45 pre-install sample | Previous Release PID 24492 responded in all 24 samples: 2.58–3.75 GB working set and 3.25–4.90 GB private committed memory (decimal GB). Full-history JSON expansion and separate display snapshots were identified as likely contributors; no runtime allocation/heap attribution was captured. Superseded by the authorized installation below. Private evidence: `artifacts/wpf-memory-20260924/`. |
| WPF memory-fix installation | Sep 24, restarted 09:30:32; SQL checked 09:32:09; process sampled through 09:33:14 | Normal close of PID 24492, five backed-up/replaced binaries with matching SHA-256, and one new Release PID 31052 loading the installed Core/WPF modules. All 24 post-restart samples responded: 0.66–1.33 GB working set and 0.57–1.32 GB private committed memory. This short fresh-process sample is not a matched workload or sustained memory bound. One renewed Live lease, nine new compact revisions/events and three updated portfolios confirm runtime resumption. Session is Monitoring with Blocked coverage and a host gap; uninterrupted collection is not established. Evidence: `artifacts/wpf-memory-20260924/installation.md`. |
| Database allocation | Sep 23, 23:55 verified | User-authorized one-time file shrink reclaimed 54.88 GiB. Data file is 16 GiB with about 6.40 GiB used; log is 1.008 GiB. Full integrity checks before/after found zero errors; all 89 application tables retained their exact row counts (1,210,867 total). Database remains ONLINE, SIMPLE, CHECKSUM, with automatic shrink off. |
| Build storage cleanup | Sep 23, 21:53 verified | User-authorized one-time removal of 11,514 old compiled files reclaimed 29.66 GiB. Artifacts now total about 7.03 GiB. All 23,438 retained inventory files have unchanged sizes/timestamps; 48 model/archive and source-manifest hashes match. Latest two nightly builds, package cache, recent work and worktree retained. |
| Backups | Sep 24, nightly receipt; hashes checked 09:03 | Hermes created a 1,091,843,072-byte full backup and recorded successful SQL checksum verification; independent morning SHA-256 reads match both local files and the runner receipt. Routine retention removed the Sep 22 standard backup in both locations. The separate Sep 23 pre-shrink COPY_ONLY checkpoint remains. Its earlier IN_SYNC observation does not establish cloud synchronization of today's backup; independent download and test restore remain unverified. |

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
| Initial real-account risk references | Preparation, reviewed saving and uncertain-save recovery implemented/tested. Migration 035 is prepared, uninstalled; initial operational setup and broker-input cutover are absent. |
| Daily risk continuation | Boundary-book valuation and append-only references/holds implemented/tested for positively verified intervals without deposits/withdrawals. Migration 039 is prepared, uninstalled; no production caller. |
| Cash-flow continuation | Percentage preservation accepted Sep 22 and implemented/tested with matched books, preserved holds and durable duplicate protection. Complete broker event coverage/effective cash timing and native morning-value qualification remain unverified; no production caller. |
| Orders and protection | Manual buy/sell clients and controls exist. Native mutation/lifecycle verification, strategy protection handover, durable activation/execution and automation qualification remain. |
| Hosting | Nightly 00:30 schedule and 07:00 supervision were installed previously; the Sep 24 scheduled chain completed with Athena Attention. Intraday monitoring depends on the desktop/console host. Standalone pre-session hosting is future work, with one active owner. |
| WPF snapshot memory | Sep 24 direct typed reads, shared report strings and streaming fingerprint checks implemented/tested under ADR-0082, installed with explicit restart authority at 09:30:32. Loaded-binary hashes and resumed compact writes verified. Full lifetime collections/repeated reads and sustained memory/capacity qualification remain (S4). |

## Verified development baseline

September 24: **1,601 Core tests passed**, including six added snapshot-memory cases. A synthetic
1,000-quote history sharing one 16 KiB report allocated 9,579,504 bytes per direct typed read versus
75,733,312 bytes through expanded JSON (about 87% fewer allocated bytes). The comparison path already
uses the improved streaming validator; this is not a measured reduction in the running app's RAM.
Tests preserve all six collections, exact report text, legacy optional fields, original fingerprint
compatibility and corruption rejection. Isolated Release WPF build passed with existing compiler
warnings; no SQL/full-solution build or operational workflow ran. Dependency-security auditing was
disabled for restore and advisories remain unresolved. No database/schema, market/broker or deployment
operation accompanied that source-validation increment; the later authorized installation is recorded
above. Evidence: `artifacts/wpf-memory-20260924/implementation.md` and
`test-results/core-tests.trx`. [Implementation contract](adr/0082-compact-live-snapshots-and-backup-retention.md#runtime-reconstruction-refinement--september-24-2026).

September 22: **1,595 Core tests passed**, including 43 new cash-flow cases covering both strategy
families' existing loss thresholds, matched books, conflicting evidence, daily boundaries and durable
recovery. Repository SQL passed an offline syntax check. Evidence: `artifacts/tfsa-cash-flow-20260922/`.
The supplied broker capture shows completed/pending cash events but does not establish their effective
account-value timing or matching books. Source qualification and operational item 1 remain open.

September 21's isolated SSDT/migration checks passed for daily continuation; migration 039 remains
uninstalled alongside 035. Existing compiler warnings remain; dependency-security advisories are
separate, and no fresh security audit or WPF/full-solution build ran. No database mutation, broker
request/order, app restart or scheduler installation accompanied this increment.
See [ADR-0083](adr/0083-opening-account-values-and-shared-engine-hosting.md).

## Known gaps and risks

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
The September 22 bounded documentation pass changed navigation and presentation only.
