CREATE TABLE [dbo].[EnginePaperRecordIdentity]
(
    [PortfolioId] UNIQUEIDENTIFIER NOT NULL,
    [Kind] NVARCHAR(32) NOT NULL,
    [RecordId] UNIQUEIDENTIFIER NOT NULL,
    [Ordinal] INT NOT NULL,
    [CreatedRevision] BIGINT NOT NULL,
    CONSTRAINT [PK_EnginePaperRecordIdentity] PRIMARY KEY ([PortfolioId],[Kind],[RecordId]),
    CONSTRAINT [UQ_EnginePaperRecordIdentity_Ordinal] UNIQUE ([PortfolioId],[Kind],[Ordinal]),
    CONSTRAINT [CK_EnginePaperRecordIdentity_Identity] CHECK ([Ordinal]>=0 AND [CreatedRevision]>=0
        AND [RecordId]<>'00000000-0000-0000-0000-000000000000'
        AND [Kind] IN(N'positions',N'actions',N'fills',N'quotes',N'marks',N'fillAction',N'buyEvaluation'))
);
GO
