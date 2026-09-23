:ON ERROR EXIT
-- ADR-0082 follow-up: identical immutable-history checks run before replacing large JSON.
-- Requires the approved conversion window, stopped Live hosts, and a fresh checksum backup.
-- 037 is immutable and remains installed. No portfolio, revision or event rows change here.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF DB_NAME()<>N'TraderDB' THROW 53800,'Run this migration only in TraderDB.',1;
IF OBJECT_ID(N'dbo.TR_DelphiLivePortfolioRevision_CompactStorage',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.DelphiLiveSnapshotContent',N'U') IS NULL
 OR OBJECT_ID(N'dbo.DelphiLiveSnapshotItem',N'U') IS NULL
 THROW 53801,'Install migration 037 first.',1;
IF OBJECT_ID(N'dbo.TR_DelphiLivePortfolioRevision_CompactUpdate',N'TR') IS NOT NULL
 THROW 53802,'Migration 038 is already installed; do not replay.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 53803,'Close all Live hosts before migration.',1;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 53804,'A fresh checksum full backup is required.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
IF @lock<0 THROW 53805,'Another storage writer prevents migration.',1;
GO
ALTER TRIGGER [dbo].[TR_DelphiLivePortfolioRevision_CompactStorage] ON [dbo].[DelphiLivePortfolioRevision]
AFTER INSERT, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted WHERE
        ISNULL(JSON_VALUE(SnapshotJson,'$."$storage".version'),N'')<>N'2'
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId')) IS NULL
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId'))<>PortfolioId
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision')) IS NULL
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision'))<>Revision)
        THROW 53707, 'New Live revisions require compact snapshot storage (ADR-0082).', 1;
    -- The reviewed maintenance tool may replace legacy JSON only; identity/time/fences never change.
    IF EXISTS(SELECT PortfolioId,Revision,LeaseId,LeaseFencingToken,SettingsAssignmentId,PersistedUtc FROM deleted
        EXCEPT SELECT PortfolioId,Revision,LeaseId,LeaseFencingToken,SettingsAssignmentId,PersistedUtc FROM inserted)
        OR EXISTS(SELECT 1 FROM deleted WHERE JSON_VALUE(SnapshotJson,'$."$storage".version') IS NOT NULL)
        THROW 53708, 'Existing compact revisions and their provenance are immutable.', 1;
END;
GO

CREATE TRIGGER [dbo].[TR_DelphiLivePortfolioRevision_CompactUpdate] ON [dbo].[DelphiLivePortfolioRevision]
INSTEAD OF UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted WHERE
        ISNULL(JSON_VALUE(SnapshotJson,'$."$storage".version'),N'')<>N'2'
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId')) IS NULL
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId'))<>PortfolioId
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision')) IS NULL
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision'))<>Revision)
        THROW 53707, 'New Live revisions require compact snapshot storage (ADR-0082).', 1;
    -- Validate before replacing the off-row LOB. Reading deleted JSON after a large-to-large
    -- update incurs repeated version-store LOB reads on SQL Server 2019.
    IF EXISTS(SELECT PortfolioId,Revision,LeaseId,LeaseFencingToken,SettingsAssignmentId,PersistedUtc FROM deleted
        EXCEPT SELECT PortfolioId,Revision,LeaseId,LeaseFencingToken,SettingsAssignmentId,PersistedUtc FROM inserted)
        OR EXISTS(SELECT 1 FROM deleted WHERE JSON_VALUE(SnapshotJson,'$."$storage".version') IS NOT NULL)
        THROW 53708, 'Existing compact revisions and their provenance are immutable.', 1;
    UPDATE r SET SnapshotJson=i.SnapshotJson
    FROM dbo.DelphiLivePortfolioRevision r
    JOIN inserted i ON i.PortfolioId=r.PortfolioId AND i.Revision=r.Revision;
END;
GO

COMMIT TRANSACTION;
GO

