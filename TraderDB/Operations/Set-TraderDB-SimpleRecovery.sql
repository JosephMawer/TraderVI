/*
Purpose: Change TraderDB from FULL to SIMPLE recovery after a recent verified
         checksum full backup exists.
Effect: Changes recovery behavior and removes point-in-time/log-backup recovery.
Safety: Refuses to run unless a healthy checksum full backup completed during
        the previous 24 hours, its local file still exists, and checksum
        verification succeeds again. It does not shrink database or log files.
*/

USE [master];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_ID(N'TraderDB') IS NULL
    THROW 51010, 'TraderDB does not exist on this SQL Server instance.', 1;

DECLARE @backupFile nvarchar(4000);

SELECT TOP (1)
    @backupFile = bmf.physical_device_name
FROM msdb.dbo.backupset AS bs
INNER JOIN msdb.dbo.backupmediafamily AS bmf
    ON bmf.media_set_id = bs.media_set_id
WHERE bs.database_name = N'TraderDB'
  AND bs.[type] = N'D'
  AND bs.backup_finish_date >= DATEADD(hour, -24, SYSDATETIME())
  AND bs.has_backup_checksums = 1
  AND bs.is_damaged = 0
  AND bmf.device_type = 2
ORDER BY bs.backup_finish_date DESC;

IF @backupFile IS NULL
    THROW 51011, 'A healthy checksum full backup completed within 24 hours is required before changing recovery model.', 1;

DECLARE @fileExists int;
EXEC master.dbo.xp_fileexist @backupFile, @fileExists OUTPUT;

IF @fileExists <> 1
    THROW 51012, 'The recent full-backup file is no longer present in the local backup staging directory.', 1;

RESTORE VERIFYONLY
    FROM DISK = @backupFile
    WITH CHECKSUM;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name = N'TraderDB'
      AND recovery_model_desc <> N'SIMPLE'
)
BEGIN
    ALTER DATABASE [TraderDB] SET RECOVERY SIMPLE;
    CHECKPOINT;
END;

SELECT name, recovery_model_desc, log_reuse_wait_desc
FROM sys.databases
WHERE name = N'TraderDB';
GO
