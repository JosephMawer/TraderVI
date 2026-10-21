# TraderVI Project Status

## October 21 post-open execution watch

**Portfolio rules only switch implementation, October 21:** the operator explicitly requests
implementation now, preserving risk and today's 16:00 expiry, for personal review/approval.
[ADR-0097](adr/0097-reviewed-october21-portfolio-rules-only-source-switch.md) records the narrow
cash-only/no-automatic-order transition. Core saves an immutable prospective source-switch review;
the desktop displays exact identity/gate bypass/retained controls and requires separate approval
and host enable. Original approval/continuation, financial memory/references/holds, ceiling and
cutoff remain. Only derived confirmation state resets, with inputs after approval required.
73 focused and 2,780 full offline Core checks pass (nine native skips); Debug WPF build passes.
Standard Release installation remains pending normal closure. No source approval, host enable, order or native write rehearsal
was performed. A 12:14 hash-verified metadata read confirms the existing sources share their exact
comparison pair and all non-identity policy settings; TFSA remains Normal, revision140/latest
StrategyEvaluated12:12:51, zero automatic orders/positions. Receipt:
`result-20261021T161421143Z-597b2d06ff494b7ea6159279d8cfdbf7.json`.
The unrelated SQL reliability draft is preserved byte-for-byte outside compiled source under
`artifacts/deferred-sql-20261021/saved-source/` (manifest, patch, source copies). Resume instructions
are updated; it is uninstalled and excluded from this build. Normal closure/zero-owner inspection
is required before standard Release replacement; operator review/approval/enable remain pending.

**Ungated target clarification and selection provenance, 11:56 Toronto:** operator requests the
existing Delphi Live **Portfolio rules only** source. Normal was recommended during October 18
preparation from limited saved paper results; the scoped setup then explicitly selected only Normal.
It was not an automatic runtime winner choice. Initial execution approval was saved October 20 at
14:54:08; today's continuation retains it. Approval of the preset does not establish an informed
preference for gated trading. The ungated source exists/updates 11:55:52, while revision 134 remains
Normal for both initial/continued review, zero automatic orders/positions and exact 16:00 expiry.
Display selection cannot replace the assignment. The [subsequent transition decision](adr/0097-reviewed-october21-portfolio-rules-only-source-switch.md)
preserves original financial history/risk and intraday/data/order controls. This earlier provenance
inspection performed no code/source switch, SQL mutation or financial approval. Receipt:
`result-20261021T155605890Z-1257597b6d944374afdab03d469ddd46.json`.

**Latest execution read 11:49:25 Toronto:** revision131/Active/StrategyEvaluated at11:46:51,
27real evaluations,no saved execution blocks/orders/fills/positions,same16:00expiry. Safe local
diagnostics identify a third observation deadline error52232 at11:37:51; real evaluations gap from
11:34:43 to11:46:51 (roughly12m8s) and recover before this read. Source advances11:46:33;
2,109paper evaluations/latest11:44:14,one current lease11:47:12,responding desktop/matching hashes.
Recurrence/recovery is distinct from the daily Breadth admission finding below; no operational or
control changes. Receipt: `result-20261021T154924844Z-af54c8ad20ae45dcb7f131ddbfda02da.json`.

**No-buy cause established by bounded reads 11:43–11:46 Toronto:** today's latest adopted source
for the approved Normal comparison contains 35 published stocks (50 lens entries); **zero are
admitted by Normal**. Every published lens entry is ineligible with first failure **Breadth**.
The daily screen's market-participation gate is therefore an upstream reason no buy can be created,
independent of intraday confirmation. All 703 saved Normal-policy observations across 37 monitored
symbols at the follow-up lack `DailySetup`; ten momentum-confirmation completions exist in this
observation-only set but none is fully buy-eligible. The earlier two candidates were momentum
watch observations, not established daily-admitted purchase candidates. Earlier explanations that
focused only on waiting for confirmation were incomplete. The 11:43 execution read still shows
revision128/latest11:34:43,26 real evaluations,no saved blocks/daily/capital holds or automatic orders/
positions,exact16:00expiry. Repeated continuity recoveries additionally reset signal history; epochs
3 and4 have no mature saved evaluations, with epoch5 starting11:38:14. These operational losses do
not explain away the separately established zero daily admission. The first-failure trace does not
prove later gates pass; no threshold/gate/profile/control change is proposed or performed.
Hash-verified receipts: `result-20261021T154337094Z-39ff177a82cf42d2ae7cff91d8ee15a8.json`,
`result-20261021T154435478Z-6aedd01548584f1ab569c5aa21b6be3a.json` and
`result-20261021T154659094Z-5f602c79c3d442ddaed5c73d3990480d.json` (reviewed ignored SQL files).

**Repeated deadline failure and recovery verified 11:16:51 Toronto:** real execution advances to
revision 119/Active/StrategyEvaluated at **11:16:46**, twenty-three post-open evaluations, no saved
execution blocks and zero automatic orders/fills/positions. The first 11:16:24 read still has the
11:04:30 evaluation (over eleven minutes old); Normal source has just advanced at 11:16:21. A safe
local diagnostic records another observation-set deadline rejection, SQL **52232 at 11:07:47**.
LiveActions finishes at 11:16:29 after 138.85 seconds. A bounded follow-up verifies real progression;
the real-evaluation interval is roughly 12 minutes 16 seconds. Shared evaluations reach 1,665 at
11:14:10 and one current lease renews at 11:14:07. Desktop responds and both Release hashes match.
This follows the earlier 10:43 rejection/10:52:16 recovery (roughly 12½-minute gap); the initial
retry-only SQL-2 draft is not evidence that these deadline failures are addressed. Exact timing
cause remains unresolved. Historical coverage gap, controls and exact 16:00 expiry remain; no
restart or deferred fix installation. Hash-verified receipts:
`result-20261021T151624122Z-93364054004c4b7980eb794fdfa17bfc.json` and
`result-20261021T151651518Z-208d1f4f8fdb430abf3a811da4428833.json`.

**Earlier bounded stall read 10:51:23 Toronto:** real execution remains revision 107/Active with nineteen
evaluations/latest **10:39:42** (11 minutes 40 seconds without progress), no saved execution blocks,
zero automatic orders/fills/positions and unchanged 16:00 expiry. Normal source remains 10:39:12,
although shared evaluations advance to 1,332 at 10:49:29 and a current lease renews at 10:49:25.
The normal desktop responds and both Release hashes still match. Local safe diagnostics record
SQL **52232 at 10:43:05** in observation-set synchronization: the existing seven-minute collection
deadline rejects late observation membership. This is a different error from the earlier save timeout
-2; the reason processing reached the deadline is not established. At the bounded stage read,
LiveActions starts 10:49:29 without a completion yet recorded. No restart, guard relaxation or deferred
fix installation is performed. Continue observing source and real-loop recovery; retained Active state
alone does not establish current execution. Hash-verified receipt:
`result-20261021T145123104Z-ea0f62f4fccb4d529cebf095e8a4f985.json`.

**Updated bounded read 10:18:09 Toronto:** real execution is revision 95/Active/StrategyEvaluated
at **10:16:00**, fifteen post-open evaluations, no saved execution blocks and zero automatic orders,
recorded fills or positions. The two candidates that started strong confirmation on the 10:05 bar
both record `StrongConfirmationBroken` on the 10:10 bar and `Watching` on the 10:15 bar. Neither
has any confirmed eligible observation in that interval; latest evidence is mature/on-time for one
and immature/not-on-time for the other. This establishes failed confirmation, not an order or a
current saved execution blocker. Exact October 21 16:00 expiry and the earlier historical coverage
gap remain. Hash-verified `ConfirmationFollowup.sql` receipt:
`result-20261021T141808456Z-be3c0b588f7d490dabace51b5940d6e6.json`. The 10:16 watch separately
verified fresh Normal source, one renewing lease, responding PID 37148 and matching Release hashes.

**Reliability work deferred by operator:** after requesting a SQL timeout correction, the operator
asks to leave implementation until tonight and focus on the running app/trades. A one-time same-chat
21:00 Toronto reminder `delphi-live-sql-fix-reminder` is created/verified Active. Partial local source
work is preserved **uninstalled and not ready for use** in the ignored byte-for-byte checkpoint
`artifacts/deferred-sql-20261021/saved-source/`, excluded from compiled source: bounded immutable evaluation-save
retry/diagnostics plus an incomplete explicit updated-build review path. The initial retry-only
focused run passes 29 tests; later build-review changes are uncompiled/untested, so that result does
not qualify the entire pending diff. Resume notes/logs remain ignored under
`artifacts/live-evaluation-timeout-20261021/`. Metadata reads find expected indexes enabled but cannot
establish the historical wait/plan cause with the helper's available permissions. No schema/write
rehearsal, Release replacement, restart or financial action is performed. Preserve today's running
build, gates, risk and cutoff; the evening reminder confers no next-session permission or authority
to use today's before-close build-review draft after expiry.

**Earlier bounded read 09:45:51 Toronto:** actual TFSA execution resumes at **09:45:45**, revision
59/Active/StrategyEvaluated, matching today's continuation/exact 16:00 expiry, no saved blocks and
zero automatic orders/positions. There are three post-open evaluations: 09:31:41, 09:32:50 and
09:45:45. The earlier successful loop observations did not establish fresh five-minute signal inputs.
For the approved Normal policy, all 37 latest 09:40-bar candidates are `Watching`, with
`familiesMature=false` and no confirmed eligibility (29 on-time, eight invalid/missing/stale).
At that read, no-order evidence indicates no mature confirmed buy signal; sustained execution
and automatic fills remain separate qualifications.

**Technical interruption also observed:** a safe local diagnostic records SQL timeout -2 in
`DelphiLiveEvaluationRepository.PersistAsync` at **09:37:46**; the evaluation stage lasts 30.10 s.
At 09:41 the real revision/source are unchanged and no paper evaluation is saved; today's session
has gained a host gap/Blocked coverage. Exact database timeout cause is not established. The next
collection completes at 09:42:19 (38 slots: 30 usable, five NoCompletedBar, three StaleNoNewBar),
111 shared evaluations persist by 09:42:32, and paper actions take another 115.53 s. Normal source
advances at 09:44:19, followed by resumed TFSA evaluation after a roughly 13-minute gap. Original
gap/coverage history is retained, not repaired by recovery. At 09:43 there is one responding normal
desktop owner; saved daily/capital/order-review/operator-buy-stop flags do not explain the stall.
The [timeout-cause follow-up](reviews/open-questions.md#live-evaluation-save-timeout--october-21-2026)
is open; no code/control change, restart or timeout relaxation was performed.

The operator requests same-chat after-open/blocker/order monitoring today. Heartbeat
`delphi-live-tfsa-watch-october-21` is created/verified Active every five minutes, bounded to today,
with first-execution notification delivered, change-only blocker/order/recovery notifications and
a final cutoff report after 16:05 before self-removal (hard stop 16:15). It changes no trading controls
or orders. Reviewed SELECTs, query-hash-verified receipt
`result-20261021T133723700Z-046ca161894d40f3baa0c29c824a8fbc.json` and notification baseline are ignored
under `artifacts/delphi-live-watch-20261021/`. No app restart, broker/market call, financial mutation,
model operation or trading gate change is performed by monitoring. Subsequent hash-verified reads
and safe stage/failure evidence are in the same ignored directory; latest receipt is
`result-20261021T134551441Z-4501242515e1475e994213d1825f5dc4.json`. The blocker/recovery and no-signal
diagnosis are reported, with the watch baseline advanced to avoid repeating unchanged historical
alerts. Actual fills/protection and today's native cutoff observation remain pending; continuous
health is not established by the recovered evaluation.

## October 21 pre-open approval implementation

The operator explicitly requests approval before the open and automatic eligible polling from
09:30 while preserving risk controls/16:00 cutoff. [ADR-0096](adr/0096-pre-open-october21-approval-and-armed-host.md)
records the accepted timing amendment. Source/UI now save an explicit pre-open continuation flag,
retain separate host enable and show armed/waiting before the open. Review checks fresh account/
orders/risk/storage and exact stored Normal definition plus today's latest Valid daily/config/model
input without relabelling old observations. Actual execution still requires fresh matching Normal
source and current account/risk at/after the open. Wrong dates, changed facts/builds, old grants,
holds, uncertainty and the original cumulative purchase ceiling remain fenced. A first eligible
poll/available signal is not a guarantee of a 09:30 purchase; paper-work admission remains.

**Validation:** 284 focused offline tests pass; full serial offline regression passes **2,738**,
nine native SQL tests skipped. New cases cover pre-open approval/recovery/separate enable, no tick/
order before open, open/close boundaries, source/account freshness, holds/storage and wrong dates.
After operator-confirmed Stop execution host/normal closure, independent 09:18 Toronto process
inventory confirms zero desktop owners. Standard WPF Release build succeeds with 262 warnings/zero
errors in normal output. Existing compiler warnings and cached Pkcs NU1902/NU1903 dependency advisories
remain separate; no fresh audit or full-solution/SSDT qualification. Logs/source/output hashes are
ignored under `artifacts/preopen-approval-20261021/`.

**Native pre-open review/approval/arming observed:** operator screenshots show all five readiness
checks passed and pre-open instructions, followed by reported approval and the UI's **Armed for
9:30 AM Toronto** confirmation. A hash-verified installed read at 09:22 confirms revision 50,
`October21ContinuationApproved`, `PreOpenApproved=true`, October 21 scope and exact 16:00 expiry,
with zero automatic orders/managed positions. Independent 09:22:55 inspection verifies one responding
normal-Release owner PID 37148, started 09:20:20, with both output hashes matching the validated build.
This verifies the saved pre-open approval and observed host arming, not post-open execution.
SQL receipt `result-20261021T132242624Z-d86ad7805159497793a99c18ebf1e2c2.json` and safe/native owner
receipts remain ignored with the build logs. No protected grant content was independently decoded.

No further setup click is needed while the same app remains running/signed in. Initial post-open
evaluation is subsequently verified above; sustained progress/cutoff and automatic fills/populated
protection remain unverified. No agent
app launch, financial save, host enable, order, SQL write/migration/native rehearsal, market workflow
or push. Operator startup/approval/enable are separately observed above. Original history and the
unrelated Obsidian edit remain preserved.

## October 21 pre-open readiness

**Read-only checks through 09:06 Toronto:** the nightly runner reports current-source build and
Hermes/Delphi/Athena succeeded, completed 00:39. Hash-verified SQL reads independently confirm an
October 21 `Valid` OfficialPaper run using October 20 market data, with a strategy present, and
installed baseline/continuation/session storage with enabled immutable triggers and max JSON columns.
The reviewed calendar covers today. This qualifies the daily input and storage checks, not today's
fresh Normal review or execution activation.

The first installed broker-v2 check requires sign-in. After the operator starts/signs into the normal
desktop, the 09:02 bounded check succeeds: one eligible TFSA, zero holdings/pending orders and matching
broker history/holdings. It does not inspect the local order journal or qualify automation. The daily
performance observation remains dated October 20 close. Independent process inspection at 09:06
confirms one responding normal-Release owner, PID 90408, started 08:57:19. Startup rebuilt source
`d828b41`; generated assembly metadata and current Core/WPF hashes are recorded in the ignored receipt,
distinct from last night's output. Only the pre-existing Obsidian workspace edit is dirty.

Saved execution remains revision 49/October 20 close with zero automatic orders/managed positions,
one original baseline and one October 20 risk continuation; no October 21 permission is saved.
An old host-grant file exists, without its protected contents being inspected. No current Live paper
lease or October 21 frozen session exists at this pre-open observation; original coverage gaps remain.
Current account import activity is observed after operator startup, not an execution cycle.
The subsequently accepted/built pre-open amendment above permits operator review/approval/arming
before 09:30 using the [updated sequence](running.md#october-21-only-normal-continuation).
Confirm actual post-open execution after enable; new buys expire at 16:00 today.
No repeated verification trades, baseline setup or carry save is required. Fresh account/risk/source
checks, unchanged-book comparison and actual activation remain pending; pre-open evidence cannot
guarantee readiness at submission time. Automatic fills/populated protection remain unverified.

Evidence: ignored `artifacts/delphi-morning-readiness-20261021/receipt.json`, broker capture
`84f65b2a-5afe-4237-a128-b3046959ce9c`, and three SQL receipts referenced there; each successful query
hash was matched before interpretation. Initial diagnostic queries failed with SQL 207 because the
event is stored inside snapshot JSON; corrected reads succeed. No source change, new test/build run,
agent app launch, SQL write/migration, model/market workflow, financial save, enablement or order.
Existing build warnings/security advisories retain their prior scope; no fresh dependency audit.

**October 20 operator-path simplification accepted and built:** setup opening, saved readiness
refresh and host enable no longer drain desktop paper work first. Source inspection confirms those
actions retain saved-state SQL fences and exact host matching; enable creates no cycle. Review,
approval and actual execution retain paper-work admission, freshness checks and all financial
controls. The [accepted findings/tradeoffs](reviews/delphi-live-continuation-contract-20261021.md#accepted-operator-path-simplification--october-20)
also identify/correct stale no-baseline/no-assignment and expired copied-runtime instructions in
the running guide. **274 focused offline tests pass** (zero skips); standard WPF Release passes
with 262 total warnings/zero errors after outside-sandbox inventory confirms zero TraderVI owners.
Existing compiler warnings and Pkcs 9.0.4 NU1902/NU1903 dependency advisories remain; no fresh audit
or full-solution/SSDT build. Existing host, continuation, approval and paper-admission tests exercise
the retained boundaries; the desktop call-site wiring is compiled and reviewed. Logs and
output/source hashes are ignored under `artifacts/operator-path-simplification-20261020/`.
No app launch, native SQL rehearsal, financial save, broker/market operation or execution enable.
No native latency improvement is claimed. October 21 review/approval/enable and actual automatic
fills/populated protection remain unverified. ADR-0094 layout/status work is untouched.
Follow-up explanation recovers the documented October 19 SQL 51413 lock-contention rationale for
the remaining review/approval wait; [narrower import coordination is open](reviews/open-questions.md#tfsa-review-waiting-for-paper-work--october-20-2026),
not accepted or implemented. No new operational observation or October 21 prerequisite follows.

**October 20 current-readiness amendment accepted:** the operator explicitly requests removing
daily replay of completed broker verification. [ADR-0095](adr/0095-current-readiness-without-daily-verification-replay.md)
and the amended [October 21 flow](running.md#october-21-only-normal-continuation) separate historical
development evidence from current account/history/orders, risk, source/build, storage and dated
permission checks. Source changes remove the continuation packet/test-ID qualification reads and
verification restart; **243 focused and 2,723 full serial offline tests pass**, nine native SQL
tests skipped. Standard WPF Release build passes (262 warnings, zero errors) after an independently
checked zero-owner state; no app launch or runtime copy. Twenty-one added cases cover current-readiness
blockers, changed-build approval without historical certificates, retained financial history and
workflow/repository rejection of a continuation disguised as a new initial session.
Financial history, original approval/references/holds/memory, append-only recovery, separate host enable
and 4 PM cutoff remain. No October 21 financial approval, broker check, enablement or order is performed.
Ignored build/hash receipt and logs use `artifacts/continuation-current-readiness-*`. Existing Pkcs
9.0.4 NU1902/NU1903 dependency advisories remain separate from compiler warnings; no fresh audit,
full-solution/SSDT build, SQL rehearsal/migration or external workflow ran. Actual October 21 current
review/approval/activation remains pending, as do automatic fills/populated protection observations.

**October 20 execution UI design:** the operator accepts the small display/layout scope, including
separate activity/permission summaries and the five-minute display-only stale warning, then requests
design documents before code. [ADR-0094](adr/0094-real-tfsa-execution-status-presentation.md) records
the accepted boundary; the [design draft](concepts/real-tfsa-execution-ui-design-draft.md) contains
account/setup wireframes, state wording and offline acceptance cases for review. The companion
mockup passes 60 offline screen/state/width checks and normal/narrow visual review; this is design
preview validation only. Source inspection
finds host `Running` is a completed Active result, not current in-progress proof; shared account/paper
waits can leave it unchanged. No product code, binary or financial state changes in this design pass.
Normal closure remains required before replacing loaded output. Automatic tomorrow start is deferred;
October 21 approval, separate enable and 4 PM Toronto cutoff remain. ADR-0095 above subsequently
replaces the native test-recovery requirement with current readiness checks.
[Remaining UI review](reviews/open-questions.md#real-tfsa-execution-status-and-permission-ui--october-20-2026).

**Earlier October 21 continuation implementation, October 20 (qualification flow superseded by ADR-0095 above):** after clarifying that
original order-ID readback does not restrict which eligible stocks Normal may buy, the operator
explicitly accepts the [dated continuation contract](reviews/delphi-live-continuation-contract-20261021.md).
Core/desktop now preserve the original assignment/approval, trades, references, holds and memory;
append linked October 21-only risk/permission records with revision fences and original-request
recovery; require all nine checks/current exact Normal/source/build and separate manual host enable.
Old grants cannot run the new permission, even with the same strategy fingerprint. Current-build
capture/restart uses the original four IDs and original two-sale selection automatically; dated,
build-specific proof preserves October 20 files. Cutoff guards cover reservation and transmission.
252 focused and 2,702 full serial offline tests pass (nine native SQL skips); Debug and final
normal Release builds pass after operator normal closure/verified process exit. A read-only saved-
shape size check found duplicated carry would exceed the existing 256 KiB risk-event bound; compact
storage now reconstructs the exact old risk hash, with an approximately 185 KB candidate shape.
Compact source is committed as c27a3cf8 (initial implementation 235e7abf); normal Release output is
built/hash-recorded at 20:50 Toronto with zero running TraderVI processes. No new launcher/runtime
copy or application launch. No SQL migration, rehearsal, financial permission, order or host enable
was run by this implementation. Actual October 21 native review/approval/enable remains required.

**October 20 evening handoff:** the operator screenshot of Review next session shows the expected
October 21 regular-session-only guard. This confirms the after-hours refusal, not checkpoint save,
source/account qualification or continuation approval; restarting tonight cannot remove that date
restriction. Under explicit push authority, `master` through 609c3e1c (including performance fixes
and continuation commits) is pushed to origin. The unrelated Obsidian workspace edit remains local.

**October 20 close verified, final bounded read 16:04:27 Toronto:** sixteen durable strategy
evaluations run from 15:20:59 through 16:00:00.470, latest revision 49/Active. Gaps are 67.79–234.20
seconds (mean 156.04), not a guaranteed 30-second cadence. Pre-close evaluations have no saved
account blocks. At 16:00:00.148, native synchronization records the expired scoped permission and
"New buys stay stopped"; final protection/evaluation records retain those blocks. Saved purchase
expiry is exactly 16:00; no automatic orders/positions or post-cutoff buy intents are present.
Native cutoff blocking is observed; an attempted rejected order/populated protection path was not
exercised. The requested close watch is complete and its heartbeat deleted. No trading control,
financial reference, order or application state was changed by monitoring. Display refresh failure
remains distinct from the progressing execution path; next-day buying is not authorized.

**Host enabled and initial native evaluation verified, October 20:** operator enable confirmation
and local permission-file creation at **15:19:37 Toronto** are observed, one responding normal-output
owner (PID 118432). The hash-verified bounded follow-up read at 15:22:03 verifies account synchronization,
protection handover/activation at **15:20:59 Toronto**, LiveProtectionPrepared and StrategyEvaluated
through revision four/Active. No saved account blocks, automatic orders or positions. Initial
activation/evaluation are verified; observed session cadence and cutoff are subsequently verified
above. Actual automatic submission/fill/populated protection and longer-run capacity remain unverified.
Preserve original IDs/references and the accepted cutoff; no next-day renewal.

**Assignment approval saved, October 20:** the operator screenshot now explicitly confirms execution
approval saved, approved Normal, Transition state, buying subject to current risk checks and automatic
trading off at approval; approval sent no order. One normal-output owner (PID 118432), corrected UI/original Core
hashes, packet present and then-absent host permission are independently checked. Subsequent host
enablement and initial evaluation are observed above. The original rejection predicate remains unproven; no
diagnostic exception is observed after the correction.

**Earlier assignment approval blocker, October 20:** the operator reports the immediate fresh-review retry
also fails quickly. A hash-verified installed read finds zero managed revisions and unchanged financial
facts in eight recent imports; the last retained setup wait is 4.49 seconds. These do not identify the
exact failing validation predicate. Desktop-only correction retains paper admission through the
existing two-minute review/approval handoff, releases on expiry/close/action, and diagnoses the actual
account input rejected by the unchanged Core approval workflow. Seventeen focused and **2,676 full
serial offline tests pass**, nine native skipped; Release WPF compilation passes. Core/protected
qualification identity remains unchanged. After operator normal close and verified process exit,
the two corrected UI files are installed in standard Release output at **14:41 Toronto**. All 123
other runtime hashes and protected checkpoint/packet hashes remain unchanged; no host permission.
Subsequent normal project restart and successful native approval confirmation are observed above.
No financial save/host enable/order is performed by the agent. [Evidence](reviews/delphi-live-morning-decisions-20261019.md#desktop-approval-diagnosis-and-review-admission-correction--october-20).

**First execution review observed, October 20:** after the normal-output same-build restart, the
operator selects the two original liquidation sales. Screenshots show exact Normal/NormalV1, carried
original references, current source review, actual purchase ceiling, today's regular-close cutoff
and all nine native/scoped evidence references present; assignment approval is available and host
enable remains disabled. Read-only file metadata confirms the protected qualification packet now
exists and host permission remains absent. This is native UI/file-existence evidence, not an
independent audit of protected contents. Subsequent operator assignment approval/readback is observed
above, followed by operator host enable/native initial evaluation. No financial action is performed by the agent.

**Normal checkout startup, October 20:** the operator requires the normal `dotnet run --project
TraderVI.WPF -c Release` workflow on `master`; all TFSA/performance/setup-wait code is already there.
The 125 captured-build runtime files are placed and hash-verified in normal Release output, Core
SHA-256 unchanged at `EAF137241560CAE2AF18042B885708DF6D39353CEC4D448AB3B3DC6C786BD35F`; only docs
changed since source 2cf08c33. Pending checkpoint recovery can therefore use normal project output
with `--no-build`, without a launcher, new source code, proof rewrite or new financial save. The
existing reviewed calendar path is persisted under explicit approval in the real Windows user
environment and independently verified outside the sandbox. The operator restarts from normal Release
output with `--no-build` at **13:43:30 Toronto**, PID **72928**. Read-only inspection verifies one
owner, all 125 runtime hashes unchanged and the dated checkpoint present; qualification packet and
host permission remained absent at the restart check. Subsequent native first review is observed
above; process restart alone did not qualify recovery. Assignment/host enable and initial evaluation are subsequently verified.
Related ignored artifact folders total about **8.45 GiB**, principally repeated build/dependency
copies. No deletion occurred; preserve live output/proof and retained evidence while obsolete-output
cleanup is deferred until normal-output recovery is verified. [Normal commands](running.md#build-and-run-the-local-desktop-checkout).

**TFSA setup coordination follow-up, October 20:** the reviewed performance copy still hits the
two-minute setup wait before account checks. Private stage timings identify an admitted Live cycle
starting 11:54:43, collection 11:58:02–11:58:11, evaluation through 11:58:13 and subsequent actions;
setup waits 11:57:12–11:59:12. This is an overlapping long financial cycle, not completed idle proof.
The prepared correction retains interactive setup's admission until active work drains, reports
elapsed time and allows closing setup to cancel only the wait. New paper cycles remain deferred;
account/sign-in are rechecked after idle, before the queued action. Financial saves retain their
busy-close guard. The noninteractive host wait/SQL fences/source/financial checks are unchanged.
Nine focused and **2,668 full serial offline tests pass**, nine native skipped; separate Release WPF
build passes with 262 existing compiler warnings and cached Pkcs NU1902/NU1903 advisories. No fresh
audit/full-solution/SSDT claim, SQL rehearsal, agent financial action or qualification waiver.
After the automated close does not exit the old app, the operator closes it normally; no owner is
left before launch. Copy **2cf08c33** starts **12:27:51 Toronto**, PID **78688**, in separate
`artifacts/tfsa-setup-wait-20261020/working-copy`. All **125 manifest files/calendar** match, one exact
owner, no host permission, packet or either dated checkpoint. Core SHA-256:
`EAF137241560CAE2AF18042B885708DF6D39353CEC4D448AB3B3DC6C786BD35F`. Subsequent October 20 operator
screenshots confirm readiness completes and carry Preview displays retained original reference
values/evidence times, unchanged cash/buying power, no holdings/pending orders/holds and matching
four original order facts, with today's reviewed close expiry. The operator then explicitly saves
the carry; a subsequent screenshot confirms saved/readback with original references/values/source
times/holds unchanged, no reconstructed historical close/open and no order/assignment/host permission.
The next operator screenshot confirms native checkpoint save after full manual verification and
cash-only baseline readback. Read-only local inspection confirms the October 20 checkpoint exists,
packet/host permission remain absent, PID 78688 uses the exact copy and all 125 manifest hashes
still match. No checkpoint content is decoded or rewritten. The normal-output same-build restart
is subsequently verified above, followed by native UI evidence of all nine references. Operator
assignment/host enable and initial native evaluation are subsequently verified above. Both performance commits remain included; further
history-cost work stays deferred. Receipt: `artifacts/tfsa-setup-wait-20261020/switch-receipt.json`.
The decision log owns detailed evidence and next native observations.

**Desktop responsiveness, October 20, 2026:** the separately approved serialization follow-up
**30775d12 starts at 11:32:14 Toronto**, PID **131204**, execution off, all 124 manifest files matching.
The previous 55e429d5 dashboard finishes at 11:31:57 and exits normally before the new launch.
Both preceding copies and the original TFSA launcher/history are preserved. Four follow-up dashboard
wakes take **91.23/73.07/217.46/65.05 seconds**; the full collection/action wake has **125.13 seconds**
of actions, effectively unchanged from the preceding copy's last 125.31-second action stage. No clear
native cycle speedup is established. Five UI applies take **19.98–41.70 ms**, with no recorded pulse
gap over two seconds and all 38 process samples responding through **11:42:00**. Memory remains
variable (714–3,168 MiB). Actual tab-navigation/Stop invocation, setup idle and sustained capacity
remain unverified. One hash-verified installed read confirms a single lease and new publication/
11:35 evaluation with the historical gap retained; the desktop remains running with execution off.
The worker/display correction previously exposed Live snapshot/protection/action cost while bounding
UI applies. The serialization change preserves exact reopened-history checks, lease fencing and
durability: fictional retained-object validation falls from 307 ms/248 MB allocated to 0.244 ms/
1,064 bytes. **61 focused/2,664 full serial offline tests pass, nine native skipped; isolated WPF
Release passes** with existing compiler warnings. No fresh security audit/full solution/SSDT claim.
No force-stop, profiler attachment, native SQL rehearsal, agent order, financial save or trading
enable ran; normal approved paper/account synchronization continues. Same-copy checkpoint/restart,
source and financial qualification remain separate requirements after the build change.
[Findings, measurements and exact installation steps](reviews/desktop-paper-responsiveness-20261020.md).

**Operator handoff:** further history-cost investigation/implementation is explicitly deferred.
Code commits **55e429d5/30775d12** and native evidence **d3ce404d** are on `master` in
`C:\src\TraderVI`, the same checkout used by **Enable live TFSA trading**. That session can resume
qualification using the current corrected copy; all existing financial/source/recovery checks remain.
No additional code change or operational switch is part of this handoff.

**TFSA session resumed, October 20, 11:53 Toronto:** following separate operator approval, the
unexpected standard Release owner PID 61768 closes normally. The reviewed performance copy
**30775d12**, including **55e429d5**, starts at **11:53:41**, PID **102648**, in
`artifacts/desktop-history-cost-20261020/working-copy`. All 124 manifest files and the calendar are
verified; one visible responding owner, no host permission, qualification packet or October 19/20
checkpoint. Core SHA-256 remains `CCC43CE8A4BFB5D8DF703E843E8E8590275EA0BAA41A6EC0728ADAA2311236AF`.
Normal paper/account synchronization resumes within the approved scope. Original records/copies
remain preserved; no agent financial action or SQL rehearsal. Next operator retries the October 20
carry preview; actual idle/account/source/carry and same-copy recovery remain unverified.
Receipt: `artifacts/desktop-history-cost-20261020/tfsa-resume-switch-receipt.json`.

**Broker history diagnostic extension, October 19, 2026:** v2 is separately installed under explicit
approval, retaining v1. It adds only exact owned activity/history reads, discovered-order-only request
guarding and final account/activity cross-checks. Sixty-six offline/four approval-scope checks pass;
Release publish passes with two existing linked-reader nullable warnings. After normal reconnect
following a 10:32 login-verification failure, the exact installed v2 command passes at **10:38 Toronto**:
42 reads, 33 historical orders (posted/expired/cancelled), matching holdings/history, two CAD whole-share
holdings and no pending orders. This does not qualify native submission, partial/uncertain recovery
or the local journal; no execution qualification is issued. No SQL, journal,
credential refresh or trade mutation code is linked. [Procedure](../Operations/BrokerReadOnlyChecks/README.md),
[ADR-0093 amendment](adr/0093-narrow-read-only-broker-checks.md#october-19-reviewed-history-extension).

**Read-only broker diagnostic access, October 18, 2026:** the operator-authorized fixed helper is
installed outside sandbox-writable roots with its own narrow no-profile `-File` approval. It compiles
only the existing read parsers/models plus a fixed session loader and independent exact-request guard;
no general Core, login/refresh, SQL or order-mutation implementation is linked. Fifty-four synthetic
offline checks pass. The installed native check succeeds at 23:12 Toronto with seven requests for
verification, discovery, financials, balances, holdings, pending orders and daily performance. No orders,
token refresh or saved-login change occurred. Detailed snapshots are CurrentUser-encrypted; only
counts/flags/times are reported. Four rule-scope tests pass. Release build succeeds with two nullable
warnings in the linked account reader; local-cache restore had auditing disabled, so no fresh
dependency-security assessment/full-solution qualification is claimed. Existing chats must reload
rules and use this fixed command; old mutable NativeRead/dotnet commands retain their approval boundary.
No migration, app restart, trading activation or push was run. This is read access, not market/risk/
execution qualification. [Procedure and evidence](../Operations/BrokerReadOnlyChecks/README.md),
[ADR-0093](adr/0093-narrow-read-only-broker-checks.md).

**Local read-only diagnostic access, October 18, 2026:** the operator-authorized fixed helper is
installed outside sandbox-writable roots with a narrow no-profile `-File` approval. Migration 046
adds a loginless dbo-SELECT user; no application rows or existing identities' permissions changed.
A fresh checksum-verified backup and hash-matched destination copy preceded installation; remote
sync/restore qualification is not claimed. The v2 helper has 33 passing offline SQL-policy cases,
12 passing native checks and a successful installed-command smoke read. Its standalone Release build
and the SSDT database project build pass. Dependency restore used the local cache with NuGet auditing
disabled; no fresh dependency-security assessment or complete solution qualification is claimed.
Initial native DML-denial evidence is retained separately. The v2 helper also rejects cross-database/
procedure routes before connecting. The prior broad sqlcmd approval
is removed. Existing Codex chats need a restart to reload rules. This covers local SQL diagnostics;
the separate broker/Windows-protected-login prompt remains outside its scope. No broker request,
order, collection/training, application restart or push was performed by this setup.
[Procedure and evidence limits](../Operations/ReadOnlyChecks/README.md), [ADR-0091](adr/0091-local-read-only-database-checks.md).

**Delphi Live → TFSA preparation, October 18–19, 2026:** Normal remains the integration target;
the [assessment](reviews/delphi-live-tfsa-readiness-20261018.md) owns negative paper returns/evidence limits.
Actual available capital after two full reviewed sales, no outside transfers/trades and stopping new
buys on mismatch are accepted first-day constraints (ADR-0092). The explicit October 19 amendment
allows a flagged limited trial to replace only production cash-event/opening-value proof; four other
native launch checks and all five scoped checks remain. Older scopes/production retain all six.
The default-off desktop host and first cash-start execution review are now implemented/tested.
The review derives actual available capital, lists every required qualification, and saves approval
only after exact source/evidence/account rechecks. Approval sends no order; enabling the exact
approved account/strategy/build is separate. The host shares the existing timer/journal/gate/SQL lease
and preserves buying mode, risk holds and original order IDs. Two explicit existing Sell all actions
remain the simplest supervised opening; no new automatic-liquidation owner is installed.
Earlier limited-cash validation: **158 focused/2,558 full Core tests pass**, nine native skips;
the subsequent native first-day producer validation is recorded below. The earlier **two
explicit native SQL rehearsals passed** in retained synthetic databases; they were not rerun after
the memory pause. Those rehearsals cover first-review approval
save/reopen, precise session/protection readback, leases/interrupted and uncertain saves, baseline
persistence and hold-chain recovery. They found/fixed the existing continuation column-mapping bug.
All broker/qualification facts in those rehearsals are synthetic. Nine offline WPF renders, including
blocked/prepared/approved first-review states, pass without binding errors. Release builds pass with
262 existing compiler warnings; cached Pkcs advisories remain separate. No fresh audit/full-solution
or SSDT claim; no database schema or actual financial record is changed by this increment.
The limited-trial producer recovers the original baseline import and both verified full-sale records,
preserves references/holds, saves protected scoped proof and retains independent native proof unchanged.
It cannot issue the remaining four native qualifications. Missing proof blocks approval. Purchase
permission ends at the reviewed close, checked in source/workflow; ongoing protection grants no
next-day buying permission. The desktop build passes; separately approved actual-account startup
is recorded below.
**Automatic trading remains off/NO-GO.** No actual grant, opening plan, baseline, assignment or order
has been created by this session. The remaining independent native source/order/restart/storage
qualification packet is not installed, and actual opening sales/initial setup remain. The first
review/scoped reconciliation path is implemented; actual qualification and operator approval remain.
The complete native cash-effective/boundary producer remains unfinished for general production and
transfers; the explicitly approved limited trial does not claim that qualification.
At 23:51 Toronto the hash-verified account-storage read finds one link, zero baselines/continuations/
managed revisions and enabled immutable triggers. At 00:17 the exact Normal progressive successor
had zero evaluations and no Monday official run/frozen session/evaluations yet. At **10:03 Toronto
October 19**, the two expressly resumed read-only helpers succeed: one connected eligible account,
two CAD whole-share holdings, zero pending orders; Monday official/frozen inputs exist and the exact
Normal successor has 160 evaluations through 09:55. Publication does not prove executable freshness
or strategy/order qualification. The small SQL query's receipt hash matches. No further SQL tests,
memory setting changes, app launch or financial operation ran. Helper launch successes and later PowerShell-wrapper approval prompts retain
their separate scope. [Morning decisions, working copy, permission handling and remaining checks](reviews/delphi-live-morning-decisions-20261019.md).

At **10:33 Toronto October 19**, the small hash-verified installed-storage read finds one link,
zero baselines/continuations/managed revisions, all three immutable triggers enabled, and the session
JSON column installed as max-length storage. This confirms current metadata, not production writes
or populated assignment recovery. Trial integration is committed as **ae082d4e** after first-review
checkpoint **cac6502e**. The rebuilt working copy/hash manifest include the limited-trial path;
the operator separately approved closing the old desktop and starting that prepared copy with normal
account/paper synchronization, leaving execution off. At **10:52 Toronto**, the hash-verified copy
starts as PID 76640; its visible TraderVI window responds. The supplied native Execution setup
screenshot confirms installed storage, missing initial references/assignment and disabled approval/
enable controls. At **10:57**, a hash-verified bounded SQL read finds the 10:52:28 reconciled import
and matching trade-sync receipt, with no import/history/observation blocks. This verifies that startup
and reconciliation path, not order submission, populated baseline/session recovery or automatic trading.
The operator subsequently authorizes preparation of a manual order-verification sequence; execution
of those orders remains theirs. [Detailed receipt and decisions](reviews/delphi-live-morning-decisions-20261019.md#supervised-working-copy-startup-and-manual-verification-preparation).

**Manual verification preparation, October 19:** the authorized operator-executed sequence is
[prepared for review](concepts/supervised-tfsa-opening-sequence.md#operator-review-bounded-native-order-verification-october-19).
It adds return-to-cash after a test buy, before initial references. The new desktop explicitly selects
the original two verified sale IDs instead of absorbing all same-day sales; it rejects unrelated,
partial/conflicting, ambiguous or unmatched records. Early setup now correctly names the four native
trial checks. **173 focused/2,573 full Core tests pass**, nine native skips; five synthetic UI renders
and selection assertions pass without binding errors. WPF Release passes. An initial broader run had
one five-second AI-host timeout; its seven tests and the full serial rerun pass, without AI changes.
Existing compiler warnings/cached Pkcs security advisories remain separate; no fresh audit/full-solution
or SSDT claim. The separately approved switch to `order-review-working-copy` completes at **11:21
Toronto**: all three hashes match, the old copy closes normally and PID 119176 has one visible,
responding TraderVI window. No host grant exists before or after startup. The **11:24** hash-verified
bounded read finds its 11:22:15 snapshot/11:22:16 import and matching trade-sync receipt, with no import,
history or observation blocks. Baseline/continuation/managed-revision counts remain zero. This verifies
the updated copy's startup and one reconciliation, not its unobserved native sale-selection dialog,
current strategy execution or order lifecycle. The native packet/independent four-check qualification
and actual financial setup remain unfinished. No real orders, baseline/grant, SQL rehearsal or memory
change ran in this increment. [Switch receipt/evidence](reviews/delphi-live-morning-decisions-20261019.md#updated-copy-supervised-switch-october-19-at-1121-toronto).

**Native first-day qualification producer, October 19:** the operator explicitly approves the
bounded native acceptance contract (ADR-0092), accepting disclosed performance gaps after exact
source review and native full-fill/normal-restart/reference/storage checks. The checkpoint,
original-ID readback and protected nine-reference publisher are now implemented/tested. Every proof
is first-session-only; production/older scopes cannot reuse it. A durable automatic-order review hold
blocks new/residual orders after partial/cancel/conflict/uncertainty and survives resume/restart,
without changing the operator's buying mode or financial holds. **205 focused/2,605 full Core tests
pass**, nine native skips; seven synthetic UI renders/WPF Release pass. Existing compiler warnings
and cached Pkcs advisories remain; no fresh audit/full-solution/SSDT claim. At 11:54 the hash-verified
bounded read confirms the immutable-storage predicate, with setup tables still empty. The separate
`native-review-working-copy` is committed as **e054e88f** and starts at **12:07 Toronto**, PID 43132:
hashes match, old instance closes normally, one responding desktop remains, no host grant/packet/
checkpoint exists. The 12:08 hash-verified bounded read finds its clean new import/trade-sync receipt
and empty setup. At 12:10 existing global/Tracked pauses and capital/daily-loss hold flags are clear;
native tickets still recheck them. Later operator screenshots show completed original sales and the
one-share verification round trip, followed by cash-only/no pending orders; these have not yet passed
the native qualification publisher. Risk setup Refresh reportedly stays blank. Offline reproduction
confirms clipped Material button labels; a WPF-only correction adds readable Workspace controls,
initial status and explicit busy/empty-result messages (six synthetic renders/build pass). Its separate
`risk-setup-working-copy` retains the exact reviewed Core hash. The underlying failed refresh still
needs native screen evidence. At 14:42 a hash-verified bounded read finds zero baseline/continuation/
managed revisions; process inspection finds the normal bin/Release PID 61196, started 13:57.
The corrected 74cb0987 copy starts at 14:45:58 as PID 52288 after normal old-process closure;
hashes match, one responding desktop remains and host permission/packet/checkpoint are absent.
Actual refreshed-screen preview, saved baseline, checkpoint, post-restart observations and packet
remain pending; execution is off. General production/native
failure qualification remains deferred. [Exact operator route and evidence](reviews/delphi-live-native-qualification-20261019.md).

**Latest operator setup evidence:** in the **bc5a7f09** diagnostic copy (verified startup 14:53:20,
PID 106248), Refresh succeeds with an actual 14:55 observation, matching cash-only setup sources,
no carried financial holds and zero protected positions. The operator then explicitly saves and supplies
the native success confirmation. These are operator screen observations, not independent SQL readback
or completed qualification. A later reopen/Refresh screenshot recovers the same original saved
request/references (saved 14:56). Capture then fails with a specific four-column storage-readiness
mapping error; a further attempt shows only the generic failure. No checkpoint success is established.
The C# record has three constructor arguments while SQL returns four Boolean columns; the fourth
becomes an optional constructor argument, retaining false for older three-argument callers. Seven
database-free mapping cases and the broader 257 focused tests pass; WPF Release passes. A separate
storage-shape copy has a new Core hash; 2,614 full offline tests pass with nine native skips. The
**71207c86** copy starts at **15:06:33 Toronto**, PID **73824**, after normal prior-process closure:
hashes/one responding owner/absent host permission, packet and checkpoint verified. The operator's
next capture still reports an unexpected failure; native file-existence inspection confirms no
checkpoint, packet or permission was created. A WPF-only staged diagnostic now covers every native
read/validation/protected-save boundary and reports the safe error category/SQL number. Nine focused
checks and WPF build pass; Core remains this mapping-corrected BFE24D07 build. The **a78f0810**
checkpoint-diagnostic copy starts at **15:15:15 Toronto**, PID **28564**, after normal prior closure;
matching hashes/one responding visible owner/absent permission, packet and checkpoint are verified.
The next operator capture identifies **SQL 51413** during reconciliation: the 30-second
EngineSettings application-lock wait failed. Code review shows desktop paper cycles hold shared
settings leases while import saves require an exclusive fence; the actual SQL lock-owner session
was not inspected. A WPF-only scheduling barrier is prepared: finish active scheduled/manual paper
cycles, defer new cycles during setup/enabled-host work, preserve every SQL fence, and resume after
success/failure/cancellation. Coordination wait is bounded at two minutes and does not cancel active
paper work. Five barrier cases plus the focused set pass (**199**); WPF build passes, Core unchanged.
The coordinated **943f964b** copy starts at **15:30:19 Toronto**, PID **74844**, after normal prior
closure. Matching hashes/unchanged Core, one responding visible owner and absent permission, packet
and checkpoint are verified. The next native capture passes reconciliation and stops at Normal
source discovery: strict original/current policy equality rejects the already-approved progressive
successor. A 15:35 hash-verified bounded read finds exactly one active Normal and its allowed
successor. The correction discovers that immutable account, then requires its latest native saved
assignment, installed successor view and exact original/current policy comparison. Original comparison
identity and recommendation eligibility remain unchanged. 217 focused and 2,621 full offline checks
pass (nine native skips); WPF Release passes with existing warnings. At 15:43 a second hash-verified
bounded read confirms one active Normal and one latest assigned allowed successor. Corrected copy
**62c7b5fd** starts at **15:46:33 Toronto**, PID **58536**, after normal prior closure; exact path,
manifest hashes/new Core, one responding visible owner and absent authority/packet/checkpoint verified.
Historical-policy fix **0accf067** is installed/verified at 15:58:57 (PID 59220), with 41 focused/
2,622 full offline passes, nine native skips/WPF Release. The first 16:00 read was stale, but a
21:56 hash-verified read now confirms Normal published at **16:03:40**, with matching pinned daily
and allowed successor. Operator UI shows the 16:00 checkpoint/120 observations. A 21:57 bounded
read confirms session finalization at **16:03:48**, honestly Incomplete/Blocked with a host gap.
Private app metadata records a later **16:21 SQL 52202**: repeated finalization reused an expired
lease. A close-once host fix renews before finalization, persists closing/research once, releases
its lease and reads status on later wakes; 37 focused/2,624 full offline passes (nine native skips)/WPF build. Correction **cf0eee49** starts at **22:20:53 Toronto**, PID **1036**, after normal prior closure; exact output/Core hashes, one visible responding owner and absent authority/packet/checkpoint are verified. Initial startup response was false, then true on recheck. No SQL fence/history/coverage rule changes. No new regular-session publication
is expected overnight. The October 19 trial purchase window expired without checkpoint/packet/
authority. The accepted [October 20 contract](reviews/delphi-live-next-session-contract-20261020.md)
is implemented: separate preview/explicit carry save, immutable original-anchor rechecks, original-ID
capture/restart and all nine trial-only references. The full offline suite has **2,655 passes/nine
native skips**; WPF Release and six synthetic panel renders pass. Copy **b1095ae9** is installed at
**23:08:41 Toronto**, PID **83608**, next-session-working-copy: manifest/Core hashes match, one visible
responding owner; host permission, packet and both dated checkpoints are absent. Actual October 20
carry save, fresh source/native checkpoint, same-copy
restart, packet, operator assignment and enable remain unverified. No agent financial action occurred.
October 20 operator Preview times out at the two-minute desktop-paper-cycle wait, before account
checks; no carry preview/save is confirmed. A hash-verified bounded read at **10:20:26 Toronto**
shows the session Monitoring/Blocked with its host gap retained, recent ledger publication at
10:20:16 and lease renewal at 10:18:54. This proves ongoing paper progress, not completed idle or
qualification. No new private Delphi failure metadata exists beyond October 19. The second reported
timeout leads to the [responsiveness correction and stage diagnostics](reviews/desktop-paper-responsiveness-20261020.md),
installed under explicit approval at 11:03:56. Native timings expose long Live snapshot/protection/
action work; a serialization follow-up is prepared only. Setup idle remains unverified, barrier intact.
Preserve original anchors and verify the closing fix at the next actual boundary. Existing
baseline/orders remain preserved. The earlier unexpected risk-screen failure did
not recur and its cause remains unlocated; no guard or reference reset was used. Two diagnostic
privacy tests/build passed before this Core correction. Execution remains off. Detailed receipts/history stay in
the [decision log](reviews/delphi-live-morning-decisions-20261019.md).

**Trading UI implementation, October 18–19, 2026:** the operator authorized implementing the accepted
Workspace layout in Trading only; other tabs, shared styles and decision/execution engines are unchanged.
The broker account now leads with compact Today/Overall performance, account value/cash, a strategy
dropdown and four-column holdings. October 19 running-screen feedback moves the selected holding
into a horizontal panel above the full-width table, with position facts, strategy explanation and
existing Buy/Sell controls. Narrow windows wrap those sections; an empty selection is compact.
The layout is built/offline-verified with six synthetic renders at 1700/1200/900/680px and no binding
errors, including exact Sell targets and full table width. It is not installed in the running app.
Repeated account headings and net deposits are omitted.
Legacy recorded-account/recommendation tools remain available in a collapsed section.
The dropdown opens the existing exact-source transition review and restores the actual selection when
it closes. The subsequent TFSA preparation exposes exact Normal comparison preview in source;
recommendation-only comparison approval remains excluded. Normal is the working preparation target,
with actual assignment/execution still pending the separate qualification route above.
The operator accepts queuing old-strategy-only exits as checks allow, including at a loss, with current
protection retained; shared holdings stay and required exits must reconcile before new buys (ADR-0079).
Automatic switching/execution remains unfinished and disabled; no assignment or order was changed.
The subsequent real-data request accepts Wealthsimple's one-day view. Trading now requests the native
latest `ONE_DAY` / `REGULAR` simple return, verifies its previous-close reference and preserves its
valuation time. Prior-date results have an explicit dated heading; missing values never use Overall
or an older complete point. The existing capture qualifies that contract; native access has separate
read-only helper evidence above, while comparison against Wealthsimple remains unverified here.
Overall retains its supplied amount/rate/period/as-of. The daily reader is display-only and isolated
from risk/accounting/order behavior; its initial restart deferral is historical, and the operator's
October 19 screenshot shows a populated performance card. This layout pass runs no new broker read.
Missing verified holding decisions are not called Hold;
linked management records can explain planned exits and queued/pending/uncertain sales.
.NET 10 WPF build and 233 focused tests pass, including 39 new daily-reader/control checks.
Four initial synthetic WPF renders at 1200/900/680 widths verify
selection, command target, Today/Overall bindings and dropdown label, with no binding errors. Existing
nullable and unused-variable compiler warnings remain; dependency audit was disabled for restore,
so no fresh security assessment or full solution/SSDT qualification is claimed. Evidence and remaining
limits are in the [implementation record](concepts/trading-workspace-redesign-draft.md#october-18-trading-only-implementation).
At the operator's subsequent rebuild/restart request, the normal Release output rebuilt with zero
errors and 262 existing compiler warnings. No older TraderVI process was running. The Release desktop
started at 22:15 Toronto and PID 20036 was responsive with the TraderVI window at 22:16. The executable
path and output WPF/Core SHA-256 hashes were recorded in ignored `artifacts/ui-redesign-20261018/`.
This verifies launch/readiness for operator review, not native Trading bindings, broker/data freshness
or strategy execution. Normal startup runs existing configured services; no separate migration,
training, broker diagnostic, manual order or strategy activation was invoked. Earlier HTML concepts
retain their browser-preview limitation. The operator also authorized committing/pushing the pending
design/implementation checkpoints; those checkpoints were synchronized at `b7d70aa2`. The new daily
reader follow-up is a separate local increment.

**AI paper delivery / Dots saved-input trial, October 17, 2026:** the operator requested a
remaining-work recap and asked about Dots instead of direct API calls. The reviewer foundation
is built; qualified invested inputs/v3 interpretation and recurring reviews/display remain before
Milestone 1, with action/protection integration, paper execution/switching, outcome learning and
end-to-end recovery validation still required for Milestone 2. October 14 collection recovery does
not qualify the separate AI risk reader. The operator authorized one historical, non-executing Dots review and confirms the Dot is ready.
Its API-replacement feasibility remains unresolved, documented
in [open questions](reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026).
The local ignored bundle `artifacts/ai-dot-saved-review-20261017/` is prepared from the checksum-verified
October 1 15:05 UTC saved packet (15 choices/four eligible), using current v3 rendering and response
schema. The original journal checksum is unchanged; the prior request was rejected before generation
for size. Input dates/eligibility are unchanged. The attachment has 30,781 input characters plus
instructions/schema. Core build passes (.NET 10.0.401; 260 existing compiler warnings/no errors).
The returned Dot proposal now passes the unchanged JSON schema and production domain validator as
historical `wait`, with all 12 evidence IDs valid, no overrides and a permitted two-minute interval.
Sixteen saved-packet factual checks pass; holdings/value arithmetic, candidate guidance, daily gate
results, incomplete performance history and expiry agree. The Dot correctly distinguishes the old
schedule explanation from the packet's current evidence. Its wait is permitted, not required by
candidate eligibility. Interpret the pre-review `FreshAiReviewRequired`/zero allowance carefully:
the review supplies missing AI attention, so this is not an independent reason for perpetual waiting
(packet builder deliberately excludes that block from choice eligibility). Breadth/entry guidance
remain discretionary, requiring recorded exceptions if disregarded. No hallucinated fact or explicit
hard-rule violation was identified; no later outcomes were used. This proves one valid wait replay,
not eligible selection/switching, decision quality or unattended execution. Earlier ten synthetic
validator checks retain their separate scope. One initial helper export failed on an
ambiguous JSON overload before export and was corrected. No fresh dependency audit/full solution or
SSDT build. This response-validation step required no rebuild, new model/market call, SQL access,
operational launch or trading. Raw answer and check results remain private in the ignored bundle.
Any further Dot trial needs its own agreed scope; API replacement and automatic operation remain open. [Procedure](running.md#dots-one-saved-input-replay).

**Current follow-up, October 14, 2026:** the nine missing bars are investigated; a narrow live
request-end correction and clearer stale-source metadata are implemented/tested/installed at
11:09 Toronto. Its first scheduled cycle is verified: 36/42 usable, five StaleNoNewBar, one
NoCompletedBar and zero CollectionFailed; all three Live books have complete current 11:10 marks
as checked at 11:15. Five of the original nine supply usable later bars; ACQ/WILD/WPK remain stale
and ESI lacks the exact bar. This is later prospective recovery, not repair of the original interval.
The previously installed exception/clock correction
continues collecting: SELECT-only evidence at 10:38 records three completed 10:25/10:30/10:35
cycles with zero CollectionFailed, but nine/ten/nine missing exact bars. ARE, PSI and YGR from the
original missing group supply usable later bars; the group is not a permanently failed stock list.

The authorized 27-request comparison completes at 10:42 with zero retries or transport/GraphQL
failures. For the original 10:20–10:25 bar, ARE's aligned record appears only when source context
extends to 10:30. The other eight exact bars remain absent: six have zero reported minute records
in that bucket; CVO/PSI each have one. Absence is not proof of no trades. There are no complete
five-minute component comparisons for these missing intervals. Earlier shared aligned bars agree;
the changed WPK row starts at 10:25, in the following interval. Later retrieval cannot repair an
original on-time slot. See [ADR-0089](adr/0089-tmx-session-context-and-supplementary-records.md).

Delphi Live now opts into same-session source context only through the observed fetch-start minute,
inside the next interval and bounded by the regular close. Logical bounds, exact completed prices,
deadline/durability/conflict/confidence checks and request counts remain unchanged. History and
ordinary diagnostics retain their existing API/end bounds. Actual source end and latest aligned
source time persist as optional receipt JSON metadata; an older source is labelled StaleNoNewBar
without substituting that price. No synthetic minute/five-minute bars or new feed are introduced.

Validation: 77 focused tests pass; final Core suite 2,405 pass/eight native skips; Release WPF project
build succeeds with 260 existing compiler warnings and no errors. One initial full run timed out in
an AI host-loop stop test; the isolated test and two later full runs pass. The new isolated artifact
path lacked restored assets; validation reused the already restored test-build path. No fresh
dependency-security audit or full-solution/SSDT build. The authorized WPF restart replaces five
checksum-verified binaries with unchanged dependency/runtime manifests and a saved rollback copy.
The 11:10 endpoint completes at 11:12:44; 123 policy evaluations persist, 105 observed on time.
All 42 receipts request context through 11:12 without exceeding fetch time, changing logical bounds,
using a wrong/forming eligible bar or violating original durable deadlines (zero checked violations).
At 11:15, WPF owns one renewed lease, all three Live books have complete 11:10 marks and all 24
current Shadow positions have same-day 11:05 prices. No private whole-monitor failure log exists;
historical host/opening/closing gaps remain. No SQL mutation/backfill, training, AI call or broker order
was requested outside normal authorized paper monitoring. The
27-request authorization is consumed. Private evidence: `artifacts/live-missing-bars-20261014/`.

The following installed milestone preserves the earlier October 14 verification and its limits.

**Earlier installed milestone, October 14:** source/clock fixes were installed at 09:56/10:21 Toronto.
The 10:25 endpoint settled 42 slots: 33 usable, nine missing and zero CollectionFailed. At 10:29 all
three Live books had complete current 10:25 marks; all 17 Shadow positions had same-day 10:20 prices.
One lease, fresh heartbeat, responsive modules and no private whole-monitor failure record were
verified. Historical host gaps and missing opening/closing marks remain. The first six-request
comparison matched 69 complete five-minute OHLCV aggregates; the opening trace identified SQL
52216, and 20 SELECT-only measurements showed up to 12.9225 ms SQL/.NET clock lag with zero binding
loss. Original timestamps and strict SQL guards remain unchanged. Native causal checks pass 20/20.
The subsequent four-request comparison qualified trailing-record handling across sparse gaps; both
earlier provider grants are consumed. Detailed installation/validation receipts remain in
`artifacts/live-collection-fix-20261014/README.md`; source commits `883d0a94`, `1837ff07` and
verification commit `e61c833b`. Continuous coverage and clean promotion are unqualified.

The following October 9 record preserves the prior AI-review milestone and diagnostic history;
its collection-correction next step is superseded by the October 14 record above.

**Prior AI-review record:** October 9, 2026. The approved v3 review view removes engineering/testing details and detailed evidence for unavailable strategies, retaining short dated blocking notices and independent managed-account/learning context. It is built/tested; the largest revised request among seven saved examples (four eligible choices) counts 11,859 tokens against the unchanged 32,000 cap. This is historical size evidence, not a fresh decision or a bound on future input growth. The operator authorized one fresh v3 review, but the 13:49-13:51 Toronto preflight found stale invested-account prices, so no review or paid call was started. The 14:30 timestamp comparison confirms TMX supplied the off-grid times and the local mapper preserved them in this sample; XIU also supplied an aligned row, but the validator rejects the whole response. The narrow 14:51 interval comparison could not verify full five-minute aggregates, so selective row acceptance is not qualified. Next verify the provider request/interval contract and restore qualified held-stock valuation through the agreed input path, then use the unconsumed single-review authorization; repeated successful desktop reviews and basic display follow. Native startup, blocked-attempt timing, bounded stop and disabled restart remain verified; v3 desktop installation remains unqualified and it has no model answer. Automatic reviews and AI trading remain off; all three milestones remain pending. Persistent collection failures, invested coverage, portfolio-specific evidence and Athena outcomes remain separate. TMX remains normal; Wealthsimple fallback and lesson criteria remain deferred.

**Narrow interval check, October 9, 14:51 Toronto:** the operator authorized the short check of
whether aligned records can safely be retained. Four read-only requests compared one- and five-minute
responses for MTL and XIU over the same 13:50-13:55 window, with 25-second per-request deadlines and
no retries. MTL supplied one complete point at 13:54 in each response, with identical OHLCV and no
13:50 row. XIU supplied two complete points (13:50, 13:52) in each response, but neither five-minute
row exactly matched its same-timestamp one-minute row. Only one/two of five exact minute components
were present, so neither complete five-minute aggregation could be checked. Missing rows were not
assumed to mean no trades. This does not prove the aligned row is invalid or safely reusable, nor that
the interval parameter is ignored. No selective acceptance fix was applied or qualified.

Next inspect the TMX request/interval contract to establish the requested aggregation, rather than
continue timestamp-only sampling. Preserve existing rejection and freshness rules; the exact Shadow
failure and held-stock recovery remain pending. The four-request scope is complete. There were no
SQL/journal writes, app changes/restart, AI calls or trades. Three synthetic script checks (aggregate
match, mismatch and missing-minute rejection) passed before the requests; no Core build/test rerun was
needed because production source did not change. Private evidence:
`artifacts/price-refresh-20261009/{Compare-Interval.ps1,interval-comparison.json}`.

**Price-refresh trace, October 9, about 14:00-14:02 Toronto:** SELECT-only records and source
inspection confirm active collection with partial failures, not a wholly stopped desktop. The 13:55
Live window settled 34 targets: 17 usable, 11 CollectionFailed and six NoCompletedBar. Completed
cycle status does not mean every stock succeeded. The selected AI source book still has one holding;
its last position update at 10:38 retained the five-minute bar starting 10:25 (valuation end 10:30).
Five held-symbol Shadow five-minute requests succeeded earlier; 31 failed, including the latest
13:58 request. All 25 held-symbol fifteen-minute requests failed. Separately saved bars reach 13:15.

`SystemShadowController` updates marks from successful current collection batches. A failed collection
does not substitute the latest database bar, and the risk reader intentionally uses the saved holding's
exact mark. Live and Shadow share `TmxClient` validation but request different windows. Their saved
failure codes omit exception detail; this cannot prove a common cause or distinguish Shadow fetch
from persistence failure. October 2's alignment rejection remains historical evidence, not today's
diagnosis. The separate 13:45 Live cycle-level failure is also unresolved.

**Authorized fixed-window diagnostic, October 9, 14:21 Toronto:** one bounded run requested MTL
(the screenshot target with a saved failed receipt) and XIU for 13:50-13:55 Toronto. Both rejected in
`TmxClient.ValidateIntradayResponse` line 394: mapped timestamps were 13:54 for MTL and 13:52 for XIU,
not five-minute boundaries. Neither timed out. Each logical request allowed at most three transport
attempts; actual failure transport counts are unavailable. This reproduces a precise rejection on the
same requested window, not the exact original provider response or every collector failure.

The request builder passes interval 5 and the requested UTC bounds. The mapper parses source date text
as Eastern time; this diagnostic did not retain raw timestamps or the complete response, so source
semantics versus mapping error is not settled. Do not round/drop offending rows or weaken validation
without establishing their meaning. Shadow uses the same validator with wider requests, but its exact
failure remains unconfirmed; no held-symbol Shadow request was part of this two-target authorization.
The subsequent timestamp comparison below resolves raw-versus-mapped times for these two requests.
The single v3 review remains authorized and unused, subject to current inputs and existing bounds.

No source changes, SQL/journal writes, restart, AI call, freshness relaxation or tests/builds occurred.
The provider diagnostic authorization is consumed; no retry ran. Evidence and the exact command are
under `artifacts/price-refresh-20261009/`, including `provider-diagnostic.log`. This historical-window
read cannot qualify fresh valuation, repair saved gaps or establish recovery.

**Timestamp comparison, October 9, 14:30 Toronto:** following the operator's instruction to continue
the timestamp investigation, the extended diagnostic made one request/transport attempt each for the
same MTL/XIU 13:50-13:55 window. MTL returned one complete point whose original date text was
`2026-10-09T13:54:00-04:00`; it mapped to 17:54 UTC. XIU returned two complete points with source
13:52 and 13:50 at offset -04:00, mapped to 17:52 and 17:50 UTC. Neither timed out. The off-grid
minutes were present in the provider response, not introduced by mapping in this environment/sample.
The five-minute validator rejects the entire XIU response despite its additional aligned 13:50 row.
This identifies the observed rejection mechanism; it does not prove what the off-grid rows represent,
that the aligned row is independently usable, or the exact cause of wider Shadow requests.

The opt-in inspection copies only bounded timestamp metadata before unchanged mapping/validation;
it cannot alter source rows or supply accepted evidence. Next establish the off-grid record semantics
before choosing a correction; preserve strict bar identity and historical failures. Do not simply round
or silently discard rows. Further provider investigation needs a concrete bounded scope/authorization.
No AI call, SQL/journal mutation, restart, trading or price repair occurred. Automatic AI remains off.

Validation: .NET 10.0.401, 47 focused offline tests pass (inspection, normal validation, diagnostic window
bounds and missing-minute diagnostics); Sandbox builds with three existing CS8632 warnings and no errors.
No dependency-security audit or full-solution/SSDT validation ran. The initial test compile found a
namespace qualification error in the new test, corrected before the passing run. Evidence:
`artifacts/price-refresh-20261009/{timestamp-diagnostic.log,timestamp-tests.log,timestamp-build.log}`.
The final output-only correction retains observed attempt counts in the failure summary; it was tested
and built after the native sample, without repeating provider calls.

**Fresh v3 review preflight blocked, October 9, 13:49-13:51 Toronto:** the operator explicitly
authorized one test using fresh inputs to obtain an AI answer. SELECT-only preflight found the selected
Breakout 3 / PortfolioRulesOnly book now has one open holding. Its valuation endpoint is 10:30; the
held-position mark is 10:25 and even the latest separately saved five-minute bar is 13:15 as checked
at 13:50. None supports the existing ten-minute test allowance. The runtime reads saved Shadow book/
bar evidence; the separately built faster TMX path is not composed into it and must not be substituted
as an unqualified shortcut. No timestamp or age limit was changed.

All 15 choices remain in the catalogue. The three Live contexts were fresh during the 13:49 check but
did not permit proposals; the 12 Shadow contexts were stale. This does not itself prevent a wait
review once account valuation qualifies. The desktop was responsive; current collector receipts
still included failures and missing bars, with a blocked-coverage session and a prior host gap.
The exact held-stock refresh failure remains to be traced; this does not establish a provider cause.

Review mode was not invoked, so no scheduler attempt, SQL review row, journal update, spending
reservation or model generation was requested. The single-review authorization remains unused for
this test, subject to fresh qualified inputs and the existing bounds. At 13:51, settings matched their
preflight checksum and remained disabled; seven local review records and two calls were unchanged,
with no pending review/unsettled call and conservative October spending still CAD 1.3087. This step
added CAD 0. No source/build/test changes or desktop restart occurred. Private evidence is under
`artifacts/ai-v3-review-20261009/{preflight.log,signals.log,current-state.txt,preflight-result.json}`.
Next trace/restore held-stock price freshness without weakening checks, then run the authorized
single review and verify its interpretation, SQL/local records and cost. No v3 answer exists yet.

**Approved input scope and v3 validation, October 9:** the operator accepted removing internal software/
format labels, research test schedules/statistical controls and experiment-count limits from strategy
definitions. The operator also requested no detailed data for unavailable strategies. The rendered view
now retains their identity/version, false eligibility, summary and original dated strategy-evidence ID
with blocking reasons; stock/source evidence, performance history, definitions and rules are omitted.
This applies to small packets too, including an unavailable active choice. Managed-account holdings,
stops and risk state, global review context and required learning assessments remain independent.
Exact-path removals preserve eligible-choice trading/data-quality settings and unknown future fields.
The full frozen source, eligibility checks, answer validation, spending guards and response contract
remain unchanged. The representation decision is in ADR-0087's October 9 approved-omissions section.

All seven saved non-pending requests rendered with the existing expansion-equality check. The latest
all-unavailable request falls from v2's 95,755 to **11,558 characters**. The largest v3 example is the
October 1 four-eligible-choice packet at **30,781 characters**. One explicitly authorized provider
count on that exact request plus current instructions/schema returned **11,859 input tokens**, below
32,000. No generation, spending reservation, SQL/journal write or desktop/settings change occurred.
No v3 model interpretation, eligible selection or continuous capacity/readiness is qualified.

Validation: **139 focused tests passed** across prompt, reviewer, scheduled reviewer, packet builder,
learning and coordinator. Coordinator mocks now read the reduced view; assertions verify full source
retention through pending/final saves and the exact rendered request.
Core/Sandbox builds passed. Existing nullable-context warnings remain; the final Sandbox build reported
three CS8632 warnings and zero errors. No new dependency-security audit or full-solution/SSDT validation
ran. Private evidence: `artifacts/ai-input-scope-20261009/{tests.log,sandbox-build.log,
saved-packet-sizes.json,provider-count.json}`. The prior v2 breakdown below remains historical and is not
a size description of v3. A fresh generation test needs current data and separate explicit authorization.

**Lossless review-view extension, October 9:** following operator agreement, v2 builds named-column
tables before sharing repeated values, stores identical row fields once as table defaults, and uses
short references with an explicit escape for literal reference-like text. Different account permissions,
exact numeric values, missing/null/empty distinctions and all previously included view data remain.
Before transmission, the renderer expands the view and refuses it if the result differs from the
pre-compression view. The existing v1 evidence-selection rules and full saved packets are unchanged;
this adds no omissions. Instructions require original, expanded decision IDs in the answer.

All seven saved non-pending packets rendered successfully with the expansion check. The last desktop
request shrank from 130,972 to 95,755 characters (26.9%). The operator authorized one count-only API check:
the exact request, current instructions and response schema counted **36,235 tokens**, down from 46,780
(22.5%) but still 4,235 above the unchanged 32,000 cap. Character reduction and reversible decoding do
not establish model understanding, future session capacity or continuous readiness. No generation,
spending reservation, SQL/journal write, desktop replacement or activation occurred in this increment.

Validation: 64 focused prompt/reviewer/scheduled-reviewer tests passed; Core and Sandbox builds passed.
Existing nullable-context compiler warnings remain; the initial Sandbox build reported three CS8632
warnings and the final incremental build reported zero warnings/errors. No dependency-security audit or full-solution/SSDT validation was performed.
Private evidence: `artifacts/ai-compression-v2-20261009/{tests.log,core-build.log,sandbox-build.log,
saved-packet-sizes.json,provider-count.log}`. Next inspect remaining repetition and reduce it losslessly;
a further provider check needs authorization after the next concrete request is ready. Fresh model
interpretation and repeated successful desktop reviews remain pending.

**Offline request-size breakdown, October 9:** the user requested category sizes before considering
possible omissions. The same 95,755-character view plus 3,949 characters of system instructions and
response schema totals 99,704 text characters. Physical compressed characters are counted once;
shared definitions are apportioned by their uses. Approximate largest categories are identifying
labels/source metadata/formatting/instructions (34.2%), portfolio choices/settings/rules (19.7%) and
paper performance history (13.1%). Actual instructions/answer format account for about 6,485 characters
within the first category; it is not all instruction prose. Strategy protection rules remain in the
portfolio-rules category, separate from managed-account risk state. There are 61 Live stock records
and 48 additional Shadow stock records; the earlier 61-record count described only Live evidence.
These are character shares, not per-category token counts or guaranteed savings if a category is
removed. The prior provider total remains 36,235 tokens. No API, SQL, journal or settings changes ran;
no new omissions were accepted. Aggregate evidence and the accounting script are in the private
`artifacts/ai-compression-v2-20261009/{category-sizes.json,measure-categories.py}` files. Use this breakdown
to discuss the next reduction; preserve the agreed evidence boundary until a further decision is made.

**Authorized desktop qualification, October 9:** the operator approved a brief restart, up to two AI
reviews with an approximately CAD 2.34 maximum reservation total, and a restart with reviews off afterward.
The first scripted close did not finish within 30 seconds; no force stop was attempted. The operator
then closed the old app manually. The prepared Release copy started at 10:09:47 Toronto with a process-only
deadline of 10:18:46. Loaded `TraderVI.WPF.dll` and `Core.dll` paths match the prepared artifact directory.

The first attempt began at 10:09:50.093 and completed DataUnavailable at 10:10:01.445; the second began
at 10:12:01.507, 120.062 seconds after completion, and finished DataUnavailable at 10:12:04.937.
Both saved Rejected records match SQL exactly, and each SQL Pending/Rejected pair preserves the exact
packet. No new generation reservation, paid generation or response was recorded. All four AI journal
locks were released while the app remained open, confirming that the two-attempt test stopped its owner.

Neither packet was expired at rejection; both expired at 10:15. Compact input sizes were 127,385 and
130,972 characters. A separate count-only check of the last saved request returned 46,780 tokens, above
the existing 32,000 admission cap. This verifies the last request cannot fit; the original desktop path
saved only DataUnavailable, not its precise rejection detail. The first attempt's exact count was not
captured. The size guard remains intact; do not raise the cap or treat blocked attempts as successful
recurring AI answers. The earlier 19,964-token wait response does not qualify growing intraday input.

The test copy then closed normally and restarted at 10:13:20 with no test variable. At 10:14:59 the app
was responsive, saved settings matched the pre-test checksum and remained disabled, all AI locks remained
free, and no third attempt or paid call existed beyond the next two-minute due time. The prepared copy
is the running desktop; the regular build-output executable was not replaced. Total conservative October
spending remains CAD 1.3087 with no unsettled calls; this test added CAD 0. At 10:17:10 all four AI journal
checksums remained unchanged. Normal desktop startup was authorized; market/broker data quality was not
requalified. AI trading was not enabled. Monitoring gaps remain historical evidence.

This qualifies native startup, blocked-attempt timing, the bounded stop and disabled restart. Repeated
successful paid reviews, in-flight native shutdown and continuous readiness remain unqualified. Offline
validation remains 73 passing focused tests and the isolated WPF Release build with 260 existing compiler
warnings (CS8632/CS0162/CS0168), zero errors. No new code/build/test, dependency-security audit or full-solution/
SSDT check ran for this operational step. Evidence is under `artifacts/ai-desktop-qualification-20261009/`:
`bounded-launch.json`, `bounded-completion.json`, `native-verification.json`, `rejected-input-count.log`,
`Verify-Test.ps1` and the prior tests/build logs. Review SQL/local writes, count-only provider access and
normal desktop activity were within the authorized test; there was no additional review retry.

**Authorized connected review, October 9:** the operator authorized empty-snapshot continuation and
one review with the existing ten-minute test ages and spending guards. Snapshot v2 was published at
09:40:51 Toronto and expires at today's 16:00 close; previous history remains intact. At 09:42:13 the
selected Breakout 3 book was cash-only and current. Three Live context envelopes were fresh but blocked;
all Shadow contexts were unavailable. The existing packet contract permits a wait review with no eligible
choices; unavailable candidates remain explicit and no gate was relaxed.

The one-shot review completed at 09:42:55 before its 09:45 packet expiry. It returned `wait`, two-minute
next timing, no rule overrides and no lesson hypotheses. The exact request uses `portfolio-review-view-v1`,
16 tables, 77 shared definitions and 424 shared references, all resolvable. It contains 15 choices,
zero eligible choices and 84 evidence items; all eight response citations resolve. Saved request checksum
and exact rendering from the full packet match. SQL Pending/Reviewed rows retain the identical packet;
the complete SQL Reviewed record matches the local record byte-for-byte by SHA-256.

Provider-counted and charged input tokens both equal 19,964; output is 651 tokens. Conservative accounting
is CAD 0.5417 against a CAD 0.8724 reservation; recorded October total is CAD 1.3087, with no unsettled call.
The response correctly distinguishes unavailable engine evidence, current managed funds and candidate
paper confirmation. It also notices an old holding/timing narrative inside the retained interval reason;
source inspection confirms this is the October 1 explanation wrapped in current scheduling evidence.
It uses the current account instead. The subsequent source correction includes the original interval-change
time and explicitly identifies the retained reason as scheduling history rather than current account/market
evidence. It preserves cadence and saved history. A regression test verifies a later review retains the
original explanation time; 61 focused scheduler/journal/prompt tests pass. Sandbox builds with three existing
CS8632 warnings and no errors. No new dependency-security audit or full-solution/SSDT build. The correction
is included in the later desktop test copy; that test produced no new model answer. This single answer supports compact-view interpretation,
without proving every measurement or an eligible selection. No second call was made.

Automatic settings remain disabled; no AI trading, provider-data diagnostic, restart or continuous loop
was invoked. Verification scripts/results and bounded command logs are under `artifacts/ai-resume-20261009/`.
Exact request/input/response inspection copies are protected under `%LOCALAPPDATA%/TraderVI/AiMomentum/inspection`
with the `20261009-` prefix. The separate scheduling-label correction has its test/build logs in the same artifact directory.

**Market-open preparation, October 9:** SELECT-only checks at 09:33 and 09:37 Toronto find eight active
comparison Shadow sessions using today's Valid daily source, with 32 Pending candidate records and no
evaluation timestamp. The Live session is Monitoring with Pending coverage and no recorded host gap;
no collection cycle was saved at those sample times. The 09:38:57 follow-up finds the 09:35 cycle completed at 09:37:39, with 11 OperationalOnTime, six CollectionFailed and one NoCompletedBar receipts. Live coverage is now Blocked, still without a host gap. At 09:39 all choices remain proposal-blocked and the 32 Shadow candidates still lack evaluations. All 15 catalogue definitions exist without catalogue
blocks; 12 Shadow contexts are not fresh and three Live envelopes are fresh but proposal-blocked.
The selected book has zero held positions; preflight verifies its cash/valuation at 09:33:24.
This replaces October 2's stale-held-price finding for the current starting book, without qualifying
invested coverage or continuous readiness. The desktop process started at 09:11 and responds.
AI settings remain disabled with the original ten-minute test ages; the only empty lesson snapshot
expired October 1. Saved October AI cost remains CAD 0.767 with no outstanding call reservation.

The bounded provider diagnostic now accepts an explicit fixed five-minute window end so a follow-up
can request the same interval rather than silently moving to a later bar. It enforces reviewed-session
bounds, UTC alignment and the existing two-minute delay; a historical window cannot qualify current
inputs. Validation: 28 focused tests pass; Sandbox builds with three existing CS8632 warnings and no
errors under .NET SDK 10.0.401. No fresh dependency-security audit or full-solution/SSDT validation.
No provider request, paid AI call, journal/SQL write, restart or trading action ran in this preparation.
Evidence: `artifacts/ai-resume-20261009/` contains read-only SQL/preflight/signal results and diagnostic
test/build logs. Prior timestamp-validation failure remains unresolved; capture its offending response
before choosing a correction, but do not make that diagnosis a prerequisite if current review inputs qualify.

**Collector warning follow-up, October 2:** the operator screenshot shows repeated
`InvalidOperationException` collection warnings, interrupted monitoring and a 10:20 checkpoint.
Some later collection therefore progressed beyond the earlier SQL sample; the screen does not
establish complete current coverage or reveal the exception message/call site. It also reports lost
holding monitoring with quote protection remaining active; that text is not independent protection
qualification. Source traces the per-symbol warning to the provider adapter's request/normalization
path, where missing data, GraphQL errors and invalid bars can all produce the displayed exception type.

The authorized two-target diagnostic completed about 11:00:52 Toronto for the 10:50–10:55 bar window.
The reported-failure target rejected in `TmxClient.ValidateIntradayResponse` at the five-minute alignment
check (line 394 at commit `44d2079f`), without timeout. Failure transport counts are unavailable, with
at most three attempts under the existing client. XIU returned the exact bar with one request/attempt.
This reproduces a timestamp-validation failure, not a blanket provider outage. It does not establish
the offending timestamp, whether mapping caused it, or the separate early cycle-level failure.

The separately authorized single-target follow-up completed at 11:21:49 Toronto for the later
11:10–11:15 window: `NoCompletedBar`, no exact bar, one request/attempt and no exception. It therefore
did not expose an offending timestamp. This was a different window, not a replay of the first failure;
it cannot establish its cause or source recovery. Missing bars alone do not prove quiet trading or a halt.

The diagnostic prints safe local validator messages (fixed text/timestamps only) and supports
`TRADERVI_TMX_FAILURE_INCLUDE_BENCHMARK=false`. Each target retains a 25-second deadline and at most
three transport attempts. Sandbox previously built with existing CS8632/CS0162/CS0168 warnings;
no code changed or tests/builds reran for this operational follow-up. No fresh dependency-security audit
or full-solution/SSDT check. No SQL/journal writes, AI calls, app restart, trading or rule relaxation.
Evidence under `artifacts/ai-concise-review-20261002/`: `failure-probe-native.log`,
`failure-detail-native.log`, `failure-probe-build.log` and `failure-detail-build.log`.

**Next-session review preparation, October 2, 09:25 Toronto:** protected metadata inspection confirms
disabled settings, the original October 1 session, ten-minute input ages and no pending/preparing files.
The only lesson snapshot contains zero evaluated lessons and expired October 1 at 16:00 Toronto.
Existing reviews and spending remain preserved. Pre-open checks found no current selected book or fresh
choice contexts. The 09:43:39 market-hours preflight now finds the selected book, but both held-stock
marks remain October 1 15:55 bars (16:00 valuation). At 09:45 all 15 definitions are present with zero
catalogue blocks; all 12 Shadow contexts are unavailable, while three Live context envelopes are fresh
but proposal-blocked. No choice permits a proposal.

SELECT-only SQL at 09:47:16 finds eight active comparison Shadow sessions with today's Valid daily
source and 16 Pending candidate records with no evaluation timestamp. The Live session is
FrozenOfficialRun / Monitoring, with Blocked coverage and a recorded host gap. The 09:35 and 09:40
five-minute cycles are Cancelled / Failed. Their three saved receipts comprise one CollectionFailed,
one OperationalOnTime and one NoCompletedBar; neither held symbol has a newer saved five-minute bar.
This establishes an intraday collection/input blocker, not another missing nightly publication.
The underlying exception is not persisted in these receipts. Desktop accessibility confirmed the
running app, but diagnostic-tab navigation failed twice with `coordinate input geometry is unavailable`;
the exact collector error remains to be obtained. No restart or repair run was attempted.
Native SQL access succeeded after the sandboxed SQL attempt failed.

`ContinueEmptyLearning` now provides explicit, disabled-host session preparation. It appends a new
dated empty snapshot expiring at the reviewed session close only when all previous snapshots are empty,
the latest is expired and no review is Pending. It refuses pre-open/closed/unreviewed sessions and
repeated renewal; it never initializes replacement history or updates settings, spending, risk or
scheduling. This is prepared tooling for a separately authorized check, not an automatic learning
policy or outcome evaluation. Runtime startup still cannot renew lessons.

Validation: 143 focused tests pass and Sandbox builds under .NET SDK 10.0.401. Existing CS8632 compiler
warnings remain; no fresh dependency-security audit or full-solution/SSDT validation was performed.
No production journal/SQL writes, paid calls, market requests, app restart or trading ran. Evidence:
`artifacts/ai-concise-review-20261002/` holds the pre-open and market-hours preflight/signal logs,
`ReadCurrentState.sql`, `current-state.txt`, `preparation-tests.log` and `preparation-build.log`.

**Concise review input, October 1:** the operator requested a shorter input. The oversized packet contained
38 Live stock entries with 114 detailed checkpoints; Live evidence represented about 86% of description
characters. Repeated daily setups, raw measurements, derived facts and window judgments dominated it.
`AiPortfolioPrompt` now creates a declared `portfolio-review-view-v1` for large packets: current Live
signals/selected measurements, data confidence and recent state history; tabular repeated records;
shared identical definitions; full current holdings/risk and all candidate eligibility/rules. Technical
source IDs/hashes, closed position rows and detailed calculation history stay in the full saved packet.
Decision/account/evidence identities and exact candidate versions remain. Small packets keep their
original wire shape. Unknown Live checkpoint shapes stay complete rather than silently losing new fields.
The full frozen packet still drives proposal validation; the call journal saves the actual rendered request.
See the [representation contract](adr/0087-ai-momentum-portfolio-boundaries.md#concise-review-view--october-1-implementation).

The final count-only check of the same failed packet returned **29,516 tokens** (89.1% below 270,651),
within the unchanged 32,000 cap; text shrank from 899,762 to 82,496 characters. The same reviewed schema,
instructions and model settings were included. This is a saved-packet size check, not a new fresh AI
decision; future larger packets still face exact token admission. No generation, spending reservation,
SQL/journal mutation, collection, restart or execution ran in this increment. 116 focused tests pass
and Sandbox builds; results are recorded with `artifacts/ai-restored-review-20261001/compact-{tests,build}.log`;
existing CS8632 warnings remain, with no fresh dependency audit or full-solution validation. Size/count
evidence is `compact-size.log` and `compact-count-final.log` in the same private artifact directory.

**Restored-input review test, October 1, 11:05 Toronto:** the operator authorized one further bounded
review using the existing settings, journals and spending guards. Input preparation accepted the current
managed-book observation and assembled 15 choices (four eligible) with 73 evidence items. The exact
provider input count was 270,651 against the configured 32,000 maximum; admission rejected before a
spending reservation or generation. There is no new AI answer or next-review recommendation.
The schedule records `DataUnavailable`; SQL contains Pending and Rejected rows at 11:05:40/42 with
identical packet JSON. Local history records Rejected and no proposal. The call journal still contains
only the earlier successful generation, with CAD 0.767 total conservative accounting and no call for
this attempt. Existing two-minute timing and disabled automatic operation are preserved.

Local size analysis identifies 38 Live signal evidence entries totaling 685,014 description characters;
their checkpoint arrays alone occupy 633,460 characters when locally reserialized. Strategy definitions,
managed-book evidence and other sections are much smaller. These are character measurements, not
per-section provider token counts. The recovery exposed an oversized input representation, not another
missing-publication failure. A concise model-facing representation requires preserved decision facts,
source ages, missingness, rules and history references; no evidence was trimmed or limit raised in this test.
No automatic retry, app restart, market collection or trading ran. No source code changed or unit test
rerun was needed for this operational check. Private evidence:
`artifacts/ai-restored-review-20261001/{review.log,verification.json,sql-verification.txt,InspectAttempt.ps1}`.

**Pipeline recovery and portfolio receipt, October 1, 10:30–10:57 Toronto:** the explicitly authorized
run `a47fb6a5-e440-4e6b-a687-5c35ef29bcec` rebuilt the isolated apps from `334008af` and completed
at 10:37:50. Hermes collection/backup and Delphi succeeded. Athena completed with exit 2 / Attention:
its log reports two invalid prediction outcomes; those outcome reasons remain a separate follow-up.
The overall run is Attention, not an unqualified success. Delphi created a Valid/OfficialPaper October 1
publication at 10:37:38 using September 30 market data. Read-only SQL joins verify all 12 active Shadow
and all three Live portfolios reference that publication, effective 10:40. Existing app monitors adopted
it without an app restart or manual source edits; historical opening freeze records remain unchanged.

The new SELECT-only `ai-paper-first-review` / `Signals` diagnostic uses the coordinator's catalogue
and engine readers without a model call, journal change or engine tick. At 10:57 it read all 15 choices:
all definitions are present and catalogue block counts are zero. Ungated Continuation 3/5 have fresh,
proposal-permitted context. Ungated Breakout 3/5 have recent observations but remain proposal-blocked;
the 10:53 candidate records still include two `MissingEvidence` entries. Of 16 candidate-account entries,
five are Qualified, six BelowPreviousSessionClose and three FallingFromPreviousFiveMinuteClose.
Eight gated/per-strategy Shadow choices have no admitted candidate decisions and therefore no selectable
fresh context. The three Live choices have fresh context envelopes but remain proposal-blocked; exact
Live admission/confirmation causes are not newly qualified. Receiving today's publication is verified;
it does not imply every portfolio meets entry or AI-selection requirements. No eligibility rule changed.

Validation: Sandbox builds and native read-only checks pass; existing CS8632 warnings remain.
No new dependency-security audit or full-solution/SSDT validation. No further AI call or AI execution ran;
automatic AI reviews remain off. Private evidence: `artifacts/ai-engine-context-20261001/`
(`recovery-console.log`, `adoption-first.txt`, `adoption-final.txt`, `signals-after-adoption.log`,
`signals-final.log`) and the private nightly run status/log.

**Missing engine context diagnosed, October 1, 10:18 Toronto:** read-only SQL shows all 12 active
Shadow variants (Breakout/Continuation, three/five positions, per-strategy/Normal/PortfolioRulesOnly)
at `NoValidDelphiRun`, with no adopted run or candidate decisions. The shared Delphi Live session also
has `NoValidDelphiRun`, no frozen run and no daily source adoption. Its three portfolios have current
session snapshots and 120 intraday evaluations through 10:15, so the intraday monitor is running;
that evidence does not supply the missing daily publication. The latest saved CalibrationRun is
Valid/OfficialPaper for September 30 using September 29 data, matching the active strategy. There
is no October 1 run to adopt. This is not a ten-minute quote-age rejection or a request to relax gates.

The scheduled 00:30 run `bd5eaf31-bb79-42fe-acfb-c0a07793519f` failed preparation at 00:30:30 with
Hermes restore error NU1004 and zero operational stages. Commit `4231cae9` added UserSecrets 9.0.0 to
Core and updated Core's lock, but omitted the Hermes/Delphi/Athena downstream locks. All three locks
now include that same dependency and its five transitive packages; existing resolved packages and
hashes are unchanged. The runner's isolated `-BuildOnly` check passed at 10:27 Toronto: all three
locked restores and Release builds succeeded, executable/source verification passed and status is
`Prepared` with zero operational stages. Existing CS8632/CS0162/CS0168/CS8321 compiler warnings
remain; dependency auditing was disabled and no full-solution/SSDT validation is claimed. That diagnosis
and build-only check made no operational/database changes; the later authorized recovery is recorded above. Evidence:
the failed run's private nightly status/log and `artifacts/ai-engine-context-20261001/`.

**First-review preparation, October 1:** explicit Preflight/Prepare/Review modes now support read-only
inspection, new private history initialization and one scheduler admission through the app's coordinator.
Preparation checks the opening book before creating files, refuses existing/partial history, leaves
automatic reviews disabled and initializes zero evaluated lessons with expiry at that session's close.
Interrupted preparation is preserved and flagged, never reset. At 09:53 Toronto the native read-only
preflight found 15 choices and the selected book with its one open holding and one price mark; its
valuation boundary was 09:50. This does not establish risk acceptance or complete source coverage.
Validation: 80 focused setup/host/coordinator/scheduling tests pass. Private evidence:
`artifacts/ai-first-review-20261001/{tests,preflight,build}.log`. Existing CS8632 warnings remain;
no fresh dependency audit or full-solution validation. The operator subsequently accepted the
ten-minute bound for this review-only test (ADR-0087); it is not continuous-operation qualification.

**Connected review, October 1, 10:00–10:09 Toronto:** installed the disabled private configuration
and four journals beneath the protected operator-local root. The initial invested saved-Shadow book
passed existing validation and risk initialization. Two pre-generation attempts saved Pending/Rejected
records and stopped at token-count admission, with no spending reservations or generation. Nested
JSON's HTML quote escaping inflated the prompt. JSON text serialization now avoids that expansion
without dropping or changing any evidence; roundtrip regression coverage confirms exact content.
The third attempt completed: `wait`, requested interval two minutes, 30,194 counted/used input tokens,
441 output tokens, CAD 0.767 conservative settled accounting (initial reservation CAD 1.1179).
There is exactly one model call; the original 32,000/4,096 token caps and CAD 90 monthly ceiling remain.
This is local accounting under approved buffers, not a provider invoice or measured CAD card charge.
Read-only verification confirms identical Pending/Reviewed packet JSON and exact SQL/local final
record equality. At that review all 15 candidates lacked fresh matching engine context; selection eligibility remains
unchanged. Price/engine age checks, historical attempts and risk state are preserved. No app restart,
broker/market collection, assignment or order occurred. Automatic reviews remain disabled.
Validation: 76 focused client/reviewer/learning/coordinator tests pass; Sandbox builds. Existing CS8632
warnings remain, with no new dependency audit or full-solution validation. Private evidence:
`artifacts/ai-first-review-20261001/{prepare,review,review-diagnostic,review-compact,format-tests,format-build}.log`
and `verification.json`. The diagnosis and subsequent pipeline recovery are recorded above;
desktop lifecycle/scheduling remain unqualified.

**API access update, September 30, 23:27 Toronto:** Core now loads Oracle's existing user-secrets
configuration directly. The authorized single model-metadata GET returned HTTP 200 with the exact
requested GPT-6 Astra identity in 634 ms. No credential was printed/copied/changed; no generation,
portfolio submission, SQL, journal initialization or app restart occurred. Generation access, funding
and actual review costs remain unverified. Thirty focused client/access tests and the Sandbox build
pass. Existing CS8632 compiler warnings remain; dependency audit was disabled during restore, so no
fresh security assessment is claimed. Private evidence: `artifacts/ai-api-setup-20260930/access-{tests,build,native}.log`.
The operator accepted a five-minute initial interval and open / open+3h / close-15m checkpoints,
plus CAD 90/month, 1.60 CAD/USD and 20% tax/fee headroom through October 31 for the first review.

**Desktop review host, September 30 (source/tested, not activated):** `AiPaperReviewRuntime` reopens
the shared spending/learning journals and scoped risk/schedule history, uses Oracle's saved key,
and composes the existing saved-Shadow input coordinator. WPF starts it on a background task only
when private settings explicitly enable it; shutdown cancels and awaits completion before releasing
ownership. The 30-second host tick delegates paid-review admission to the existing adaptive scheduler.
Reviewed-session advancement preserves schedule history; unexpected failures stop the host with a
generic attention message. Missing history is never created/reset on startup. Proposals remain SQL
evidence; no assignment, order or protection integration is added. Input-age limits have no defaults.
Validation: 89 focused host/coordinator/scheduler tests pass, including cancellation, single ownership,
missing-history rejection and cost expiry. WPF builds; existing CS8632 (Core/tests) and CS0168 (WPF)
warnings remain. No fresh dependency audit, full-solution build or native desktop lifecycle test.
At this September 30 checkpoint no settings, production journal, learned snapshot or paid generation
existed. October 1 setup and connected qualification are recorded above; the installed app has not
been restarted. Source still uses
saved Shadow book/bar readers; faster-price collection and invested coverage are not newly qualified.
Evidence: `artifacts/ai-api-setup-20260930/host-tests.log`, `host-wpf-build.log`.

Desktop controls and saved account refreshes were observed September 25; operational evidence retains
its original dates below.

**Coordinator scope clarified, September 30:** adaptive review timing and AI choice of the active
portfolio are reconfirmed. Continue Step 7 with current input checks; the preference to review dated
or incomplete information with explicit gaps is recorded, but changing rejection/eligibility rules
is deferred unless it blocks integration. Existing switching/history boundaries remain. This is a
documentation decision. The subsequent coordinator increment below implements the connection without changing input policy or enabling operations.

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
All three are pending; the reviewer is the next visible target. The October 1 connected reviewer and
SQL readback now pass, with engine context and desktop scheduling/display still pending. Step 3 has
cash-only persistence, corrected receipt timing and one accepted invested saved-Shadow book under
the first-test bound; continuous held-stock coverage and faster-price qualification remain open.
Duplicate AI/learning task lists are consolidated into that sequence; completed price investigations
link to the dated evidence below. Milestone labels do not establish operational readiness.

**Source inspection, September 29 (no broker calls):**
`Core/WSTrade/Authentication/WealthsimpleAuthClient.ManualBuys.cs` already selects `quoteV2`
bid/ask and `quotedAsOf`, checks security/currency identity and keeps receipt time separate. Its shared
manual-order reader also checks buy/sell eligibility; it is not a general portfolio-price adapter.
Current TMX-backed recommendation quotes carry receipt time only. September 30's authorized native
comparison is recorded below. No valuation-policy change or source adoption follows from inspection
or diagnostic completion; timestamp meaning and operating coverage remain open.

**Documentation review, September 30:** official-domain searches for `quotedAsOf`, `quoteV2`,
quote timestamps and quoted-as-of wording found no relevant public field definition. The reviewed
[buying help](https://help.wealthsimple.com/hc/en-ca/articles/360056581314-Buy-a-stock-or-ETF)
describes real-time on-demand quotes; the current
[execution disclosure, Use of Market Data](https://www.wealthsimple.com/en-ca/legal/best-execution-disclosure)
describes web snapshots of real-time quotes. Neither establishes whether this API timestamp represents
an exchange update, provider observation or response creation, or whether it advances if upstream data
is stale. This is a bounded unsuccessful definition lookup, not proof no private specification exists.
The operator accepts the uncertainty remaining explicit and clarifies that exploration is only for a limited, deferred AI fallback. The earlier framing as a general valuation switch was too broad; bid/midpoint is not an immediate decision. ADR-0087 and the parser comment now reflect this. No new broker calls, support messages or executable behavior changes were made;
existing input qualification requirements remain in place.

## Latest operational evidence

| Area | Last evidence (Toronto) | Verified result and limit |
|---|---|---|
| AI review SQL storage | Sep 30, backup verified 22:36:49; migration verified 22:39:23; native tests afterward | Authorized migration 042 installed after a checksum-verified 1,539.34 MB COPY_ONLY backup and hash-matching OneDrive-folder copy. Cloud synchronization and a restore drill were not verified. Nine columns, primary key, three trusted checks and the enabled immutable trigger verified. Eight account/history tables retain matching counts/content hashes before installation and after testing. Four opt-in native tests pass: exact packet/answer roundtrip, duplicate idempotency, scope/input/outcome conflicts, immutable update/delete rejection and recovered-answer persistence. Synthetic review rows were confined to rolled-back transactions; the table is empty afterward. This is not cross-process concurrency or crash qualification. Source check: 32 focused tests pass, four native tests skip without explicit opt-in. Existing CS8632 and cached NU1902–NU1904 warnings remain. No app restart, paid AI call, market/broker request, account/risk/order mutation or activation. Private evidence: `artifacts/ai-review-install-20260930/`. |
| Held-stock broker quote samples | Sep 30, 09:34:43–09:35:51 | Two polls separated by 60 seconds cover all 11 current paper-held symbols: 11 security lookups, 22 successful quote reads, one successful authentication verification (34 logical broker reads; HTTP attempt count not instrumented). All 22 have positive bid/ask with ask at least bid, OPEN status and declared quote times within the regular session. `quotedAsOf` is 24.6–46.8 ms before app receipt; this is a difference between reported clocks, not verified market-data latency. All 11 times advance; seven bid/ask pairs change and four remain equal. Final held-symbol set and saved login are unchanged. Timestamp behavior is consistent with on-demand snapshots, but does not establish exchange update time or rule out stale upstream prices. No source is adopted, no valuation/age limits change, and invested acceptance/persistence remains unverified. SQL reads and broker quote/authentication reads only: no SQL/account/risk/order writes, credential refresh/persistence, paid AI calls or app restart. Source validation: 94 focused tests and Sandbox build pass (existing CS8632 warnings). Evidence: `artifacts/ai-risk-values-20260930/broker-quote-market-hours.log`. |
| Broker quote diagnostic preflight | Sep 30, 09:13:40 | Saved Windows-protected login verifies as Authenticated without refresh/persistence. The reviewed calendar blocks pre-open quote collection (`OutsideRegularSession`): zero quote reads, no SQL reads or writes, account reads, orders or app restart. Prepared Sandbox diagnostic caps held symbols at 16 and takes two samples a minute apart. It reuses the existing sell-quote/eligibility reader, newly exposed for explicit diagnostics; ordinary trading behavior is unchanged. All 94 focused quote/buy/sell tests pass; Sandbox builds with three existing CS8632 warnings. No full-solution build or fresh dependency-security audit. Market-hours age/coverage and timestamp meaning remain unverified. Evidence: `artifacts/ai-risk-values-20260930/broker-quote-native.log`, `broker-quote-tests.log`, `broker-quote-build.log`. |
| Held-stock quote timing alternative | Sep 29, 13:00:11–13:01:15 | Two polls of ten held symbols plus XIU: 22 requests/transport attempts, no failures, final held-symbol set unchanged. All 22 current quotes match the latest reported trade, lack a source timestamp and return no positive bid/ask pair. Held last trades are 4.5–822.9 seconds old; 6/10 then 7/10 are within the diagnostic 120 seconds. The separate delayed endpoint supplies bid/ask in all 22 samples, but only last-trade timestamps, aged 902.3–1,336.6 seconds; these do not date bid/ask. These queried fields cannot establish fresh complete portfolio valuation. Same-minute/expiry rules are unchanged; invested acceptance/persistence remains unverified. New bounded diagnostic: 34 focused tests pass; Sandbox builds with existing CS8632 warnings. No new dependency-security audit or full-solution build. SQL reads and TMX requests only; no SQL/account/risk/order writes, paid AI/broker calls, app restart or activation. Evidence: `artifacts/ai-risk-values-20260929/quote-timing-native.log`, `quote-timing-tests.log`, `quote-timing-build.log`. |
| AI receipt-clock fix and price-block diagnosis | Sep 29, detailed run 10:42:50; corrected run 10:47:15–10:48:27 | Detailed diagnostics reproduce five Shadow receipts 0.0418–0.2467 ms before the caller's start; account dates/update/fill ordering agree. The repository now stamps app UTC after complete SQL reads while serializable locks remain held; no tolerance or price-age change. Corrected run: all 15 books reconcile, 48 Shadow receipts inside caller bounds, all 20 cash-only observations accepted/saved, 30 journal reopens unchanged, zero timestamp rejections. Four Shadow and one Live accounts were invested at each run's baseline (ordinary running engines changed holdings since 09:52). No invested value was accepted: nine latest-run reads report incomplete/expired prices; one Live read reaches ledger-time validation after all prices end at 10:48. A saved fill at 10:48:04.569 falls after that price boundary and before the account read, consistent with the rejection. Source now labels this case `TradeAfterPrice`, verified offline; that clearer label was added after the native run. Detailed price output shows absent completed bars, stale bars and differing latest minutes; no price failure was bypassed. Runs used 35 then 33 TMX requests/returned attempts. Latest per-account checks: 22–2,501 ms, excluding baseline/inter-pass delay. Bounds remain diagnostic (account 45 s, price 120 s, receipt 30 s); invested persistence, continuous coverage and operating freshness remain unqualified. SQL reads/private diagnostic journals only; no SQL/account/order mutation, paid AI/broker call, production journal change, app restart or deployment. Evidence: `artifacts/ai-risk-values-20260929/connected-detailed.log`, `connected-clock-fixed.log`, `live-fill-timing.log`. |
| Connected AI risk path during market hours | Sep 29, started 09:52:25; two passes over about 70 seconds | All 12 Shadow and three Live books reconcile; two Shadow and one Live accounts are invested. Pass 1 accepts all 12 cash-only accounts; pass 2 accepts 11 and rejects one Shadow read as `InvalidAccountReceipt`. All six invested reads reject: five `PricesIncompleteOrExpired`, one `PricesHaveDifferentTimes`. Fourteen TMX requests/returned transport attempts; 23 accepted cash-only observations and matching saved risk transitions; 30 journal reopens match saved state. Expired accepted observations reject; diagnostic buying allowance remains zero. Per-account read/check/reopen took 7–2,743 ms, excluding baseline SQL reads and the inter-pass delay. Diagnostic bounds: account 45 s, price 120 s, receipt 30 s. Fresh held-price acceptance/persistence, continuous coverage, concurrency and operating ages remain unqualified. That rejected receipt was not retained; the later detailed run above reproduced and fixed a receipt-clock discrepancy. SQL reads and isolated diagnostic journals only; no SQL/account/order writes, production journal changes, paid AI/broker calls or restart. Evidence: `artifacts/ai-risk-values-20260929/connected-market-hours.log`; private journals: `connected-native-journals/`. Pre-open baseline stopped on a missing current Shadow session; at 09:31 all three Live and eight comparison Shadow accounts had initialized. The four standard Shadow accounts depend on the desktop's normal polling window starting 09:47. Prerequisite evidence: `connected-preopen-authorized.log`, `preopen-session-counts.log`, `open-session-counts.log` in the same directory. |
| Connected AI risk path after hours | Sep 28, saved books about 19:19; connected check 19:21:36 Toronto | All 12 Shadow and three Live cash/holding histories reconcile; two Shadow and one Live accounts are invested. Saved-price values are expired/outside-session. The new connected diagnostic rejects all 15 as `OutsideSession`, makes zero TMX requests, accepts zero observations and saves zero risk transitions. Fifteen newly isolated diagnostic journals reopen unchanged; per-account rejection/reopen took 13–73 ms, excluding baseline SQL reads. This checks the closed-market branch and empty-journal recovery only. Fresh combined values, native accepted-value persistence, concurrency and regular-session latency remain unverified. No SQL/account/order writes, production-journal changes or restart. Evidence: `artifacts/ai-risk-values-20260928/evening-saved-books.log`, `connected-native-evening.log`; private empty diagnostic journals: `connected-native-journals/`. |
| Missing-minute source/trade diagnosis | Sep 28, 14:12 and 14:21 Toronto | Authorized raw-response probes for five held symbols plus XIU. Final sample covers 09:30–14:20: 542/1,740 requested minute slots absent at source, zero null/incomplete rows; the client filter caused none of these gaps. All 207 sparse five-minute groups match summed minute volume; all 130 complete groups match OHLCV. Of 150 recent trades, 105 fall within the completed window across 24 minutes; all have bars. Nineteen comparable trade-minute volumes match; 32 absent minutes lie between reported trade times. Latest gaps for five targets remain absent on narrow rerequest. This supports no reported trading in sampled gaps, not exhaustive venue coverage or production qualification. Final run: 23 requests/attempts, held-symbol set unchanged, no SQL/account/risk/order writes or restart. Evidence: `artifacts/ai-risk-values-20260928/missing-minute-trades-native.log`; first raw-only run: `missing-minute-native.log`. |
| Held-stock one-minute source | Sep 28, 13:25–13:29 Toronto | Authorized read-only probe sampled five held symbols plus XIU across five polls, then compared intervals and retried gaps: 44 requests/transport attempts. Latest-minute held coverage was 3/5, 4/5, 3/5, 4/5, 4/5; held price ages were 10.067–370.298 seconds after bar end. Zero revisions/disappearances across 188 repeated completed-bar comparisons. Four held symbols retained historical gaps; their first missing minutes remained absent on both broad and narrow rerequests. All seven sparse five-minute groups had matching summed minute volumes; all five complete groups matched OHLCV exactly. This suggests, but does not establish, no-trade minutes. Final held-symbol set unchanged; account consistency and production freshness remain unqualified. No SQL/account/risk/order writes or app restart. Evidence: `artifacts/ai-risk-values-20260928/held-minute-native.log`. |
| Candidate one-minute price source | Sep 28, 13:13–13:17 Toronto | Authorized read-only XIU probe completed five requests/transport attempts, one per minute. Latest completed prices were 10.069–10.582 seconds old at receipt; all four subsequent polls advanced exactly one minute. Zero revisions/disappearances across 38 repeated completed-bar comparisons. Two requested historical minutes were absent throughout (8/10 completed minutes initially, 12/14 finally); cause is not established. Forming bars were excluded. This small benchmark sample does not qualify held stocks, continuous age bounds, publication latency before the poll, or production freshness limits. No SQL/account/risk/order writes or app restart. Evidence: `artifacts/ai-risk-values-20260928/one-minute-native.log`. |
| AI stock-price freshness | Sep 28, SQL 12:40 and 12:56; account reads 12:55 | At 12:40, successful V3 five-minute receipts in the preceding hour arrived 144.722–158.396 seconds after bar end. The 12:55 risk-reader check reconciles all 15 books; two invested Shadow values are 338.3–338.4 seconds old and one Live value is 339.5 seconds old. None is within 60 seconds; this is a diagnostic comparison, not a newly approved production limit. Current sources do not establish one-minute coverage. The 12:56 window includes 378 usable mature Live slots, 23 without completed bars and nine failed slots; one cycle was cancelled with completion code `Failed`, leaving eight unfinished slots. Its underlying exception is not established by these receipts. The session retains `Blocked`/`HostGapObserved`; twelve V2 fifteen-minute receipts also report `CompletedBarConflict`. No external collection, SQL writes or restart. Evidence: `artifacts/ai-risk-values-20260928/price-freshness-*`. |
| AI paper risk-value readers | Sep 28, native reads 11:47 | All 12 Shadow and three Live books map and reconcile cash/holdings/prices; none is missing. Ten Shadow cash-only books carry current transaction verification receipts; two invested Shadow books and one invested Live book retain price/checkpoint evidence. All 15 meet the diagnostic ten-minute age/session check. Altered cash, wrong account, missing/incomplete marks and expired-value allowance are rejected in memory. This is a bounded reader check, not production feed or recurring host qualification. No SQL/journal writes, AI calls, broker calls or app restart were performed. Evidence: `artifacts/ai-risk-values-20260928/cash-verification-native.log`. |
| Live symbol-alias repair / comparison adoption | Sep 28, installation 11:17; SQL through 11:22; native readers 11:23 | Authorized migration 041 installed after a checksum-verified 1,121.07 MB COPY_ONLY backup and SHA-256-matching local copy in the OneDrive folder; cloud synchronization and a restore drill were not verified. Before/after counts and content hashes match across eight checked account/history tables. Restarted PID 32308 is responsive and loaded Core/WPF hashes match source build `8106499a`. Today's session froze successfully, canonical tracked membership exists while the original position symbol remains unchanged, all eight comparison Shadow sessions are Active with adopted sources, and all three Live ledgers have today's session. The common source became effective at 11:20; a fresh Live cycle completed at 11:22:26 and saved evaluations/ledger updates. Coverage remains Blocked with HostGapObserved; missed intervals were not backfilled as valid observations. The final native AI reader test passes for 12 Shadow and three Live contexts; none grants a selectable proposal. Private receipts: `artifacts/symbol-alias-install-20260928/`; earlier diagnosis: `artifacts/ai-source-investigation-20260928/`. |
| Nightly pipeline | Oct 10–13, completed 00:37:51 / 00:37:18 / 00:34:15 / 00:34:18; SQL checked Oct 13 morning | All four current-source chains and Hermes/Delphi/Athena stages Succeeded. Hermes inserted 483 Friday bars on Oct 10, then zero because no later TSX session had completed; all runs report zero Hermes failures. SQL confirms four Valid publications with zero observation mismatches. Athena saved 1,124 valid outcomes Oct 10, zero new outcomes later, and no new invalid outcomes in any runner window. Latest Scheduler result 0, Ready for Oct 14. No rerun needed. Older missed-wake and historical invalid evidence remain separate. |
| Delphi inputs | Oct 13, 11:03–11:09 audit | All four recent publications use Oct 9 data, matching the reviewed local TSX calendar after the weekend/Oct 12 closure. Each has 211 candidates / 422 lens rows and 50 published rows; latest has 29 Buy rows (18 Breakout, 11 Continuation) and 21 Hold. All 483 active symbols have the Oct 9 daily endpoint, including ILLM/RCTR; XIU/SPY and checked derived series also reach Oct 9. Six historical feature exclusions remain; endpoints do not prove full histories or leadership-mover availability. Live and all twelve Shadow accounts adopted today's publication. |
| Broker connection/history | Sep 25, screenshot 16:40:55; SQL 17:02:22 | Operator confirms the integrated Strategy management panel in the normal checkout. The screen reports successful account/history and trade synchronization. Eight recent SQL imports from 16:18–16:57 each have a trade-sync receipt, spaced about 5m30s apart. This bounded sample supports recurring persistence, not uninterrupted coverage, broker rate limits or cash-event qualification. |
| Strategy controls and setup | Sep 25, SQL 17:02:22 | One account binding; zero baselines, continuations or managed-session revisions. All three risk/session immutable triggers remain enabled. The screenshot's no-approved-strategy state and disabled stop/resume buttons agree with storage. The account setup dialog and recovery of a populated real assignment remain unverified. |
| External-trade synchronization | Sep 24, migration 10:41; corrected desktop 10:59; repeat SQL 11:01; UI 11:02 | Authorized migration 040 installed after a fresh checksum-verified COPY_ONLY backup and SHA-256-matching local mirror. Financial records were preserved by migration. Corrected Core/WPF loaded with matching hashes; fresh broker reconciliation saved the reported exit exactly once and closed its tracked position. Later automatic refresh retained identical trade counts; the UI reports successful synchronization and current recorded holdings exclude the exited symbol. Original trades, binding and account reports retain exact hashes. One Live lease resumed. Evidence: `artifacts/broker-sync-20260924/`; this does not establish continuous coverage, cloud backup durability or complete cash-event/risk inputs. |
| Storage and desktop | Sep 17, installed and observed | Compact snapshot migrations 037/038 and UI-worker fix installed. All 4,472 historical/current snapshots verified; subsequent compact writes observed. Full-history reconstruction still consumes background CPU/memory. |
| WPF memory investigation | Sep 24, 09:08:49–09:10:45 pre-install sample | Previous Release PID 24492 responded in all 24 samples: 2.58–3.75 GB working set and 3.25–4.90 GB private committed memory (decimal GB). Full-history JSON expansion and separate display snapshots were identified as likely contributors; no runtime allocation/heap attribution was captured. Superseded by the authorized installation below. Private evidence: `artifacts/wpf-memory-20260924/`. |
| WPF memory-fix installation | Sep 24, restarted 09:30:32; SQL checked 09:32:09; process sampled through 09:33:14 | Normal close of PID 24492, five backed-up/replaced binaries with matching SHA-256, and one new Release PID 31052 loading the installed Core/WPF modules. All 24 post-restart samples responded: 0.66–1.33 GB working set and 0.57–1.32 GB private committed memory. This short fresh-process sample is not a matched workload or sustained memory bound. One renewed Live lease, nine new compact revisions/events and three updated portfolios confirm runtime resumption. Session is Monitoring with Blocked coverage and a host gap; uninterrupted collection is not established. Evidence: `artifacts/wpf-memory-20260924/installation.md`. |
| Database allocation | Sep 23, 23:55 verified | User-authorized one-time file shrink reclaimed 54.88 GiB. Data file is 16 GiB with about 6.40 GiB used; log is 1.008 GiB. Full integrity checks before/after found zero errors; all 89 application tables retained their exact row counts (1,210,867 total). Database remains ONLINE, SIMPLE, CHECKSUM, with automatic shrink off. |
| Build storage cleanup | Sep 23, 21:53 verified | User-authorized one-time removal of 11,514 old compiled files reclaimed 29.66 GiB. Artifacts now total about 7.03 GiB. All 23,438 retained inventory files have unchanged sizes/timestamps; 48 model/archive and source-manifest hashes match. Latest two nightly builds, package cache, recent work and worktree retained. |
| Backups | Oct 12/13 nightly receipts; hashes checked Oct 13 morning | Independent SHA-256 reads match all four retained local files to their runner receipts. Both logs record successful SQL checksum verification; each location retains exactly Oct 12 and Oct 13. Sizes are 2,378,721,792 / 2,379,421,696 bytes. Older runs' receipts remain, but retained-file verification covers only these two. Cloud synchronization, independent download and test restore remain unverified. |
| Delphi Live | Oct 13, SQL 11:05 and 11:09 | Existing Release WPF PID 30836 (started Oct 9) is responsive in five 11:06 samples, with one renewed Live lease. All three ledgers use today's session. By 11:09, twelve cycles completed and seven were Cancelled / Failed; 333/726 settled slots are usable, 147 CollectionFailed, 65 NoCompletedBar and 181 UnfinishedCollection. Nine continuity epochs show eight monitor recoveries within the still-running app. After the failed 11:00 cycle, 11:05 completed at 11:08 with 21/39 usable receipts and further ledger events. Progress does not qualify uninterrupted operation. |
| System Shadow | Oct 13, checked 11:05–11:09 | All twelve active sessions adopted today's source through revisions. Twelve open positions have same-day prices, spanning 09:45–10:50 in the final snapshot; freshness/protection is not qualified. One Buy fill exists with no checked timing violation and no pending orders. Many five-/fifteen-minute fetch failures, MissingEvidence candidates and one fifteen-minute CompletedBarConflict persist. The repeat snapshot has 361 five-minute and five fifteen-minute bars, with zero failures of the checked price/volume predicates. |
| Live correction / current host | Oct 14: observed-end follow-up installed 11:09; verified 11:14–11:15 | First cycle: 42 settled, 36 usable, five stale and one missing exact bar, zero CollectionFailed. All requests use observed source context with zero checked logical/price/deadline violations; all three Live books have complete current 11:10 marks, one renewed lease and responsive host. Five original missing targets supply later bars; four remain unavailable. Historical gaps/conflicts and continuous coverage limits remain. Private evidence: `artifacts/live-missing-bars-20261014/README.md`. |
| Broker risk/session storage | Sep 22, 23:34:11 | Authorized migrations 035/039/040 installed after a fresh verified backup/copy. Three empty tables, three enabled immutable triggers, eight trusted foreign keys, three trusted checks and six native zero-row update/delete rejections verified. Linked binding/import and report/promotion counts and hashes unchanged. No baseline/session created. |

Private evidence directories: `artifacts/nightly-readiness-20261013/` (latest four-night and current runtime check), `artifacts/nightly-readiness-20261009/`, `artifacts/nightly-readiness-20261007/`, `artifacts/nightly-readiness-20261006/`, `artifacts/nightly-readiness-20261002/`, `artifacts/nightly-readiness-20260930/`, `artifacts/nightly-readiness-20260924/`, `artifacts/nightly-readiness-20260922/`, `artifacts/app-health-20260922/`,
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
The reader returns a preparation record; the subsequent journal integration below now owns its durable
risk assessment. Account balances, stops and engine marks are untouched. Native timing/concurrency
and operating freshness remain unqualified. No production age limits were selected and no schedule
or application wiring was enabled.
Validation: 163 focused tests pass, including 34 new combined-reader cases and regressions for the
price adapter, saved readers and both paper risk monitors. Cases cover arithmetic, account ownership,
missing history/prices, stale/mixed times, changed accounts and intervening trades, slow reads,
cash-only closed history, saved holds, unsupported corporate actions, cancellation and storage failure.
Core/test compilation succeeds; existing CS8632 compiler warnings remain. No fresh dependency-security
audit (`NuGetAudit=false`), full Core suite or WPF/full-solution build ran. No native TMX/SQL operation,
database mutation, AI/broker call, order, deployment or app restart ran in this increment.
Evidence: `artifacts/ai-risk-values-20260928/fast-account-tests.log`.

**Combined-value risk journal, September 28 (source/tested only):** `PaperAiFastRiskMonitor` revalidates
the retained whole-account/price evidence and configured age bounds, then uses the existing shared
persist-before-assess writer. `PaperAiPortfolioRiskReader` composes collection through that monitor;
accepted values also feed `AiPortfolioPacketBuilder`. Continuation preserves original capital,
generation, exact strategy and recorded position/fill history. An explicit same-account, same-family
handover can adopt the faster source from saved five-minute evidence without replacing the round,
peak or attempt history; ordinary observation never silently changes source contracts.
Repeated prices keep the original journal identity and expiry. All previously accepted minute bars
are checked for changed OHLCV; a conflict is saved in the same journal and blocks further valuations,
new rounds and usable AI packets across restart. Existing required closure remains latched and its
reconciliation can still be recorded. No automatic conflict-clear path exists; reviewed operational
recovery remains Step 7 work. Journals without a conflict retain the prior format; older binaries
reject the new conflict field rather than silently ignoring its block.
Validation: 284 focused tests pass, including 24 new journal/packet cases and existing risk-policy,
broker/paper monitor, saved-reader, price-source, journal and packet regressions. Tests exercise both
paper families, expiry without renewal, capital/history/strategy replacement rejection, altered evidence,
explicit handover, sale-to-cash continuity, conflict recovery and preserved exits. Core/test compilation
succeeds with existing CS8632 compiler warnings; no new warnings remain. No fresh dependency-security
audit (`NuGetAudit=false`), full Core suite or WPF/full-solution build ran. No SQL/TMX operation, database
change, paid AI/broker call, deployment, app restart or order ran. Test journals use synthetic temporary
accounts only. Native timing/concurrency, full-session coverage, storage size/retention and production
freshness remain unqualified. Evidence: `artifacts/ai-risk-values-20260928/fast-risk-journal-tests.log`.

**Prepared connected native diagnostic, September 28:** `AiFastRiskNativeTests` is explicitly opt-in.
It validates real account cash/history, caps the workload at 20 accounts/eight holdings each and 24
held-symbol occurrences, and creates unique diagnostic journal directories. During a regular session
it makes two passes a minute apart with at most 48 TMX requests/144 transport attempts; after hours it
verifies rejection without collecting prices. The 45-second account, 120-second price and 30-second
receipt bounds are diagnostic, not approved operating settings. Accepted values would be checked
against saved risk state and reopened journals; expiry rejection uses an explicitly advanced test clock.
Output contains counts, reasons and timings, not financial values or account identities. Diagnostic
journals may contain private source evidence and must remain outside commits.
Validation: 100 focused tests passed with the native test skipped by default; both the saved-book SQL
check and the opt-in connected after-hours check then passed. The first launch mistakenly replaced the
inherited calendar variable with an absent user-scoped value; rerunning with the inherited reviewed
calendar resolved it. Sandbox SQL access was denied; the authorized checks succeeded outside it.
Core/test compilation has existing CS8632 warnings. No fresh dependency-security audit (`NuGetAudit=false`),
full Core suite or WPF/full-solution build ran. As of that September 28 check, the regular-session branch had not run against TMX;
no external market, broker or AI call occurred during that diagnostic. Evidence: `connected-native-build-tests.log`,
`connected-native-evening.log` and `evening-saved-books.log` in `artifacts/ai-risk-values-20260928/`.

**Account receipt-clock correction, September 29 (source/tested and bounded native verification):**
`SystemShadowRepository` captures app UTC after the complete read and before releasing SQL serializable
locks, avoiding cross-clock sub-millisecond rejection while retaining strict caller bounds, saved update/fill
times and expiry. The native diagnostic records anonymous account/symbol slots, exact caller read margins,
bar coverage/times and separate cash/invested outcomes. No financial values or identities enter its output.
Live post-price fills now report `TradeAfterPrice` rather than a generic evidence failure, preserving rejection.
Validation: 116 focused price, combined-value/journal and saved-book reader tests pass; the native check is
skipped unless opted in. One-tick receipt violations remain rejected. The clock correction passed the
separate native run above; its sample still accepted no invested value. Existing CS8632 compiler warnings
remain; no fresh security audit (`NuGetAudit=false`), WPF/full-solution build or full Core suite ran.
Evidence: `artifacts/ai-risk-values-20260929/receipt-clock-final-tests.log`; operational counts/limits above.

**Review coordinator, September 30 (source/tested only):** `AiPaperPortfolioReviewInputs` reads the catalogue,
both saved engine contexts and the account's existing risk reader/journal through the existing packet builder.
`AiPortfolioReviewCoordinator.FromSavedPaperInputs` composes those SQL readers, the durable scheduler,
learning-aware reviewer, budgeted Responses client and `AiPortfolioReviewRepository`. The host must still
supply reviewed configuration, current active-choice/hold state, initialized journals and ticks; construction
does not initialize history or start a timer. Scope mismatches and active-choice/hold changes during collection
reject the review. Undefined/unavailable choices remain visible under current packet rules.
Migration 042 adds append-only Pending/Reviewed/Rejected evidence with full packets and validated answers,
plus queryable action/selected-portfolio columns. The exact learning-enriched input saves before a paid call;
local journals retain recovery evidence and raw call/cost data. A failed final SQL save can be retried from
the local review, including after restart, without another model call or restoring review permission.
Reviewed describes a recorded assessment, not current execution permission; events during saving still
suppress returning an older decision. Input preparation failures remain in the scheduling journal.
Validation: 157 focused coordinator, scheduler/recovery, packet, learning and risk-reader tests pass with
synthetic sources/model/storage; Core builds and Visual Studio MSBuild builds TraderDB.sqlproj successfully.
No native SQL roundtrip, migration, market/broker request, paid AI call, app restart or activation ran.
Existing CS8632 compiler warnings remain. Cached dependency audit warnings separately include NU1902–NU1904
for System.Data.SqlClient, System.DirectoryServices.Protocols, System.Drawing.Common and System.Security.Cryptography.Xml;
no dependency changes or fresh audit were performed. No full Core suite or complete solution build ran.
Evidence: `artifacts/ai-coordinator-tests.log`, `artifacts/ai-coordinator-sql-build.log`.

**Existing API credential located, September 30 (local read-only check):** Oracle's user-secrets store contains `Oracle:OpenAi:ApiKey`; its suffix matches the operator's screenshot of the TraderVI key created May 10 and last used May 27. This agrees with the September 26 saved-key finding below. The environment-only check did not inspect this store. `Oracle/Oracle.csproj` declares user-secrets ID `00ba8049-f46a-4e8b-8262-a6ac14848c58`, and `Oracle/Program.cs` loads it through `AddUserSecrets`. See [credential reuse](running.md#ai-momentum-api-key-and-starting-portfolio-inspection). No secret was displayed or copied, no configuration changed and no API or database call ran. Current authentication/model access and client startup integration remain unverified.

**API setup tooling, September 30 (source/tested only):** `Set-TraderVIOpenAiKey.ps1` provides masked
local entry into the existing environment configuration, a nonpersistent session option and presence-only
checks. Script parsing and isolated synthetic-key checks pass for masking, output secrecy, session configuration,
invalid-input preservation and unchanged user settings. Actual user-environment key persistence was not run;
the presence check still reports no key at all three environment scopes. Twenty existing AI client tests pass
offline. Existing CS8632 compiler and cached NU1902–NU1904 dependency-security warnings remain; no fresh
security audit or full-solution build. `Read-AiStartingPortfolio.sql` locates the chosen variant and returns
current cash, open holdings and saved reviews; SQL Server 2019 syntax validation passes, but this new query
was not executed. No starting portfolio was injected, no real key saved and no SQL/API/app operation ran.
Review inputs become frozen evidence only when saved in `AiPortfolioReview.RecordJson`. The subsequent
credential/access check and source host are recorded at the top of this page. Client evidence: `artifacts/ai-api-setup-20260930/client-tests.log`.

The authorized installation/native storage rehearsal is recorded above. The operator selected
Breakout 3 with skipped daily gates for the first cash/holdings source; the read-only catalogue lookup
uniquely matches an active BreakoutTop3 / PortfolioRulesOnlyV1 account. The private identity receipt is
`artifacts/ai-review-install-20260930/starting-portfolio.json`; no assignment or risk history was changed.
The app owner is now composed in source; next prepare input configuration/private history and qualify a bounded connected review.
At the after-hours setup check, OPENAI_API_KEY was absent from process, user and
machine environments; other credential stores were not inspected. The market was closed, so the current
scheduler could not admit an actual review. No startup configuration, new history or API access was activated.
Independent protection and execution stay separate.

**Review scheduler and recovery, September 28 (source/tested only):** `AiReviewScheduler` uses a reviewed calendar
session and explicitly supplied checkpoint times. It combines/deduplicates entry, confirmed-closure
and risk-permission events, clamps requested intervals to 1–15 minutes, admits one review at a time
and paces attempts at least one minute after completion. Missed checkpoints combine into one current
review; events arriving during a call remain pending. Changed event identities block further reviews.
`AiScheduledPortfolioReviewer` prepares fresh input on an explicit tick and invokes the existing
learning-aware supervisor. Trigger reasons/evidence and requested/applied timing accompany its private
saved packet. Spending admission still belongs to the existing shared call journal; this adds no budget
or funding reset. Failures preserve review blocks until fresh success. Returned proposals cannot outlive
the account packet, learning context/lessons or session; newly pending events suppress the older result.
These are review-status blocks, not completed execution enforcement. No protection callback is coupled
to paid reviews. `AiReviewScheduleJournal` now binds saved transitions to account/portfolio/mode and
explicit original configuration. It replays history, validates calendar bounds, holds one writer and
saves before returning admission/completion. Recovery preserves pending evidence, cadence and failures;
an interrupted review keeps its original ID as Interrupted, waits at least one minute and requires a
new fresh review. Existing call reservations are not touched. Even a previously successful review
does not regain permission after reopening. Explicit session advance carries unresolved events/blocks
into today's reviewed session without replaying missed days or clearing an event-history fault.
Missing, corrupt, altered or pending history blocks use; a storage failure invalidates the current
owner. Full history is bounded at 32 MB and rewritten per transition; checksums do not authenticate
malicious edits or detect rollback between process lifetimes. Protected storage, retention, sustained
I/O performance, current-source/account binding and operational host recovery remain unqualified.
The September 29 timing increment below completes the next source connection; operating checkpoint
choices and qualified automatic host composition remain pending.
Validation: 143 focused tests pass across scheduling/recovery, reviewer, learning, client/spending and packet
construction. .NET 10.0.401 builds Core/tests with existing CS8632 compiler warnings; no new warnings.
No fresh dependency-security audit (`NuGetAudit=false`), full Core suite, WPF or complete solution build
ran. Synthetic temporary journals only; no SQL, external market/API/broker request, app restart,
deployment or trading action. Evidence: `artifacts/ai-risk-values-20260928/review-schedule-recovery-tests.log`;
the preceding 120-test scheduler baseline remains in `review-scheduler-tests.log`.

**AI-requested review timing, September 29 (source/tested only):** scheduled reviews use a version-3
proposal requiring `next_review.minutes` (whole number 1–15) and a nonblank reason of at most 1,000
characters. Missing, fractional, out-of-range, duplicate or unexpected fields reject the review.
Version-1/2 callers and stored proposals retain their existing contracts; learning consultation and
execution preparation retain their checks. Valid fresh results apply timing atomically with the saved
completion transition. Expired results, changed event history or newly pending events cannot change
cadence. Earlier checkpoints/events, shared budget, data expiry and independent protection retain their
own boundaries. No actual model call or operating schedule is enabled.
Validation: 161 focused scheduling/recovery, reviewer, learning, client/budget and packet tests pass,
including durable timing replay and failure cases. Existing CS8632 compiler warnings remain. No fresh
dependency-security audit (`NuGetAudit=false`), full Core suite, WPF/full-solution build, SQL operation,
external market/AI/broker request, restart or deployment accompanied this source increment. Evidence:
`artifacts/ai-risk-values-20260928/review-model-timing-tests.log`. The subsequent authorized market-hours
diagnostic and its remaining blockers are recorded separately in Latest operational evidence above.

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

**Profit protection installed/enabled, October 18:** all 15 active paper portfolios (12 Daily Shadow,
three Delphi Live, including the ten paired comparison accounts) have prospective ADR-0090 versions
assigned. All 37 open holdings (32 Shadow/five Live) have saved progressive state. Native before/after
checks preserve holdings/costs, cash, recorded fills, stronger existing stops, pending protective sells,
comparison profiles and safety holds. Original entry-pause settings are restored. Frozen V1/default
definitions remain unchanged and false; the active assigned versions opt in.

Migrations 043–045 were individually applied after a fresh checksum-verified backup and hash-matched
OneDrive-folder copy; cloud synchronization and a restore drill are not established by that copy.
The narrow invested-account API and comparison SQL guards allow this successor only. Ordinary editing
still requires paused/empty portfolios. Initial guard/encoding rejections occurred before any assignment;
they were corrected and every prepared comparison successor passed native validation before rollout.
No trigger was disabled and no account/profile/history was rewritten.

Validation: 59 rollout-focused tests, full Core suite 2,423 pass/eight skips, Release WPF and SSDT
project builds pass. The earlier implementation passed 166 focused tests including Practice. Five
desktop binaries were installed with rollback copies and matching hashes; the normal-path host is
responsive and loaded the exact installed Core. Post-start readback rechecks all 15 assignments/37
holdings. The weekend quote-only wake returns zero signals with unchanged orders/quote observations;
no new Live failure metadata is recorded through 00:19 Toronto. Existing 260 desktop compiler warnings
remain; no fresh dependency-security audit or full-solution build. Tests before this rollout are in
`artifacts/progressive-profit-protection*`; private operational receipts are in
`artifacts/profit-protection-rollout-20261018/`.

This verifies installation, enabled configuration, saved-state preservation and off-session host
behavior. A regular-session bid touch/sale and continuous timing are not observed by this weekend check.
Quote investigation remains deferred: local receipt age does not qualify upstream bid freshness.
The rollout tools requested no market quotes or broker orders; real TFSA execution remains unactivated.
[Accepted contract](adr/0090-progressive-individual-profit-trailing.md),
[rollout procedure](running.md#progressive-profit-protection--implemented-disabled).

The following research record preserves the earlier studies and their limits; its no-implementation
statements describe those earlier steps and are superseded by the installed/enabled state above.

**Gain-retention research, October 17, 2026:** after the operator corrected the scope to individual
stocks and accepted day-move/purchase-profit measures separately, the [stock-level review](reviews/stock-gain-paths-20261017.md)
reuses saved paper evidence through October 16. It covers 235 held-through-noon position-days:
87 rise by noon, then 40 fade and 47 improve. Twenty-one rising carried-position mornings remain
below purchase cost. The saved September 30 walkthrough separates a winner's giveback, another
holding's recovery and a later purchase. Direct source checks confirm all 235 reported price/cost
rows; 84 afternoon-entry paths and complete-path later highs are separately described. Protection
behavior remains undecided; no trading rule changed. This extension uses no new SQL or provider run.
The [whole-account background audit](reviews/paper-gain-patterns-20261017.md) retains the earlier
15-cash-book/192-close reconciliation but does not answer the stock-level question. Next discuss
ordinary individual-winner protection and later upside/new purchases; keep repairs secondary.

**Progressive trailing design, October 17:** the operator confirmed a shrinking percentage gap
as an individual position's purchase profit grows, and prioritizes profit protection over post-sale
upside. The [DSV/ARE walkthrough](reviews/stock-gain-paths-20261017.md#dsv-versus-a-winner-that-kept-climbing)
uses complete saved close paths (46/46 and 78/78 points). The subsequent authorized offline
[saved-trade comparison](reviews/progressive-trailing-trade-study-20261017.md) supplies 53 distinct
non-DSV, complete single-purchase Shadow paths: seven earlier modeled exits, six profitable, two
recorded losses converted to modeled profits; 46 unchanged. Median return for those seven is +0.53%
modeled versus +0.14% recorded. All 75 underlying recorded fills and 11 modeled fills/first crossings
verify; original bar/fill references match SELECT-only supplements. This is an isolated trade overlay,
not a full engine backtest or realized profit. The [ADR-0090](adr/0090-progressive-individual-profit-trailing.md)
bands remain unapproved for operation. The larger-winner extension reuses the frozen files: entry-based
ARE/AGI +3% examples retain modeled +2.64% and +3.06%/+2.10%; conditional already-held +5% illustrations
retain ARE +12.35% and CAS +4.55%. These day windows do not prove survival from entry; FVI exits under
a lower band before its later +5% high. The operator then clarified 10 a.m./noon Toronto tightening
and authorized five fixed schedule comparisons. Of 55 distinct closed trades including DSV, the agreed
time version improves three, reduces one and leaves 51 unchanged; the gentler version improves three
and leaves 52 unchanged. Both reduce the separate larger ARE entry's profit (+2.64% to +0.92%), while
conditional already-held ARE benefits. Recommendation remains original bands as baseline. All 415
scenario outcomes independently verify, including 101 modeled fills; 83 original-schedule cases
reproduce prior results. [Detailed scopes and checks](reviews/progressive-trailing-trade-study-20261017.md#time-of-day-gap-comparison).
The operator then retained the original bands and accepted one combined holding for additional buys:
update average cost but preserve the existing stop and tightest earned gap. The
[stop/add-on walkthrough](adr/0090-progressive-individual-profit-trailing.md#existing-stops-and-additional-purchases--design-walkthrough)
checks current source and illustrative arithmetic. The subsequent
[saved add-on test](reviews/progressive-trailing-trade-study-20261017.md#saved-add-on-trade-comparison)
supports seven records/six distinct cost histories across ARE/MATR/CAS. After morning add-ons, three
ARE records retain modeled +1.58%/+1.93%; a fourth sells before its later add-on (+2.64%). MATR/CAS
remain unchanged below activation. Actual ARE sales yield larger profits; this protects a smaller
profit sooner. Seven recorded returns, four earlier fills/crossings and six add-on transitions
independently verify. This is an additional close-based overlay, not full engine replay or implementation.
The subsequent [update-interval comparison](reviews/progressive-trailing-trade-study-20261017.md#five-minute-versus-fifteen-minute-stop-updates)
holds five-minute closing-price breach checks constant: 61 distinct closed histories give 56 equal
returns, four higher with five-minute updates and one higher with fifteen-minute updates. DSV retains
+1.29%/+2.03% respectively; earlier ARE +1.25%/+0.82%; add-on ARE is unchanged. All 85 five-minute
baselines reproduce, and 170 scenario outcomes independently verify. These are controlled samples of
one price source, not native engine replay or operating cadence adoption. The operator subsequently
selected completed five-minute stop updates and accepted first-fresh-observed-touch triggers, without
waiting for a close. Newly raised stops apply only to later observations. This is a design decision;
the close-based studies do not test touch returns. The operator subsequently requested all portfolios,
including and especially Delphi Live, and after explanation accepted fresh bid as the touch input.
Source inspection confirms Live already checks fresh bids against its existing floor; this does not
qualify other families or implement progressive bands. The operator now accepts the highest completed
five-minute close since purchase and existing holdings adopting the strategy as soon as enabled,
preserving stronger protection. After the example, the operator accepted five-/one-minute checks
outside/within 0.25% above the stop, a new quote per check and a 60-second receipt-age limit for design.
The October 17 source review traces Live's current quote query/model/receipt: no bid-event/as-of time,
so current market age cannot be verified. A direct reread of the September 29 saved probe confirms
22 current quotes/zero positive bid-ask pairs; the separate delayed endpoint has pairs but no bid time.
That pair flag does not prove bids alone absent, and these are older samples, not new market evidence.
The source's market freshness remains unverified. The operator subsequently declined additional
market-hours/source-guarantee investigation as an implementation prerequisite; defer that study.
The later implementation request authorizes source integration and testing using the existing source,
with that limitation recorded. Integration and synthetic bid tests are complete as summarized above;
installation, prospective assignments and operational verification remain separate future work.
[Finding and deferral](adr/0090-progressive-individual-profit-trailing.md#quote-source-freshness-check--october-17-2026).
No new market requests or SQL access ran; morning/noon alternatives stay deferred.
The [decision list/example](adr/0090-progressive-individual-profit-trailing.md#remaining-decisions-before-implementation) owns details.
[Agreed scope and bid input](adr/0090-progressive-individual-profit-trailing.md#all-portfolio-scope-and-bid-price-recommendation).
No trading rules, new SQL reads/writes, producers or running apps changed.

**Session recovery, October 16, 2026:** the [session-work register](session-work-register.md)
accounts for 47 prior TraderVI/support chats and this consolidation, using chronological source
updates and targeted detail. It preserves implementation follow-ups, unfinished ideas and explicit
deferrals, including the operator's revisit-soon intraday-gain research goal. The completed September 8
exploratory Practice starter trial replaces the roadmap's stale pending-trial wording. This is
documentation reconciliation, not new operational verification; existing operational dates remain.

Programs, side effects and commands: [running guide](running.md). Architecture:
[system design](system-design.md). Models, signals, gates and price definitions:
[design rules](design-rules.md). Original measurements, old snapshots and superseded summaries:
[preserved status history](project-status-history-20260922.md).

Replace the affected current row after new evidence; preserve its verification date until that evidence
changes. Put long run/build narratives in linked reports or dated history. Keep next steps in the
checklist and priority changes in the roadmap, then reconcile their short links/status labels.
The earlier September 22 bounded documentation pass changed navigation only; this later execution
increment adds source/test evidence and explicitly dated native read-only verification.
