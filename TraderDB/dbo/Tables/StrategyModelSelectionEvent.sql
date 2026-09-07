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
