CREATE TABLE [dbo].[DelphiLiveSnapshotItem]
(
    [PortfolioId] UNIQUEIDENTIFIER NOT NULL,
    [CollectionName] NVARCHAR(32) NOT NULL,
    [Ordinal] INT NOT NULL,
    [Revision] BIGINT NOT NULL,
    [ContentHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT [PK_DelphiLiveSnapshotItem] PRIMARY KEY ([PortfolioId],[CollectionName],[Ordinal],[Revision] DESC),
    CONSTRAINT [FK_DelphiLiveSnapshotItem_Portfolio] FOREIGN KEY ([PortfolioId]) REFERENCES [dbo].[DelphiLivePortfolioLedger]([PortfolioId]),
    CONSTRAINT [FK_DelphiLiveSnapshotItem_Content] FOREIGN KEY ([ContentHash]) REFERENCES [dbo].[DelphiLiveSnapshotContent]([ContentHash]),
    CONSTRAINT [CK_DelphiLiveSnapshotItem_Identity] CHECK ([Ordinal]>=0 AND [Revision]>=0 AND [CollectionName] IN
        (N'sessionOpeningPositions',N'positions',N'actions',N'fills',N'quotes',N'marks'))
);
GO
