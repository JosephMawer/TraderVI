# TraderDB backup and schema-change operations

This is the operational authority for protecting and changing the local `TraderDB` database. The design decision is recorded in ADR-0018.

## Chosen operating model

- `TraderDB.sqlproj` is a source-controlled current-schema reference and SSDT build target, not a deployment package.
- Schema changes are applied manually from one dated script under `TraderDB/Migrations`.
- The database uses SIMPLE recovery once the initial verified backup has been created.
- A new full backup is created after every successful Hermes run and immediately before a manual schema migration.
- Backup files use unique timestamps and are never overwritten.
- Completed backups are verified locally before they are copied off-machine.

SIMPLE recovery means SQL Server automatically reclaims inactive transaction-log space, but work performed after the newest full backup cannot be recovered from backup. Hermes is the routine backup boundary; Delphi, Hercules, TraderVI, and Oracle writes made afterward are protected by the next full backup.

## Initial activation checklist

The 2026-08-21 audit found no recorded TraderDB backup and no recorded successful `DBCC CHECKDB`. Complete these steps in order:

1. Execute `TraderDB/Operations/Backup-TraderDB.sql` manually.
2. Confirm the result reports `HasBackupChecksums = 1` and `IsDamaged = 0`.
3. Copy the completed `.bak` file to the approved off-machine destination and confirm OneDrive has finished syncing it.
4. Execute `TraderDB/Operations/Check-TraderDB-Integrity.sql` and require zero reported errors.
5. Execute `TraderDB/Operations/Set-TraderDB-SimpleRecovery.sql`.
6. Re-query the database recovery model and confirm `SIMPLE`.

This initial activation completed successfully on 2026-08-22. The first accessible backup was 31.00 MB compressed, its staging and OneDrive copies had matching SHA-256 hashes, `DBCC CHECKDB` reported no errors, and the final recovery model was `SIMPLE`.

Do not delete or shrink the existing transaction log as part of this transition. File-size optimization is a separate operation requiring evidence.

## Routine post-Hermes backup

Run `Backup-TraderDB.sql` only after Hermes exits successfully. The script:

- uses the narrow shared staging directory `C:\ProgramData\TraderVI\Backups`;
- creates a unique `TraderDB_FULL_yyyyMMdd_HHmmss_fff.bak` file;
- enables backup compression and checksums;
- refuses to overwrite a file;
- runs `RESTORE VERIFYONLY WITH CHECKSUM`;
- reports the completed path and compressed size;
- performs no retention cleanup.

The first version remains manually initiated. Automating it as a post-Hermes wrapper is a later step after the destination and retention behavior have been observed successfully.

## OneDrive copy and retention

Do not configure SQL Server to write directly into a OneDrive user folder. SQL Server runs under a service account, and OneDrive synchronizes under the interactive Windows user.

Use this flow:

1. SQL Server writes and verifies the backup in `C:\ProgramData\TraderVI\Backups`, where only the SQL Server service has added modify access.
2. The interactive user copies the completed file to `$env:OneDrive\Joseph\Tradervi\backups`.
3. Wait for OneDrive to report that synchronization is complete.
4. Keep uniquely named generations; do not replace a fixed `latest.bak` file.

Initial retention target: 30 successful full backups, subject to measured compressed size and available OneDrive quota. Cleanup is deliberately manual until the first several backup sizes are known. OneDrive is synchronized storage, so deletions also propagate; an occasional separate external-drive copy adds protection from account loss, mistaken cleanup, or ransomware.

Never place `.bak`, `.dif`, or `.trn` files in Git. They are changing binary artifacts that can contain private application data and permanently inflate repository history.

## Manual schema migration checklist

For every database change:

1. Confirm the latest post-Hermes backup is acceptable; otherwise create a fresh backup.
2. Add a dated migration using the conventions in `TraderDB/Migrations/README.md`.
3. Update the matching canonical definition under `TraderDB/dbo`.
4. Build `TraderDB.sqlproj` with Visual Studio MSBuild and SSDT.
5. Review the exact migration script, especially data effects and rollback/recovery.
6. Obtain explicit authorization for that script.
7. Execute it manually against `TraderDB`.
8. Verify schema postconditions and row preservation.

Direct DACPAC publish, Visual Studio Publish, MSBuild Deploy, and schema-compare update are outside this workflow and intentionally blocked/documented as unsupported.

## Destructive-change rule

Additive changes are the default. A script must not delete, truncate, drop, narrow, replace, or rebuild stored data merely to make the deployed schema resemble the project.

If removal is eventually necessary:

- deprecate first and delete in a later migration;
- preserve or export the affected data;
- record row counts and required invariants;
- create and verify a fresh backup;
- review a specific rollback/recovery plan;
- obtain explicit authorization for the destructive operation.

## Restore testing

`RESTORE VERIFYONLY` establishes that the backup is complete and readable; it does not prove that the application works against a restored database. Periodically restore a selected backup as a differently named temporary database, validate expected tables and representative counts, then remove that temporary database only with explicit authorization. Never test by restoring over live `TraderDB`.

## SQL Server upgrade boundary

Do not combine routine schema work with a major SQL Server upgrade. After backups and a restore drill are established, plan the SQL Server 2025 move as a separate side-by-side migration: restore into the new instance, validate TraderVI, and retain the SQL Server 2019 instance until acceptance.
