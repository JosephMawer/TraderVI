# ADR-0018 — Manual database migrations and SIMPLE-recovery backups

- **Status:** Accepted
- **Date:** 2026-08-21
- **Domains:** architecture, data-pipeline, risk-management

## Context

TraderDB contains historical market data and advisory/trading records that have no established backup mechanism. A read-only audit found SQL Server 2019 Developer RTM, `TraderDB` in FULL recovery, no full/differential/log backup history, no recorded successful `DBCC CHECKDB`, and SQL Server Agent stopped.

The SSDT project targeted SQL Server 2025 while the deployed engine was SQL Server 2019. A previously generated broad deployment plan included unrelated rebuilds, drops, and database-option changes. The intended development workflow is smaller: when one schema object changes, review and execute one narrow script manually while retaining the database project as source-controlled schema history.

## Decision

Use manual, additive-by-default migrations and full-backup checkpoints:

- Treat `TraderDB.sqlproj` as a current-schema reference and build-validation artifact, not a deployment mechanism.
- Target the currently deployed SQL Server 2019 provider (`Sql150`) until a separately validated server upgrade is complete.
- Disable project deployment and block the SSDT Deploy targets.
- Store each new applied schema change as a dated immutable script under `TraderDB/Migrations`, while updating the canonical definition under `TraderDB/dbo`.
- Prohibit unapproved destructive or data-replacement migrations. Require explicit preconditions, transactions where supported, postcondition verification, and a fresh backup before any authorized destructive change.
- Use SIMPLE recovery with a checksum/compressed full backup after each successful Hermes run and before manual schema changes.
- Write backups first to the narrow local staging directory `C:\ProgramData\TraderVI\Backups`, verify them, and only then copy completed files to OneDrive or another approved off-machine location.
- Invoke the routine backup from Hermes itself after its data-update stages return successfully, so the protection is independent of whether Visual Studio or a terminal launches Hermes. A backup failure must be visible through exit code `2` without undoing or concealing the completed data update.
- Publish the OneDrive filename only after a temporary copy has the same SHA-256 as the verified staging file. Preserve the staging generation if copying fails.
- Never overwrite a backup generation or store database backup files in Git.

## Alternatives considered

- **DACPAC publishing with conservative properties.** Rejected for the current phase because unresolved schema drift makes broad state reconciliation harder to reason about than one reviewed migration.
- **FULL recovery with scheduled log backups.** Rejected for now because the desired operational checkpoint is the Hermes import and the project accepts recovery only to the latest full backup in exchange for a simpler, observable process.
- **Only post-Hermes backups, including before schema changes only when convenient.** Rejected because a manual migration is a separate risk boundary and must have a recent known-good recovery point.
- **Write SQL backups directly to OneDrive.** Rejected because SQL Server and OneDrive run under different accounts and a sync client can observe a partially written file.
- **Store backups in Git or Git LFS.** Rejected because backups are private, changing binary artifacts and source-control history is not a retention system.

## Consequences

**Easier:**

- Every live schema change is narrow, reviewable, and preserved in execution order.
- The SQL project builds against the deployed engine instead of advertising unsupported capabilities.
- Backup creation follows every normal successful Hermes launch path without relying on operator memory or IDE configuration.
- Accidental project-wide deletion or rebuild is blocked from the normal workflow.

**Harder:**

- The database cannot be restored to a point after the latest full backup.
- Delphi, Hercules, TraderVI, or Oracle writes after the newest backup remain exposed until the next full backup.
- Schema drift must be reconciled deliberately instead of automatically.
- OneDrive retention and periodic restore drills require operational discipline.

**Would tell us this was wrong:**

- Losing post-backup advisory or trading records becomes unacceptable.
- Backup size or duration makes a full backup after Hermes impractical.
- The number or complexity of migrations makes manual application unreliable.
- A restore drill cannot meet the desired recovery time.

Any of these should trigger reconsideration of FULL recovery with log backups, automated migration tooling, or a dedicated backup platform.

## Review questions

1. Why is a successful DACPAC build useful even though DACPAC deployment is disabled?
2. What data-loss window does SIMPLE recovery create in TraderVI's chosen backup schedule?
3. Why is a fresh backup required before a schema migration even when routine backups follow Hermes?
4. Why is OneDrive the copy destination rather than SQL Server's direct backup target?
