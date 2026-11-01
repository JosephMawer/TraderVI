# ADR-0122: Reuse existing nightly outputs at 06:30

- **Status:** Accepted; runner validated and Windows task updated; first scheduled execution pending
- **Date:** 2026-11-01
- **Domains:** architecture, data-pipeline
- **Supersedes:** [ADR-0078](0078-isolated-latest-source-nightly-builds.md) source capture/restore/build preparation, and the 00:30 clock in [ADR-0046](0046-guarded-nightly-operations-runner.md) / [ADR-0058](0058-daily-ingestion-and-observed-benchmark-confirmation.md)
- **Preserves:** [ADR-0113](0113-nightly-data-audit-report.md) stage/report contract, [ADR-0114](0114-explicit-nightly-preparation-date.md) preparation-date rules, and existing failure, backup and no-retry controls

## Decision and purpose

Run Hermes, DataAudit, Delphi and Athena from their latest successfully built existing Release outputs every
calendar day at 06:30 Toronto local time. The immediate problem is selecting binaries without touching
the running collector, engine or UI. The parent problem is unnecessary nightly copying and compilation.
The root goal is dependable daily preparation without another full binary/source copy on disk each night.

The operator explicitly rejected nightly rebuilding and authorized today's already compiled outputs
in place. The runner must not restore, compile, publish, copy application/source files, fall back to an
older output, or infer that current source is the code being executed. This replaces the proposed
release-publishing machinery in the [earlier design](../reviews/nightly-precompiled-releases-20261101.md).
Further Delphi extraction is not a prerequisite for this workflow.

## Selected outputs and identity

`Operations/nightly-runner.json` schema 4 selects ExistingOutputs mode. The reviewed
`Operations/nightly-outputs-20261101.json` manifest supplies the initial outputs until a newer successful
Release build registers for each application. The initial directories are:

```text
C:\src\TraderVI\artifacts\athena-extraction-20261101\build\bin\Hermes\release
C:\src\TraderVI\artifacts\athena-extraction-20261101\build\bin\DataAudit\release
C:\src\TraderVI\artifacts\athena-extraction-20261101\build\bin\Delphi\release
C:\src\TraderVI\artifacts\athena-extraction-20261101\build\bin\Athena\release
```

These are now active selected runtime directories despite the historical validation-folder name.
Preserve selected directories during cleanup and do not rebuild into them while a nightly run is active. The normal project Release
directories contain September binaries; they are not selected. No binary was moved, copied or rebuilt
to install this change. The collector, engine and WPF retain their separate existing installed files.

The manifest records a SHA-256 digest of every relative filename/content hash in each output directory,
with ordinal sorting so PowerShell 5.1 and 7 agree. Required DLL/dependency/runtime manifests must exist.
All four outputs are verified before the first stage. Files are held with read-only sharing throughout
the run, preventing overwrite/deletion while remaining readable by the applications. A directory hash
is also checked immediately before each stage. Missing, changed, linked or inaccessible outputs fail
preparation; there is no repair or build fallback.

These binaries identify compilation on `d479c7ca` with a Dirty working tree; the reviewed extraction was
subsequently committed as `0326771d`. Both facts are recorded. The runner passes the selected build's
commit/working-tree provenance to the applications, never the checkout's current HEAD. Later source
edits alone do not alter this selection.

### November 1 refinement: automatic selection after successful Release builds

The operator rejected requiring a manual paths/hashes update for future builds. This supersedes the
initial manual-selection rule: each of the four CLI projects imports
`Operations/NightlyReleaseRegistration.targets`. After that application's successful Windows Release
Build target, the hook records its actual output path, directory fingerprint and compiled revision.
It accepts framework-dependent or win-x64 outputs, excluding Debug, design-time and other-platform builds.
It does not build another project, run tests or apps, copy binaries, publish, or touch installed hosts.
A successful application build is eligible independently of other projects or later test/solution outcomes.
A build of only Core or WPF does not update an unbuilt nightly app.

`artifacts/nightly-output-registry/<App>.json` holds one atomically replaced receipt per application,
with one reusable metadata lock per application; receipts do not accumulate. Both ordinary Release
folders and custom output folders within this checkout work without changing configuration. An output
in another checkout does not register here. The next nightly run reads each application's receipt once,
then locks and verifies those exact files. New registrations during a run apply to the following run.
Until an app first registers, it uses the initial November 1 manifest. An invalid receipt or changed/missing
registered output fails preparation; it does not silently fall back to the seed or older September files.

Each app's source commit comes from its compiled informational version, not current Git HEAD. Automatic
receipts conservatively record working-tree state as Unknown, because a post-build checkout check cannot
prove which uncommitted source bytes were compiled. Directory hashes identify the actual executed bytes.
Per-app provenance is passed to each stage and saved in status; mixed revisions are not attributed to one
common commit. Build-hook failure fails that build rather than claiming registration succeeded.

Reusing files in place cannot preserve an earlier successful build after a later failed build overwrites
that same folder. The recorded hash detects this and nightly stops; a subsequent successful build repairs
the selection. During nightly execution, read locks reject writes to selected files. No rollback copies
or automatic repair/rebuild are introduced. Cleanup must preserve the currently selected output folders.

## Schedule and unchanged operational scope

Use a floating local 06:30 weekly trigger with all seven days selected, preserving Toronto wall-clock
time across daylight-saving changes. Registration chooses a future occurrence; it does not launch a
missed run immediately. Updating an existing task requires the reviewed action and an idle task, and
preserves the installed account, action and settings. WakeToRun, StartWhenAvailable, interactive login,
single-instance behavior and the existing time limits remain. 06:30 is a start time, not a guarantee
of completion before market open; a missed wake can still run later under StartWhenAvailable.

The stage order remains Hermes → DataAudit → Delphi → Athena. Preparation-date duplicate suppression,
explicit Force handling, stage timeouts, exit-code/audit classifications, backup requirements and no
automatic operational retry remain. Only per-run logs/status/audit reports are written by the runner.
Status schema 4 records `outputs` verification rather than claiming a build occurred. Historical status
and logs are retained. No broker order, model training, migration or live-host replacement is authorized.

## Validation and remaining work

`Operations/Test-TraderVINightly.ps1` checks inert synthetic outputs, automatic per-app registration,
frozen run selection, provenance, read locks, local-time scheduling and the existing stage/report behavior.
An SDK-free MSBuild fixture verifies success/failure/Debug/design-time/platform hook conditions without
compiling any TraderVI application. Real `-ValidateOnly` reads and fingerprints
the selected outputs without starting programs or writing operational status. The old BuildOnly option
and source-copy/build implementation are removed; changing source cannot silently re-enable them.

Installation evidence and protected-host checks are in [project status](../project-status.md), with
private records under `artifacts/nightly-existing-outputs-20261101/`. The first scheduled run is not
claimed successful until observed. This runner change performed no cleanup. The subsequent separately
authorized removal of retired nightly copies is recorded in
[project status](../project-status.md#november-1--retired-nightly-copies-removed). Other artifact cleanup,
log/cache retention, single-file packaging and a dashboard Run now action remain separate decisions.
