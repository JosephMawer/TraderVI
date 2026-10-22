-- ADR-0103. Additive shared input storage; no historical conversion, removal or account mutation.
-- Stop engine and legacy Live writers. Explicit installation approval and a fresh checksum backup are required.
-- Rollback: retain these immutable objects/evidence and restore the prior binaries only while the engine is stopped.
-- This is a prepared manual migration, not an application-startup action.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 54410,'A fresh checksum full backup of TraderDB is required.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.DelphiLiveSessionSymbol',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLivePolicyVersion',N'U') IS NULL
 OR OBJECT_ID(N'dbo.DelphiLiveHostLease',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLiveEvaluation',N'U') IS NULL
 THROW 54411,'Expected shared collection and evaluation schema is missing.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 54412,'Stop all Live hosts before installation.',1;
IF OBJECT_ID(N'dbo.EngineEvidenceContent') IS NOT NULL OR OBJECT_ID(N'dbo.EngineInputPublication') IS NOT NULL
 OR OBJECT_ID(N'dbo.TR_EngineEvidenceContent_Immutable') IS NOT NULL OR OBJECT_ID(N'dbo.TR_EngineInputPublication_Immutable') IS NOT NULL
 THROW 54413,'Unexpected existing shared input objects; inspect instead of replaying this migration.',1;
GO
CREATE TABLE [dbo].[EngineEvidenceContent]
(
    [EvidenceSequence] BIGINT IDENTITY(1,1) NOT NULL,
    [ContentHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ContentJson] NVARCHAR(MAX) NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineEvidenceContent_Recorded] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EngineEvidenceContent] PRIMARY KEY ([ContentHash]),
    CONSTRAINT [UQ_EngineEvidenceContent_Sequence] UNIQUE ([EvidenceSequence]),
    CONSTRAINT [CK_EngineEvidenceContent_Hash] CHECK (LEN([ContentHash])=64 AND [ContentHash] NOT LIKE '%[^0-9A-F]%'),
    CONSTRAINT [CK_EngineEvidenceContent_Json] CHECK (ISJSON([ContentJson])=1)
);
GO

CREATE TABLE [dbo].[EngineInputPublication]
(
    [PublicationSequence] BIGINT IDENTITY(1,1) NOT NULL,
    [EvaluationId] UNIQUEIDENTIFIER NOT NULL,
    [SessionId] UNIQUEIDENTIFIER NOT NULL,
    [PolicyVersionId] UNIQUEIDENTIFIER NOT NULL,
    [Symbol] NVARCHAR(20) NOT NULL,
    [BarEndUtc] DATETIME2 NOT NULL,
    [ContinuityEpoch] INT NOT NULL,
    [LeaseFencingToken] BIGINT NOT NULL,
    [ObservedOnTime] BIT NOT NULL,
    [ConfirmedLiveEligible] BIT NOT NULL,
    [InputJson] NVARCHAR(MAX) NOT NULL,
    [ResultJson] NVARCHAR(MAX) NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineInputPublication_Recorded] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EngineInputPublication] PRIMARY KEY ([EvaluationId]),
    CONSTRAINT [UQ_EngineInputPublication_Sequence] UNIQUE ([PublicationSequence]),
    CONSTRAINT [FK_EngineInputPublication_SessionSymbol] FOREIGN KEY ([SessionId],[Symbol]) REFERENCES [dbo].[DelphiLiveSessionSymbol] ([SessionId],[Symbol]),
    CONSTRAINT [FK_EngineInputPublication_Policy] FOREIGN KEY ([PolicyVersionId]) REFERENCES [dbo].[DelphiLivePolicyVersion] ([DelphiLivePolicyVersionId]),
    CONSTRAINT [UQ_EngineInputPublication_Checkpoint] UNIQUE ([SessionId],[PolicyVersionId],[Symbol],[BarEndUtc]),
    CONSTRAINT [CK_EngineInputPublication_Json] CHECK (ISJSON([InputJson])=1 AND ISJSON([ResultJson])=1),
    CONSTRAINT [CK_EngineInputPublication_Version] CHECK (JSON_VALUE([InputJson],'$."$evidence".version') IS NOT NULL AND JSON_VALUE([InputJson],'$."$evidence".version')=N'1'),
    CONSTRAINT [CK_EngineInputPublication_Causality] CHECK ([RecordedUtc]>[BarEndUtc] AND [ContinuityEpoch]>0 AND [LeaseFencingToken]>0),
    CONSTRAINT [CK_EngineInputPublication_Confirmation] CHECK ([ConfirmedLiveEligible]=0 OR [ObservedOnTime]=1)
);
GO

CREATE TRIGGER [dbo].[TR_EngineEvidenceContent_Immutable] ON [dbo].[EngineEvidenceContent]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54402,'Shared evidence content is immutable.',1;
END;
GO

CREATE TRIGGER [dbo].[TR_EngineInputPublication_Immutable] ON [dbo].[EngineInputPublication]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54403,'Shared input publications are immutable.',1;
END;
GO

IF OBJECT_ID(N'dbo.EngineEvidenceContent',N'U') IS NULL OR OBJECT_ID(N'dbo.EngineInputPublication',N'U') IS NULL
 OR OBJECT_ID(N'dbo.TR_EngineEvidenceContent_Immutable',N'TR') IS NULL OR OBJECT_ID(N'dbo.TR_EngineInputPublication_Immutable',N'TR') IS NULL
 OR OBJECTPROPERTYEX(OBJECT_ID(N'dbo.TR_EngineEvidenceContent_Immutable'),N'ExecIsTriggerDisabled')<>0
 OR OBJECTPROPERTYEX(OBJECT_ID(N'dbo.TR_EngineInputPublication_Immutable'),N'ExecIsTriggerDisabled')<>0
 THROW 54414,'Shared input storage postcondition failed.',1;
COMMIT TRANSACTION;
GO
