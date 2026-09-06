CREATE TABLE [dbo].[DelphiLiveExperimentProtocol]
(
    [ProtocolId] UNIQUEIDENTIFIER NOT NULL,
    [OperationalPortfolioId] UNIQUEIDENTIFIER NOT NULL,
    [Revision] BIGINT NOT NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [UpdatedUtc] DATETIME2 NOT NULL,
    CONSTRAINT [PK_DelphiLiveExperimentProtocol] PRIMARY KEY ([ProtocolId]),
    CONSTRAINT [FK_DelphiLiveExperimentProtocol_Portfolio] FOREIGN KEY ([OperationalPortfolioId]) REFERENCES [dbo].[DelphiLivePortfolioLedger] ([PortfolioId]),
    CONSTRAINT [CK_DelphiLiveExperimentProtocol_State] CHECK ([Revision] >= 0 AND ISJSON([SnapshotJson]) = 1)
);
GO
