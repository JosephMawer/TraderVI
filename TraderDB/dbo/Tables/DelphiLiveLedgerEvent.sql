CREATE TABLE [dbo].[DelphiLiveLedgerEvent]
(
    [EventId] UNIQUEIDENTIFIER NOT NULL,
    [PortfolioId] UNIQUEIDENTIFIER NOT NULL,
    [Revision] BIGINT NOT NULL,
    [EventKind] NVARCHAR(64) NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL,
    [DataJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_DelphiLiveLedgerEvent] PRIMARY KEY ([EventId]),
    CONSTRAINT [FK_DelphiLiveLedgerEvent_Revision] FOREIGN KEY ([PortfolioId], [Revision]) REFERENCES [dbo].[DelphiLivePortfolioRevision] ([PortfolioId], [Revision]),
    CONSTRAINT [CK_DelphiLiveLedgerEvent_Content] CHECK (LEN(LTRIM(RTRIM([EventKind]))) > 0 AND ISJSON([DataJson]) = 1)
);
GO
CREATE INDEX [IX_DelphiLiveLedgerEvent_PortfolioRevision] ON [dbo].[DelphiLiveLedgerEvent] ([PortfolioId], [Revision]);
GO
