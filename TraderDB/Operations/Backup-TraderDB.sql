/*
Purpose: Create a uniquely named full TraderDB backup in the narrow shared
         staging directory and verify the completed file with checksums.
Data effect: Reads all database pages and writes a new .bak file plus msdb
             backup-history rows. It does not modify TraderDB application rows.
Safety: Never overwrites an existing backup file and never deletes old backups.

After this succeeds, copy the reported VerifiedBackupFile to the approved
off-machine location. See Docs/database-operations.md.
*/

USE [master];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_ID(N'TraderDB') IS NULL
    THROW 51000, 'TraderDB does not exist on this SQL Server instance.', 1;

IF CONVERT(nvarchar(60), DATABASEPROPERTYEX(N'TraderDB', N'Status')) <> N'ONLINE'
    THROW 51001, 'TraderDB must be ONLINE before it can be backed up.', 1;

DECLARE @backupDirectory nvarchar(4000) =
    N'C:\ProgramData\TraderVI\Backups';

IF NULLIF(LTRIM(RTRIM(@backupDirectory)), N'') IS NULL
    THROW 51002, 'The configured backup staging directory is empty.', 1;

IF RIGHT(@backupDirectory, 1) NOT IN (N'\', N'/')
    SET @backupDirectory += N'\';

DECLARE @now datetime2(3) = SYSDATETIME();
DECLARE @timestamp varchar(19) =
    CONVERT(char(8), @now, 112) + '_' +
    REPLACE(CONVERT(char(8), @now, 108), ':', '') + '_' +
    RIGHT('000' + CONVERT(varchar(3), DATEPART(millisecond, @now)), 3);

DECLARE @backupFile nvarchar(4000) =
    @backupDirectory + N'TraderDB_FULL_' + @timestamp + N'.bak';

DECLARE @fileExists int;
EXEC master.dbo.xp_fileexist @backupFile, @fileExists OUTPUT;

IF @fileExists = 1
    THROW 51003, 'The generated backup filename already exists; no file was overwritten.', 1;

BACKUP DATABASE [TraderDB]
    TO DISK = @backupFile
    WITH COMPRESSION,
         CHECKSUM,
         STATS = 10;

RESTORE VERIFYONLY
    FROM DISK = @backupFile
    WITH CHECKSUM;

SELECT
    @backupFile AS VerifiedBackupFile,
    bs.backup_finish_date AS BackupFinishedAt,
    CAST(bs.backup_size / 1048576.0 AS decimal(18, 2)) AS UncompressedMB,
    CAST(bs.compressed_backup_size / 1048576.0 AS decimal(18, 2)) AS CompressedMB,
    bs.has_backup_checksums AS HasBackupChecksums,
    bs.is_damaged AS IsDamaged
FROM msdb.dbo.backupset AS bs
INNER JOIN msdb.dbo.backupmediafamily AS bmf
    ON bmf.media_set_id = bs.media_set_id
WHERE bs.database_name = N'TraderDB'
  AND bs.[type] = N'D'
  AND bmf.physical_device_name = @backupFile;
GO
