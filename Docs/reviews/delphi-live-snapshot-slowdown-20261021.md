# October 21 Delphi Live snapshot slowdown correction

The operator resumed this work during the session: fix the slowdown now so signals stop resetting,
preserving strategy rules, risk controls and today's 16:00 Toronto cutoff. This supersedes the earlier
evening deferral for this focused correction; the older SQL-save retry draft remains parked.

## Evidence and scope

Fixed local diagnostics show repeated snapshot read/unpack work during long LiveEngine cycles:
502.90 seconds ending 12:42:30 (Actions 188.39 seconds), then 372.33/353.67-second cycles with
Actions 172.49/163.61 seconds. Setup waits 438.82 seconds for admitted work. A later observation-set
deadline error 52232 at 13:07:41 clears context; the original SQL -2 evaluation-save failure is a
separate observation. SQL server wait/CPU attribution has not been established.

The hash-verified 13:36 read shows real StrategyEvaluated at 13:32:31/revision177/profile1,
37 actual symbols through the 13:25 bar, none mature/confirmed; no automatic orders, positions,
saved blocks or daily/capital holds; original exact 16:00 expiry. The selected paper source advances
13:36:12 and the lease 13:32:38. Receipt:
`artifacts/read-only-db/result-20261021T173621981Z-58fc7e32ef9a47d1a8b09ea844aa9b7a.json`.

## Implementation and pending validation

[ADR-0098](../adr/0098-verified-snapshot-read-reuse-and-build-review.md) defines exact fresh-payload
reuse, bounded memory and explicit build review. The storage reader still loads every SQL part at
the original revision/transaction; the cache never supplies a database or account observation.
Writes still reread and fully validate before commit. Persisted encoding/fingerprints are unchanged.

The reachable snapshot graph uses sealed immutable records, strings, ImmutableArray and
ImmutableDictionary, including lifecycle, quote, progressive protection and comparison fields.
Caller-supplied mutable membership collections are copied before validation/reuse.

Initial synthetic baseline: three repeated-report full validations take 3,615 ms. Local canonical
string encoding reuse reduces that to 2,787 ms in the same focused experiment; it is insufficient
alone. The bounded verified-read cache additionally avoids repeated full-history reconstruction
when fresh contents exactly match. Artifacts retain synthetic benchmark/test logs.

First full offline pass: 2,820 Core tests pass, nine native SQL skips; Debug WPF build succeeds.
The targeted warm-cache benchmark takes154ms for100reads; 24 focused cache/build-review checks pass.
Subsequent read-only size verification finds15,454distinct item contents/19,926,728raw bytes in the
selected source (root25,702bytes;13,269quotes). Receipt query hash804BB2E55A54D51D0386228646E6F79416F56E4B1D938C2ED051CCEE7C4F8899,
`result-20261021T174753245Z-b0d2f2c710af485785cb06348b416633.json`.
The conservative cache allowance is therefore256MiB/fourentries/one revision per portfolio.
Narrowed report-size query adds59reports/2,017,496raw bytes (hash2958321E760536C72AA30BC319F63B0BBDF7716F8966FA4861B91D1D1CE49E8D,
receipt`result-20261021T175214679Z-14164cb2bedb46c881560a328476becf.json`). A combined aggregate first
needed explicit hash collation, then timed out; no result from that failed query was interpreted.
The bounded large-history test uses16,000quotes and verifies default-cache admission and revision
replacement. Final full offline run: **2,821pass/nine native skips/2,830total**, Debug WPF build
passes with zero errors. Logs:`artifacts/snapshot-slowdown-full-tests-final.log` and
`artifacts/snapshot-slowdown-wpf-debug-final.log`. Existing compiler warnings are CS8632/CS0168;
cached Pkcs9.0.4 dependency advisories NU1902/NU1903 remain separate. Full solution/SSDT was not built.
Current native diagnostic sample has59unpacks averaging1,132ms versus62SQL reads averaging269ms;
this supports targeting repeated unpack work, without establishing SQL server wait attribution.

Standard Release installation is recorded below; no app was launched, approved or enabled by the
agent. No schema,
market-service, training, broker mutation or native write rehearsal was run. Native sustained
five-minute capacity, uninterrupted continuity, order activity and cutoff verification remain open.

## Installation and acceptance

**Initial twenty-minute recovery verified14:31–14:32:** real StrategyEvaluated14:31:12/revision202/
Active/profile1 consumes the14:25bar with **26/37actual families mature**, zero confirmed entries,
orders, positions, savedblocks or daily/capitalholds; original16:00expiry. Source continuity remains
epoch13/start14:05 for stock and benchmark through14:25;34/37latest observations are on time.
The20minute warmup completes without another source reset. Targetinputs444/latestrecord14:29:00;
source14:31:32/sessionMonitoring/onelease14:31:31. Latest fullcycle273.62s/Actions125.02s; captured
fullcycles283.69/265.13/264.38/273.62s are below five minutes. All12source/runtime hashes still match
one respondingPID97192 at14:32:30; no appfailure newer than13:43:22. This verifies initial recovery
and actual mature-signal consumption after the focused correction, not guaranteed future latency,
eligible entries, fills, whole-day reliability or restored historical coverage. The existing watch
continues through close for regressions/orders/cutoff; historicalCoverageState remainsBlocked.

Hash-verified receipts: `result-20261021T182959519Z-e7424b81dfc54216b19a94a7f55bb31e.json`
(MaturityTiming.sql,26source mature/34on-time/epoch13/start14:05/14:25bar),
`result-20261021T183138970Z-1b9b382d28f64a6a962fb3e781d19814.json` (BoundedRecoveryWatch.sql,
actual26mature/zero confirmed/real14:31:12). Earlier14:28read also verifies real14:26:03/
revision199/actual14:20bar/zero mature and unchanged epoch. A real-evaluation gap near ten minutes
occurred between14:16:03 and14:26:03 while paper source inputs progressed; lower latency and
longer-running host cadence remain limitations to watch, not silently closed by source maturity.

**Second full cycle and uninterrupted early warmup,14:17–14:18:** second full cycle finishes14:15:56
in265.13s/Actions120.99s. Actual evaluation advances14:16:03/revision196/profile1, consuming37symbols'
14:10bar. Durable targetinputs333 through14:10/latestrecord14:13:55; selectedsource14:16:55,
sessionMonitoring/onelease14:16:31. Stock and benchmark continuity remain epoch13/start14:05,
30/37on-time, zero mature/confirmed; two endpoints are early warmup, not20minute acceptance.
Zeroorders/positions/holds/savedblocks; same16:00expiry. No application failure newer than13:43:22
observed. RespondingPID97192 samples2,231MiBworking set/2,607MiBprivate memory; one sample does not
establish a leak or steady-state bound. Receipts:
`result-20261021T181708433Z-d98a9c4760ab41f4adb5f9ac2e494af4.json` (progress) and
`result-20261021T181804259Z-dcd0ec6421464c3481a4d017fb6cc4b1.json` (continuity). Continue the existing
read-only five-minute watch through close. Alert on a new post-build deadline/reset, over10minute
real-evaluation stall or other meaningful blocker/order activity. Verify later on-time bars under
the same epoch and actual mature signal families before accepting sustained recovery; do not force
orders or change any confirmation/data/financial rule. This remains the open native dependency.

**First new-build real execution verified14:13:** user reports updated-build approval/enable.
One responding standard-Release ownerPID97192 starts14:02:38; all12reviewed source/runtime hashes
match. Durable ImplementationReviewed is revision190/14:06:29; new effective Core identity matches.
Real StrategyEvaluated follows14:11:23/revision193/Active/profile1. Original source approval12:43:10,
original approval/continuation, financial risk history, orders/protection and exact16:00expiry remain.
No orders/positions/savedblocks/daily/capitalholds or order-review/invalidation hold at this check.

The initial SQL comparison of whole serialized Memory returns unequal bytes; 50symbol entries remain,
no financial scalar properties differ, but37nested symbol payloads also differ as serialized text.
This was investigated rather than accepted as preservation. A recursive SQL diagnostic first fails
type/collation validation, then its corrected version times out; neither failure is an application
SQL failure. The installed helper subsequently reads just the two reviewed Memory objects into
ignored evidence; local recursive object-key sorting (array order/scalars preserved) verifies equal
canonical SHA256. **All signal/financial memory is semantically preserved; only JSON object order
differs across processes.** No private payload is displayed or committed. Evidence scripts/receipts
remain ignored. ReviewRead now labels its raw-byte comparison MemorySerializationBytesMatch so a
false raw comparison is not presented as a semantic failure.

Receipts: `result-20261021T181320200Z-eb4ce9f5f80a48449e05d9c4c06789cf.json` (review/execution),
`result-20261021T180856707Z-890c07f72e2e4a3790c2c110e91db87c.json` (aggregate memory properties),
`result-20261021T181226623Z-577b3ec4f49e4f9dbabcae4f25255b88.json` (private memory input;
queryhashD050251D3090CDBDFD9C16AE05082B59BADABF0FF81D7D1BDC31C996F07B8EB2), with
`artifacts/snapshot-slowdown-20261021/Compare-Memory.ps1` producing CanonicalMemoryMatches=true.

Initial restart engine pass takes161.64s. First full observed cycle completes14:11:15 in283.69s
(Collect3.59s/Evaluate0.73s/Actions127.61s), versus preceding353.67/372.33/502.90s observations;
workload/cold-start differences limit this comparison. A setup wait still times out after120.02s
before another wait drains81.85s; first live evaluation is confirmed only after that boundary.
Current unpack sample157loads averages365.5ms versus prior59loads/1,132ms. No newer application
failure than13:43:22 observed. This is first execution/performance evidence, **not sustained capacity
or uninterrupted continuity acceptance**; next complete cycles and fresh source inputs must be read.

**Installed14:02 Toronto:** operator confirms Stop execution host/normal close. Independent owner
counts are zero14:00:57 and14:01:12. Standard Release builds from code commit484f8cff2c6dac3874ef8bf4a1fdb1a48442b7db
with zero errors (262existing warning emissions, including CS8632/CS0168 and separate Pkcs dependency
advisories). Full solution/SSDT not run. All125previous output files are preserved with hashes under
`artifacts/snapshot-slowdown-20261021/before-release`; the three prior runtime binaries match the
previous reviewed manifest. The new125file manifest, reviewed source/runtime hashes, installation
receipt and build log are in the same artifact folder. WPF's installed Core matches the Release Core
output. No owners remain at the installation receipt. No app startup, SQL mutation, broker operation,
financial approval or host enable was performed. Operator normal --configuration Release --no-build
startup/sign-in, Review updated build, Approve reviewed build and separate Enable reviewed host are
pending. Normal restart can require one fresh warmup; sustained native timing/continuity is unverified.

**Read-only watch while closure is pending,13:58–13:59:** one responding unchanged standard-Release
ownerPID39964; all three expected runtime hashes still match the prior reviewed copy. Real evaluation
advances to13:52:53/revision186/profile1, actual37symbols through13:45bar, zero mature/confirmed,
automaticorders/positions/savedblocks/daily/capitalholds; original16:00expiry. Targetinputs259 through
13:50bar recorded13:56:46; source13:58:40/sessionMonitoring/onelease13:56:42. App diagnostic latest
52232occurrence is13:43:22; current stock/benchmark continuity is epoch12/start13:45,30/37on-time,
zero mature. This is another occurrence of the already-known pending-fix blocker, with real evaluation
recovery; it does not establish sustained recovery or a purchase ETA. No repeated unchanged alert or
installation/approval/control action. Hash-verified receipts:
`result-20261021T175849064Z-74e214881e364842a4b9cce71ce8dbfc.json` (BoundedRecoveryWatch.sql) and
`result-20261021T175928124Z-51b5df67ffe84d76bb986a01792f2f5a.json` (MaturityTiming.sql).

1. Complete offline regression/build review and commit the focused change.
2. Operator stops execution host and closes TraderVI normally; independently verify zero owners.
3. Build standard Release and record exact output/source hashes. No automatic app startup.
4. Operator opens normally, signs in, confirms unchanged account facts and chooses **Review updated
   build**, **Approve reviewed build**, then separately **Enable reviewed host**.
5. Read-only evidence must prove new durable build review, unchanged source/risk/expiry, advancing
   real evaluations and on-time paper input continuity. Observe stage timings over successive
   five-minute cycles; do not infer performance or trading success from UI or process liveness.

If account orders/positions/holds appear, this cash-only build-review path refuses approval and
requires separate recovery. If preparation passes the reviewed close, no new buying is authorized.
