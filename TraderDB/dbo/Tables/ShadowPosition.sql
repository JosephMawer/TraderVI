CREATE TABLE [dbo].[ShadowPosition]
(
    [PositionId]             UNIQUEIDENTIFIER NOT NULL,
    [PortfolioId]            UNIQUEIDENTIFIER NOT NULL,
    [Symbol]                 NVARCHAR(20)     NOT NULL,
    [Status]                 NVARCHAR(16)     NOT NULL,
    [Shares]                 INT              NOT NULL,
    [AverageCost]            DECIMAL(19,6)    NOT NULL,
    [CostBasis]              DECIMAL(19,6)    NOT NULL,
    [FullPositionTarget]     DECIMAL(19,6)    NOT NULL,
    [EntryUtc]               DATETIME2        NOT NULL,
    [EntryTradingDate]       DATE             NOT NULL,
    [AddOnCount]             TINYINT          NOT NULL CONSTRAINT [DF_ShadowPosition_AddOnCount] DEFAULT ((0)),
    [SameDayReentryCount]    TINYINT          NOT NULL CONSTRAINT [DF_ShadowPosition_ReentryCount] DEFAULT ((0)),
    [HighestFifteenClose]    DECIMAL(19,6)    NULL,
    [LastFifteenMinuteBarUtc] DATETIME2       NULL,
    [TrailingStopPrice]      DECIMAL(19,6)    NULL,
    [ProfitProtectionArmed]  BIT              NOT NULL CONSTRAINT [DF_ShadowPosition_ProfitProtectionArmed] DEFAULT ((0)),
    [LastPrice]              DECIMAL(19,6)    NOT NULL,
    [LastPriceEventUtc]      DATETIME2        NOT NULL,
    [RealizedProfitLoss]     DECIMAL(19,6)    NOT NULL CONSTRAINT [DF_ShadowPosition_RealizedProfitLoss] DEFAULT ((0)),
    [ExitUtc]                DATETIME2        NULL,
    [ExitReasonCode]         NVARCHAR(64)     NULL,
    [CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPosition_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPosition_UpdatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_ShadowPosition] PRIMARY KEY CLUSTERED ([PositionId]),
    CONSTRAINT [FK_ShadowPosition_Portfolio] FOREIGN KEY ([PortfolioId]) REFERENCES [dbo].[ShadowPortfolio] ([PortfolioId]),
    CONSTRAINT [FK_ShadowPosition_Symbol] FOREIGN KEY ([Symbol]) REFERENCES [dbo].[Symbols] ([Symbol]),
    CONSTRAINT [CK_ShadowPosition_Status] CHECK ([Status] IN (N'Open', N'Closed')),
    CONSTRAINT [CK_ShadowPosition_Values] CHECK
    (
        [Shares] > 0 AND [AverageCost] > 0 AND [CostBasis] > 0
        AND [FullPositionTarget] > 0 AND [LastPrice] > 0
        AND [AddOnCount] BETWEEN 0 AND 1
        AND [SameDayReentryCount] BETWEEN 0 AND 1
        AND ([HighestFifteenClose] IS NULL OR [HighestFifteenClose] > 0)
        AND ([TrailingStopPrice] IS NULL OR [TrailingStopPrice] > 0)
    ),
    CONSTRAINT [CK_ShadowPosition_Lifecycle] CHECK
    (
        ([Status] = N'Open' AND [ExitUtc] IS NULL AND [ExitReasonCode] IS NULL)
        OR ([Status] = N'Closed' AND [ExitUtc] IS NOT NULL AND [ExitReasonCode] IS NOT NULL)
    )
);
GO

CREATE UNIQUE INDEX [UX_ShadowPosition_OpenSymbol]
    ON [dbo].[ShadowPosition] ([PortfolioId], [Symbol])
    WHERE [Status] = N'Open';
GO

CREATE INDEX [IX_ShadowPosition_PortfolioStatus]
    ON [dbo].[ShadowPosition] ([PortfolioId], [Status]);
GO
