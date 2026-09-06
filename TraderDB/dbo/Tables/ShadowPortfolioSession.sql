CREATE TABLE [dbo].[ShadowPortfolioSession]
(
    [SessionId]              UNIQUEIDENTIFIER NOT NULL,
    [PortfolioId]            UNIQUEIDENTIFIER NOT NULL,
    [TradingDate]            DATE             NOT NULL,
    [CalibrationRunId]       UNIQUEIDENTIFIER NULL,
    [Status]                 NVARCHAR(32)     NOT NULL,
    [ActivationBaselineUtc]  DATETIME2        NULL,
    [OpeningValue]           DECIMAL(19,6)    NOT NULL,
    [ClosingValue]           DECIMAL(19,6)    NULL,
    [DailyLossGuardActive]   BIT              NOT NULL CONSTRAINT [DF_ShadowPortfolioSession_DailyLossGuard] DEFAULT ((0)),
    [StartedUtc]             DATETIME2        NOT NULL,
    [CompletedUtc]           DATETIME2        NULL,
    [CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPortfolioSession_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPortfolioSession_UpdatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_ShadowPortfolioSession] PRIMARY KEY CLUSTERED ([SessionId]),
    CONSTRAINT [FK_ShadowPortfolioSession_Portfolio] FOREIGN KEY ([PortfolioId]) REFERENCES [dbo].[ShadowPortfolio] ([PortfolioId]),
    CONSTRAINT [FK_ShadowPortfolioSession_CalibrationRun] FOREIGN KEY ([CalibrationRunId]) REFERENCES [dbo].[CalibrationRun] ([RunId]),
    CONSTRAINT [UQ_ShadowPortfolioSession_PortfolioDate] UNIQUE ([PortfolioId], [TradingDate]),
    CONSTRAINT [CK_ShadowPortfolioSession_Status] CHECK ([Status] IN (N'Active', N'Completed', N'NoValidDelphiRun', N'Paused')),
    CONSTRAINT [CK_ShadowPortfolioSession_Values] CHECK ([OpeningValue] > 0 AND ([ClosingValue] IS NULL OR [ClosingValue] >= 0)),
    CONSTRAINT [CK_ShadowPortfolioSession_Completion] CHECK
    (
        ([Status] IN (N'Active', N'Paused', N'NoValidDelphiRun') AND [CompletedUtc] IS NULL AND [ClosingValue] IS NULL)
        OR ([Status] = N'Completed' AND [CompletedUtc] IS NOT NULL AND [ClosingValue] IS NOT NULL)
    )
);
GO

CREATE INDEX [IX_ShadowPortfolioSession_DateStatus]
    ON [dbo].[ShadowPortfolioSession] ([TradingDate], [Status]);
GO

