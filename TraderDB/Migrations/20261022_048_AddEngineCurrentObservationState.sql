-- ADR-0103. Add a compact, revision-bound collection membership projection.
-- No history is copied or removed by this script. Existing portfolios require explicit one-time qualification.
-- Stop all engine/Live writers; review and authorize this installation after a fresh verified checksum backup.
-- Rollback: retain this additive table and original immutable ledger history. Do not use an unqualified projection.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 54420,'A fresh checksum full backup of TraderDB is required.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.EngineInputPublication',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLivePortfolioLedger',N'U') IS NULL
 OR OBJECT_ID(N'dbo.DelphiLiveSnapshotContent',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLiveSnapshotItem',N'U') IS NULL
 THROW 54421,'Shared inputs and compact portfolio schema are required.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 54422,'Stop all Live hosts before installation.',1;
IF OBJECT_ID(N'dbo.EngineCurrentState') IS NOT NULL
 THROW 54423,'Unexpected existing current-state table; inspect instead of replaying this migration.',1;
GO
CREATE TABLE [dbo].[EngineCurrentState]
(
    [Kind] NVARCHAR(32) NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NOT NULL,
    [Revision] BIGINT NOT NULL,
    [SourceHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [StateHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [StateJson] NVARCHAR(MAX) NOT NULL,
    [UpdatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineCurrentState_Updated] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EngineCurrentState] PRIMARY KEY ([Kind],[OwnerId]),
    CONSTRAINT [CK_EngineCurrentState_Revision] CHECK ([Revision]>=0),
    CONSTRAINT [CK_EngineCurrentState_Json] CHECK (ISJSON([StateJson])=1),
    CONSTRAINT [CK_EngineCurrentState_Hashes] CHECK (LEN([SourceHash])=64 AND LEN([StateHash])=64
        AND [SourceHash] NOT LIKE '%[^0-9A-F]%' AND [StateHash] NOT LIKE '%[^0-9A-F]%')
);
GO

IF OBJECT_ID(N'dbo.EngineCurrentState',N'U') IS NULL OR COL_LENGTH(N'dbo.EngineCurrentState',N'StateJson')<>-1
 THROW 54424,'Current-state projection postcondition failed.',1;
COMMIT TRANSACTION;
GO
