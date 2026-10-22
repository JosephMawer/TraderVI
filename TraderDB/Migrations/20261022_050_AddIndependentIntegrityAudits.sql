-- ADR-0103. Durable bounded audit receipts, immutable findings and explicit verified resolutions.
-- CUTOVER ONLY: this retires the legacy DelphiLiveEvaluation writer. Do not install with an old host.
-- Stop all app/engine writers; review this complete script after a fresh verified checksum full backup.
-- Additive schema only. No financial history is converted, erased, repaired or pruned.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 54455,'A fresh checksum full backup of TraderDB is required.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.EngineMarketCycle',N'U') IS NULL OR OBJECT_ID(N'dbo.EngineConsumerReceipt',N'U') IS NULL
 THROW 54456,'Install reviewed migrations 047 through 049 first.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 54457,'Stop all Live hosts before installing the integrity cutover.',1;
IF OBJECT_ID(N'dbo.EngineIntegrityIncident') IS NOT NULL OR OBJECT_ID(N'dbo.EngineIntegrityResolution') IS NOT NULL
 OR OBJECT_ID(N'dbo.EngineAuditRun') IS NOT NULL OR OBJECT_ID(N'dbo.DelphiLiveEvaluation_RetiredWriter') IS NOT NULL
 OR OBJECT_ID(N'dbo.DelphiLiveEvaluation_ArchiveImmutable') IS NOT NULL
 THROW 54458,'Unexpected existing integrity schema; inspect instead of replaying migration.',1;
GO
CREATE TABLE [dbo].[EngineAuditRun]
(
    [RunId] UNIQUEIDENTIFIER NOT NULL,
    [Kind] NVARCHAR(16) NOT NULL,
    [BoundaryUtc] DATETIME2 NOT NULL,
    [PlanJson] NVARCHAR(MAX) NOT NULL,
    [PlanHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ProgressJson] NVARCHAR(MAX) NOT NULL,
    [ProgressHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Revision] BIGINT NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [UpdatedUtc] DATETIME2 NOT NULL,
    CONSTRAINT [PK_EngineAuditRun] PRIMARY KEY ([RunId]),
    CONSTRAINT [CK_EngineAuditRun_State] CHECK ([Kind] IN(N'NewHistory',N'Archive',N'Recovery')
        AND [Status] IN(N'Running',N'Complete',N'Failed') AND [Revision]>=0
        AND ISJSON([PlanJson])=1 AND ISJSON([ProgressJson])=1
        AND LEN([PlanHash])=64 AND [PlanHash] NOT LIKE '%[^0-9A-F]%'
        AND LEN([ProgressHash])=64 AND [ProgressHash] NOT LIKE '%[^0-9A-F]%')
);
GO

CREATE TABLE [dbo].[EngineIntegrityIncident]
(
    [IncidentId] UNIQUEIDENTIFIER NOT NULL,
    [Scope] NVARCHAR(16) NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NULL,
    [EvidenceKey] NVARCHAR(200) NOT NULL,
    [FailureCode] NVARCHAR(64) NOT NULL,
    [DiscoveredUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineIntegrityIncident_Time] DEFAULT SYSUTCDATETIME(),
    [ProtectionUntrusted] BIT NOT NULL,
    CONSTRAINT [PK_EngineIntegrityIncident] PRIMARY KEY ([IncidentId]),
    CONSTRAINT [CK_EngineIntegrityIncident_Scope] CHECK ([Scope] IN(N'Global',N'Broker',N'Paper',N'Research')
        AND ([Scope]<>N'Global' OR [OwnerId] IS NULL))
);
GO

CREATE TABLE [dbo].[EngineIntegrityResolution]
(
    [IncidentId] UNIQUEIDENTIFIER NOT NULL,
    [RecoveryRunId] UNIQUEIDENTIFIER NOT NULL,
    [VerifiedHeadsHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ClearedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineIntegrityResolution_Time] DEFAULT SYSUTCDATETIME(),
    [OperatorName] NVARCHAR(128) NOT NULL,
    [Reason] NVARCHAR(500) NOT NULL,
    CONSTRAINT [PK_EngineIntegrityResolution] PRIMARY KEY ([IncidentId]),
    CONSTRAINT [FK_EngineIntegrityResolution_Incident] FOREIGN KEY ([IncidentId]) REFERENCES [dbo].[EngineIntegrityIncident]([IncidentId]),
    CONSTRAINT [FK_EngineIntegrityResolution_Recovery] FOREIGN KEY ([RecoveryRunId]) REFERENCES [dbo].[EngineAuditRun]([RunId]),
    CONSTRAINT [CK_EngineIntegrityResolution_Proof] CHECK (LEN([VerifiedHeadsHash])=64 AND [VerifiedHeadsHash] NOT LIKE '%[^0-9A-F]%'
        AND LEN(LTRIM(RTRIM([OperatorName])))>0 AND LEN(LTRIM(RTRIM([Reason])))>0)
);
GO

CREATE TRIGGER [dbo].[EngineAuditRun_Progress] ON [dbo].[EngineAuditRun]
AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.RunId=d.RunId
        WHERE i.RunId IS NULL OR d.Status<>N'Running' OR i.Revision<>d.Revision+1
        OR i.Kind<>d.Kind OR i.BoundaryUtc<>d.BoundaryUtc OR i.PlanHash<>d.PlanHash
        OR CONVERT(varbinary(max),i.PlanJson)<>CONVERT(varbinary(max),d.PlanJson)
        OR i.UpdatedUtc<d.UpdatedUtc
        OR TRY_CONVERT(int,JSON_VALUE(i.ProgressJson,'$.TargetIndex')) IS NULL
        OR TRY_CONVERT(bigint,JSON_VALUE(i.ProgressJson,'$.VerifiedUnits')) IS NULL
        OR TRY_CONVERT(int,JSON_VALUE(i.ProgressJson,'$.TargetIndex'))<TRY_CONVERT(int,JSON_VALUE(d.ProgressJson,'$.TargetIndex'))
        OR TRY_CONVERT(bigint,JSON_VALUE(i.ProgressJson,'$.VerifiedUnits'))<TRY_CONVERT(bigint,JSON_VALUE(d.ProgressJson,'$.VerifiedUnits')))
        THROW 54452,'Audit boundaries and completed or failed receipts cannot be rewritten.',1;
END;
GO

CREATE TRIGGER [dbo].[EngineIntegrityIncident_Immutable] ON [dbo].[EngineIntegrityIncident]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54450,'Integrity findings are immutable; use a verified explicit resolution.',1;
END;
GO

CREATE TRIGGER [dbo].[EngineIntegrityResolution_Immutable] ON [dbo].[EngineIntegrityResolution]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54451,'Integrity resolutions are immutable.',1;
END;
GO

CREATE TRIGGER [dbo].[DelphiLiveEvaluation_RetiredWriter] ON [dbo].[DelphiLiveEvaluation]
INSTEAD OF INSERT AS
BEGIN
    SET NOCOUNT ON;
    THROW 54453,'The legacy Live writer is retired; use the independent engine input publisher.',1;
END;
GO

CREATE TRIGGER [dbo].[DelphiLiveEvaluation_ArchiveImmutable] ON [dbo].[DelphiLiveEvaluation]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54454,'The legacy Live evaluation archive is immutable.',1;
END;
GO

IF OBJECT_ID(N'dbo.EngineAuditRun',N'U') IS NULL OR OBJECT_ID(N'dbo.EngineIntegrityIncident',N'U') IS NULL
 OR OBJECT_ID(N'dbo.EngineIntegrityResolution',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLiveEvaluation_RetiredWriter',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.DelphiLiveEvaluation_ArchiveImmutable',N'TR') IS NULL
 THROW 54459,'Independent integrity postcondition failed.',1;
COMMIT TRANSACTION;
GO