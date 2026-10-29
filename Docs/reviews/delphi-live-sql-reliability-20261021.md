# Remaining SQL reliability and paper-workload findings — October 21

The operator resumes the deferred SQL investigation now, preserving financial risk and tomorrow's
separate review/approval. The operator explicitly selects **Fix SQL only for now** when asked about
an optional paper-workload switch. This supersedes the evening implementation deferral, not any
trading permission. Standard Release SQL correction fromc4d094fd is installed21:31Toronto after
normal operator stop/closure; tomorrow's personal approval/enable and native acceptance remain pending.

## Findings

The original SQL -2 at09:37:46Toronto is in evaluation persistence; historical execution recovers
after roughly thirteen minutes. The exact original server wait/plan remains unknown. Current
read-only helper metadata again confirms enabled evaluation/checkpoint and collection-slot/conflict
indexes:167,268evaluation rows,90,887slots and zero conflicts. Neither VIEW DATABASE STATE nor SHOWPLAN
is available. This does not prove an optimal plan, no blocking or a missing index. No permission
extension, unrestricted SQL connection, native write rehearsal or schema migration was used.

Metadata SQL is `artifacts/sql-reliability-20261021/Metadata.sql`, receipt
`artifacts/read-only-db/result-20261022T011831971Z-acf04a2598a04c0483aa2f3a6867a689.json`.
Verified SHA256:80F18ED9DCE0237BB094C7F26EABC58F29BB3826ED7DC227A133272797E3BA8F.

The earlier438.82second setup wait drains a502.90second admitted engine cycle. Collection3.34s/
evaluation1.49s are short; paperActions188.39s and many nested snapshot reads/unpacks are expensive.
The desktop barrier addresses known SQL lock contention between paper settings leases and account
import writes; it is not a strategy requirement that paper accounts be idle. Removing it blindly
would restore the known conflict. The SQL error52232 seen later is a collection-deadline guard,
distinct from SQL -2, and must not be retried as transient failure or bypassed.

After the separately installed21:10lag/decision build, one responding app owner35388 starts21:13:18.
Read-only timing samples at the start of this investigation show initial PortfolioDisplay54.21s,
TrackedDisplay46.07s and repeated history reads/unpacks; six overnight LiveEngine wakes total3.11s
and have a0.584smaximum. Nested stages cannot be added as independent wall time. A process sample
shows about717MiB working set and80.84CPU seconds accumulated since start; this is not instantaneous
CPU utilization or proof of a leak. Retained failure records inspected are historical, not a new
post-install evaluation-save timeout. Off-session status/read progress does not prove market capacity.

Paper processing includes reconstructing/verifying historical ledgers and repeated evidence reports,
not merely computing momentum indicators. New snapshot changes reduce repeated report expansion,
but fresh reads/full verification and financial action work remain. There is no current global
paper-off switch that keeps all real dependencies fed: entry pauses still monitor, and stopping the
shared Live producer removes fresh collection/evaluation and source updates used by the TFSA host.
A workload switch is deferred by the operator; no paper or real controls were changed.

## Correction and validation

[ADR-0102](../adr/0102-bounded-immutable-live-evaluation-save-recovery.md) adapts only the reviewed
evaluation-save recovery portion of the old parked draft. No old source/build-review files are
restored over current source. One qualifying SQL failure can replay the exact immutable save once,
after250ms and connection disposal. All original guards and timeout limits remain. Cancellation,
permission/identity/evidence/lease/deadline failures still propagate; orders are never retried.
Fixed SQL/client stages record safe failure metadata through a bounded background logger.

Initial46focused checks pass. Expanded71focused checks include real monitor orchestration with a
synthetic save failure before/after commit: successful recovery preserves the active epoch, exact
observation time and single saved evaluation; non-transient failure still releases/stops normally.
The focused rebuild briefly waits for the preceding test runner's DLL lock, then completes without
failure. SQL ScriptDom parsing reports zero errors; removing fixed informational markers and ELSE
grouping yields the same commands/guards as source12801bbb. Initial full Core2,916pass/nine native
SQL skips (2,925total) precedes three added orchestration cases. Final full run passes2,919Core/
nine native SQL skips (2,928total), with test collection concurrency disabled as in earlier validation.
The earlier unrelated default-parallel AI cleanup timing limitation remains recorded; it was not
worked around in production code or requalified by this serialized run.
Isolated WPF Release passes/zero errors/260existing compiler warnings. Known dependency advisories
remain separate; no fresh audit or dependency upgrade. Full solution/SSDT was not built.

Artifacts: `artifacts/sql-reliability-20261021/{focused-tests-final.log,full-tests-serial-final.log,
wpf-release.log,EvaluationPersist.sql}`. Native SQL-stage delivery, historical root cause and
market-session capacity remain unverified. Testing does not restart the app or approve execution.

## Standard Release installation — October 21, 21:31 Toronto

Operator confirms app closed and authorizes installation. Independent inspection finds zero
TraderVI.WPF owners immediately before replacing normal outputs and again afterward. Standard
`dotnet build TraderVI.WPF/TraderVI.WPF.csproj --configuration Release --no-restore -p:NuGetAudit=false
--verbosity minimal` succeeds in13.82seconds with zero errors. Source commit is
c4d094fd81b25865cac53d1946142cc78eea501f; installed Core product version identifies that commit and
Core project/desktop SHA256 copies match.125output hashes and eight source/output hashes are saved.
Build reports262warnings:260existing compiler emissions (CS8632/CS0162/CS0168) and two existing
dependency advisories (NU1902/NU1903). No dependency upgrade or fresh security audit is claimed.

Evidence under `artifacts/sql-reliability-20261021/`: `standard-release-install.log`,
`pre-install-output-hashes.json`, `installed-output-hashes.json`, `source-output-hashes.json`,
`installation-receipt.json`. Receipt time21:31:36Toronto. No schema deployment, native write
rehearsal, app launch/restart, market collection, trading approval/enable or order was performed.
The previous2,919offline passes/nine native SQL skips retain their scope; full solution/SSDT remains
unbuilt. All existing risk/source/freshness/financial/expiry rules and the October22 review-only
path remain unchanged. The unrelated Obsidian workspace modification is preserved.

Next operator action: open the normal installed output with
`dotnet run --project TraderVI.WPF -c Release --no-build` and sign in. On October22 review/approve
the exact current build/source through Review October22, Approve October22 continuation, then
separately Enable reviewed host. Arming still requires current matching daily/account/source/storage
facts and fresh ordinary checks at/after09:30. No promise of signal confirmation or an order follows.

## Work-item recovery — October 28, 2026

The dates and operator steps above are historical, not current restart or financial authority.
Current source still uses the bounded save recovery in independent shared-input services. Remaining
native recovery/stage-delivery qualification is owned by [GitHub #17](https://github.com/JosephMawer/TraderVI/issues/17);
the installed architecture's historical/shared-resource capacity is already [#13](https://github.com/JosephMawer/TraderVI/issues/13).
At Joseph's explicit request, the recurring parallel AI cleanup-test limitation is captured separately
in [#18](https://github.com/JosephMawer/TraderVI/issues/18); its remedy needs discussion, and serialized
passes do not prove a correction. No implementation, native test, operational query or runtime change
was performed for this recovery review. Broader documentation reconciliation retains its existing
owner in [#8](https://github.com/JosephMawer/TraderVI/issues/8).
