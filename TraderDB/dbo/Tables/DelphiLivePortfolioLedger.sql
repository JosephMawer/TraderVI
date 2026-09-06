CREATE TABLE [dbo].[DelphiLivePortfolioLedger]
(
    [PortfolioId] UNIQUEIDENTIFIER NOT NULL,
    [GenerationId] UNIQUEIDENTIFIER NOT NULL,
    [DelphiLivePolicyVersionId] UNIQUEIDENTIFIER NOT NULL,
    [Revision] BIGINT NOT NULL,
    [SnapshotSchemaVersion] INT NOT NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [UpdatedUtc] DATETIME2 NOT NULL,
    CONSTRAINT [PK_DelphiLivePortfolioLedger] PRIMARY KEY ([PortfolioId]),
    CONSTRAINT [FK_DelphiLivePortfolioLedger_Generation] FOREIGN KEY ([GenerationId]) REFERENCES [dbo].[DelphiLivePortfolioGeneration] ([GenerationId]),
    CONSTRAINT [FK_DelphiLivePortfolioLedger_Policy] FOREIGN KEY ([DelphiLivePolicyVersionId]) REFERENCES [dbo].[DelphiLivePolicyVersion] ([DelphiLivePolicyVersionId]),
    CONSTRAINT [UQ_DelphiLivePortfolioLedger_Generation] UNIQUE ([GenerationId]),
    CONSTRAINT [CK_DelphiLivePortfolioLedger_Revision] CHECK ([Revision] >= 0 AND [SnapshotSchemaVersion] = 1 AND ISJSON([SnapshotJson]) = 1)
);
GO
