CREATE TABLE [dbo].[MarketCollectorConflict]
(
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] DATETIME2 NOT NULL,
    [BarStartUtc] DATETIME2 NOT NULL,
    [RequestId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_MarketCollectorConflict] PRIMARY KEY ([SourceContract],[Symbol],[SessionOpenUtc],[BarStartUtc]),
    CONSTRAINT [FK_MarketCollectorConflict_Observation] FOREIGN KEY ([SourceContract],[Symbol],[SessionOpenUtc],[BarStartUtc]) REFERENCES [dbo].[MarketCollectorObservation]([SourceContract],[Symbol],[SessionOpenUtc],[BarStartUtc]),
    CONSTRAINT [FK_MarketCollectorConflict_Request] FOREIGN KEY ([RequestId],[SourceContract],[Symbol],[SessionOpenUtc]) REFERENCES [dbo].[MarketCollectorRequest]([RequestId],[SourceContract],[Symbol],[SessionOpenUtc])
);
GO
