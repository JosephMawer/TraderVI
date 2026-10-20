# Desktop paper-cycle responsiveness — October 20, 2026

Status: correction implemented and offline validated; separate outputs prepared. Not installed or
natively verified. The running reviewed TFSA copy and its launcher are unchanged.

## Evidence and limits

The operator reports WPF PID 73252 at approximately 31% CPU/1.8 GB and an unresponsive UI during
paper cycles. Preview October 20 carry timed out twice before account checks. The earlier bounded
10:20 Toronto read confirms recent publication/lease renewal, not cycle completion. These facts
establish neither a memory leak nor a SQL bottleneck. The
[morning evidence](delphi-live-morning-decisions-20261019.md#october-20-first-carry-preview--coordination-timeout-before-account-checks)
and [accepted same-copy contract](delphi-live-next-session-contract-20261020.md) remain authoritative.
No process attachment, profiler, new SQL/broker read, native rehearsal or operational restart was run.
Native stage durations and the longest October 20 stage therefore remain unknown.

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

## Exact installation and acceptance steps — separate approval required

1. Review the prepared manifest and separate `artifacts/desktop-responsiveness-20261020/working-copy`.
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

Remaining: native cause attribution, actual idle-boundary/setup success, sustained CPU/memory and
held-source capacity, bound lifetime reconstruction under separately reviewed integrity/freshness
rules if timings justify it, and the next actual closing-boundary observation. Unit tests and the
synthetic dispatcher measurements do not establish live responsiveness.
