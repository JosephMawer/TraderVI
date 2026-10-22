-- ADR-0103. Stable archive identities and version-3 paper roots. No existing history is rewritten/deleted.
-- Stop every app/engine writer and review the complete script after a fresh verified checksum backup.
-- After installation, separately authorize --prepare-working-state before starting the new engine.
-- Old binaries cannot read version-3 roots after the first new append; rollback requires a compatible reader.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 54471,'A fresh checksum full backup of TraderDB is required.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.EngineIntegrityIncident',N'U') IS NULL OR OBJECT_ID(N'dbo.EngineCurrentState',N'U') IS NULL
 THROW 54472,'Install reviewed migrations 047 through 050 first.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 54473,'Stop all Live hosts before installing working-state storage.',1;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
IF @lock<0 THROW 54474,'Another snapshot writer prevents this migration.',1;
IF OBJECT_ID(N'dbo.EnginePaperRecordIdentity') IS NOT NULL
 THROW 54475,'Unexpected existing paper identity schema; inspect instead of replaying migration.',1;
GO

CREATE TABLE [dbo].[EnginePaperRecordIdentity]
(
    [PortfolioId] UNIQUEIDENTIFIER NOT NULL,
    [Kind] NVARCHAR(32) NOT NULL,
    [RecordId] UNIQUEIDENTIFIER NOT NULL,
    [Ordinal] INT NOT NULL,
    [CreatedRevision] BIGINT NOT NULL,
    CONSTRAINT [PK_EnginePaperRecordIdentity] PRIMARY KEY ([PortfolioId],[Kind],[RecordId]),
    CONSTRAINT [UQ_EnginePaperRecordIdentity_Ordinal] UNIQUE ([PortfolioId],[Kind],[Ordinal]),
    CONSTRAINT [CK_EnginePaperRecordIdentity_Identity] CHECK ([Ordinal]>=0 AND [CreatedRevision]>=0
        AND [RecordId]<>'00000000-0000-0000-0000-000000000000'
        AND [Kind] IN(N'positions',N'actions',N'fills',N'quotes',N'marks',N'fillAction',N'buyEvaluation'))
);
GO

CREATE TRIGGER [dbo].[TR_EnginePaperRecordIdentity_Immutable] ON [dbo].[EnginePaperRecordIdentity]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54470,'Paper record identities are immutable; use a separately reviewed recovery migration.',1;
END;
GO

ALTER TRIGGER [dbo].[TR_DelphiLivePortfolioLedger_CompactStorage] ON [dbo].[DelphiLivePortfolioLedger]
AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted WHERE
        ISNULL(JSON_VALUE(SnapshotJson,'$."$storage".version'),N'') NOT IN(N'2',N'3')
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId')) IS NULL
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId'))<>PortfolioId
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision')) IS NULL
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision'))<>Revision)
        THROW 53706, 'This Live writer must be upgraded to compact snapshot storage (ADR-0082).', 1;
END;
GO

ALTER TRIGGER [dbo].[TR_DelphiLivePortfolioRevision_CompactStorage] ON [dbo].[DelphiLivePortfolioRevision]
AFTER INSERT, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted WHERE
        ISNULL(JSON_VALUE(SnapshotJson,'$."$storage".version'),N'') NOT IN(N'2',N'3')
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

ALTER TRIGGER [dbo].[TR_DelphiLivePortfolioRevision_CompactUpdate] ON [dbo].[DelphiLivePortfolioRevision]
INSTEAD OF UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted WHERE
        ISNULL(JSON_VALUE(SnapshotJson,'$."$storage".version'),N'') NOT IN(N'2',N'3')
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
