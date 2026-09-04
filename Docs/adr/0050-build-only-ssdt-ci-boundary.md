# ADR-0050: Build-only SSDT CI boundary

- **Status:** Accepted
- **Date:** 2026-09-03
- **Domains:** architecture, data-pipeline, risk-management
- **Related:** ADR-0018, ADR-0048, ADR-0049

## Context

The immediate problem is that basic Windows CI validates Core tests and focused .NET application builds but
does not compile `TraderDB.sqlproj`. After ADR-0049 made the SSDT project a trustworthy representation of
runtime write contracts, omitting it from CI leaves schema syntax and relationship regressions undetected.

The parent problem is keeping application and database contracts dependable without turning CI into a
deployment system. The root goal is protecting advisory evidence and Real-trade records while retaining the
manual, reviewed migration boundary established by ADR-0018.

The original-style SQL project requires Visual Studio MSBuild and SSDT; `dotnet build` cannot validate it.
[GitHub's documented `windows-2025-vs2026` image](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-VS2026-Readme.md)
includes Visual Studio 2026, SSDT, and the SQL Server tooling required by the project's `Sql150` provider.

## Decision

1. Add a separate `database-project-build` job to the existing Windows GitHub Actions workflow. Keep it
   independent from Core tests and application builds so failures identify the affected boundary directly.
2. Pin the job to `windows-2025-vs2026`, matching the Visual Studio 18/SSDT generation used for local
   validation. Verify the expected MSBuild executable and SSDT targets before building; fail clearly if the
   runner contract changes.
3. Invoke only the `Build` target for `TraderDB/TraderDB.sqlproj` in Release/AnyCPU and explicitly set
   `DeployOnBuild=False`.
4. Give the workflow read-only repository permission and no database credentials. Do not start SQL Server,
   execute migrations, call a publish/deploy target, run schema comparison, or contact the live database.
5. Do not upload the generated DACPAC. It is an ephemeral validation output, not an approved deployment
   artifact. The project's existing `BlockTraderDbDeployment` target remains defense in depth.
6. Treat runner-image or SSDT removal as a visible CI failure requiring a reviewed workflow update. Do not
   silently skip the database build or replace it with a broad deployment-capable toolchain.

## Alternatives considered

- **Keep SSDT outside CI.** Rejected because canonical schema regressions would remain dependent on a manual
  local build.
- **Build the complete solution in the application job.** Rejected because it couples unrelated failures and
  obscures the special Visual Studio/SSDT dependency.
- **Convert immediately to an SDK-style SQL project.** Deferred because that is a larger project-format and
  output-compatibility change than this validation boundary requires.
- **Upload the DACPAC as an artifact.** Rejected because no automated consumer is authorized and publishing
  a deployable package would blur the manual migration boundary.

## Consequences

**Easier:**

- Every push and pull request validates the canonical database schema as its own status.
- Missing SSDT tooling fails explicitly instead of producing a misleading green application build.
- CI remains unable to alter TraderDB or execute a migration.

**Harder:**

- The job depends on a pinned GitHub-hosted image and must be deliberately updated when that image changes.
- CI produces an ephemeral DACPAC during compilation even though it never uploads or deploys it.

**Would tell us this was wrong:**

- The pinned image becomes unreliable or unavailable often enough to obscure real schema failures, or an
  SDK-style project proves equivalent and materially simpler under measured CI use.

## Review questions

1. Why is the database project a separate job instead of another step in the .NET job?
2. What prevents this job from deploying its generated DACPAC?
3. Why is the runner pinned rather than using `windows-latest`?
