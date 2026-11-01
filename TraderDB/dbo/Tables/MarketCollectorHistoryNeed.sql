CREATE TABLE [dbo].[MarketCollectorHistoryNeed]
(
    [SourceContract] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] datetime2 NOT NULL,
    [BarStartUtc] datetime2 NOT NULL,
    [Rule] varchar(24) NOT NULL,
    [ExpiresUtc] datetime2 NOT NULL,
    [RequestId] uniqueidentifier NULL,
    CONSTRAINT [PK_MarketCollectorHistoryNeed] PRIMARY KEY ([SourceContract],[SessionOpenUtc],[Symbol],[BarStartUtc]),
    CONSTRAINT [FK_MarketCollectorHistoryNeed_Attempt] FOREIGN KEY ([RequestId]) REFERENCES [dbo].[MarketCollectorAttempt]([RequestId]),
    CONSTRAINT [CK_MarketCollectorHistoryNeed_Window] CHECK ([BarStartUtc]>=[SessionOpenUtc] AND
        [ExpiresUtc]>DATEADD(MINUTE,1,[BarStartUtc]) AND [ExpiresUtc]<=DATEADD(DAY,1,[SessionOpenUtc]) AND
        DATEPART(SECOND,[BarStartUtc])=0 AND DATEPART(NANOSECOND,[BarStartUtc])=0 AND [Rule] IN ('MinuteEntry','HoldingClose'))
);
GO
CREATE INDEX [IX_MarketCollectorHistoryNeed_Request] ON [dbo].[MarketCollectorHistoryNeed]([RequestId]);
GO
