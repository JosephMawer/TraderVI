-- ADR-0082. Additive compact storage; this script does not convert or delete any history.
-- Close all WPF/Live writers first. Review and explicitly authorize installation.
-- Supply -v VerifiedBackupFile="<fresh verified staging backup absolute path>" to sqlcmd.
-- After installation use the reviewed storage-maintenance preview/conversion, then updated binaries.
-- Preserve added objects on rollback; old binaries cannot write to the new storage contract.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 53710,'A fresh checksum full backup of TraderDB is required.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
IF @lock<0 THROW 53711,'Another Live storage writer is active.',1;
IF OBJECT_ID(N'dbo.DelphiLivePortfolioLedger',N'U') IS NULL
 OR OBJECT_ID(N'dbo.DelphiLivePortfolioRevision',N'U') IS NULL
 OR OBJECT_ID(N'dbo.DelphiLiveHostLease',N'U') IS NULL
 OR COL_LENGTH(N'dbo.DelphiLivePortfolioRevision',N'SettingsAssignmentId') IS NULL
 THROW 53712,'Expected Live ledger, revision, lease and settings schema is missing.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 53713,'Stop all Live hosts and wait for lease release before installation.',1;
IF OBJECT_ID(N'dbo.DelphiLiveSnapshotContent') IS NOT NULL OR OBJECT_ID(N'dbo.DelphiLiveSnapshotItem') IS NOT NULL
 OR OBJECT_ID(N'dbo.TR_DelphiLiveSnapshotContent_Immutable') IS NOT NULL
 OR OBJECT_ID(N'dbo.TR_DelphiLiveSnapshotItem_Immutable') IS NOT NULL
 OR OBJECT_ID(N'dbo.TR_DelphiLivePortfolioLedger_CompactStorage') IS NOT NULL
 OR OBJECT_ID(N'dbo.TR_DelphiLivePortfolioRevision_CompactStorage') IS NOT NULL
 THROW 53714,'Unexpected existing compact storage objects; inspect instead of replaying this migration.',1;
GO
CREATE TABLE [dbo].[DelphiLiveSnapshotContent]
(
    [ContentHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ContentJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_DelphiLiveSnapshotContent] PRIMARY KEY ([ContentHash]),
    CONSTRAINT [CK_DelphiLiveSnapshotContent_Hash] CHECK (LEN([ContentHash])=64 AND [ContentHash] NOT LIKE '%[^0-9A-F]%'),
    CONSTRAINT [CK_DelphiLiveSnapshotContent_Json] CHECK (ISJSON(N'['+[ContentJson]+N']')=1)
);
GO

CREATE TABLE [dbo].[DelphiLiveSnapshotItem]
(
    [PortfolioId] UNIQUEIDENTIFIER NOT NULL,
    [CollectionName] NVARCHAR(32) NOT NULL,
    [Ordinal] INT NOT NULL,
    [Revision] BIGINT NOT NULL,
    [ContentHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT [PK_DelphiLiveSnapshotItem] PRIMARY KEY ([PortfolioId],[CollectionName],[Ordinal],[Revision] DESC),
    CONSTRAINT [FK_DelphiLiveSnapshotItem_Portfolio] FOREIGN KEY ([PortfolioId]) REFERENCES [dbo].[DelphiLivePortfolioLedger]([PortfolioId]),
    CONSTRAINT [FK_DelphiLiveSnapshotItem_Content] FOREIGN KEY ([ContentHash]) REFERENCES [dbo].[DelphiLiveSnapshotContent]([ContentHash]),
    CONSTRAINT [CK_DelphiLiveSnapshotItem_Identity] CHECK ([Ordinal]>=0 AND [Revision]>=0 AND [CollectionName] IN
        (N'sessionOpeningPositions',N'positions',N'actions',N'fills',N'quotes',N'marks'))
);
GO

CREATE TRIGGER [dbo].[TR_DelphiLiveSnapshotContent_Immutable] ON [dbo].[DelphiLiveSnapshotContent]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 53704, 'Snapshot content is immutable; use a separately reviewed recovery migration.', 1;
END;
GO

CREATE TRIGGER [dbo].[TR_DelphiLiveSnapshotItem_Immutable] ON [dbo].[DelphiLiveSnapshotItem]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 53705, 'Snapshot item versions are immutable; use a separately reviewed recovery migration.', 1;
END;
GO

CREATE TRIGGER [dbo].[TR_DelphiLivePortfolioLedger_CompactStorage] ON [dbo].[DelphiLivePortfolioLedger]
AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted WHERE
        ISNULL(JSON_VALUE(SnapshotJson,'$."$storage".version'),N'')<>N'2'
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId')) IS NULL
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId'))<>PortfolioId
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision')) IS NULL
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision'))<>Revision)
        THROW 53706, 'This Live writer must be upgraded to compact snapshot storage (ADR-0082).', 1;
END;
GO

CREATE TRIGGER [dbo].[TR_DelphiLivePortfolioRevision_CompactStorage] ON [dbo].[DelphiLivePortfolioRevision]
AFTER INSERT, UPDATE, DELETE AS
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

IF OBJECT_ID(N'dbo.DelphiLiveSnapshotContent',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLiveSnapshotItem',N'U') IS NULL
 OR OBJECT_ID(N'dbo.TR_DelphiLiveSnapshotContent_Immutable',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.TR_DelphiLiveSnapshotItem_Immutable',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.TR_DelphiLivePortfolioLedger_CompactStorage',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.TR_DelphiLivePortfolioRevision_CompactStorage',N'TR') IS NULL
 THROW 53715,'Compact storage schema postconditions failed.',1;
COMMIT TRANSACTION;
GO
