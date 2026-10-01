CREATE TABLE [dbo].[AiPortfolioReview]
(
    [ReviewId] UNIQUEIDENTIFIER NOT NULL,
    [Status] VARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [AccountScopeId] UNIQUEIDENTIFIER NOT NULL,
    [PortfolioId] UNIQUEIDENTIFIER NOT NULL,
    [AccountMode] VARCHAR(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [RecordedUtc] DATETIMEOFFSET(7) NOT NULL,
    [RecordJson] NVARCHAR(MAX) NOT NULL,
    [Action] AS CONVERT(NVARCHAR(16),JSON_VALUE([RecordJson],'$.Proposal.Action')),
    [SelectedPortfolioId] AS CONVERT(NVARCHAR(256),JSON_VALUE([RecordJson],'$.Proposal.PortfolioId')),
    CONSTRAINT [PK_AiPortfolioReview] PRIMARY KEY ([ReviewId],[Status]),
    CONSTRAINT [CK_AiPortfolioReview_Status] CHECK ([Status] IN ('Pending','Reviewed','Rejected')),
    CONSTRAINT [CK_AiPortfolioReview_Mode] CHECK ([AccountMode]='paper'),
    CONSTRAINT [CK_AiPortfolioReview_Json] CHECK (ISJSON([RecordJson])=1)
);
