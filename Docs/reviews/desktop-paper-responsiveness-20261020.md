# Desktop paper-cycle responsiveness — October 20, 2026

Status: worker/display correction **55e429d5 installed with execution off at 11:03:56 Toronto**,
PID 78872. The separately approved history-serialization follow-up **30775d12 starts at 11:32:14**,
PID 131204, after the prior dashboard finishes and its desktop exits normally. Both preceding copies
and the original TFSA launcher remain unchanged. Bounded native follow-up observation confirms
continued cycles and short display applies, with no clear full-cycle speedup. No successful
carry/checkpoint/qualification follows.

## Evidence and limits

The operator reports WPF PID 73252 at approximately 31% CPU/1.8 GB and an unresponsive UI during
paper cycles. Preview October 20 carry timed out twice before account checks. The earlier bounded
10:20 Toronto read confirms recent publication/lease renewal, not cycle completion. These facts
establish neither a memory leak nor a SQL bottleneck. The
[morning evidence](delphi-live-morning-decisions-20261019.md#october-20-first-carry-preview--coordination-timeout-before-account-checks)
and [accepted same-copy contract](delphi-live-next-session-contract-20261020.md) remain authoritative.
At initial diagnosis, no process attachment, profiler, new SQL/broker read, native rehearsal or
operational restart had run. Subsequent explicitly approved switches and bounded native observations
are recorded separately below; they do not retrospectively qualify the earlier timeouts.

The immediate problem is a UI that cannot supervise long paper work. Its parent problem is reliable
desktop monitoring/setup coordination; the root goal is recoverable advisory and account management.
This is a mechanical hosting/display correction, with no new policy decision or qualification waiver.

## Execution-path findings

| Path | Source evidence before correction | Correction |
|---|---|---|
| Dashboard 30-second DispatcherTimer | Non-overlap guard exists; its barrier registration covers Live, settings access, recommendation evaluation, tracked/Shadow cycles, protection and display reads | Keep sequence/non-overlap; register financial operations at their own boundaries; exclude optional display reads from setup occupancy |
| Live tick/snapshot | Already Task.Run in DelphiLiveDesktopService; compact snapshot storage also uses workers | Retain worker and all ownership/fence checks; time engine/protocol/read/unpack/pack/verification and collection/evaluation/action stages |
| Tracked monitor | Called directly from UI; ordinary awaits capture WPF context, including synchronous Git provenance discovery and CPU continuations | Invoke the complete operation on a worker; capture UI preference before dispatch |
| Shadow poll/protection | Called directly from UI; source collection, configuration, policy/persistence continuations can capture dispatcher | Invoke complete operations on workers; retain controller gate, sequence and awaited persistence |
| Recommendation tick | Runs workflow directly on dispatcher under timer-wide barrier | Worker invocation and its own barrier scope; UI presentation follows completion |
| Live presentation | Observation sorting/evidence JSON prepared on dispatcher; cached immutable research reformatted on every Apply; quotes grow with lifetime history | Prepare observation rows on worker; reuse identical cached research; format full research only on explicit evidence action; show latest 250 quote attempts with explicit heading |
| Portfolio refresh | Scheduled Shadow refresh followed by another dashboard refresh; row selection during refresh triggers detail reads and explicit refresh repeats them; concurrent refreshes queue | One final scheduled display refresh; join concurrent display requests; suppress only programmatic selection callbacks, then load final details once; real user selections retain request/identity checks |
| Setup barrier | New cycles are already deferred fairly while setup waits; timeout releases deferral without interrupting work | Retain two-minute timeout, all-active accounting and SQL fences; allow idle between completed financial stages and optional presentation |

The observed `.Result` accesses in portfolio details follow awaited `Task.WhenAll`; `Wait(0)` calls
are nonblocking probes. Neither is evidence of a synchronous UI wait. Git provenance discovery does
use synchronous process/read/wait calls; tracked/Shadow worker invocation contains these off the UI.
The Live service constructor's initial provenance/calendar work remains startup work, outside this
periodic-cycle correction. Full lifetime snapshot reconstruction/validation remains; no cache skips
freshness/integrity checks. No SQL tuning or history pruning is justified by this investigation.

## Measured offline evidence

`Tools/DesktopResponsiveness` runs a real WPF dispatcher and a 10 ms pulse timer. It constructs a
fictional 3,000-quote ledger with shared 20,000-character reports and uses the production snapshot
codec, including full integrity verification. It starts no desktop service, SQL or provider workflow.
It compares CPU work after a synchronously completed await on the dispatcher with the same work
through the production worker helper. This isolates thread placement, not native cycle throughput.

Initial measurement (SDK 10.0.401, Debug, warmed serializers):

| Invocation | Reconstruction elapsed | Maximum dispatcher gap | Pulse count | Work has UI synchronization context |
|---|---:|---:|---:|---|
| Dispatcher | 612.2 ms | 614.6 ms | 7 | Yes |
| Worker | 549.6 ms | 26.3 ms | 39 | No |

Both restore all 3,000 quotes. A synthetic setup wait also completes after financial admission is
released while later work stays deferred. These measurements support worker isolation and removing
display work from barrier occupancy. They do **not** attribute PID 73252's CPU, memory, multi-minute
runtime or freeze to one measured native stage. Bound collection/display traffic structurally falls
from two scheduled portfolio refreshes to one, and selected quote rows are bounded at 250; durable
quotes and financial history are retained in full.

Validation: 65 focused tests pass; full Core suite **2,660 passed, nine native skipped**. WPF Release
build passes with 260 existing compiler warnings, zero errors. Package restore initially hit the
sandbox network boundary and was retried with explicit dependency-restore approval. NuGet auditing
was disabled; no fresh dependency-security assessment is claimed. No complete solution/SSDT build
or database deployment was run. Logs are in ignored `artifacts/desktop-responsiveness-20261020/`
and sibling `artifacts/desktop-responsiveness-*.log` files.
The initial full run passes; a repeat during simultaneous compilation/test load has one five-second
timeout in unchanged `AiReviewDesktopTestTests.DeadlineCancelsInFlightWorkAndWaitsForItsCleanup`.
All 11 tests in that group then pass in isolation, and the full serial rerun passes 2,660/nine skips.
Its wall-clock sensitivity is disclosed; no AI implementation or timeout was changed.

## Bounded diagnostics for native follow-up

`%LOCALAPPDATA%\TraderVI\Diagnostics\paper-cycle-stages.jsonl` and `.previous` contain fixed enum
stage names, UTC, process ID, scope ID, Started/Finished, elapsed milliseconds and whether the scope
started with a synchronization context. No arbitrary exception text, query, account/symbol identity,
amount, credential or provider payload is accepted. Finished means the scope ended, including failure;
it is not a success receipt. Pair by process/scope ID; nested durations overlap and must not be added.

One worker batches appends at most once per second. The pending queue retains at most 256 entries;
the current file rotates at 512 KiB with one prior file (one bounded batch may exceed the threshold).
I/O failures, competing writers, queue pressure and process exit can drop samples. The diagnostics
grant no financial authority and cannot establish healthy operation merely by their absence.
A one-second WPF pulse records gaps greater than two seconds. It detects dispatcher scheduling delay,
including machine load/GC/suspension, rather than proving which stage blocked the thread. The
HasSynchronizationContext field is an invocation-context flag, not a managed CPU profile.

After an approved switch, inspect multiple normal cycles and compare engine stages, snapshot CPU
versus read stages, UI apply time and dispatcher gaps. Then retry native setup under operator control.
If a financial stage itself still exceeds two minutes, preserve its start/finish evidence and diagnose
that stage. Do not enlarge the timeout, cancel it, bypass the barrier or substitute stale data.

## Approved switch and native observations — October 20

The operator explicitly approves the normal switch with execution off and observation of paper
cycles/timings. All 124 prepared files match the manifest. Host permission/qualification packet are
absent; the old setup dialog shows the prior timeout with financial save/approve/enable disabled.
Its idle dialog closes through Alt+F4, followed by normal main-window closure. PID 73252 exits normally
before the guarded launcher runs; no process is killed or financial operation force-stopped. Close
button/index inputs had unavailable geometry, so standard keyboard closure was used. The corrected
copy starts at **11:03:56**, PID **78872**, with one enumerated TraderVI window and responding process.
UI accessibility confirms automatic trading off and Stop available; Stop is not invoked. Keyboard
focus/tab-navigation changes were not confirmed, so full interactive acceptance is not claimed.

Installed path: `artifacts/desktop-responsiveness-20261020/working-copy/TraderVI.WPF.exe`.
Core SHA-256: `4CAFAA334AB686E11E96B893D6327AC895540F249BD3DB3682F2AEF1CA7E4325`.
WPF DLL SHA-256: `ED06E94CC13F83C032089C6ADC1753406484C398509097A2F7D73BF3819C01BC`.
The original next-session Core remains `4BBBD6F141CD50F1D634BEAA4D0533ECFF307B4C65642B84C6C25346214DC07F`.
No saved grant, qualification or checkpoint is created by this switch. Same-copy proof must be
captured anew before recovery review; original account anchors/orders must not be repeated/reset.

Fixed-stage observation covers **11:03:59–11:17:50 Toronto**. Two complete dashboard scopes:

| Stage | First wake | Second wake |
|---|---:|---:|
| Dashboard | 431.65 s | 289.38 s |
| Live engine, including snapshot/protocol work | 419.44 s | 277.67 s |
| Before collection, including initialization/protection | 253.38 s | 79.86 s |
| Live collection | 3.03 s | 3.34 s |
| Live evaluation/persistence | 0.80 s | 1.17 s |
| Live action phase, including snapshot work | 162.19 s | 193.23 s |
| Tracked monitor | 0.44 s | 0.38 s |
| Shadow monitor | 2.87 s | 1.56 s |

The pre-collection duration is derived from engine-start to collection-start, not a separately
instrumented protection-only measurement. It includes initialization on the first wake. Nested
durations overlap. Second-window timing overlaps subsequent offline builds/tests and is not an
isolated throughput comparison. Across retained observations, 798 snapshot reconstructions finish
on workers; largest reconstruction 7.33 s, pack 6.21 s, verification 3.14 s. Snapshot read includes
SQL and materialization; its maximum is 10.00 s. These data identify substantial repeated snapshot
work but do not prove a server-side SQL bottleneck or precise inclusive CPU attribution.

Three Live display applies take **26.1–53.3 ms** (maximum 53.3 ms); no dispatcher-pulse gaps over two
seconds are recorded through the two completed wakes. UI accessibility reads take 74–2,781 ms;
this includes automation overhead and is not a dispatcher benchmark. Process working set fluctuates
about 1,052–4,137 MiB in the sampled windows, with later large falls; no leak diagnosis is established.
Do not claim sustained capacity or that all freezes are eliminated from this bounded observation.

Two exact installed read-only helper checks use query SHA-256
`D77D9A52D35061F18E69751E116990D63EB405B62E4B15CD92AEA400B251B68B`, verified against both result receipts.
At **11:08:05**, paper publication is 11:07:43 with the first wake still active: progress, not idle.
At **11:17:29**, one held lease is renewed 11:17:25 (expires 11:32:25), publication is 11:16:58,
and the 11:10 evaluation bar is saved at 11:13:50. Session remains Monitoring/Blocked with its
historical host gap retained. No new safe failure entry is observed since startup at those checks.
The second completed dashboard ends 11:17:15; the next wake has already begun. No setup-preview or
financial-save action is taken, and actual setup idle success remains unverified.

Receipts/observations: `artifacts/desktop-responsiveness-20261020/switch-receipt.json`,
`native-process-samples.json`, `native-stage-entries.json`, `native-second-process-samples.json`,
`native-second-stage-entries.json`, `native-stage-summary-corrected.json`; bounded SQL result files
`artifacts/read-only-db/result-20261020T150805018Z-c3ff9c420f2e4860a42cbcbf0ded5aa1.json` and
`result-20261020T151729426Z-d7d98cf1f7844ae4ac250940d039d930.json`.

## Measured history-serialization follow-up

Source inspection finds `PreservePrefix` serializes both sides of every historical fill/quote/mark,
even when an in-process transition retains the identical immutable object. It runs before the
repository's independent reloaded-state validation. The repository also serializes the whole next
snapshot unconditionally before discovering that the exact-retry comparison is unnecessary.

The follow-up skips only same-object prefix comparisons; distinct/reopened records retain the exact
JSON comparison. It removes the unused next-snapshot serialization on normal revisions, keeping
exact-retry comparison, revision checks, lease fencing, transition validation, compact reconstruction,
full fingerprints and save verification. No SQL, policy, timing guard, risk or history rule changes.

The same warmed fictional 3,000-quote append validation measures **307.00 ms/248,065,064 bytes before**
and **0.244 ms/1,064 bytes after**. This isolates retained-object validation, not total cycle latency;
reloaded-state checks, packing, reads and full hashing still cost work. Four new offline cases check
bounded allocation, exact reopened history, report whitespace, decimal representation, reorder,
drop and replacement rejection. **61 focused/2,664 full serial offline tests pass, nine native
skipped; WPF Release passes** with the existing 260 compiler warnings. No fresh dependency-security
audit/full-solution/SSDT or native SQL rehearsal is claimed.

Output: `artifacts/desktop-history-cost-20261020/working-copy/TraderVI.WPF.exe`, with its own
`manifest.json` and guarded `Launch-Prepared.ps1`. Neither preceding copy is overwritten.

## Approved serialization-copy switch and native observation

The operator explicitly approves the normal switch with execution off. The prior desktop's dashboard
finishes at **11:31:57.954 Toronto**. Normal Alt+F4 closure follows before the next scheduled wake;
process exit is verified at 11:32:04. No active work is force-stopped. The guarded follow-up launcher
starts **30775d12 at 11:32:14.257**, PID **131204**. All 124 prepared manifest files match; one owner
and one targetable window are observed at the exact separate path. Execution-host permission and
qualification files are absent; the UI reports automatic trading off. Both earlier Core hashes match
their recorded values. Changing the build still requires fresh same-copy checkpoint/restart proof.

Installed Core SHA-256: `CCC43CE8A4BFB5D8DF703E843E8E8590275EA0BAA41A6EC0728ADAA2311236AF`;
WPF DLL: `B1BC52CCF4040A36421D75A470B06F65F8E72796397411E4C8F1BB04B55132B5`.
Receipt: `artifacts/desktop-history-cost-20261020/switch-receipt.json`.
Normal configured paper/account synchronization continues. No competing build/test is run during
this observation. The desktop remains open for monitoring; no preview, financial save or enable is
part of this switch.

The first three completed dashboard scopes take **91.23, 73.07 and 217.46 seconds**, ending at
11:34:43, 11:36:25 and 11:40:19 Toronto. Live takes **77.46, 64.08 and 209.27 seconds** respectively.
The first two do not enter collection/actions: restart chooses the next eligible endpoint and the
existing two-minute bar delay remains. The third reaches collection at 11:38:01, evaluation at
11:38:05 and actions at 11:38:06. Collection takes **3.798 s**, evaluation **0.682 s**, actions
**125.134 s**. Its precollection interval is **79.62 s** and includes protection/snapshot work;
there is no separate pure protection CPU measurement.

A fourth no-collection wake ends at **11:41:47**, taking **65.05 s dashboard / 57.35 s Live**.
Across **38 process samples from 11:32:34 through 11:42:00**, all report responding. No dispatcher
pulse gap over two seconds is recorded after its post-startup timer begins. Five Live UI applies
take **19.98–41.70 ms**. Working set ranges **714–3,168 MiB**, with large falls; sampled CPU time
averages **1.78 CPU cores** over this window. This is substantial continuing work, not proof of a
memory leak or sustained capacity. The log records 622 completed snapshot reconstructions, all on
workers; maxima are **6.66 s unpack / 8.16 s pack / 2.10 s verify / 2.55 s read**. Nested/concurrent
timings overlap; bounded best-effort diagnostics can omit burst entries. Display wrapper durations
include awaited work and cannot be treated as continuous dispatcher blockage.

Retained pre-switch evidence adds important workload context: the preceding copy's last no-collection
wake takes **92.00 s dashboard / 80.62 s Live**, while its last collection/action wake takes
**209.66 s dashboard / 199.29 s Live / 125.310 s actions**. Earlier full wakes take 264.90/274.51 s
as well as the initially recorded 431.65/289.38 s. Thus the new **217.46 s** full wake and nearly
unchanged action duration establish **no clear end-to-end improvement** from the serialization
follow-up in this small, varying workload. The isolated allocation reduction is real; it is not a
native throughput result. Repeated reconstruction, exact reloaded-state validation, packing and
full fingerprints remain expensive and retain their safety purpose. Do not extrapolate the
microbenchmark to a solved capacity problem or infer a SQL bottleneck/memory leak.

Accessibility reads during protection and actions take **117–166 ms** (including helper overhead).
The UI reports execution off and exposes Stop as enabled after startup synchronization. Indexed tab
clicks fail with the helper's `coordinate input geometry is unavailable`; one keyboard focus attempt
takes 2,211 ms without confirmed focus movement. No full tab-navigation or Stop-action acceptance
is claimed, and Stop is not invoked. Dispatcher-pulse and process observations provide bounded
native response evidence, not proof that every future freeze is eliminated.

One installed read-only helper check at **11:40:40 Toronto** verifies the reviewed query hash
`D77D9A52D35061F18E69751E116990D63EB405B62E4B15CD92AEA400B251B68B` against the receipt. There is
**one held lease**, renewed 11:38:01, expiring 11:51:42; latest paper publication is **11:40:05**;
the **11:35** evaluation bar is saved at **11:38:06**. Session remains Monitoring/Blocked with
the original host-gap flag retained. Stage completion plus these reads demonstrate bounded durable
progress, not successful source/financial qualification. Receipt:
`artifacts/read-only-db/result-20261020T154040108Z-c3771f204e96452b85f68cd733f0bc42.json`.

Raw safe observations are under `artifacts/desktop-history-cost-20261020/`:
`native-followup-*`, `native-followup-second-*`, `native-ui-samples.json`,
`pre-followup-retained-stage-entries.json` and combined `native-summary.json`.
Actual setup-preview idle success, native checkpoint/restart, sustained capacity and closing behavior
remain unverified. No financial history reset, order, authorization save, risk-reference change,
trading enable, profiler attachment, competing build/test or native SQL rehearsal occurs.

## Exact switch procedure — repeat only with operational approval

1. The current serialization follow-up is the separate
   `artifacts/desktop-history-cost-20261020/working-copy`. For a future approved restart, review its
   manifest; both preceding copies remain preserved.
   Recheck execution is off and whether a checkpoint/packet has been created since the last evidence.
   Changing Core/build invalidates prior same-copy proof. Preserve existing proof/history; capture
   anew in the corrected copy and restart that same corrected executable for recovery review.
2. Obtain approval for the normal operational switch, which resumes configured paper collection and
   normal broker synchronization. Finish any current financial operation and close the old desktop
   normally. If closure is refused/busy, wait and investigate; never force-kill it.
3. Verify no TraderVI.WPF owner remains and verify every prepared file against `manifest.json`.
   Use the separate `Launch-Prepared.ps1` only after approval. It refuses a running owner or an
   existing execution-host permission file; that condition requires reviewed recovery, not deletion.
   It validates the reviewed calendar and launches with repository working directory. It does not
   overwrite `next-session-working-copy`, its manifest, or `Run-DelphiLiveNormal.cmd`.
4. Confirm one visible responding owner at the exact new path and matching Core/WPF hashes. Keep
   execution off. Record native stage durations and UI response/Stop/status availability across
   ordinary cycles; observe a complete publication and lease renewal, not merely recent activity.
5. Retry setup only under the original operator financial-action boundary. Review account/source
   freshness, original IDs/references, carry and the complete qualification sequence. The October 20
   trial still expires at its reviewed close; this correction grants no later-session permission.
   Native SQL rehearsals remain paused. No order, authorization save, risk reset or trading enable
   is part of installing or accepting this responsiveness correction.

Remaining: actual idle-boundary/setup success,
sustained UI/CPU/memory and held-source capacity, any further lifetime reconstruction work under
separately reviewed integrity/freshness rules, and the next actual closing-boundary observation.
The bounded native wakes qualify only the described response/timing observations.
