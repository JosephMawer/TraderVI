-- ADR-0060. Manual migration only. Requires a verified full backup and operator authorization.
-- Stop all older TraderVI hosts before applying: older binaries do not participate in the settings fence.
-- Additive; does not assign a version or change a trading account.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
BEGIN TRANSACTION;
IF DB_NAME()<>N'TraderDB' THROW 51314,'Expected TraderDB.',1;
IF OBJECT_ID(N'dbo.EngineStrategyVersion',N'U') IS NOT NULL THROW 51315,'Settings migration already installed; do not reapply.',1;
GO
CREATE TABLE [dbo].[EngineStrategyVersion]
(
    [VersionId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_EngineStrategyVersion] PRIMARY KEY,
    [Family] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(80) NOT NULL,
    [SettingsJson] NVARCHAR(MAX) NOT NULL,
    [SettingsHash] CHAR(64) NOT NULL,
    [CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineStrategyVersion_Created] DEFAULT SYSUTCDATETIME(),
    [CreatedBy] NVARCHAR(128) NOT NULL,
    [Reason] NVARCHAR(512) NOT NULL,
    CONSTRAINT [UQ_EngineStrategyVersion_Name] UNIQUE ([Family],[Name]),
    CONSTRAINT [CK_EngineStrategyVersion_Content] CHECK
      ([Family] IN (N'DelphiLive',N'SystemShadow',N'TrackedPositions') AND ISJSON([SettingsJson])=1
       AND [SettingsHash]=CONVERT(CHAR(64),HASHBYTES('SHA2_256',[SettingsJson]),2) AND LEN(LTRIM(RTRIM([Name])))>0)
);

GO
CREATE TABLE [dbo].[EngineStrategyAssignment]
(
    [AssignmentId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_EngineStrategyAssignment] PRIMARY KEY,
    [Sequence] BIGINT IDENTITY NOT NULL,
    [TargetId] UNIQUEIDENTIFIER NOT NULL,
    [VersionId] UNIQUEIDENTIFIER NOT NULL,
    [AssignedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineStrategyAssignment_Utc] DEFAULT SYSUTCDATETIME(),
    [AssignedBy] NVARCHAR(128) NOT NULL,
    [Reason] NVARCHAR(512) NOT NULL,
    [PriorStateJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [FK_EngineStrategyAssignment_Version] FOREIGN KEY ([VersionId]) REFERENCES [dbo].[EngineStrategyVersion]([VersionId]),
    CONSTRAINT [CK_EngineStrategyAssignment_Json] CHECK (ISJSON([PriorStateJson])=1)
);
GO
CREATE UNIQUE INDEX [IX_EngineStrategyAssignment_TargetSequence] ON [dbo].[EngineStrategyAssignment]([TargetId],[Sequence] DESC);

GO
ALTER TABLE dbo.DelphiLivePortfolioRevision ADD SettingsAssignmentId UNIQUEIDENTIFIER NULL;
GO
ALTER TABLE dbo.DelphiLivePortfolioRevision ADD CONSTRAINT FK_DelphiLivePortfolioRevision_Settings
 FOREIGN KEY(SettingsAssignmentId) REFERENCES dbo.EngineStrategyAssignment(AssignmentId);
ALTER TABLE dbo.DelphiLivePortfolioRevision DROP CONSTRAINT CK_DelphiLivePortfolioRevision_Identity;
ALTER TABLE dbo.DelphiLivePortfolioRevision ADD CONSTRAINT CK_DelphiLivePortfolioRevision_Identity CHECK
 (ISJSON(SnapshotJson)=1 AND ((Revision=0 AND LeaseId IS NULL AND LeaseFencingToken IS NULL AND SettingsAssignmentId IS NULL)
 OR (Revision>0 AND LeaseId IS NOT NULL AND LeaseFencingToken>0 AND SettingsAssignmentId IS NULL)
 OR (Revision>0 AND SettingsAssignmentId IS NOT NULL AND LeaseId IS NULL AND LeaseFencingToken IS NULL)));
GO
CREATE TRIGGER [dbo].[EngineStrategyVersion_Immutable] ON [dbo].[EngineStrategyVersion] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51311, 'Saved strategy versions are immutable. Save a new version.', 1;
END;

GO
CREATE TRIGGER [dbo].[EngineStrategyAssignment_Immutable] ON [dbo].[EngineStrategyAssignment] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51312, 'Strategy assignment history is immutable.', 1;
END;

GO
COMMIT TRANSACTION;
GO
