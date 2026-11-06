# Engine and Paper restart session reconciliation

Reviewed November 5, 2026; times below are Toronto unless stated otherwise. This recovers the relevant
October 21–November 5 work, including the immediately preceding cause of the requested two-week window.
The problem was hours of historical checking delaying restart and current trading; the parent problem
was coupling current readiness to accumulated history. The goal remains prompt recovery of approved
trading and existing protection, with truthful, separately scoped historical verification.

## Scope and evidence

Eighteen relevant active/archived Codex chats were paged through their available history and screened for
restart decisions, results and follow-ups. The source index below retains stable IDs. Events and explicit
operator corrections were reconciled chronologically across overlapping chats, rather than treating the
last-edited document or a chat's title as the newest decision. This is not a claim that every unrelated
chat, attachment or original command output was re-audited. Current source and owning records were
cross-checked; no operational state was refreshed in this review.

The [execution checklist](../strategy-alignment-implementation-checklist.md) owns acceptance,
[status](../project-status.md) owns dated current facts, and [roadmap](../roadmap.md) owns priority.
This report owns the cross-chat chronology, supersession and recovered evidence, not a parallel backlog.
GitHub issues were read only for ownership: several bodies predate subsequent approved/installed work.
They were not changed, and their old checkboxes were not treated as current implementation facts.

## Chronological reconciliation

### October 21–23: separate live work from Paper history

S01–S03 and S05–S06 show long Paper cycles, repeated snapshot/hash work and shared settings-lock waits
holding up Real evaluation. Asynchronous display work did not remove that dependency. The accepted
[ADR-0103](../adr/0103-real-execution-independent-of-paper-research.md) direction separated engine,
Real/Paper work and shared evidence; installed October 22 with migrations 047–051. Dashboard closure
could leave the engine running. [ADR-0104](../adr/0104-scoped-recovery-and-sequential-history-audits.md)
then made recovery sequential and scoped: Real completed its own 260-unit proof while Paper retained
its restrictions; the original 205,058-unit Paper proof completed October 22 at 23:43:48.

These were completed restoration milestones, not evidence that every future restart or audit cost was
bounded. The detailed [implementation record](independent-engine-implementation-20261022.md) retains
the historical defects, failed receipts and native limits. Do not reopen those finite passes as new work.

### October 26–29: recovery correctness, collection and another reboot

Historical approval representation repairs restored exact original fingerprints instead of weakening
current validation. Ongoing management under [ADR-0109](../adr/0109-explicit-ongoing-delphi-live-management.md)
removed the repeated daily financial-approval requirement while retaining explicit changed-build review.
The independent collector/legacy relay was installed October 28 under
[ADR-0111](../adr/0111-independent-collector-and-recovered-confirmation.md).

The later accepted confirmation rule allows validated recovered observations to supply both qualifying
observations without another post-restart wait, provided the latest endpoint is current and qualifying.
That successor is implemented/tested, not a financially adopted one-minute strategy; the approved Real
strategy remains five-minute. Earlier fresh-after-recovery waiting descriptions must not override it.

[ADR-0115](../adr/0115-exact-audit-prefix-hash-reuse.md)'s installed exact-prefix reuse improved synthetic
hashing but did not eliminate lifetime cost: one native recovery still took about 2h51m. A subsequent
reboot started another 299,036-unit recovery. The expiring, process-bound
[ADR-0116](../adr/0116-temporary-paper-continuation-during-reboot-recovery.md) exception restored Paper
temporarily at October 29 02:49; genuine clearance at 09:28 superseded that exception. #15's original
restriction-restoration work is complete. Preserve missed observations as missed; no hypothetical
unfilled trade is established by an unavailable interval.

### October 30–November 1: compact Real restart and explicit scope limits

S04's sequential design review accepted [ADR-0117](../adr/0117-compact-trading-current-storage.md): current
account/instructions/protection plus unfinished original requests, seven scalar tables and two alternating
commit markers. Completed receipts remain stored but leave ordinary startup's work set. The markers
identify committed saves; they are not two full rollback snapshots. Uncertain buys retain reservations;
an independently verified protective exit can proceed on another position. Saved original exit decisions
survive restart without requiring a second price crossing or a redundant account read before the sale.

Migrations 054/055 and compact restart were installed November 1 (`e507729c`). The observed live account
check took 2.88 seconds with no unfinished orders; isolated interrupted-order/protection rehearsals
provide additional, explicitly synthetic evidence. Neither establishes a universal startup SLA. The
isolated SQL read missed the proposed p95/p99 read targets even with a smaller book, so realistic load,
contention, broker latency and failure acceptance remain.

The latest operator clarification on November 1 explicitly kept ordinary strategy history-read/write
optimization as a separate proposal, not a mandatory second restart phase. Grouped order-detail reads
passed offline tests and a read-only comparison (48 requests versus eight for the same observed history),
but were not adopted by the trading reader. An unchanged activity fingerprint cannot prove unchanged
older fees/net fill values; a delta-only design still needs correction coverage. The operator chose
fix-forward for faults in the installed restart, superseding the earlier proposed return to the old writer.

Exceptional damage/old-backup redesign (#25), retention (#16) and SQL cleanup (#26) were explicitly split
out. They are not prerequisites for ordinary restart, and that deferral never authorizes trusting a
divergent database or missing instructions. No three-month purge or general manual-fill adoption was agreed.

### November 2–3: finish original-order handoffs, then clear Paper separately

S07–S10 and S15 record compact-closed sells failing the managed-history handoff, original protection
ownership missing across build review, and broker/local timestamp ordering rejecting an original fill.
The repairs preserved original requests, immutable settings successors, protection and separate enable;
the November 3 original sells reconciled once. The permanent receipt-time repair then passed normal
restart and fresh Real evaluation. These are implemented/installed fixes, not unstarted backlog items.
See the [November 2 investigation](wealthsimple-auth-and-engine-diagnosis-20261102.md) and ADR-0117's
dated clarifications. [ADR-0125](../adr/0125-reusable-strategy-sale-proceeds.md) separately approves
reusable reconciled sale proceeds; legacy sales-contribute-zero terms must not overwrite that permission.

The separate 212,508-unit Paper proof completed November 3 at 23:42:09; supported Paper-only clearance
followed at 23:57:44, with five previously held workers resuming. Prior failed receipts remain historical.
The [profiling review](paper-recovery-bottleneck-20261103.md) measured about 2h12m total and concentrated
CPU cost in fingerprint verification; position changes limited prefix reuse. That evidence motivated
the separately reviewed Paper design below, not another mandatory lifetime-hashing optimization.

### November 4: installed Paper checkpoint restart and incremental verification

S11 records explicit approval of [ADR-0127](../adr/0127-paper-checkpoint-restart-and-incremental-verification.md):
adopt the completed cleared baseline, reuse `EnginePaperWorkingStore`, qualify current state and original
pending actions, verify only subsequent changes at night and scope findings to their actual owner.
No new Paper account store/schema was needed. A delta is the changes since the saved verified boundary;
new references to older content still require verification. Boundary/progress saves must be atomic.

Six isolated-copy phases passed. A copied database changes recovery identity; its synthetic receipt was
isolated test setup, never authority to manufacture a production baseline. The authorized production
baseline adoption took 0.742 seconds with no intervening legacy tail. Normal startup qualified all three
Paper portfolios within 2.967 seconds including process/control observation; a later normal restart
qualified them within 3.3 seconds. Automatic Paper NewHistory scheduling verified zero units.
See [copy and installation evidence](paper-restart-copy-rehearsal-20261104.md).

Automatic weekly/full Paper scans are disabled. Full Paper verification is an explicit separately
reviewed need; Real/shared weekly verification remains. Source inspection of
[IndependentAuditService](../../Core/Runtime/IndependentAuditService.cs) confirms separate Paper delta
scheduling and Real archive scheduling, with ordinary work outside the protected market window.
The 4,096-record/five-second budget is checked between records; it is not a hard bound on one expensive
record or SQL call. Nonempty native scheduled delta, actual pending-action restart, interruption and
representative load remain open; zero-unit scheduling and isolated tests do not close them.

### Publication recovery and dashboard reconnection

The installed November 4 [#24 repair](verified-symbol-mapping-20261104.md#live-transition) preserved the
original filled buy, restored matching broker bids/protection and completed an ordinary protective sale.
The 15:29 stale-publication observation was taken just after the 15:28:25 restart. S12's later investigation
and ignored `artifacts/shared-publication-20261104/findings.json` show the scheduler selected 15:30 and
waited its normal two-minute collection offset:

| Bar | Published | Paper applied |
|---|---|---|
| 15:30 | 15:32:12 | 15:32:24 |
| 15:35 | 15:37:24 | 15:37:28 |
| 15:40 | 15:42:34 | 15:42:43 |

At 15:42:44 Real consumed the 15:40 inputs with no entry-risk/valuation block. Through 15:43:21 the
remaining protection matched, recent bid receipts existed and no managed order remained unfinished.
This was engine diagnostics plus hash-verified saved SQL evidence, not an independent broker call.
All three cycles still had immature indicators/no confirmed entries. The apparent persistent publication
stall is resolved by this later evidence; uninterrupted readiness and prospective new-holding acceptance
remain separate. No further runtime repair, restart or gate relaxation was indicated.

The dashboard independently retained a 15:26 holdings snapshot, with failed refresh backoff reaching
30 minutes. The likely explanation was an engine-issued account ticket retained across a restart whose
connection revision repeated. The exact failing request was not captured. Operator-authorized reopening
of only the dashboard restored holdings/history refresh at 15:53:36, supporting the diagnosis without
proving it. Automatic reconnection remains unimplemented. The November 3 independent-holdings fallback
fix is distinct; it does not establish recovery of stale account tickets. Current
[view-model code](../../TraderVI.WPF/Viewmodels/WealthsimpleAccountViewModel.cs) still resets on session
revision/session loss and retains failure backoff up to 30 minutes.

Display-only distinctions for fresh-input warm-up versus mature/no signal, and collector between-window
waiting versus actual failure, were suggested but not approved as mandatory work. Keep them as proposals;
neither justifies relaxing entry freshness or confirmation.

### November 5 host lifetime and remaining boundaries

S17's 09:11 check was pre-open. Its later 10:06 check reported fresh Real evaluation with no account-risk
or quote-read blocks, superseding the statement that no November 5 market-hour evaluation had been
observed. This is a recovered chat report, not a new operational check or sustained Paper acceptance.

S16 records all three hosts stopped at 21:13; a Codex sandbox-service update at 20:26:09 coincided with
the last collector receipt. Shared process ancestry was plausible, not a proven individual termination
cause. Authorized restart at 21:17 restored 15 engine workers with no consecutive failures, collector
OutsideSession and a connected dashboard at 21:18.

The operator requested independence from Codex and ordinary build outputs. `647da521` prepared manual
Windows task launchers and built the collector with 90 passing tests, but **registration was declined**
and work stopped. No tasks were installed or running hosts replaced by that work. A source build folder
does not determine process ancestry. Engine/dashboard already used ordinary Release output; the
collector still used its prior artifact package. [ADR-0128](../adr/0128-windows-owned-application-launch.md)
now preserves that later disposition: review before installation, no inferred automatic reboot/login
schedule and no claim of already-proven Codex independence.

S13/S18 reaffirm ordinary build outputs or the reusable isolated build directory. Completed one-time
cleanup and nightly execution from selected existing outputs do not establish a recurring retention
policy. They should not become another prerequisite for the installed restart path.

## Decisions that must not regress

- Current Real recovery and protection must not wait on unrelated Paper research or full lifetime history;
  genuinely shared/unknown integrity failures retain their applicable safeguards.
- Paper uses its adopted verified baseline plus current qualification and nightly deltas, not automatic
  full/weekly Paper replay or renewal of October 29's temporary permission.
- Preserve original orders, uncertain reservations, raised protection, pause/permission state and evidence
  times; no second submission, invented fill, reset protection or rewritten missed observation.
- Validated recovered confirmation may avoid an extra wait only within its qualified successor contract;
  the installed five-minute strategy is not silently upgraded.
- Ordinary trading history optimization is a proposal; old-backup redesign and retention are separate
  deferrals, and Windows task installation was declined rather than completed.
- Measurements are scoped observations: 2.88-second Real and roughly three-second Paper checks do not
  prove power-loss, pending-order, market-readiness or large-archive acceptance in all circumstances.

## Outstanding work and existing ownership

Each row is a concise extraction, with acceptance retained in the linked checklist/issue owner; this is
preparation for later issue reconciliation, not authorization to update GitHub or perform native tests.

| Outstanding item | Existing owner / disposition |
|---|---|
| Verify nonempty scheduled Paper deltas, pending-action expiry/protection across restart and sustained market-session operation. | #28; ADR-0127 acceptance in the [execution checklist](../strategy-alignment-implementation-checklist.md). |
| Complete representative startup/audit latency, SQL contention, memory and interruption/ownership qualification while proving unrelated historical work cannot stall current trading. | #13/#14/#19; preserve already-passed proofs and separate native authority. |
| Qualify independent collector/trader outage recovery and recovered confirmation before any explicitly reviewed successor adoption. | #5/#6; [collector acceptance](../strategy-alignment-implementation-checklist.md#independent-collector-and-recovered-confirmation--october-27-overnight). |
| Reproduce and fix dashboard account reconnection after engine restart so fresh holdings/history return without reopening the app. | Recovered follow-up; [restart follow-through](../strategy-alignment-implementation-checklist.md#restart-follow-through--november-5-reconciliation), not yet matched to a dedicated issue. |
| Review and complete Codex-independent hosting and the collector's ordinary-output move, then verify ownership and deliberate-stop behavior. | ADR-0128 / #5 hosting boundary; prepared mechanism uninstalled, review required before installation. |
| Retain safe execution failure causes, retry state and recovery evidence without changing order or permission behavior. | #23; current generic catch in [BrokerExecutionHost](../../Core/Trader/Promotion/BrokerExecutionHost.cs) remains. |
| Verify sustained protective operation and the next naturally occurring qualifying new-holding path through subscriptions, publication and reconciliation. | #19/#20; #24's installed repair and bounded existing-holding evidence are complete. |

Separate proposals/deferrals: ordinary history/read-write optimization, damaged-state/old-backup design
(#25), retention/SQL cleanup (#16/#26), optional clock-skew monitoring, display wording and general
manual-fill adoption. Preserve these for review without converting them into accepted implementation
requirements or reviving completed #15 restoration. Existing #19/#24/#28 descriptions need later
reconciliation with the installed evidence rather than duplicate new issues.

## Source index

These are retrieval IDs, not independent backlog entries. Titles may later change; the IDs remain the
source pointers. S11's title is shown as returned by the app. Overlapping chats were reviewed by event
time; row order is an index, not a claim that each entire chat preceded the next.

| Ref | Codex chat | Stable ID |
|---|---|---|
| S01 | Diagnose paper-cycle execution waits (archived) | `01a1614f-7a99-77b2-9b4e-df5c6f1a0b86` |
| S02 | Complete Oct 23 readiness checks | `01a166b7-be01-7c91-b770-7299158e789a` |
| S03 | Assess readiness and supervise | `01a17852-d4a3-7023-8e4f-1c5e3f22d718` |
| S04 | Redesign engine restart recovery | `01a18def-9f8a-7c10-b18e-a05986e17746` |
| S05 | Prepare Delphi Live strategy | `01a15e86-45f0-7cb3-8a4b-624a0d2ee60d` |
| S06 | Update Delphi Live approval and data | `01a17b08-7bd0-77b3-bb45-42137b2331f6` |
| S07 | Fix Wealthsimple login loop | `01a19d93-f083-7b73-87a0-c2c2fe01d236` |
| S08 | Investigate engine reconciliation fault | `01a19dd4-7ac4-7f50-9685-593b7d040e96` |
| S09 | Verify engine and collector readiness | `01a19fde-9afc-7651-915c-4f2893718e4f` |
| S10 | Monitor TraderVI through market open | `01a1a1c7-33e6-76d3-b4a9-a5085a914d9b` |
| S11 | Review Paper checkpoint recovery — issue… | `01a1a4b5-fe31-7e23-a2c4-f5f7df92733b` |
| S12 | Trace stalled strategy publication | `01a1a84a-55b9-7621-847a-824d78061e3c` |
| S13 | Find TraderVI disk growth | `01a19751-a3b8-7981-8852-3efeafb6e391` |
| S14 | Check trading page refresh | `01a1a7c9-d12c-7d22-b359-dea9fc4d504b` |
| S15 | Fix Wealthsimple holdings display | `01a1a2dd-d1be-7db0-9c6b-2df07d807c80` |
| S16 | Move trade history to its own tab | `01a1ad46-5267-7213-98a4-065ca5142246` |
| S17 | Check TraderVI nightly runs | `01a1ac0b-647f-7373-b7f7-ffbe03d607d8` |
| S18 | Consolidate project status (archived) | `01a19a94-f8f0-7a13-9689-b82161e73860` |

## Review boundary

The changed ADRs were reviewed as historical decision records, preserving evidence and explicitly
identifying supersession. The execution checklist was reviewed for ownership, duplicate/stale restart
claims, mixed completion states, dated approvals, links and unresolved work; safe corrections remain
within its role. Unrelated strategy/AI qualification was not re-certified. Registry dates attest to this
documentation review, not renewed operational verification. No code, database, broker, running host,
trading permission or GitHub issue was changed by the reconciliation.
