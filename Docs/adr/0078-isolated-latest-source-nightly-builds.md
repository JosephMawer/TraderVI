# ADR-0078: Isolated latest-source nightly builds

- **Status:** Accepted; implemented and build-validated
- **Date:** 2026-09-14
- **Domains:** architecture, data-pipeline
- **Supersedes:** ADR-0046's shared build directory, no-restore and source-stability-during-compilation rules

## Decision and purpose

The immediate problem is repeated unattended preparation failures. The parent problem is reliable
overnight ingestion and publication. The root goal remains attributable daily recommendations from
current code and complete market inputs. The operator explicitly requires the latest changes, including
uncommitted work, and authorized this implementation, the outstanding commits and a recovery run.

Each attempt captures tracked and untracked non-ignored source, including deletions, into its own
`artifacts/nightly/<run-id>/source-<attempt>` directory. Hashes of the original source before and after
copying must agree with the copied files. Commit identity and working-tree state must also remain stable
during capture. Three capture attempts, ten seconds apart, are allowed. Links are rejected. Once captured,
later editor changes belong to the next run and cannot invalidate this build. No older build is substituted.

NuGet lock files for Core, Hermes, Delphi and Athena are reviewed source. Every nightly restore uses
`--locked-mode`, the explicit NuGet.org configuration, a dedicated package cache and the same resolved
dotnet executable. Missing lock files, changed dependency declarations and package-integrity failures
stop preparation. Transient download failures may retry three times, ten seconds apart, with five minutes
per restore attempt and a thirty-minute preparation budget checked before each process. Source-copy work
and process termination can add bounded overhead to that budget. Compilation errors are not retried.

The copied projects own their intermediate and executable outputs. Root MSBuild import boundaries prevent
searching parent development directories when those imports are absent from the snapshot; a generated
global.json pins the detected SDK when source has none. Generated boundary hashes, SDK identity, restore
attempts, source manifest and executable hashes are retained. The runner verifies copied source after
building and checks each executable directory before its stage starts. Stages run with the captured
working directory and the existing explicit commit/working-tree provenance environment variables.

Hermes, Delphi and Athena still execute once in that order. Their existing failure, attention, backup,
same-date suppression and exclusive-lock rules remain. Preparation retries do not retry any operational
stage. Timed-out processes are terminated with their child processes before proceeding; failure to stop
them fails preparation. Builds and restores create no official recommendations.

## Validation and operations

`Test-TraderVINightly.ps1` runs synthetic Git fixtures and mocked processes without market calls or SQL.
It exercises source edits/deletions, changing captures, bounded retries, dependency errors, copy/artifact
tampering, isolated builds and captured stage provenance. `-BuildOnly` performs actual restore/build
validation but requires a separate status directory under repository artifacts. It records `Prepared`,
never a successful operational run, and cannot suppress the real schedule.

Nightly restore disables the advisory-network audit; dependency-security review remains separate from
compiler and pipeline health. Updating a package requires regenerating and reviewing affected lock files.
Runtime model selection, market-data readiness, gates and thresholds are unchanged. Captured source,
builds and package caches are ignored by Git; cleanup remains manual pending a retention policy.

## Review questions

1. Which moment defines the latest source, and why can later edits continue safely?
2. Why can preparation retry while official Delphi publication cannot automatically retry?
3. How do locked restore and separate output directories address different failure causes?
