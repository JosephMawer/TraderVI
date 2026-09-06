CREATE TABLE [dbo].[ShadowPortfolio]
(
    [PortfolioId]            UNIQUEIDENTIFIER NOT NULL,
    [GenerationId]           UNIQUEIDENTIFIER NOT NULL,
    [PortfolioCode]          NVARCHAR(32)     NOT NULL,
    [DisplayName]            NVARCHAR(128)    NOT NULL,
    [Lens]                   NVARCHAR(16)     NOT NULL,
    [MaximumPositions]       TINYINT          NOT NULL,
    [SelectionActor]         NVARCHAR(16)     NOT NULL CONSTRAINT [DF_ShadowPortfolio_SelectionActor] DEFAULT (N'System'),
    [ExecutionMode]          NVARCHAR(16)     NOT NULL CONSTRAINT [DF_ShadowPortfolio_ExecutionMode] DEFAULT (N'Ghost'),
    [Status]                 NVARCHAR(32)     NOT NULL,
    [CashBalance]            DECIMAL(19,6)    NOT NULL,
    [HighestClosingValue]    DECIMAL(19,6)    NOT NULL,
    [PauseReason]            NVARCHAR(128)    NULL,
    [CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPortfolio_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPortfolio_UpdatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_ShadowPortfolio] PRIMARY KEY CLUSTERED ([PortfolioId]),
    CONSTRAINT [FK_ShadowPortfolio_Generation] FOREIGN KEY ([GenerationId]) REFERENCES [dbo].[ShadowPortfolioGeneration] ([GenerationId]),
    CONSTRAINT [UQ_ShadowPortfolio_GenerationCode] UNIQUE ([GenerationId], [PortfolioCode]),
    CONSTRAINT [CK_ShadowPortfolio_Code] CHECK ([PortfolioCode] IN (N'ContinuationTop3', N'ContinuationTop5', N'BreakoutTop3', N'BreakoutTop5')),
    CONSTRAINT [CK_ShadowPortfolio_Lens] CHECK ([Lens] IN (N'Continuation', N'Breakout')),
    CONSTRAINT [CK_ShadowPortfolio_MaximumPositions] CHECK ([MaximumPositions] IN (3, 5)),
    CONSTRAINT [CK_ShadowPortfolio_Identity] CHECK
    (
        ([PortfolioCode] = N'ContinuationTop3' AND [Lens] = N'Continuation' AND [MaximumPositions] = 3)
        OR ([PortfolioCode] = N'ContinuationTop5' AND [Lens] = N'Continuation' AND [MaximumPositions] = 5)
        OR ([PortfolioCode] = N'BreakoutTop3' AND [Lens] = N'Breakout' AND [MaximumPositions] = 3)
        OR ([PortfolioCode] = N'BreakoutTop5' AND [Lens] = N'Breakout' AND [MaximumPositions] = 5)
    ),
    CONSTRAINT [CK_ShadowPortfolio_Actors] CHECK ([SelectionActor] = N'System' AND [ExecutionMode] = N'Ghost'),
    CONSTRAINT [CK_ShadowPortfolio_Status] CHECK ([Status] IN (N'Draft', N'Active', N'Paused', N'CapitalReviewRequired', N'Stopped')),
    CONSTRAINT [CK_ShadowPortfolio_Values] CHECK ([CashBalance] >= 0 AND [HighestClosingValue] > 0),
    CONSTRAINT [CK_ShadowPortfolio_DisplayName] CHECK (LEN(LTRIM(RTRIM([DisplayName]))) > 0)
);
GO

CREATE INDEX [IX_ShadowPortfolio_GenerationStatus]
    ON [dbo].[ShadowPortfolio] ([GenerationId], [Status]);
GO

