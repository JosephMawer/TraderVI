# Paper restart rehearsal on an isolated copy — November 4, 2026

## Authority and isolation

Joseph authorized a rehearsal on an isolated copy with no broker access, expressly excluding installation
and restart of the running engine. This qualifies part of [ADR-0127](../adr/0127-paper-checkpoint-restart-and-incremental-verification.md).
During the rehearsal, no production trading record, permission, schema or installed binary was changed. One fresh production
`COPY_ONLY` compressed/checksummed backup was taken because the existing backups predated recovery completion.
It was verified and restored into a new private SQL Server 17 LocalDB instance. The restore upgraded the
copy's database format; it did not upgrade or change the production server.

The source repositories now accept an explicit connection factory, retaining the existing default for
ordinary operation. Every rehearsal connection uses `RestartRehearsalDatabase`, which addresses only
the token-named LocalDB instance and rejects a missing/wrong database marker. The harness creates a
separate ownership lock and continuity file in ignored artifacts. It constructs no broker client,
authentication/session store, market client, normal engine host or installed RPC endpoint. It does not
read installed broker credentials or the live engine's continuity file.

The original restored recovery receipt correctly failed adoption because restore changes database identity.
For subsequent tests only, the harness added a clearly synthetic copy of that completed proof with the
copy's database identity and a synthetic clearance record. It preserved the original receipt. This test
mapping is **not** a production recovery or permission mechanism. The copied continuity stamp was similarly
paired to a new test-local witness. Each later phase ran in a fresh test process, calling the actual SQL
repositories and continuity component; the ordinary engine executable was never launched.

## Results

All six native phases passed against the copied three-portfolio book:

| Phase | Evidence |
|---|---|
| Adopt | Original restored identity rejected; synthetic copy proof adopted in about 2.03 seconds; all original portfolio heads/JSON unchanged; repeat preparation rejected. |
| Restart | All three saved checkpoints qualified. Captured SQL commands contained no archive-item read. An audit with no new revisions verified zero records. |
| Append/retry | Two harmless test revisions were appended through the real working ledger writer. Discarding the first reply and repeating the same request created no duplicate revision; cash/holdings/fills/pending actions were unchanged by that retry. |
| Audit rollback | A temporary copy-only SQL trigger forced failure after boundary preparation and before audit progress could commit. Both boundary and cursor remained unchanged. The trigger was removed in `finally`. A successful step's returned reply was then deliberately discarded. |
| Fresh-process resume | Reloaded the durable audit/boundary pair, resumed after the committed step, and completed with exactly two verified units. The next audit verified zero units. No new Recovery/Archive run appeared. |
| Negative checks | Missing continuity rejected. A damaged current checkpoint held only its portfolio; the other two remained eligible. Restoring its exact original bytes allowed current qualification again, preserving the original finding. |

Across the five restart phases, measured elapsed time through repository setup, continuity and Paper
qualification was approximately 0.69–0.80 seconds, with 29 traced SQL commands in the Paper qualification
section and no archive-item reads. These are fresh test-process measurements on the restored LocalDB
copy with warm OS/database caches, not a whole-engine startup or cold-disk performance guarantee.
The first append fixture omitted mandatory audit events and was rejected before any portfolio append;
the fixture was corrected and rerun. No production behavior correction was required by the six phases.

## Limits and remaining acceptance

- The copied heads exactly matched the recovery boundaries: **zero intervening legacy revisions**.
  The one-time legacy catch-up path remains unmeasured and untested against native post-recovery data.
- The injection established SQL rollback and durable retry after a discarded reply. It did not simulate
  machine power loss or a real network disconnect during `COMMIT`.
- The actual installed engine startup, shared-worker scheduling, desktop presentation, broker/account
  checks, actual overnight scheduling and market-hour Paper cycles were intentionally not exercised.
  Existing offline scheduler/workflow checks complement this repository-level rehearsal.
- Native pending-buy expiry/protection with actual market observations remains installation acceptance;
  no live quote or market request was made. This rehearsal does not repair issue #24's holding mapping.
- Retention and database-size analysis remain deferred. The dedicated backup is about 4.44 GB and the
  restored files about 29.27 GB (decimal); both were subsequently removed under the explicit cleanup authority below.

## Reproduction and private evidence

Source: `Operations/New-PaperRestartCopy.ps1`, `Operations/Invoke-PaperRestartCopyPhase.ps1`, and
`TraderVI.Core.Tests/PaperRestartCopyTests.cs`. Run only under explicit isolated-copy authority.
The copy script performs the one production backup and restores into a new private instance without
`REPLACE`. The phase runner executes only the opt-in native test, with phases in order:
`adopt`, `restart`, `append`, `audit`, `resume`, `negative`. Ordinary tests skip this fixture.
Do not point this harness or its synthetic receipt mapping at production.

Private run: `artifacts/paper-rehearsal/08d60286d4e84f9e8fc135177f9f0f08/`. It contains the backup/restore
logs and copy manifest, phase logs and `*-passed.json` aggregate receipts. The test-only witness and
lock were subsequently removed. The dedicated backup was
`PaperRehearsal_08d60286d4e84f9e8fc135177f9f0f08.bak` in the established backup staging folder.
The private LocalDB instance was stopped after the six phases, then removed with its files and backup
under the separate cleanup authority below. This does not establish a general retention policy.

Post-rehearsal regression: 3,656 full-suite tests passed with 17 opt-in native tests skipped. The isolated
Debug WPF build also built the engine, with no errors and two existing CS0168 warnings. Existing nullable
warnings remain in the core test build. Dependency-security auditing was disabled; the SQL project/full
solution was not built and no schema definition changed. Logs: `artifacts/build/paper-rehearsal-full.log`
and `artifacts/build/paper-rehearsal-host-build.log`.

The live engine remained PID 21140 with its November 3 11:58:39 start; collector PID 17980 retained its
09:21:05 start and dashboard PID 12028 retained its 22:01:25 start (Toronto). These process identity
checks establish no restart, not a fresh broker-health or permission check.

## Live installation and rehearsal cleanup

Joseph subsequently requested removal of the isolated-test resources and installation in the live app.
On November 4, the exact stopped rehearsal LocalDB registration, restored data/log files, dedicated
backup, residual instance logs and test witness/lock were removed. Approximately 33.7 GB of disposable
test files were deleted; this is not net disk growth after the new release backup and preservation copies.
Small verification receipts and the reproducible source harness remain. Other databases/backups were untouched.

Release source `ca82e73b` built successfully; its exact Core binary passed 3,656 tests with 17 native
tests skipped. The six earlier opt-in SQL phases remain separately recorded above. The Release build
reported 257 existing compiler warnings and zero errors; dependency-security auditing was disabled.
The SQL project/full solution was not built. No schema migration was needed or applied.

A fresh full backup passed SQL checksum verification, and its established OneDrive copy matched SHA-256
and reported synchronized before the switch. The old runtime and protected compact/journal/continuity
records were copied and hash-verified. The first installer attempt stopped before mutations when it
detected the collector had changed from PID 17980 to PID 21964 at 01:34:06 Toronto. All installed
collector package/configuration hashes still matched; fresh status was OutsideSession. No collector
restart was requested by this installation. Its earlier process-exit cause is not established here.

Engine PID 21140 and dashboard PID 12028 stopped normally. All 140 staged package files were installed
and hash-verified in the standard Release dashboard directory, retaining collector routing. The genuine
production recovery `28f8f730-76ce-471f-94d2-cde1904cef0d` was adopted at 01:40:46 Toronto in 741.635 ms;
no synthetic restored-copy proof was used. Preflight showed zero intervening legacy revisions.
Hash-verified read-only postconditions confirmed one policy, three boundaries at the unchanged current
revisions, the original recovery reference, zero unresolved incidents and no newly created audit run.

The installed engine is PID 24068 in **Build review only**, with dashboard PID 33432. Native controls
report automatic trading Off and Wealthsimple connected. Core SHA-256 is
`0724D67C143F2A26EA6231AC58E452F6AF96B8C57476FD843A68ECB8570368A6`.
The existing compact Real qualifier requires the saved settings' exact build identity even when trading
is stopped. Therefore normal startup was not attempted before the operator's existing build-review
procedure. No financial approval or grant edit was made. This leaves Paper qualification timing,
pending-action handling, overnight scheduling and market-hour acceptance open; 0.742 seconds measures
baseline preparation, not an actual engine restart. After review, use the normal controlled transition
and verify Paper qualification and delta coverage without requesting full recovery.

Private release evidence: `artifacts/paper-install-20261104/` (package/installation/preparation receipts,
backup verification/synchronization, before/after read-only results, controls, cleanup and Release tests).
