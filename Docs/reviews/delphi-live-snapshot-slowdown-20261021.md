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

Nothing installed, restarted, approved or enabled by this work yet. No schema,
market-service, training, broker mutation or native write rehearsal was run. Native sustained
five-minute capacity, uninterrupted continuity, order activity and cutoff verification remain open.

## Installation and acceptance

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
