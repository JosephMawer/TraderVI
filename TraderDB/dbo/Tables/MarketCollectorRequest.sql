CREATE TABLE [dbo].[MarketCollectorRequest]
(
    [RequestId] UNIQUEIDENTIFIER NOT NULL,
    [Sequence] BIGINT IDENTITY(1,1) NOT NULL,
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] DATETIME2 NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NOT NULL,
    [FencingToken] BIGINT NOT NULL,
    [ContentHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ContentJson] NVARCHAR(MAX) NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_MarketCollectorRequest_Recorded] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_MarketCollectorRequest] PRIMARY KEY ([RequestId]),
    CONSTRAINT [FK_MarketCollectorRequest_Attempt] FOREIGN KEY ([RequestId]) REFERENCES [dbo].[MarketCollectorAttempt]([RequestId]),
    CONSTRAINT [UQ_MarketCollectorRequest_Sequence] UNIQUE ([Sequence]),
    CONSTRAINT [UQ_MarketCollectorRequest_Scope] UNIQUE ([RequestId],[SourceContract],[Symbol],[SessionOpenUtc]),
    CONSTRAINT [FK_MarketCollectorRequest_Book] FOREIGN KEY ([SourceContract],[Symbol],[SessionOpenUtc]) REFERENCES [dbo].[MarketCollectorBook]([SourceContract],[Symbol],[SessionOpenUtc]),
    CONSTRAINT [CK_MarketCollectorRequest_Content] CHECK (ISJSON([ContentJson])=1 AND DATALENGTH([ContentJson])<=8388608 AND [FencingToken]>0)
);
GO
