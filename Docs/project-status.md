# TraderVI Project Status

**Current record:** September 23, 2026. Operational evidence is dated per row below;
the storage inspection did not rerun or reverify application behavior.

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
| Nightly pipeline | Sep 22, completed 09:25:33; SQL checked 09:30 | Missed run recovered under operator authority. Hermes inserted 482 daily bars with no collection failures; Delphi saved a Valid publication using Sep 21 data. Athena completed with Attention: 1,124 valid outcomes and seven missing historical outcomes. |
| Delphi inputs | Sep 22, 09:30 | Active daily identity is `v3.4-observed-benchmarks`. Publication: 211 candidates, 422 lens rows, zero source-observation mismatches, 25 Hold rows per lens blocked by Breadth. XIU/SPY and checked derived series are current; one active symbol is older. Publication is not buy eligibility. |
| Live and Shadow | Sep 22, 09:59 | One responding Release WPF host and one advancing Live lease. Latest two Live cycles each had 40/40 usable receipts; three portfolios saved complete 09:55 values. All 12 Shadow sessions adopted today's publication; all 13 open positions advanced to the bar ending 09:55. |
| Live limitations | Sep 22, 09:59 | Held comparison account lacks opening NAV (net asset value): today's return is unavailable and existing entry rules block new buys. Coverage remains Blocked with a host gap and earlier missing/failed receipts. One tracked symbol's 15-minute bar conflicted on three polls. Latest-cycle recovery does not establish uninterrupted coverage. |
| Broker connection/history | Sep 16, 22:06 | Native TFSA reads and linked history refresh verified: 32 orders already saved, no current import blocker. This does not prove complete cash-event history or strategy execution readiness. |
| Storage and desktop | Sep 17, installed and observed | Compact snapshot migrations 037/038 and UI-worker fix installed. All 4,472 historical/current snapshots verified; subsequent compact writes observed. Full-history reconstruction still consumes background CPU/memory. |
| Disk capacity | Sep 23, read-only inspection | TraderDB files reserve 71.891 GiB: data uses 6.496 of 67.508 GiB, log uses 0.058 of 4.383 GiB. The repository occupies about 45.49 GiB, including 36.66 GiB of artifacts (14.35 GiB nightly). Staging and local OneDrive each have two completed backup files totaling 1.96 GiB per location. No cleanup or shrink performed. |
| Backups | Sep 22 recovery | SQL checksum and local-copy SHA-256 verified; 842.05 MB backup/copy. Latest-two retention installed Sep 17. OneDrive cloud synchronization and a test restore remain unverified. |

Private evidence directories: `artifacts/nightly-readiness-20260922/`, `artifacts/app-health-20260922/`,
`artifacts/broker-import-20260916/`, `artifacts/live-storage-20260917/`, and `artifacts/ui-freeze-20260917/`.
Detailed dated accounts remain in the [status history](project-status-history-20260922.md#executive-summary).

Storage inventory: private `artifacts/disk-audit-20260923/`; filesystem allocation estimates and
SELECT-only SQL catalog/space queries. About 61.012 GiB is reusable inside the data file; this is not
Windows free space or a guaranteed shrink yield. Backup verification dates above remain unchanged.
Build/artifact retention and any separately reviewed one-time shrink remain
[open](reviews/open-questions.md#backup-capacity-and-retention); no trading records were inspected.

## Implemented versus operational

| Capability | Current boundary |
|---|---|
| Paper strategies and manual recommendation selection | Implemented; migrations through the comparison/selection milestones are installed. Recommendation approval under ADR-0062 does not submit broker orders. |
| Broker history | Migration 034 installed; permanent account link and native refresh verified. Existing paper/legacy records remain independent. |
| Initial real-account risk references | Preparation, reviewed saving and uncertain-save recovery implemented/tested. Migration 035 is prepared, uninstalled; initial operational setup and broker-input cutover are absent. |
| Daily risk continuation | Boundary-book valuation and append-only references/holds implemented/tested for positively verified intervals without deposits/withdrawals. Migration 039 is prepared, uninstalled; no production caller. |
| Cash-flow continuation | Percentage preservation accepted Sep 22 and implemented/tested with matched books, preserved holds and durable duplicate protection. Complete broker event coverage/effective cash timing and native morning-value qualification remain unverified; no production caller. |
| Orders and protection | Manual buy/sell clients and controls exist. Native mutation/lifecycle verification, strategy protection handover, durable activation/execution and automation qualification remain. |
| Hosting | Nightly 00:30 schedule and 07:00 supervision were installed previously; Sep 22 required recovery. Intraday monitoring depends on the desktop/console host. Standalone pre-session hosting is future work, with one active owner. |

## Verified development baseline

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
