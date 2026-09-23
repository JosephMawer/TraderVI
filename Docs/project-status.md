# TraderVI Project Status

**Current record:** September 22, 2026, with native read-only evidence at 22:25 Toronto.
Operational evidence is dated per row; source implementation does not imply installation.

## Executive summary

TraderVI runs independent paper strategies and has a connected Wealthsimple TFSA with saved
broker history. **Automatic real strategy trading is off; September 23 launch is NO-GO.** The operator
selected the ungated Breakout three-position portfolio for review. Broker projection, setup UI,
synchronization, protection handover and durable supervised execution are implemented in an isolated
source branch and tested offline. Qualified native risk evidence, installed storage, native order
qualification and supervised host rollout remain unfinished. The root goal is one deliberately approved complete strategy
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
| Broker connection/history | Sep 22, 22:25:27 | Signed-in read-only history, holdings, pending orders and regular day graph succeeded; activity checkpoint stable and observed holdings match. No import or order ran. Local journal, complete effective cash history, native opening method and order lifecycle remain unqualified. Sep 16 remains the last saved linked-history refresh evidence. |
| Storage and desktop | Sep 17, installed and observed | Compact snapshot migrations 037/038 and UI-worker fix installed. All 4,472 historical/current snapshots verified; subsequent compact writes observed. Full-history reconstruction still consumes background CPU/memory. |
| Backups | Sep 22 recovery | SQL checksum and local-copy SHA-256 verified; 842.05 MB backup/copy. Latest-two retention installed Sep 17. OneDrive cloud synchronization and a test restore remain unverified. |

Private evidence directories: `artifacts/nightly-readiness-20260922/`, `artifacts/app-health-20260922/`,
`artifacts/broker-import-20260916/`, `artifacts/live-storage-20260917/`, and `artifacts/ui-freeze-20260917/`.
Detailed dated accounts remain in the [status history](project-status-history-20260922.md#executive-summary).

## Implemented versus operational

| Capability | Current boundary |
|---|---|
| Paper strategies and manual recommendation selection | Implemented; migrations through the comparison/selection milestones are installed. Recommendation approval under ADR-0062 does not submit broker orders. |
| Broker history | Migration 034 installed; permanent account link and native refresh verified. Existing paper/legacy records remain independent. |
| Initial real-account risk references | Preparation/save/recovery and an account setup screen implemented. Migration 035 prepared, uninstalled; no initial operational baseline or installed setup UI. |
| Daily risk continuation | Boundary valuation/references/holds tested. Broker orchestration calls the existing continuation workflow. Migration 039 uninstalled; qualified native source and operational verification absent. |
| Cash-flow continuation | Accepted percentage preservation implemented/tested. Broker projection adopts the continued reference and preserves holds. Complete event/effective timing and matched native books remain unqualified; the default source blocks new risk. |
| Broker inputs and execution | Exact comparison profile, real position cycles/costs, synchronization, preserved protection, immutable approval/order intent and restart recovery implemented/tested. Production composition reuses existing clients, journal and gates. Migration 040 prepared only. No qualified execution host, native mutation evidence or automation activation. |
| Hosting | Nightly 00:30 schedule and 07:00 supervision were installed previously; Sep 22 required recovery. Intraday monitoring depends on the desktop/console host. Standalone pre-session hosting is future work, with one active owner. |

## Verified development baseline

September 22: **1,640 Core tests passed**, including 45 new managed-session/transport cases. WPF builds with
261 existing compiler warnings and zero errors; the database project separately builds using Visual
Studio SSDT. No full-solution health claim or fresh dependency-security audit. Existing reviewed work
was checkpointed as `761fb78`; the new work is isolated in `codex/supervised-tfsa-20260923` under
`artifacts/worktrees/supervised-tfsa-20260923`.

The [activation review](reviews/supervised-session-20260923.md) owns detailed validation and the no-go
evidence list. Local ignored evidence is in that worktree's `artifacts/launch-validation/`.
Read-only broker/SQL verification ran; no database mutation, order, app restart or automation ran.
Migrations 035/039 were absent in native schema reads and 040 is newly prepared. The prior 1,595-test
cash-flow baseline remains under `artifacts/tfsa-cash-flow-20260922/`.
See [ADR-0084](adr/0084-supervised-broker-strategy-session.md) and
[ADR-0083](adr/0083-opening-account-values-and-shared-engine-hosting.md).

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
The earlier September 22 bounded documentation pass changed navigation only; this later execution
increment adds source/test evidence and explicitly dated native read-only verification.
