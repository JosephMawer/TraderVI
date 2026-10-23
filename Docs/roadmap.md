# TraderVI Roadmap

This is the authoritative priority list. [Project status](project-status.md) owns dated evidence;
the [strategy alignment checklist](strategy-alignment-implementation-checklist.md) owns execution
dependencies. [Design rules](design-rules.md) and applicable accepted ADRs own behavior.
The [session-work register](session-work-register.md) preserves cross-chat progress, unfinished ideas
and source review points; use its owning-record links when resuming work.

## Accepted direction — prove strategies, then adopt one deliberately

**Current request — complete the revised architecture:** ADRs0103/0104 independence, shared/current
storage and scoped sequential recovery are installed. Real recovery and authorized real-only hold
clearance pass, including the separately authorized post-Windows-restart proof/clearance. Paper completes
its original recovery plan with eight restrictions retained. ADR-0105 is installed in normal mode.
October 23 nightly/daily/exact-model checks pass; Joseph's own continuation approval then
fails at the account-risk event size guard. The ADR-0106 correction passes offline preservation/size,
focused/full Core and Release checks and is installed through a guarded normal stop/restart in a090757f.
The next operator attempt saves the October23 risk event but fails at the permission archive with SQL547.
The tested archive correction d8364eff is installed under explicit authority. Joseph's fresh approval
and separate enable now succeed: revision260 links to unchanged risk sequence3, and the host is armed
for09:30. Pre-open account/daily/model/hash and scoped-hold checks pass. The later market-hours
investigation finds fresh collection but no published signal inputs: the real-only source adapter
retains the original comparison policy during admission despite a validated progressive successor.
Joseph now authorizes correction, tests and installation. The adapter/diagnostics are implemented;
full Core/Release validation and ADR-0107 current-session build review pass.1a790e60 installs10:58;
the real-source review follow-up5144535c installs11:10:57. Joseph's updated-build approval and separate
enable succeed. Fresh real consumption produces a confirmed entry on the11:45 bar, but buy preflight
fails SQL51501 by reacquiring its held account lock through a separate risk transaction. Joseph
authorizes a tested correction and normal restart installation. Correction ba3ece6a is installed at
12:09:37. Joseph's build approval898 and separate enable pass; native12:18 checks verify fresh real
consumption through12:15. Risk reads now use the existing safety transaction. Observe the next
qualified entry's native preflight and first fill. The requested12:37 checkpoint is complete: fresh12:35
inputs are consumed,27 symbols are Watching,one is dismissed for weakening and12 remain warming/unavailable.
No confirmed entry reaches the corrected buy checks; no new Real blocker is observed.
The first post-restart strong reading at12:50 breaks confirmation at12:55; its13:00 reading has only
one supportive family. Await three-of-four support on two consecutive valid observations before
evaluating native buy-check recovery.
Continue the first-fill watch without weakening existing controls or erasing source gaps.
The S54 one-date morning readiness check is complete and its heartbeat removed. Actual trades depend on
qualified live inputs and existing risk/gate rules. [Current blocker and evidence](reviews/independent-engine-implementation-20261022.md#october-23-overnight-readiness).
Joseph's subsequent request adds a separate same-chat five-minute09:30–16:05 watch for the first
confirmed Delphi fill and material blockers; it changes no execution authority or recovery policy.

Review retained Paper restrictions and complete native concurrency, market-hours latency, interruption/restore
and weekly capacity acceptance. Preserve exact permission, freshness and financial history. Measure
residual full broker-append/historical-hash cost and storage growth; extend paths if capacity fails.
Backup/migrations047–051/checkpoints are complete; nightly scheduling is restored. The overnight target
was missed. Retention decisions remain separate; no disk reclamation is claimed.
[Current implementation and cutover review](reviews/independent-engine-implementation-20261022.md);
[accepted architecture](adr/0103-real-execution-independent-of-paper-research.md).

**Earlier request — remaining SQL reliability only:** implement/validate ADR-0102's bounded exact
evaluation-save recovery and fixed private stages.71focused/2,919Core pass/nine native SQL skips
with test concurrency disabled; isolated WPF Release/SQL parsing and unchanged-guard comparison pass.
Standard Release fromc4d094fd is installed21:31Toronto after normal closure/zero-owner verification;
native acceptance remains. Original SQL wait/plan and heavy paper
history cost are unresolved; the operator defers a workload switch. Preserve all risk/dated approval
controls. [Owning work](reviews/delphi-live-sql-reliability-20261021.md).

**Earlier lag/diagnostics preparation:** finish remaining Live processing optimization and exact
confirmed-signal/order diagnostics without weakening any control. ADR-0099 write optimization and
ADR-0100 durable decision/quote/preflight evidence are implemented;2,852Core checks pass/nine skips
with test concurrency disabled, isolated WPF Release passes. Standard Release from97ad0a11 is
installed21:10Toronto after normal closure/zero-owner verification; native timing remains. Today's final16:06
read and heartbeat deletion are complete; no orders/fills/post-cutoff intents, permission expired16:00.
The explicitly requested October 22 review-only path is implemented under ADR-0101; 386 related
checks and isolated Release build pass; final 2,885 Core pass/nine native SQL skips (2,894 total),
with test concurrency disabled. Installation is complete; operator review/approval and separate
enable on October 22 remain, followed by native latency/decision-publication verification. Preparing for
tomorrow does not renew the dated scope. [Owning work](reviews/delphi-live-lag-and-decisions-20261021.md).

**Earlier October21 milestones:** the [first snapshot correction](reviews/delphi-live-snapshot-slowdown-20261021.md)
was installed14:02, operator-approved/enabled and verified with initial20minute recovery14:31.
Later confirmations were transient; their native quote outcomes were not persisted. The completed
watch retains historicalBlockedcoverage and unproved whole-day/lower-host-latency acceptance.
Preserve Portfolio rules only, financial/signal history,15:45entry cutoff and original16:00expiry.
The older SQL-save retry draft stays parked separately; do not restore it over reviewed source.

Local diagnostics now use separate installed [SQL](../Operations/ReadOnlyChecks/README.md) and
[broker-read](../Operations/BrokerReadOnlyChecks/README.md) helpers (ADRs 0091/0093). Reload existing
Codex chats and use their fixed commands. Read access does not authorize operational writes or
strategy activation; this supporting workflow changes no trading priority.

Keep independent paper strategies, deliberately approve an exact complete strategy for the permanent
TFSA, and enable real execution only after the required account, protection and broker checks.
[ADR-0055](adr/0055-independent-strategies-to-approved-live-execution.md) and
[ADR-0062](adr/0062-complete-strategy-recommendation-promotion.md) distinguish recommendation approval
from automatic trading. The operator's September 23 real-trading target is an aspiration, not verified
readiness or order authority. Any portfolio must be promotable, with ongoing management until pause
or switch. Manual buys stay outside by default, with explicit purchase-time or later handover into
exit management alongside the strategy's normal automatic positions
([ADR-0085](adr/0085-portfolio-wide-ongoing-tfsa-management.md)); ungated Breakout Top 3 is an
initial candidate. The [September 23 activation review](reviews/supervised-session-20260923.md) says NO-GO.

## Now — restore a dependable advisory loop

**October 21 source switch:** operator explicitly requests implementation now of existing
**Delphi Live · Portfolio rules only** instead of Normal. The prospective source-switch workflow
and explicit desktop review/approval are implemented under
[ADR-0097](adr/0097-reviewed-october21-portfolio-rules-only-source-switch.md). Offline validation
passes and standard Release is installed after normal closure/zero-owner verification. Operator
source approval/separate enable and first real evaluation are verified12:44. Next: read-only watch
of fresh post-approval market inputs, sustained real execution, orders/blockers and today's cutoff.
The7m19s setup wait drains12:42:30; snapshot/action cost and narrower coordination remain an open
investigation. This source switch does not install the deferred SQL reliability fix.
After another13:07 deadline failure, real input processing recovers13:18 with a new13:10 continuity
window. Monitor sustained progress/maturity/confirmation while keeping the separately deferred
reliability work explicit; no qualifying signal or order is established.
Current wait includes20m uninterrupted measurement history plus two consecutive Strong readings;
observed paper/host processing still adds minutes. Timing estimates depend on continuity, not an
order target or authorization to bypass confirmation.
Preserve original financial history/risk, intraday/data/order controls and 16:00 expiry.
[Facts](project-status.md#october-21-post-open-execution-watch),
[operational dependencies](strategy-alignment-implementation-checklist.md#paper-strategy-to-real-tfsa--current-checklist).
No current approval silently expands to another complete strategy.

**October 21 preparation:** the operator accepts the [dated continuation contract](reviews/delphi-live-continuation-contract-20261021.md).
Append-only existing-assignment continuation is implemented. The operator subsequently accepts
[ADR-0095](adr/0095-current-readiness-without-daily-verification-replay.md): retain completed broker
tests as development evidence and check current account/history/orders, risk, exact Normal/source/build,
storage and dated permission without another test packet or verification restart. The simplification
passes 243 focused/2,723 full offline tests (nine native skips) and the standard Release build.
The accepted operator-path follow-up removes paper-work waits from setup opening, saved readiness
refresh and host enable only, and corrects historical setup instructions; 274 focused offline tests
and standard Release build pass. Review/approval/execution waits and all timing/financial controls remain.
An [open follow-up](reviews/open-questions.md#tfsa-review-waiting-for-paper-work--october-20-2026)
questions the broad paper/import lock coupling; it is not an added October 21 prerequisite or agreed code scope.
October 21 [pre-open checks](project-status.md#october-21-pre-open-readiness) confirm successful nightly
stages, Valid daily input, immutable storage and a signed-in cash-only/order-free broker snapshot;
one responding normal desktop was observed before the subsequent coordinated build closure.
The operator then accepts [pre-open approval/arming](adr/0096-pre-open-october21-approval-and-armed-host.md):
implemented with 284 focused/2,738 full offline passes (nine native skips) and normal Release build.
Native operator pre-open review/approval/arming is now observed; SQL revision 50 confirms today's
flag/16:00 expiry, and one responding owner matches the tested output. The [first post-open check](project-status.md#october-21-post-open-execution-watch)
verifies initial operation; a subsequent SQL evaluation-save timeout causes a roughly 13-minute gap
before real evaluation resumes at 09:45:45; candidates at that read are Watching/immature, with no
confirmed eligibility or orders. Track the [open timeout cause](reviews/open-questions.md#live-evaluation-save-timeout--october-21-2026)
with implementation now **operator-deferred to tonight** (21:00 Toronto reminder); preserve the
incomplete uninstalled source work and leave the running build unchanged. The 10:18 follow-up shows
fifteen real evaluations and no orders; both previously confirming candidates lost confirmation on
the 10:10 bar and return to Watching. A new 10:51 check finds over eleven minutes without real
evaluation and stale source; local diagnostics identify a 10:43 observation collection-deadline
rejection (52232), distinct from the earlier timeout. The 10:56 read verifies resumed real execution
at 10:52:16 after roughly 12½ minutes. The same deadline failure recurs at 11:07:47; a bounded
11:16 follow-up confirms another recovery at 11:16:46 after roughly twelve minutes, still no orders.
Include this distinct recurring deadline failure in tonight's investigation; the SQL-2 retry draft
does not establish its correction. The fix stays deferred and controls remain.
The11:49 check verifies a third52232 occurrence11:37:51 and real recovery11:46:51 after another
roughly12-minute gap,27real evaluations/no orders; sustained reliability remains unresolved.
The 11:46 no-buy analysis establishes a separate primary gate: Normal admits zero of today's35
published stocks; all50 lens entries first fail Breadth. Intraday momentum confirmation alone cannot
authorize a purchase. Earlier confirmation-only explanations are incomplete; preserve the approved
Normal profile and daily gate. [Evidence](project-status.md#october-21-post-open-execution-watch).
Continue the requested five-minute read-only progress/blocker/order watch through today's cutoff. Sustained progress,
automatic fills and cutoff evidence remain separate; no routine setup clicks are needed. Preserve
original trades/references/holds; no repeated initial setup, silent date extension, inherited host
grant or October 22 renewal. October 20 permission stays expired.

The small [execution-status/buying-permission UI scope](adr/0094-real-tfsa-execution-status-presentation.md)
is accepted. Review the requested [design draft](concepts/real-tfsa-execution-ui-design-draft.md)
before code, then implement/test offline and coordinate normal closure before building over loaded
standard output. Automatic next-morning startup is deferred; authority and October 21 boundaries
remain unchanged.

**October 18 execution request:** prioritize the shortest qualified Delphi Live → TFSA route
for the requested October 19 target, preserving the existing account/protection/execution boundaries.
Normal is the working integration candidate after comparing the three saved books; none is qualified
for automatic execution. Exact Normal preview, Core Live protection/source and opening-sequence
foundations are now implemented/offline-tested. The accepted first-day scope uses actual available
capital after both current holdings sell, no outside transfers/trades, and stops new buys on mismatch.
Ordinary production/older scopes retain their qualification. The separately approved October 19
limited trial replaces only cash-event/opening-value proof with scoped evidence, retains four native
and five scoped checks and stops purchases at the reviewed close. Actual activation remains NO-GO.
Next qualify native risk/storage, exact source, broker lifecycle and one shared execution host before
operator-controlled activation. [Assessment](reviews/delphi-live-tfsa-readiness-20261018.md), [ADR-0092](adr/0092-scoped-supervised-delphi-live-tfsa-session.md).
The default-off host, desktop controls and first cash-start execution review are now built/tested.
Latest native-trial checks pass: 205 focused/2,605 full tests (nine native skips), seven synthetic
renders/WPF Release. Earlier two SQL rehearsals retain their narrower scope and were not repeated.
Native storage checks found/fixed continuation readback.
The scoped producer now recovers the original saved baseline import/full-sale records and composes
protected scoped proof without issuing the remaining native qualifications. Next qualify those four
checks, perform actual sales/setup and review, then operator-controlled activation. General native
transfer/boundary integration remains deferred from the explicitly limited trial, not declared done.
At 10:03 Toronto Monday inputs and current-policy evaluations are published; current executable
freshness still needs review. SQL tests remain paused after the memory concern; only the two bounded
read helpers were expressly resumed. [Morning decisions](reviews/delphi-live-morning-decisions-20261019.md).
The history-enabled v2 broker reader is now separately installed (66 offline/four scope checks);
its native read passes at 10:38 after normal reconnect (33 historical orders/matching holdings,
zero pending orders). Local journal/native submission and partial/uncertain recovery remain separate;
the helper issues no execution proof. The 10:33 small
storage read confirms empty setup tables/enabled triggers. Actual sales/setup and independent native
proof remain prerequisites; no automatic trading is enabled. The separately approved prepared-copy
startup and native setup screen are now observed at 10:52, with a reconciled account import checked
at 10:57. The operator-executed verification sequence and explicit original-sale selection are now
prepared/tested (173 focused/2,573 full, nine native skips; five UI renders). The updated copy's approved
11:21 switch/startup and 11:24 clean import check pass with execution off and empty setup. Next
complete the approved native first-day route below before treating actual activation as ready.

The subsequent explicit native first-day contract is accepted (ADR-0092): exact source/performance
review with disclosed gaps, actual full-fill/manual normal-restart/reference/storage observations,
and a durable automatic-order review hold on unsupported outcomes. Its checkpoint/nine-reference
publisher and first-session-only proof restriction are committed as e054e88f. The native-review copy
is now running with execution off after the 12:07 switch; clean import/empty setup and existing
pause/hold flags are checked at 12:08/12:10. Operator screenshots subsequently show the manual
round trip complete and cash-only/no pending orders. Risk setup remains blocked by a reported blank
Refresh result. A WPF-only repair reproduces/fixes clipped labels and adds explicit status/busy messages;
six synthetic renders/build pass, reviewed Core unchanged. The 14:42 bounded read still finds empty
setup; process inspection finds normal bin/Release. The corrected 74cb0987 risk-setup copy then
starts at 14:45 with one responding owner and no host permission/packet/checkpoint. Next verify
actual preview/save/readback, capture checkpoint, restart and Prepare first execution review.
Only actual matching evidence
publishes the packet; financial approval and host enable remain separate. Broader production and
unobserved native failure paths remain deferred. [Operator steps](reviews/delphi-live-native-qualification-20261019.md).

Latest operator screenshots in bc5a7f09 show successful risk preview from the 14:55 observation and
explicit initial-reference save confirmation. The earlier unexpected failure did not recur; its cause
is unlocated. Reopened setup now confirms the original saved references. Checkpoint capture exposes
a four-column/three-constructor storage-readiness mapping bug. The narrow record fix preserves every
readiness check (257 focused tests/WPF build pass; 2,614 full offline tests pass/nine native skips).
Corrected copy 71207c86 starts at 15:06, hashes/one responding owner/no authority/checkpoint verified.
The next capture still fails generically; native inspection confirms no checkpoint file. A WPF-only
stage/category diagnostic (nine focused checks/build pass, Core unchanged) is committed as a78f0810
and verified running at 15:15 with one owner/no permission, packet or checkpoint. Next inspect the
failing stage. The next native diagnostic is SQL 51413, a settings-lock timeout during reconciliation.
A WPF-only barrier now sequences setup/enabled-host checks after active desktop paper cycles and
defers new cycles during that work (199 focused checks/build pass; bounded wait; SQL fences/Core
unchanged). Coordinated copy 943f964b is verified running at 15:30 as PID 74844, no permission/
packet/checkpoint. Capture now passes reconciliation but rejects Normal's approved progressive
successor as an original-policy mismatch. A bounded native read confirms one active Normal and
allowed successor; the correction preserves original comparison identity and requires its latest
saved assignment plus exact protection-only policy difference (217 focused/2,621 full offline passes,
nine native skips/WPF build pass). A 15:43 bounded read confirms the latest assignment. Corrected
copy 62c7b5fd is installed/verified at 15:46, PID 58536, one responding owner/no authority/packet/
checkpoint. Later native read confirms Normal publication recovered at 16:03:40 and finalization at 16:03:48; host-gap coverage stays blocked. A separate 16:21 expired-lease error comes from repeated finalization. Close-once renewal/release fix passes 37 focused/2,624 full offline passes (nine native skips)/WPF build; cf0eee49 installed/verified at 22:20, PID 1036, hashes/one responding owner/no authority/packet/checkpoint. Overnight publication is not expected. The October 19 trial expired; implement the explicitly accepted [October 20 carry contract](reviews/delphi-live-next-session-contract-20261020.md) before
packet/approval/enable. Its Core/UI integration is now implemented and prepared: **2,655 full offline
passes/nine native skips**, WPF build and six synthetic renders pass. Copy b1095ae9 is installed and
verified at 23:08, PID 83608, exact hashes/one visible responding owner/no authority/packet/checkpoints.
Next complete actual October 20 carry save/source/checkpoint/restart/all nine
checks before manual approval/enable. Native SQL rehearsals stay paused. No baseline
reset, repeated trades or qualification bypass.

After the performance-copy retry still times out, stage logs confirm active Live work spans the
entire wait. Interactive setup now has a prepared cancellable queued wait with elapsed status,
retaining its admission until active work finishes; closing setup cancels the wait only. Nine
focused/2,668 full serial passes, nine native skipped/WPF Release. Separate setup-wait copy 2cf08c33
is launched at 12:27:51, PID 78688, after operator normal closure/verified prior exit; all 125 manifest
files/calendar match, one owner/no authority/packet/checkpoints. Operator screenshots now show
readiness completes and a matching original-reference/cash-only/four-order carry Preview. The
operator saves it; the next screenshot confirms saved/readback with original references/holds
unchanged and no order/assignment/host permission. The next screenshot confirms checkpoint saved;
local read-only inspection verifies its dated file and unchanged 125-file build, no packet/host
permission. Normal-output same-build restart is verified; subsequent operator screenshots show
complete native first review/all nine references and approval available, with host enable disabled.
Protected packet existence is independently observed; operator assignment approval/readback
is subsequently confirmed saved in native UI, Transition state/automatic trading off. Next separate
operator enable/initial native evaluation are subsequently verified below. This changes
neither host polling's timeout nor qualification, and preserves both performance improvements.
Approval and the immediate retry are rejected before append; installed read confirms zero managed
revisions and unchanged recent financial imports. Desktop-only review admission/precise rejection
diagnostics are prepared: 17 focused/2,676 full offline passes, nine native skips, WPF compile passes.
Core/proof identity and every validation guard remain unchanged. Operator normal closure/process
exit is verified and the two UI files are installed in standard Release output at 14:41 Toronto;
123 other runtime/protected checkpoint/packet hashes remain unchanged, no host permission. Next
normal project --no-build restart is subsequently verified (one owner, PID 118432, exact corrected UI/
original Core hashes), and operator screenshot confirms assignment saved/no order/host off at approval.
Operator subsequently enables the exact host; permission creation is observed at 15:19:37 Toronto.
After the first paper wait, the hash-verified 15:22:03 installed read shows synchronization,
activation/protection handover and StrategyEvaluated at 15:20:59, revision four/Active, no saved
blocks/automatic orders/positions. Initial activation/evaluation are verified. Next sustained
cadence and cutoff are subsequently verified below; actual automatic order/populated protection
paths remain unexercised. No forced trade or next-day buying renewal follows. Original rejection
predicate remains unproven; successful approval is not full-production qualification.
Authorized close watch completes with a hash-verified installed read at 16:04:27: sixteen saved
evaluations through 16:00:00.470/revision 49, 67.79–234.20-second gaps, pre-close evaluations unblocked.
Native expired-scope/new-buy-stop blocks are recorded at 16:00:00.148; exact 16:00 expiry and zero
automatic orders/post-cutoff buy intents. Heartbeat deleted, no financial/control mutation. Next
retain the trial's limitations: no attempted rejected-buy or populated order/protection proof,
remaining display/performance work and no next-day renewal under this accepted contract.

Use the operator's normal `master` checkout/`dotnet run --project TraderVI.WPF -c Release` workflow
going forward; no further full artifact copies are needed. The normal Release output now has all
125 captured-build files byte-identical. For this saved checkpoint's one recovery restart use the
same command with `--no-build`. Operator restart at 13:43:30 Toronto, PID 72928, is verified against
all 125 captured hashes; checkpoint present, packet/host permission absent at that check. Later
native UI review displays all nine references and the protected packet now exists; host permission
remains absent. Process restart alone did not qualify recovery.
Calendar user-setting persistence is approved/verified. Obsolete generated-output cleanup follows
verified normal-output recovery, preserving financial proof/evidence; no files are deleted yet.

October 20 Preview times out twice at the two-minute paper-cycle wait, before account checks.
Bounded 10:20 evidence shows progress, not idle. The [worker/display correction and bounded stage
diagnostics](reviews/desktop-paper-responsiveness-20261020.md) are installed under explicit approval
at 11:03:56, execution off; the original next-session copy is preserved. Native wakes still take
7.2/4.8 minutes, dominated by Live snapshot/protection/action work, while bounded dispatcher timing
improves. Serialization follow-up 30775d12 is separately installed under explicit approval at
11:32:14, execution off, after normal completion/closure of the preceding copy; both earlier copies
remain preserved. Four native wakes show bounded UI timing but no clear full-cycle speedup; snapshot/
action work remains expensive. The operator explicitly defers further history-cost investigation/fix
until later. Resume that work within existing integrity/freshness contracts, preserving history and
both previous copies, execution off, separate validation and a separately coordinated switch.
TFSA qualification can continue in its own session; actual setup idle, sustained response/capacity
and same-copy recovery remain unverified.
Preserve all qualification, same-copy proof and the dated trial expiry; financial actions stay separate.
The operator subsequently approves the TFSA-session switch from standard Release back to the
reviewed performance copy. At 11:53:41, 30775d12 (including 55e429d5) starts as PID 102648, 124 manifest
matches/one visible responding owner/no authority/packet/checkpoints. Next retry carry Preview in
this copy; use it again for checkpoint recovery, retaining the October 20 close-time expiry.

**October 18–19 UI track:** Workspace and Trading's main arrangement/explanations are accepted. The
operator authorized Trading-only implementation; its first WPF increment is built and offline-tested.
Today/Overall performance, four-column holdings, selected holding Buy/Sell panel and strategy dropdown
are in source; the dropdown opens the existing transition review. Other tabs remain unchanged.
October 19 feedback replaces the right-hand holding panel with a horizontal panel above the full-width
table. This is built/offline-verified in six synthetic renders; the new layout still needs installation
and operator review. Additional table information is a later refinement; keep the four current columns.
Queue old-strategy-only exits as checks allow, including at a loss, preserving shared holdings/protection;
confirmed required exits reconcile before new buys. Execution/automatic switching remains unfinished.
The operator-authorized Release rebuild/launch is responsive as of October 18, 22:16 Toronto.
The daily follow-up now reads Wealthsimple's native previous-close one-day return with original time
and dated prior-session headings; build and 233 focused tests pass. Current native availability and
operator comparison remain to verify. The earlier daily-reader restart deferral is historical; this
new horizontal layout has not been installed. Next obtain layout feedback and complete verified
decision/strategy execution integration. The separate TFSA track now prepares Normal, without an
activated assignment or qualification waiver; existing recommendation-only comparison limits remain. Remaining-screen order and detailed views stay open;
do not redesign other screens during this bounded implementation pass.
Existing operating priorities and execution prerequisites below remain.
[Design draft](concepts/trading-workspace-redesign-draft.md#october-18-visual-redesign--draft).

September 26 requested direction: add an AI-managed momentum portfolio and use it for real trading,
targeting September 28. AI Explorer is deferred to a future session; the first pass is AI Momentum
in paper and intended live use. [ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md) accepts
discretion, enforced boundaries, reducing risk as losses deepen and no loosening stops to prolong
losing trades. The first API client, shared spending journal and portfolio-proposal contract are
implemented offline under the operator's initial below-CAD-100 monthly ceiling. Codex subscription
access remains an alternative. The operator also raised Dots on October 17; its connection feasibility
is [unresolved](reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026),
with one saved-input replay now returned and validated as a permissible historical wait. No API replacement
or delivery-prerequisite change is agreed. Proceed with implementation of the agreed framework. Portfolio scope
includes all current strategy portfolios and their variants, expanding as new portfolios are built
and integrated (ADR-0087). The operator defers lesson criteria until they are relevant to Step 6;
continue delivery without another design discussion on that topic. Concrete rule mapping,
authoritative packet adapters and production budget/storage configuration are implementation work.
Learning consultation is source-integrated into the supervisor and execution-preparation coordinator;
one post-session learning review is now agreed alongside intraday saved-lesson consultation.
Steps 6–7 will connect qualified outcome evaluation and host scheduling under ADR-0087, leaving immature
or inconclusive cases pending and using the same monthly allowance.
Follow the [AI Momentum delivery sequence](strategy-alignment-implementation-checklist.md#ai-momentum-delivery-sequence):
its [delivery milestones](strategy-alignment-implementation-checklist.md#delivery-milestones) are a
visible AI reviewer, a complete tested paper trader, then supervised live trading. The reviewer is
the next visible target; it uses initial runtime/display portions of Steps 7–8 while the ten-step
checklist retains all paper/live completion requirements. Independent Step 7 development proceeded
while Step 3 awaited regular-session data. The offline review
scheduler, durable scheduling/events/review-block recovery and explicit tick adapter are built/tested.
Structured model-proposed cadence is now built/tested too. Per the September 29 instruction, work returned
to Step 3: its connected check verifies cash-only persistence/recovery and the receipt-clock fix.
Missing/old or differently timed held-stock bars still block invested values; trades after a price boundary
must wait for newer prices. The tested TMX quote fields do not solve freshness: last sales can be old and
bid/ask has no quote timestamp. An existing Wealthsimple quote query includes a source-time field;
the September 30 authorized samples cover all current holdings, with advancing timestamps and some
changed prices. The operator clarifies that TMX remains the normal source. Wealthsimple is only a
possible limited fallback for AI review of delayed/unavailable held-stock inputs, with timestamp
uncertainty explicit. Defer this lower-priority item and the associated valuation questions; no source
switch is planned or implemented. Return to the main visible-reviewer integration work (Steps 7–8).
The Step 7 coordinator connects saved inputs, the existing reviewer and SQL decision storage in
source, preserving current input checks. Migration 042 is installed and native storage checks pass.
The operator selected the existing Breakout 3 / PortfolioRulesOnlyV1 book as the first cash/holdings source.
Local masked key entry and the starting-portfolio inspection query are prepared; the existing Oracle user-secrets key has been located (project status).
Core loads that credential; metadata access and one connected generation with recorded usage are verified.
The initial five-minute interval and open / open+3h / close-15m checkpoints are agreed.
Timing and initial cost bounds are agreed. Opt-in desktop startup/shutdown and the Core review host
are implemented/tested but not activated. Private history is initialized under the accepted ten-minute
first-test input age. The October 1 connected review saved `wait`, a two-minute next-review request and
conservative token/cost accounting; SQL/local evidence matches. Missing downstream dependency locks
caused the absent daily publication. The repair and authorized recovery restored the October 1 publication
to all 15 portfolios; two ungated Continuation choices had proposal-permitted AI context at 10:57.
Other choices retain their existing admission/evidence blocks. Athena's two invalid prediction outcomes
remain a separate follow-up; see project status for the Attention result and dated verification.
The authorized follow-up assembled four eligible choices but exceeded the unchanged input-token cap
before generation. The concise review view is now built/tested and its saved-packet token count fits
the existing cap. Full source packets and eligibility/spending rules remain unchanged. The authorized
October 9 concise-input review returned a valid wait with all choices unavailable; SQL/local evidence,
exact request rendering and cost match (project status). Empty learning was explicitly continued through
today's close with prior history preserved. This qualifies the blocked-choice wait path, not eligible
selection or continuous readiness. The later authorized desktop test verifies startup, blocked-attempt
timing, the bounded stop and disabled restart, but the last compact request exceeds the unchanged input
cap as session evidence grows. After v2 remained oversized, the operator approved removing engineering/
testing details and detailed evidence for unavailable choices. The v3 view is built/tested and its
largest saved example passes the unchanged cap; full source records and independent account/risk
context remain (ADR-0087 and project status). One fresh v3 review is now authorized, but its preflight
found stale invested-account prices; no paid attempt ran. The read-only trace confirms ongoing Live/Shadow
collection with failures and a stalled held-position mark. The authorized fixed-window diagnostic reproduces
five-minute timestamp-alignment rejection on both targets. The follow-up confirms source times are
preserved by mapping in the sample. The October 14 authorized full-session comparison qualifies the
implemented stable-context and supplementary-record correction (ADR-0089); the native opening trace
also identifies receipt-clock rejection, corrected with actual-store-time waiting. Both fixes are
tested and installed; scheduled Live cycles and current portfolio marks recover. The sparse trailing-
record refinement is installed and its first scheduled cycle has zero CollectionFailed receipts;
missing exact bars and historical/canonical conflicts remain explicit. The nine-stock follow-up
identifies an end-boundary omission plus unavailable sparse intervals; the observation-time end
correction and stale-source metadata are installed, with their first scheduled cycle/marks verified
(ADR-0089). Remaining unavailable intervals stay blocked. Next qualify
continuing collection and held-stock valuation through
the agreed input path, then use that single-review authority to qualify v3 interpretation, followed by
repeated successful desktop reviews and the basic display. Future
input growth remains guarded, not continuously qualified. Automatic reviews remain
off; further paid tests/activation require authorization. Collection health and invested coverage remain
unqualified. Timestamp-only inspection is built/tested and the two-target comparison is complete.
Further provider reads need authorization; do not round timestamps or silently discard records.
One invested saved-Shadow value passed under the test bound; continuous coverage remains unqualified.
Defer more permissive
handling of dated/incomplete review inputs unless it blocks integration (ADR-0087, September 30 clarification).
The AI chooses its active portfolio; the existing switching/history contract remains in force.
Normal held-stock coverage, faster invested-value persistence and storage/operational qualification remain
open Step 3 dependencies; preserve stale-input blocks rather than treating deferral as qualification.
Step 1 is complete offline; Step 2's framework and portfolio scope are agreed, with lesson criteria
deliberately deferred. Step 4 has begun with the tested offline continuous-round risk core, shared
assessment contract and scoped local journal/recovery. Step 3 now has tested broker and both paper-family
valuation adapters feeding the shared risk journal, now connected in source to paper saved-record readers.
The maintained portfolio catalogue and account-scoped review-packet builder are also implemented offline.
Native catalogue/decision-reader mapping and saved risk-value arithmetic are checked for all 15 installed
portfolios; source/freshness and Step 7 storage/host qualification remain pending (project status).
Both Daily Shadow and Delphi Live now supply saved signals, account-specific decision/confirmation
state, rules and daily paper performance to those packets. The missing comparison-source investigation
identified a tracked broker-symbol/canonical-symbol mismatch blocking Live session initialization.
The alias lookup and migration 041 are installed under ADR-0088; prospective source adoption and
a fresh Live cycle are verified. The Shadow risk reader now preserves the same legacy policy identities
as the decision reader. Current cash-only Shadow reads now carry checked transaction timestamps;
held prices retain their original freshness limits. Saved price timing is now measured: current
five-minute collection has multi-minute delay and gaps, so one-minute coverage remains unqualified.
Authorized one-minute samples show uneven latest-minute coverage. Raw rows and recent trades now
support quiet trading as the explanation for sampled gaps; the client did not drop those rows.
The one-minute price adapter and combined paper-account reader are built/tested. The reader reconciles
cash/history, rechecks the account after collection and requires fresh prices ending at the same minute.
Combined values now feed the risk journal and AI input packets, with tested history continuity,
explicit same-account source handover and persistent price-conflict blocks. Native after-hours checks
reconcile all 15 paper books and verify rejection/empty-journal recovery. The regular-session check now
verifies cash-only persistence/recovery. The SQL/app receipt-clock discrepancy is fixed and checked
natively without tolerance changes. Invested reads still reject incomplete/expired or differently timed
prices and post-price trades. The bounded quote alternative check leaves source qualification open.
Next identify usable timestamped prices, qualify reliable held-stock coverage, verify fresh invested
values/persistence and freshness/recovery, resolve collection gaps,
connect recurring paper risk observations, and qualify SQL concurrency and broker cash events, then connect
existing risk/execution adapters; source qualification
and activation remain separate. Existing engine daily templates use 7%.
The AI gradual scale now follows continuous losses/recovery across days, replacing its daily framing
and session-minimum lock (ADR-0087). Both starting balance and portfolio peak are selected references.
The accepted 5% / 3% / 1% cycle schedule replaces the AI 7% boundary and stops new buying after
the sequence's third failed round for the remainder of that session. The sequence and attempt count
carry across days without morning refill or renumbering. Failed-round closure and confirmed exits before another attempt are
agreed. Fresh qualifying entry signals from the selected engine permit AI-reviewed re-entry without
an added fixed delay. Proportional buying allowance within each round, with recovery before its
hard limit, is agreed. Round limits and allowance now trail the highest verified value within each
round; preserve the separate overall closing-peak history. The 10% cumulative review hold requiring operator approval and explicit UI
threshold visibility are agreed. Round continuity across closures and new-session engine consultation
are agreed. Three consults complement ongoing intraday monitoring; AI review intervals of one to
fifteen minutes during market hours are agreed, subject to budget. Provider/budget fallback and
fresh-review recovery are agreed (ADR-0087). Initial entry-signal, holding-closure and risk-limit
review hooks are agreed; shared timing/budget checks also apply to later hooks. The
scheduler now combines/deduplicates events, enforces bounded intervals and serializes reviews through
the existing learning-aware supervisor. Account-scoped scheduling now persists and replays through
restart and explicit session changes without releasing old spending reservations. Actual checkpoint
times and qualified production integration remain pending. Version-3 scheduled proposals supply
validated 1–15-minute intervals and reasons; accepted timing is saved atomically with review completion.
Budget pacing is
settled: no daily allocations or future-consult reserves; use the
shared monthly allowance until exhausted, with only explicit operator extensions (ADR-0087).
An exhausted, closed/reconciled sequence may restart next trading session with fresh entry evidence
and AI review; carried rounds and approval holds persist. Step 8 must make these states and restart
conditions explicit, including the rising round trigger. Ordinary sales, profits and all-cash periods
keep the same round/peak and count; only a round-limit hit consumes an attempt. Independent threshold
checks on every fresh verified portfolio update are agreed, including qualified one-minute data.
AI review timing cannot slow protection. Qualify valuation sources/collection for that coverage;
confirmed deposits/withdrawals now reuse percentage-preserving reference adjustment without resetting
attempts or holds. Cash-event qualification and AI integration remain pending. Reuse existing protection;
Step 8 owns the threshold display.
Shared risk authority is agreed: use an explicit portfolio risk configuration, AI continuous rounds
in place of its daily-pause calculation, and the existing single execution path. Extract only the
needed boundary in Steps 4/5 while preserving ordinary portfolios, saved holds and risk history.
Starting-capital capture and upfront initial thresholds are confirmed: later rounds use actual
reconciled opening capital, with any earlier previews labelled estimates (ADR-0087).
Per-stock protection reuses engine settings: AI may tighten stops or sell earlier but never loosen
or remove established stops. Record blocked alternatives for evaluated learning; enforce boundaries
in code. Initial switching behavior is agreed: retain compatible holdings/protection, reconcile
required exits before replacements and preserve round/risk history. Catalogue mapping and Step 5 integration
remain pending. Add further portfolio choices over time; their designs/order remain future work.
Stop/action enforcement remains Step 4 and outcome comparison Step 6 work. Keep the MVP focus and
one necessary decision at a time. The sequence includes
data/decision/execution integration, learning, host/UI, paper validation and native live qualification.
Integrate and qualify the client and independent protective host before enabling any paper/live actions.
This is not a verified launch commitment
or completed replacement of the execution work below. See the
[open scope](reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026).

1. **Complete the real-account foundation and execution path in dependency order.** Percentage-preserving
   cash adjustments and the broker projection/synchronization/protection/durable-session source path
   are implemented/tested. Next qualify native cash-event coverage/effective timing and boundary books,
   and native orders/strategy evidence. Storage migrations 035/039/040 are installed; operational setup
   and baseline/continuation SQL runtime recovery still need verification. Managed-session SQL recovery
   is rehearsed in isolation; shared regular account/history refresh is source/tested and observed natively.
   September 25 combines this work with the newer main-checkout memory and resolved-trade fixes.
   The operator confirms the integrated controls and successful account refresh in the normal checkout;
   no further restart is needed for this check. Next verify account risk setup and qualified risk inputs.
   Populated assignment recovery and broader native synchronization remain unverified.
   The operator ended the edge-case design pass and deferred same-stock
   manual additions. Complete the agreed ongoing-promotion path and known in-app order reconciliation, then extend both
   families and replacement transitions, then verify the host before actual execution approval. Follow the
   accepted stop/restart behavior: no further automatic purchases while ordinary exits continue; restore
   the same strategy and saved buying mode after fresh account/order checks. Core ongoing duration,
   daily/restart recovery and saved TFSA controls are source/tested. Execution-host startup, family
   coverage, native qualification and rollout remain.
   Retain the existing Market sell all row action and distinct full execution pause. See the
   [current checklist](strategy-alignment-implementation-checklist.md#left-to-do--development-order).
   A dedicated setup screen is not a prerequisite for developing the continuation logic.
   The September 24 [external-trade rollout](project-status.md#latest-operational-evidence) installed
   migration 040 and verified resolved-order synchronization, the reported exit and repeat-refresh
   duplicate prevention under ADR-0084. Cash/risk input qualification and working partial-order handling
   remain under checklist items 1–4.
2. **Resolve current collection/valuation blockers.** Diagnose the five-minute collection failures and
   the invested rules-only Live account's missing opening value. The opening/source-adoption check is
   complete for Live and comparison Shadow; October 8 saved records also verify original Shadow adoption.
   The historical tracked 15-minute conflict remains a separate follow-up. Preserve immutable evidence and coverage flags.
   Verify current input adoption, overnight completion and uninterrupted regular-session Live/Shadow
   operation, including full-watchlist/provider capacity and SQL durability. Dated engineering
   activations are not full-session qualification or reusable exceptions.
   The [latest nightly check](project-status.md#latest-operational-evidence) confirms on-time success
   for October 10–13 with no new Athena invalid outcomes and all active endpoints through the prior
   TSX session. October 13 source adoption and partial runtime progress are verified, but recurring
   monitor/cycle failures, incomplete held-account valuation and Blocked coverage remain. Capture the
   underlying cycle/monitor exception alongside the unresolved provider interval contract. October 13
   passive traces confirm off-grid timestamp rejection for sampled stock failures; they did not capture
   the separate whole-monitor exception or qualify a correction. Earlier
   missed-wake and historical invalid evidence remain separate from full-session qualification.
3. **Establish trustworthy comparisons.** Accumulate mature forecast/pick outcomes, verify contiguous
   leadership-mover history and compare common strategy/policy histories. Observe skipped entries,
   causal fills, Session-2 rotation and risk exits across clean Shadow sessions; exclude Sep 4 shakedown
   evidence from performance conclusions. The proposed TSX-gainers versus Delphi analysis remains
   analysis work, not authorization to retune gates. Use the
   [calibration checklist](calibration-implementation-checklist.md) and
   [Live checklist](delphi-live-implementation-checklist.md).
4. **Extend the controlled Practice environment.** The exploratory 20-session starter trial completed
   September 8; do not repeat its acquisition/execution merely because an older handoff says pending.
   Preserve missing inputs and current-universe/model limitations. Checkpoint resume, isolated SQL
   recovery/load and failure trials, Live replay and broader historical qualification remain separate
   follow-ups. [Current scope](reviews/open-questions.md#historical-practice-environment--follow-up-milestones)
   and [contracts](concepts/historical-practice-implementation.md).

### Reliability and correctness prerequisites

- Bound WPF account-history memory (S4). The September 24 process check confirms a multi-GB
  footprint. Direct typed reconstruction, shared reports and streaming validation are implemented/tested
  and installed; resumed compact writes are verified. Qualify sustained memory/capacity, then bound
  display/history payloads and avoid unchanged-revision reconstruction under explicit freshness rules,
  preserving immutable history and fingerprint validation. Repeated reads and lifetime engine
  collections remain.
  Evidence: [project status](project-status.md#latest-operational-evidence).
- Perform a documented restore into a separate test database and continue checking backup synchronization.
  OneDrive reports the September 23 pre-shrink backup IN_SYNC; no restore drill has run. Routine latest-two
  retention is installed; the additional manual recovery checkpoint and all existing backups were preserved.
- Reconcile remaining deployed/project schema drift using reviewed manual migrations, never broad DACPAC
  deployment. Continue universe/listing checks after ingestion changes, including acquired/delisted symbols.
- Resolve short-session behavior and extend reviewed calendar coverage before **2026-12-24**, including
  protection for carried holdings.
- Protect consequential boundaries before broader refactoring: immutable Delphi facts and typed
  load/evaluate/evidence/publish stages (preserve ADR-0035 presentation), explicit Hermes completion,
  separated Hercules training/evaluation/activation, and immutable strategy/policy ownership. Retain
  the [open audit register](strategy-alignment-implementation-checklist.md#remaining-audit-work).
- Address dependency-security advisories separately from compiler cleanup; broaden CI host coverage.
  Review the June/August stabilization boundary for a possible tag/release note. Phase in nullable/analyzer
  enforcement, smaller module/public surfaces and dormant-code cleanup after protected boundaries.

## Next — improve data and model evidence

- **Revisit intraday-gain retention soon** and carry it into future strategy/risk work. The operator
  preserved this important research goal October 16. October 17 establishes the individual-winner
  direction below; whole-portfolio protection remains background. Implementation is complete and
  tested and enabled for all 15 active paper portfolios October 18; original defaults stay false.
  [Research scope](reviews/open-questions.md#intraday-gain-retention-revisit-soon).
  **October 17 scope correction:** the intended unit is **individual stocks within a portfolio**.
  The [whole-account review](reviews/paper-gain-patterns-20261017.md) is background and does not answer
  that question. The operator accepted day-move and purchase-profit measures separately. The
  [stock-level review](reviews/stock-gain-paths-20261017.md) now covers ordinary holding paths, later
  purchases and later upside. The operator then confirmed **a trailing percentage gap that shrinks as
  purchase profit grows**, prioritizing protected profit over post-sale upside. [Accepted ADR-0090](adr/0090-progressive-individual-profit-trailing.md)
  records the three-band proposal and its authorized offline [saved-trade comparison](reviews/progressive-trailing-trade-study-20261017.md).
  Larger-winner entry tests now exercise +3% in ARE/AGI; +5% has conditional already-held ARE/CAS
  illustrations, with survival from entry unproven. Tightening does not always retain more profit.
  A five-schedule 10 a.m./noon comparison is now complete: mixed individual-stock profit retention
  supports keeping original bands as baseline; the operator now retains them for design and accepts
  combined-holding add-ons with updated cost, preserved stop and tightest earned gap. Morning/noon
  alternatives stay deferred research. The [bounded add-on test](reviews/progressive-trailing-trade-study-20261017.md#saved-add-on-trade-comparison)
  now preserves profit in ARE while keeping its stop/band across extra buys; it protects a smaller
  profit earlier than the actual sale. A [five/fifteen-minute update comparison](reviews/progressive-trailing-trade-study-20261017.md#five-minute-versus-fifteen-minute-stop-updates)
  now holds breach checks at five minutes: four closed histories favor faster updates, one (DSV)
  favors slower, 56 are equal. The operator subsequently chose completed five-minute raises and fresh
  observed-touch triggers for design. Scope now includes all portfolios, especially Delphi Live.
  Fresh bid, the highest completed five-minute close since purchase, and inclusion of existing holdings
  when enabled are accepted for design. Adaptive bid timing is now accepted too: five/one minutes at
  0.25% distance, new quote per check and a 60-second receipt-age limit. The current source/saved check
  does not qualify Live bid freshness; the operator defers further source investigation as a coding
  prerequisite. Daily Shadow/Delphi Live integration, prospective comparison assignments, recommendation
  paths and safe handover are implemented: 166 focused tests including Practice and the final full Core suite pass.
  October 18 rollout is installed/enabled across all 15 active paper portfolios, preserving all 37
  open holdings' protection, financial facts and comparison profiles; original entry controls are restored.
  Next observe normal regular-session polling/touch behavior when available. Quote investigation stays
  deferred and gap repairs secondary; weekend state checks and synthetic studies do not establish
  bid-trigger returns or continuous coverage. [Dated installation evidence](project-status.md#reference-map-and-maintenance).
- Backfill `RelativeStrengthFeatures`, including `CompositeScoreZ`, with coverage diagnostics; compare
  raw RS and Z-score rankings before any executed-lens change. Integrate historical RS into Hercules
  only after validated backfill.
- Audit historical enabled `ModelRegistry` rows and retire obsolete artifacts deliberately.
- Measure `ObvSignalWeight` from paper outcomes, accumulate CLX confirmation/divergence evidence, and
  run Granville cross-family overlap analysis before changing weights.
- Complete comparable capital/dates/fills/costs/coverage/downside/untouched evidence requirements before
  automatic Wealthsimple strategy execution; recommendation-only approval keeps ADR-0062's distinct rules.

## Later — expand system capability

- Extract a standalone pre-session engine using shared Core and one active owner; arrange Windows Task
  Scheduler hosting and verify operation. WPF/console dependence remains until that rollout. Shadow
  headless operation also retains its clean-session prerequisite.
- Add useful Scorecards charts after sufficient matured cohorts; preserve Athena and optional exports.
- Consider Hermes status/run and System Log surfaces over shared workflows, retaining explicit run authority.
- Complete Oracle Phase 3 debate/evaluation, Granville Overdueness (#23–#24), Heavy Volume/later groups,
  and broader walk-forward/version simulation.
- Any separate future Sentinel broker stop-loss/rotation workflow needs its own decision, authentication,
  limits, previews, partial-fill/reconciliation handling and kill switch. The TFSA execution path above
  does not implicitly authorize it.

## Explicitly deferred

- Dullness (#21–#22): longer history or better universe under ADR-0005.
- CLX as a gate/ranking input: evidence and a new ADR.
- Executed RS Z-score: validated backfill and rank comparison first.
- New ML features, thresholds or models: design-rule review and an ADR for behavioral changes.
- Nightly captured-source/build/cache retention: reviewed policy remains open in
  [open questions](reviews/open-questions.md#nightly-build-retention-adr-0078-2026-09-14); the
  [September 23 one-time compiled-output cleanup](project-status.md#latest-operational-evidence) is
  complete. The separately authorized one-time SQL shrink is also complete with preservation checks;
  recurring build retention remains undecided, and automatic SQL shrinking remains off.
  Installed database-backup retention does not cover build artifacts.

## Completed work and history

The September 22 documentation cleanup preserves the full prior
[roadmap](roadmap-history-20260922.md), [status](project-status-history-20260922.md) and
[alignment checklist](strategy-alignment-history-20260922.md). Those snapshots include completed
milestones and historical authority, not fresh instructions. Current installed/verified facts live
only in [project status](project-status.md); this page keeps priorities and links. No operational
verification date was advanced by the cleanup.
