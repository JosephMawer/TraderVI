-- ADR-0103. Immutable shared checkpoints and independent consumer receipts.
-- Stop all hosts. Review/authorize installation after a fresh verified checksum backup.
-- No history is rewritten or removed. Rollback retains these additive records.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 54437,'A fresh checksum full backup of TraderDB is required.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.EngineInputPublication',N'U') IS NULL OR OBJECT_ID(N'dbo.EngineCurrentState',N'U') IS NULL
 THROW 54438,'Install shared inputs and current observation schema first.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 54439,'Stop all Live hosts before installation.',1;
IF OBJECT_ID(N'dbo.EngineMarketCycle') IS NOT NULL OR OBJECT_ID(N'dbo.EngineConsumerReceipt') IS NOT NULL
 THROW 54440,'Unexpected existing consumer schema; inspect instead of replaying migration.',1;
GO
CREATE TABLE [dbo].[EngineMarketCycle]
(
    [Sequence] BIGINT IDENTITY(1,1) NOT NULL,
    [CycleId] UNIQUEIDENTIFIER NOT NULL,
    [SessionId] UNIQUEIDENTIFIER NOT NULL,
    [BarEndUtc] DATETIME2 NOT NULL,
    [ContinuityEpoch] INT NOT NULL,
    [ContextHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [InputIdsJson] NVARCHAR(MAX) NOT NULL,
    [PublishedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineMarketCycle_Published] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EngineMarketCycle] PRIMARY KEY ([CycleId]),
    CONSTRAINT [UQ_EngineMarketCycle_Sequence] UNIQUE ([Sequence]),
    CONSTRAINT [UQ_EngineMarketCycle_End] UNIQUE ([SessionId],[BarEndUtc]),
    CONSTRAINT [FK_EngineMarketCycle_Collection] FOREIGN KEY ([CycleId]) REFERENCES [dbo].[IntradayCollectionCycle]([CycleId]),
    CONSTRAINT [FK_EngineMarketCycle_Context] FOREIGN KEY ([ContextHash]) REFERENCES [dbo].[EngineEvidenceContent]([ContentHash]),
    CONSTRAINT [CK_EngineMarketCycle_Inputs] CHECK (ISJSON([InputIdsJson])=1 AND [ContinuityEpoch]>0 AND [PublishedUtc]>[BarEndUtc])
);
GO

CREATE TABLE [dbo].[EngineConsumerReceipt]
(
    [Consumer] NVARCHAR(16) NOT NULL,
    [CycleId] UNIQUEIDENTIFIER NOT NULL,
    [Outcome] NVARCHAR(32) NOT NULL,
    [CompletedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineConsumerReceipt_Completed] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EngineConsumerReceipt] PRIMARY KEY ([Consumer],[CycleId]),
    CONSTRAINT [FK_EngineConsumerReceipt_Cycle] FOREIGN KEY ([CycleId]) REFERENCES [dbo].[EngineMarketCycle]([CycleId]),
    CONSTRAINT [CK_EngineConsumerReceipt_Consumer] CHECK ([Consumer] IN(N'Paper',N'Research')),
    CONSTRAINT [CK_EngineConsumerReceipt_Outcome] CHECK ([Outcome] IN(N'Applied',N'NotActionableLate',N'DefinitionChanged',N'EvidenceConflict',N'ResearchRecorded',N'ResearchNotApplicable'))
);
GO

CREATE TRIGGER [dbo].[TR_EngineMarketCycle_Immutable] ON [dbo].[EngineMarketCycle]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54435,'Published market checkpoints are immutable.',1;
END;
GO

CREATE TRIGGER [dbo].[TR_EngineConsumerReceipt_Immutable] ON [dbo].[EngineConsumerReceipt]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54436,'Consumer outcomes are immutable.',1;
END;
GO

IF OBJECT_ID(N'dbo.EngineMarketCycle',N'U') IS NULL OR OBJECT_ID(N'dbo.EngineConsumerReceipt',N'U') IS NULL
 OR OBJECT_ID(N'dbo.TR_EngineMarketCycle_Immutable',N'TR') IS NULL OR OBJECT_ID(N'dbo.TR_EngineConsumerReceipt_Immutable',N'TR') IS NULL
 THROW 54441,'Consumer schema postcondition failed.',1;
COMMIT TRANSACTION;
GO