CREATE TABLE [dbo].[ShadowPortfolioEvent]
(
    [EventId]                UNIQUEIDENTIFIER NOT NULL,
    [PortfolioId]            UNIQUEIDENTIFIER NOT NULL,
    [SessionId]              UNIQUEIDENTIFIER NULL,
    [PositionId]             UNIQUEIDENTIFIER NULL,
    [OrderId]                UNIQUEIDENTIFIER NULL,
    [OccurredUtc]            DATETIME2        NOT NULL,
    [EventType]              NVARCHAR(32)     NOT NULL,
    [ReasonCode]             NVARCHAR(64)     NOT NULL,
    [DetailsJson]            NVARCHAR(MAX)    NOT NULL,
    [CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPortfolioEvent_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_ShadowPortfolioEvent] PRIMARY KEY CLUSTERED ([EventId]),
    CONSTRAINT [FK_ShadowPortfolioEvent_Portfolio] FOREIGN KEY ([PortfolioId]) REFERENCES [dbo].[ShadowPortfolio] ([PortfolioId]),
    CONSTRAINT [FK_ShadowPortfolioEvent_Session] FOREIGN KEY ([SessionId]) REFERENCES [dbo].[ShadowPortfolioSession] ([SessionId]),
    CONSTRAINT [FK_ShadowPortfolioEvent_Position] FOREIGN KEY ([PositionId]) REFERENCES [dbo].[ShadowPosition] ([PositionId]),
    CONSTRAINT [FK_ShadowPortfolioEvent_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[ShadowOrder] ([OrderId]),
    CONSTRAINT [CK_ShadowPortfolioEvent_Type] CHECK ([EventType] IN (N'Lifecycle', N'Candidate', N'Order', N'Position', N'Risk', N'Capital', N'DataQuality', N'Counterfactual')),
    CONSTRAINT [CK_ShadowPortfolioEvent_Json] CHECK (ISJSON([DetailsJson]) = 1)
);
GO

CREATE INDEX [IX_ShadowPortfolioEvent_PortfolioTime]
    ON [dbo].[ShadowPortfolioEvent] ([PortfolioId], [OccurredUtc] DESC);
GO

