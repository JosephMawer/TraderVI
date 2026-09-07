-- ADR-0057. Add immutable model assignments and append-only selection history.
-- Reviewed additive schema only: no model/strategy activation or historical-row edits.
-- Requires a fresh verified backup. Run manually, never through DACPAC deployment.
USE [TraderDB];
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF OBJECT_ID(N'dbo.StrategyVersion', N'U') IS NULL OR OBJECT_ID(N'dbo.ModelRegistry', N'U') IS NULL
    THROW 51262, 'Required strategy/model registry schema is missing.', 1;
IF OBJECT_ID(N'dbo.StrategyModelBinding') IS NOT NULL OR OBJECT_ID(N'dbo.StrategyModelSelectionEvent') IS NOT NULL
    THROW 51263, 'Model binding schema already exists; inspect its installed definition before proceeding.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
CREATE TABLE [dbo].[StrategyModelBinding]
(
    [StrategyVersionId] UNIQUEIDENTIFIER NOT NULL,
    [ModelSetId] UNIQUEIDENTIFIER NOT NULL,
    [ModelSetJson] NVARCHAR(MAX) NOT NULL,
    [ModelSetSha256] CHAR(64) NOT NULL,
    [RegisteredUtc] DATETIME2 NOT NULL CONSTRAINT [DF_StrategyModelBinding_RegisteredUtc] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_StrategyModelBinding] PRIMARY KEY ([StrategyVersionId]),
    CONSTRAINT [FK_StrategyModelBinding_Strategy] FOREIGN KEY ([StrategyVersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId]),
    CONSTRAINT [CK_StrategyModelBinding_Json] CHECK (ISJSON([ModelSetJson]) = 1),
    CONSTRAINT [CK_StrategyModelBinding_Hash] CHECK ([ModelSetSha256] = CONVERT(CHAR(64), HASHBYTES('SHA2_256', [ModelSetJson]), 2))
);
CREATE TABLE [dbo].[StrategyModelSelectionEvent]
(
    [EventId] UNIQUEIDENTIFIER NOT NULL,
    [PreviousStrategyVersionId] UNIQUEIDENTIFIER NOT NULL,
    [SelectedStrategyVersionId] UNIQUEIDENTIFIER NOT NULL,
    [SelectedModelSetSha256] CHAR(64) NOT NULL,
    [ReviewReference] NVARCHAR(512) NOT NULL,
    [CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_StrategyModelSelectionEvent_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_StrategyModelSelectionEvent] PRIMARY KEY ([EventId]),
    CONSTRAINT [FK_StrategyModelSelectionEvent_Previous] FOREIGN KEY ([PreviousStrategyVersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId]),
    CONSTRAINT [FK_StrategyModelSelectionEvent_Selected] FOREIGN KEY ([SelectedStrategyVersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId]),
    CONSTRAINT [CK_StrategyModelSelectionEvent_Change] CHECK ([PreviousStrategyVersionId] <> [SelectedStrategyVersionId]),
    CONSTRAINT [CK_StrategyModelSelectionEvent_Review] CHECK (LEN(LTRIM(RTRIM([ReviewReference]))) > 0)
);
EXEC(N'CREATE TRIGGER [dbo].[StrategyModelBinding_Immutable] ON [dbo].[StrategyModelBinding]
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51260, ''Strategy model assignments are immutable; register a new strategy version.'', 1;
END;');
EXEC(N'CREATE TRIGGER [dbo].[StrategyModelSelectionEvent_Immutable] ON [dbo].[StrategyModelSelectionEvent]
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51261, ''Strategy model selection history is append-only.'', 1;
END;');
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
