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
