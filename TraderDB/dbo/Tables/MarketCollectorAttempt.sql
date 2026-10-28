CREATE TABLE [dbo].[MarketCollectorAttempt]
(
    [RequestId] UNIQUEIDENTIFIER NOT NULL,
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] DATETIME2 NOT NULL,
    [SessionCloseUtc] DATETIME2 NOT NULL,
    [ExpectedEndUtc] DATETIME2 NOT NULL,
    [ReservedUtc] DATETIME2 NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NOT NULL,
    [FencingToken] BIGINT NOT NULL,
    CONSTRAINT [PK_MarketCollectorAttempt] PRIMARY KEY ([RequestId]),
    CONSTRAINT [UQ_MarketCollectorAttempt_Slot] UNIQUE ([SourceContract],[SessionOpenUtc],[Symbol],[ExpectedEndUtc]),
    CONSTRAINT [CK_MarketCollectorAttempt_Bounds] CHECK ([ExpectedEndUtc]>[SessionOpenUtc] AND [ExpectedEndUtc]<=[SessionCloseUtc] AND [ReservedUtc]>[ExpectedEndUtc] AND [FencingToken]>0)
);
GO
