CREATE TABLE [dbo].[PositionExecutionAudit]
(
    [AuditId]          UNIQUEIDENTIFIER NOT NULL,
    [PositionId]       UNIQUEIDENTIFIER NOT NULL,
    [FromMode]         NVARCHAR(8)      NOT NULL,
    [ToMode]           NVARCHAR(8)      NOT NULL,
    [AccountLabel]     NVARCHAR(64)     NULL,
    [Reason]           NVARCHAR(256)    NOT NULL,
    [CreatedUtc]       DATETIME2        NOT NULL CONSTRAINT [DF_PositionExecutionAudit_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_PositionExecutionAudit] PRIMARY KEY CLUSTERED ([AuditId]),
    CONSTRAINT [FK_PositionExecutionAudit_Position] FOREIGN KEY ([PositionId]) REFERENCES [dbo].[ActivePosition] ([PositionId]),
    CONSTRAINT [CK_PositionExecutionAudit_FromMode] CHECK ([FromMode] IN (N'Ghost', N'Real')),
    CONSTRAINT [CK_PositionExecutionAudit_ToMode] CHECK ([ToMode] IN (N'Ghost', N'Real')),
    CONSTRAINT [CK_PositionExecutionAudit_Changed] CHECK ([FromMode] <> [ToMode]),
    CONSTRAINT [CK_PositionExecutionAudit_AccountLabel] CHECK
    (
        ([ToMode] = N'Ghost' AND [AccountLabel] IS NULL)
        OR ([ToMode] = N'Real' AND LEN(LTRIM(RTRIM([AccountLabel]))) BETWEEN 1 AND 64)
    )
);
