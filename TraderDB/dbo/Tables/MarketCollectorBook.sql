CREATE TABLE [dbo].[MarketCollectorBook]
(
    [SourceContract] NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] DATETIME2 NOT NULL,
    [SessionCloseUtc] DATETIME2 NOT NULL,
    CONSTRAINT [PK_MarketCollectorBook] PRIMARY KEY ([SourceContract],[Symbol],[SessionOpenUtc]),
    CONSTRAINT [CK_MarketCollectorBook_Session] CHECK ([SessionCloseUtc]>[SessionOpenUtc] AND DATEDIFF(MINUTE,[SessionOpenUtc],[SessionCloseUtc])<=1440)
);
GO
