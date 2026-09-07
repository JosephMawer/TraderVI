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
