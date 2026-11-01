# ADR-0122: Reuse existing nightly outputs at 06:30

- **Status:** Accepted; runner validated and Windows task updated; first scheduled execution pending
- **Date:** 2026-11-01
- **Domains:** architecture, data-pipeline
- **Supersedes:** [ADR-0078](0078-isolated-latest-source-nightly-builds.md) source capture/restore/build preparation, and the 00:30 clock in [ADR-0046](0046-guarded-nightly-operations-runner.md) / [ADR-0058](0058-daily-ingestion-and-observed-benchmark-confirmation.md)
- **Preserves:** [ADR-0113](0113-nightly-data-audit-report.md) stage/report contract, [ADR-0114](0114-explicit-nightly-preparation-date.md) preparation-date rules, and existing failure, backup and no-retry controls

## Decision and purpose

Run Hermes, DataAudit, Delphi and Athena from explicitly selected existing Release outputs every
calendar day at 06:30 Toronto local time. The immediate problem is selecting binaries without touching
the running collector, engine or UI. The parent problem is unnecessary nightly copying and compilation.
The root goal is dependable daily preparation without another full binary/source copy on disk each night.

The operator explicitly rejected nightly rebuilding and authorized today's already compiled outputs
in place. The runner must not restore, compile, publish, copy application/source files, fall back to an
older output, or infer that current source is the code being executed. This replaces the proposed
release-publishing machinery in the [earlier design](../reviews/nightly-precompiled-releases-20261101.md).
Further Delphi extraction is not a prerequisite for this workflow.

## Selected outputs and identity

`Operations/nightly-runner.json` schema 4 selects ExistingOutputs mode and the reviewed
`Operations/nightly-outputs-20261101.json` manifest. Each application runs from its existing directory:

```text
C:\src\TraderVI\artifacts\athena-extraction-20261101\build\bin\Hermes\release
C:\src\TraderVI\artifacts\athena-extraction-20261101\build\bin\DataAudit\release
C:\src\TraderVI\artifacts\athena-extraction-20261101\build\bin\Delphi\release
C:\src\TraderVI\artifacts\athena-extraction-20261101\build\bin\Athena\release
```

These are now active selected runtime directories despite the historical validation-folder name.
Preserve them during cleanup and do not rebuild into them while selected. The normal project Release
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
edits do not alter this selection. A future application update needs a deliberate reviewed change to
the selected paths/hashes/provenance after its build completes; automatic promotion is not installed.

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

`Operations/Test-TraderVINightly.ps1` checks inert synthetic outputs, pinning, provenance, read locks,
local-time scheduling and the existing stage/report behavior. Real `-ValidateOnly` reads and fingerprints
the selected outputs without starting programs or writing operational status. The old BuildOnly option
and source-copy/build implementation are removed; changing source cannot silently re-enable them.

Installation evidence and protected-host checks are in [project status](../project-status.md), with
private records under `artifacts/nightly-existing-outputs-20261101/`. The first scheduled run is not
claimed successful until observed. Old artifact cleanup, log/cache retention, single-file packaging,
and a dashboard Run now action remain separate decisions; this change performs no cleanup.
