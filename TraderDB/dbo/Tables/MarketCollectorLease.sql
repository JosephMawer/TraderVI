CREATE TABLE [dbo].[MarketCollectorLease]
(
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NOT NULL,
    [FencingToken] BIGINT NOT NULL,
    [ExpiresUtc] DATETIME2 NOT NULL,
    CONSTRAINT [PK_MarketCollectorLease] PRIMARY KEY ([SourceContract]),
    CONSTRAINT [CK_MarketCollectorLease_Fence] CHECK ([FencingToken]>0)
);
GO
