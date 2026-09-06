CREATE TABLE [dbo].[ShadowOrder]
(
    [OrderId]                UNIQUEIDENTIFIER NOT NULL,
    [PortfolioId]            UNIQUEIDENTIFIER NOT NULL,
    [SessionId]              UNIQUEIDENTIFIER NULL,
    [PositionId]             UNIQUEIDENTIFIER NULL,
    [CandidateTrackingId]    UNIQUEIDENTIFIER NULL,
    [Symbol]                 NVARCHAR(20)     NOT NULL,
    [Side]                   NVARCHAR(8)      NOT NULL,
    [OrderKind]              NVARCHAR(24)     NOT NULL,
    [Status]                 NVARCHAR(16)     NOT NULL,
    [SignalReceivedUtc]      DATETIME2        NOT NULL,
    [EarliestFillUtc]        DATETIME2        NOT NULL,
    [Budget]                 DECIMAL(19,6)    NULL,
    [Shares]                 INT              NULL,
    [RawFillPrice]           DECIMAL(19,6)    NULL,
    [AdjustedFillPrice]      DECIMAL(19,6)    NULL,
    [FillUtc]                DATETIME2        NULL,
    [FrictionRate]           DECIMAL(9,6)     NOT NULL,
    [ReasonCode]             NVARCHAR(64)     NOT NULL,
    [CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowOrder_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowOrder_UpdatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_ShadowOrder] PRIMARY KEY CLUSTERED ([OrderId]),
    CONSTRAINT [FK_ShadowOrder_Portfolio] FOREIGN KEY ([PortfolioId]) REFERENCES [dbo].[ShadowPortfolio] ([PortfolioId]),
    CONSTRAINT [FK_ShadowOrder_Session] FOREIGN KEY ([SessionId]) REFERENCES [dbo].[ShadowPortfolioSession] ([SessionId]),
    CONSTRAINT [FK_ShadowOrder_Position] FOREIGN KEY ([PositionId]) REFERENCES [dbo].[ShadowPosition] ([PositionId]),
    CONSTRAINT [FK_ShadowOrder_Candidate] FOREIGN KEY ([CandidateTrackingId]) REFERENCES [dbo].[ShadowPortfolioCandidate] ([CandidateTrackingId]),
    CONSTRAINT [FK_ShadowOrder_Symbol] FOREIGN KEY ([Symbol]) REFERENCES [dbo].[Symbols] ([Symbol]),
    CONSTRAINT [CK_ShadowOrder_Side] CHECK ([Side] IN (N'Buy', N'Sell')),
    CONSTRAINT [CK_ShadowOrder_Kind] CHECK ([OrderKind] IN (N'Initial', N'AddOn', N'RiskExit', N'RotationExit', N'SessionTwoExit', N'Reentry')),
    CONSTRAINT [CK_ShadowOrder_Status] CHECK ([Status] IN (N'Pending', N'Filled', N'Expired', N'Cancelled')),
    CONSTRAINT [CK_ShadowOrder_Times] CHECK ([EarliestFillUtc] > [SignalReceivedUtc]),
    CONSTRAINT [CK_ShadowOrder_Friction] CHECK ([FrictionRate] >= 0 AND [FrictionRate] < 1),
    CONSTRAINT [CK_ShadowOrder_Fill] CHECK
    (
        ([Status] = N'Pending' AND [FillUtc] IS NULL AND [RawFillPrice] IS NULL AND [AdjustedFillPrice] IS NULL)
        OR ([Status] = N'Filled' AND [FillUtc] IS NOT NULL AND [RawFillPrice] > 0 AND [AdjustedFillPrice] > 0 AND [Shares] > 0)
        OR ([Status] IN (N'Expired', N'Cancelled') AND [FillUtc] IS NULL AND [RawFillPrice] IS NULL AND [AdjustedFillPrice] IS NULL)
    ),
    CONSTRAINT [CK_ShadowOrder_Budget] CHECK ([Budget] IS NULL OR [Budget] > 0)
);
GO

CREATE INDEX [IX_ShadowOrder_PendingFill]
    ON [dbo].[ShadowOrder] ([Status], [EarliestFillUtc], [PortfolioId]);
GO
