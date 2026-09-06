CREATE TABLE [dbo].[DelphiLivePortfolioRevision]
(
    [PortfolioId] UNIQUEIDENTIFIER NOT NULL,
    [Revision] BIGINT NOT NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [LeaseId] UNIQUEIDENTIFIER NULL,
    [LeaseFencingToken] BIGINT NULL,
    [PersistedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_DelphiLivePortfolioRevision_PersistedUtc] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_DelphiLivePortfolioRevision] PRIMARY KEY ([PortfolioId], [Revision]),
    CONSTRAINT [FK_DelphiLivePortfolioRevision_Portfolio] FOREIGN KEY ([PortfolioId]) REFERENCES [dbo].[DelphiLivePortfolioLedger] ([PortfolioId]),
    CONSTRAINT [FK_DelphiLivePortfolioRevision_Lease] FOREIGN KEY ([LeaseId]) REFERENCES [dbo].[DelphiLiveHostLease] ([LeaseId]),
    CONSTRAINT [CK_DelphiLivePortfolioRevision_Identity] CHECK
        (ISJSON([SnapshotJson]) = 1 AND (([Revision] = 0 AND [LeaseId] IS NULL AND [LeaseFencingToken] IS NULL) OR
         ([Revision] > 0 AND [LeaseId] IS NOT NULL AND [LeaseFencingToken] > 0)))
);
GO
