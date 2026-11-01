# Nightly precompiled releases — design draft, November 1, 2026

The operator accepted precompiled releases for Hermes, DataAudit, Delphi and Athena during the
disk-space review. Release preparation, activation, scheduling, a dashboard action and retention
details remain proposals for discussion. This draft records that boundary. It is not an accepted
replacement ADR or authorization to install a release, change the scheduled task, run the pipeline,
delete files, or change SQL. The installed runner still follows ADR-0078.

## Problem and intended outcome

The immediate decision is how a completed application change reaches the next nightly run. The
parent problem is repeated compilation and retained output consuming disk space. The root outcome
is dependable daily preparation with bounded storage and little routine operator work.

The November 1 inventory measured 46.67 GiB allocated under `artifacts/nightly`. The October 31
run folder contained 1.334 GiB of file contents, including 1.321 GiB in build directories. The
reviewed older-library subset contained 10,684 files in 34 run directories, totaling 31.834 GiB
allocated. All selected files remained present during this discussion. The private inventory is
`artifacts/nightly-library-cleanup-20261101/storage-inventory.txt`.

## Current behavior

The scheduled task starts the repository PowerShell runner daily at 00:30 Toronto. The runner
captures source, restores locked dependencies and compiles four applications into a fresh run
directory. The captured source may include uncommitted changes. It then runs Hermes, DataAudit,
Delphi and Athena in order, checks executable identities and saves logs and status.

Source isolation was accepted to allow edits during preparation. Current task settings support
waking and a missed start while the Windows user remains signed in. The WPF application has a
Delphi action. A single action for the full nightly pipeline would be new.

## Decision record

- **Accepted direction.** Use precompiled releases for nightly execution. The prior requirement
  to capture and compile the latest working files each night is being reconsidered explicitly.
- **Proposed release preparation.** Completing a relevant code change includes building and
  testing a consistent package containing all four applications and their dependencies. Shared
  Core changes also count. Package completion must be explicit so ordinary in-progress build
  output cannot be mistaken for a finished release. This can be part of the development workflow.
- **Proposed activation.** Publish a fully prepared package to a dedicated release directory,
  then change the selected-release reference in one operation. The next run reads that reference
  once, records the release identity and uses that directory throughout. A new release prepared
  during a run becomes available to a subsequent run. Reuse the package across nights.
- **Proposed operator experience.** Routine changes completed through that workflow require no
  separate nightly build/copy chore. Editing files alone does not prepare a release. A developer
  working outside the workflow needs one explicit preparation/activation command. Who may activate
  a package and which checks must pass remain open.
- **Proposed retention.** Keep the selected release and one previous release for rollback, plus
  any additional release still in use. The final pruning rules and cleanup of historical source,
  libraries, logs and package caches require their own decision.
- **Open schedule.** The operator suggested 05:30 Toronto to avoid active development hours.
  That leaves four hours before the regular 09:30 open. The October 31 run took about ten minutes,
  but current maximum stage times can exceed that window. Confirm the desired completion window
  and handling of delayed runs before changing the installed task.
- **Open dashboard action.** A full-pipeline Run now button could invoke the same runner and
  selected release. Retain the exclusive run lock and preparation-date duplicate guard. Existing
  early preparation supports tomorrow after 16:00 under completed-session checks. Decide how
  the button presents the preparation date, progress, completed warnings and a reviewed retry.
- **Deferred.** A SQL table for nightly logs was a thought explicitly held for later. Other
  artifact directories and the separately active SQL cleanup remain separate review items.

## Candidate acceptance checks and next decisions

Before implementation, settle release activation and the routine development workflow, then the
schedule and whether the dashboard action belongs in the first increment. A final accepted ADR
should supersede the affected ADR-0078 source/build rules and identify any other changed contracts.

Validation should show that package preparation failure keeps the prior release selectable, a
run uses one complete release throughout, and each run records the actual executable/source
identity. The selected package needs the existing preparation-date, stage, model-assignment and
database prerequisites. Repeated nights should reuse binaries without creating another full copy.
Cleanup must respect active users of a package, including the running collector and trading engine.

Governing references are [ADR-0078](../adr/0078-isolated-latest-source-nightly-builds.md),
[ADR-0110](../adr/0110-nightly-isolated-symbol-warnings.md),
[ADR-0113](../adr/0113-nightly-data-audit-report.md),
[ADR-0114](../adr/0114-explicit-nightly-preparation-date.md), and the
[nightly operating procedure](../running.md#guarded-nightly-pipeline).
