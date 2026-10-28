CREATE TABLE [dbo].[MarketCollectorObservation]
(
    [ObservationId] UNIQUEIDENTIFIER NOT NULL,
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] DATETIME2 NOT NULL,
    [BarStartUtc] DATETIME2 NOT NULL,
    [RequestId] UNIQUEIDENTIFIER NOT NULL,
    [ContentHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ContentJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_MarketCollectorObservation] PRIMARY KEY ([SourceContract],[Symbol],[SessionOpenUtc],[BarStartUtc]),
    CONSTRAINT [UQ_MarketCollectorObservation_Id] UNIQUE ([ObservationId]),
    CONSTRAINT [FK_MarketCollectorObservation_Request] FOREIGN KEY ([RequestId],[SourceContract],[Symbol],[SessionOpenUtc]) REFERENCES [dbo].[MarketCollectorRequest]([RequestId],[SourceContract],[Symbol],[SessionOpenUtc]),
    CONSTRAINT [CK_MarketCollectorObservation_Content] CHECK (ISJSON([ContentJson])=1 AND DATALENGTH([ContentJson])<=16384 AND [BarStartUtc]>=[SessionOpenUtc])
);
GO
