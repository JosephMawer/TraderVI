-- ADR-0111. PREPARED ONLY: explicit reviewed migration operation, not applied by builds/startup.
-- Stop all market writers; preserve broker/history records. Requires a verified checksum full backup.
-- All objects are additive. Rollback retains evidence; never drop these records to roll back binaries.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 54567,'A fresh checksum full backup is required for collector schema installation.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.EngineInputPublication',N'U') IS NULL OR OBJECT_ID(N'dbo.EngineMarketCycle',N'U') IS NULL
 THROW 54568,'Existing independent-engine schema is required.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 54569,'Stop all existing market writers before migration.',1;
IF OBJECT_ID(N'dbo.MarketCollectorLease') IS NOT NULL OR OBJECT_ID(N'dbo.MarketCollectorBook') IS NOT NULL
 OR OBJECT_ID(N'dbo.MarketCollectorRequest') IS NOT NULL OR OBJECT_ID(N'dbo.MarketCollectorObservation') IS NOT NULL
 OR OBJECT_ID(N'dbo.MarketCollectorConflict') IS NOT NULL OR OBJECT_ID(N'dbo.MarketCollectorAttempt') IS NOT NULL
 THROW 54570,'Collector objects already exist; inspect rather than replay migration.',1;
GO
CREATE TABLE [dbo].[MarketCollectorLease]
(
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NOT NULL,
    [FencingToken] BIGINT NOT NULL,
    [ExpiresUtc] DATETIME2 NOT NULL,
    CONSTRAINT [PK_MarketCollectorLease] PRIMARY KEY ([SourceContract]),
    CONSTRAINT [CK_MarketCollectorLease_Fence] CHECK ([FencingToken]>0)
);
GO

CREATE TABLE [dbo].[MarketCollectorAttempt]
(
    [RequestId] UNIQUEIDENTIFIER NOT NULL,
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] DATETIME2 NOT NULL,
    [SessionCloseUtc] DATETIME2 NOT NULL,
    [ExpectedEndUtc] DATETIME2 NOT NULL,
    [ReservedUtc] DATETIME2 NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NOT NULL,
    [FencingToken] BIGINT NOT NULL,
    CONSTRAINT [PK_MarketCollectorAttempt] PRIMARY KEY ([RequestId]),
    CONSTRAINT [UQ_MarketCollectorAttempt_Slot] UNIQUE ([SourceContract],[SessionOpenUtc],[Symbol],[ExpectedEndUtc]),
    CONSTRAINT [CK_MarketCollectorAttempt_Bounds] CHECK ([ExpectedEndUtc]>[SessionOpenUtc] AND [ExpectedEndUtc]<=[SessionCloseUtc] AND [ReservedUtc]>[ExpectedEndUtc] AND [FencingToken]>0)
);
GO

CREATE TABLE [dbo].[MarketCollectorBook]
(
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] DATETIME2 NOT NULL,
    [SessionCloseUtc] DATETIME2 NOT NULL,
    CONSTRAINT [PK_MarketCollectorBook] PRIMARY KEY ([SourceContract],[Symbol],[SessionOpenUtc]),
    CONSTRAINT [CK_MarketCollectorBook_Session] CHECK ([SessionCloseUtc]>[SessionOpenUtc] AND DATEDIFF(MINUTE,[SessionOpenUtc],[SessionCloseUtc])<=1440)
);
GO

CREATE TABLE [dbo].[MarketCollectorRequest]
(
    [RequestId] UNIQUEIDENTIFIER NOT NULL,
    [Sequence] BIGINT IDENTITY(1,1) NOT NULL,
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] DATETIME2 NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NOT NULL,
    [FencingToken] BIGINT NOT NULL,
    [ContentHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ContentJson] NVARCHAR(MAX) NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_MarketCollectorRequest_Recorded] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_MarketCollectorRequest] PRIMARY KEY ([RequestId]),
    CONSTRAINT [FK_MarketCollectorRequest_Attempt] FOREIGN KEY ([RequestId]) REFERENCES [dbo].[MarketCollectorAttempt]([RequestId]),
    CONSTRAINT [UQ_MarketCollectorRequest_Sequence] UNIQUE ([Sequence]),
    CONSTRAINT [UQ_MarketCollectorRequest_Scope] UNIQUE ([RequestId],[SourceContract],[Symbol],[SessionOpenUtc]),
    CONSTRAINT [FK_MarketCollectorRequest_Book] FOREIGN KEY ([SourceContract],[Symbol],[SessionOpenUtc]) REFERENCES [dbo].[MarketCollectorBook]([SourceContract],[Symbol],[SessionOpenUtc]),
    CONSTRAINT [CK_MarketCollectorRequest_Content] CHECK (ISJSON([ContentJson])=1 AND DATALENGTH([ContentJson])<=8388608 AND [FencingToken]>0)
);
GO

CREATE TABLE [dbo].[MarketCollectorObservation]
(
    [ObservationId] UNIQUEIDENTIFIER NOT NULL,
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] DATETIME2 NOT NULL,
    [BarStartUtc] DATETIME2 NOT NULL,
    [RequestId] UNIQUEIDENTIFIER NOT NULL,
    [ContentHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ContentJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_MarketCollectorObservation] PRIMARY KEY ([SourceContract],[Symbol],[SessionOpenUtc],[BarStartUtc]),
    CONSTRAINT [UQ_MarketCollectorObservation_Id] UNIQUE ([ObservationId]),
    CONSTRAINT [FK_MarketCollectorObservation_Request] FOREIGN KEY ([RequestId],[SourceContract],[Symbol],[SessionOpenUtc]) REFERENCES [dbo].[MarketCollectorRequest]([RequestId],[SourceContract],[Symbol],[SessionOpenUtc]),
    CONSTRAINT [CK_MarketCollectorObservation_Content] CHECK (ISJSON([ContentJson])=1 AND DATALENGTH([ContentJson])<=16384 AND [BarStartUtc]>=[SessionOpenUtc])
);
GO

CREATE TABLE [dbo].[MarketCollectorConflict]
(
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] DATETIME2 NOT NULL,
    [BarStartUtc] DATETIME2 NOT NULL,
    [RequestId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_MarketCollectorConflict] PRIMARY KEY ([SourceContract],[Symbol],[SessionOpenUtc],[BarStartUtc]),
    CONSTRAINT [FK_MarketCollectorConflict_Observation] FOREIGN KEY ([SourceContract],[Symbol],[SessionOpenUtc],[BarStartUtc]) REFERENCES [dbo].[MarketCollectorObservation]([SourceContract],[Symbol],[SessionOpenUtc],[BarStartUtc]),
    CONSTRAINT [FK_MarketCollectorConflict_Request] FOREIGN KEY ([RequestId],[SourceContract],[Symbol],[SessionOpenUtc]) REFERENCES [dbo].[MarketCollectorRequest]([RequestId],[SourceContract],[Symbol],[SessionOpenUtc])
);
GO

CREATE TRIGGER [dbo].[TR_MarketCollectorAttempt_Immutable] ON [dbo].[MarketCollectorAttempt]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54572,'Collector request attempts cannot be erased or retried.',1;
END;
GO

CREATE TRIGGER [dbo].[TR_MarketCollectorBook_Immutable] ON [dbo].[MarketCollectorBook]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54563,'Collector session identity is immutable.',1;
END;
GO

CREATE TRIGGER [dbo].[TR_MarketCollectorRequest_Immutable] ON [dbo].[MarketCollectorRequest]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54564,'Collector source receipts are immutable.',1;
END;
GO

CREATE TRIGGER [dbo].[TR_MarketCollectorObservation_Immutable] ON [dbo].[MarketCollectorObservation]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54565,'First completed collector observations are immutable.',1;
END;
GO

CREATE TRIGGER [dbo].[TR_MarketCollectorConflict_Immutable] ON [dbo].[MarketCollectorConflict]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54566,'Collector conflicts cannot be silently cleared.',1;
END;
GO

IF OBJECT_ID(N'dbo.MarketCollectorAttempt',N'U') IS NULL OR OBJECT_ID(N'dbo.TR_MarketCollectorAttempt_Immutable',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.MarketCollectorLease',N'U') IS NULL OR OBJECT_ID(N'dbo.MarketCollectorBook',N'U') IS NULL
 OR OBJECT_ID(N'dbo.MarketCollectorRequest',N'U') IS NULL OR OBJECT_ID(N'dbo.MarketCollectorObservation',N'U') IS NULL
 OR OBJECT_ID(N'dbo.MarketCollectorConflict',N'U') IS NULL
 OR OBJECT_ID(N'dbo.TR_MarketCollectorBook_Immutable',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.TR_MarketCollectorRequest_Immutable',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.TR_MarketCollectorObservation_Immutable',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.TR_MarketCollectorConflict_Immutable',N'TR') IS NULL
 THROW 54571,'Collector schema postcondition failed.',1;
COMMIT TRANSACTION;
GO