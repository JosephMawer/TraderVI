/*
Purpose: Add a loginless, dbo-SELECT-only diagnostic principal for the fixed Codex helper.
Preconditions: verified fresh backup; principal absent; TRUSTWORTHY/chaining disabled.
Data effect: security metadata only; no application rows, logins, passwords or roles change.
Recovery: remove the helper approval first; DROP USER [TraderVIReadOnlyChecks] only after review.
The helper uses EXECUTE AS USER WITH NO REVERT on a non-pooled connection before untrusted SQL.
*/
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF USER_ID(N'TraderVIReadOnlyChecks') IS NOT NULL
    THROW 51000, 'Diagnostic user already exists; do not replay this migration.', 1;
IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id=DB_ID()
    AND (is_trustworthy_on=1 OR is_db_chaining_on=1))
    THROW 51001, 'Unexpected database trust configuration.', 1;
IF NOT EXISTS (SELECT 1 FROM msdb.dbo.backupset WHERE database_name=N'TraderDB'
    AND type='D' AND has_backup_checksums=1 AND is_damaged=0
    AND backup_finish_date >= DATEADD(minute,-30,GETDATE()))
    THROW 51002, 'A fresh verified full backup is required before this security migration.', 1;
BEGIN TRANSACTION;
CREATE USER [TraderVIReadOnlyChecks] WITHOUT LOGIN;
GRANT CONNECT TO [TraderVIReadOnlyChecks];
GRANT SELECT ON SCHEMA::[dbo] TO [TraderVIReadOnlyChecks];
DENY INSERT, UPDATE, DELETE, EXECUTE, ALTER, TAKE OWNERSHIP,
    CREATE TABLE, CREATE VIEW, CREATE PROCEDURE, CREATE FUNCTION, CREATE SCHEMA,
    CREATE SYNONYM, CREATE ASSEMBLY, BACKUP DATABASE, BACKUP LOG
    TO [TraderVIReadOnlyChecks];
DENY IMPERSONATE ON USER::[dbo] TO [TraderVIReadOnlyChecks];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals
    WHERE name=N'TraderVIReadOnlyChecks' AND type='S' AND authentication_type=0)
    THROW 51003, 'Diagnostic user postcondition failed.', 1;
IF (SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id=USER_ID(N'TraderVIReadOnlyChecks')
    AND state='G') <> 2
    THROW 51004, 'Unexpected diagnostic grants.', 1;
COMMIT TRANSACTION;
SELECT N'ReadOnlyDiagnosticUserInstalled' AS Result;
GO
